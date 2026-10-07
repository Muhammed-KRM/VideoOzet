import os
import sys
import json
import time
import argparse
import threading
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler

# Add CUDA library paths to os.environ and sys.path if nvidia pip packages exist
def setup_cuda_paths():
    python_dir = os.path.dirname(sys.executable)
    site_packages = os.path.join(python_dir, "Lib", "site-packages")
    nvidia_dir = os.path.join(site_packages, "nvidia")
    if os.path.exists(nvidia_dir):
        for sub in ["cublas", "cudnn", "cuda_runtime", "cuda_nvrtc"]:
            bin_path = os.path.join(nvidia_dir, sub, "bin")
            if os.path.exists(bin_path):
                os.environ["PATH"] = bin_path + os.pathsep + os.environ.get("PATH", "")
                if hasattr(os, "add_dll_directory"):
                    try:
                        os.add_dll_directory(bin_path)
                    except Exception:
                        pass

setup_cuda_paths()

from faster_whisper import WhisperModel

GLOBAL_MODEL = None
DEVICE_USED = "unknown"
MODEL_NAME = os.environ.get("WHISPER_MODEL", "large-v3-turbo")
GPU_LOCK = threading.Lock()

def init_model(model_name="large-v3-turbo"):
    global GLOBAL_MODEL, DEVICE_USED, MODEL_NAME
    MODEL_NAME = model_name
    print(f"[*] Initializing Faster-Whisper model '{model_name}'...")
    
    # 1. Try CUDA (float16 for fast GPU inference on GTX 1650)
    try:
        print("[*] Attempting to load on CUDA (GPU)...")
        t0 = time.time()
        m = WhisperModel(model_name, device="cuda", compute_type="float16")
        # Warmup verification: CTranslate2 loads cublas during actual inference
        import numpy as np
        warmup_audio = np.zeros(16000, dtype=np.float32)
        _ = list(m.transcribe(warmup_audio)[0])
        GLOBAL_MODEL = m
        DEVICE_USED = "cuda"
        print(f"[+] Loaded and verified successfully on CUDA in {time.time()-t0:.2f}s!")
        return
    except Exception as e:
        print(f"[-] CUDA verification failed ({e}).")
        print("[*] Falling back to high-performance CPU (8 threads, int8)...")

    # 2. Fallback to 8-core CPU
    try:
        t0 = time.time()
        threads = min(8, os.cpu_count() or 4)
        GLOBAL_MODEL = WhisperModel(model_name, device="cpu", compute_type="int8", cpu_threads=threads)
        DEVICE_USED = "cpu"
        print(f"[+] Loaded successfully on CPU ({threads} threads) in {time.time()-t0:.2f}s!")
    except Exception as e:
        print(f"[!] Critical error loading model on CPU: {e}")
        raise

class WhisperRequestHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        # Concise logging
        print(f"[HTTP] {self.address_string()} - {args[0]} {args[1]}")

    def _send_json(self, code, data):
        payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        if self.path == "/health":
            resp = {
                "status": "healthy",
                "ready": GLOBAL_MODEL is not None,
                "device": DEVICE_USED,
                "model": MODEL_NAME
            }
            self._send_json(200, resp)
        else:
            self._send_json(404, {"error": "Not found"})

    def do_POST(self):
        if self.path == "/transcribe":
            content_length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(content_length).decode("utf-8")
            try:
                data = json.loads(body)
            except Exception as e:
                self.send_error_response(400, f"Invalid JSON payload: {e}")
                return

            file_path = data.get("file_path")
            language = data.get("language", "tr")
            beam_size = int(data.get("beam_size", 1))
            stream_mode = bool(data.get("stream", False))

            if not file_path or not os.path.exists(file_path):
                self.send_error_response(400, f"File not found: {file_path}")
                return

            if GLOBAL_MODEL is None:
                self.send_error_response(503, "Whisper model not initialized yet.")
                return

            try:
                base_name = os.path.basename(file_path)
                print(f"[*] Queued request for '{base_name}' (stream={stream_mode}, beam={beam_size}). Waiting for GPU lock...")
                with GPU_LOCK:
                    t0 = time.time()
                    print(f"[*] Acquired GPU lock. Starting transcription for '{base_name}' (model={MODEL_NAME}, lang={language}, beam={beam_size})...")
                    
                    if stream_mode:
                        self.send_response(200)
                        self.send_header("Content-Type", "application/x-ndjson; charset=utf-8")
                        self.send_header("Connection", "close")
                        self.end_headers()

                    segments, info = GLOBAL_MODEL.transcribe(
                        file_path,
                        language=language,
                        beam_size=beam_size,
                        vad_filter=True,
                        vad_parameters=dict(min_silence_duration_ms=500)
                    )

                    seg_list = []
                    full_text_parts = []
                    last_emit_sec = -10.0

                    for s in segments:
                        seg_list.append({
                            "start": round(s.start, 2),
                            "end": round(s.end, 2),
                            "text": s.text.strip()
                        })
                        full_text_parts.append(s.text.strip())

                        if stream_mode:
                            # Emit progress every ~3 audio seconds
                            if (s.end - last_emit_sec >= 3.0) or (s.end >= info.duration - 1.0):
                                last_emit_sec = s.end
                                pct = min(99, int(round((s.end / info.duration) * 100))) if info.duration > 0 else 0
                                progress_evt = json.dumps({
                                    "type": "progress",
                                    "current_sec": round(s.end),
                                    "total_sec": round(info.duration),
                                    "percent": pct
                                }) + "\n"
                                try:
                                    self.wfile.write(progress_evt.encode("utf-8"))
                                    self.wfile.flush()
                                except Exception:
                                    break

                    elapsed = time.time() - t0
                    full_text = " ".join(full_text_parts)
                    print(f"[+] Completed '{base_name}' in {elapsed:.2f}s! Duration: {info.duration:.1f}s, Segments: {len(seg_list)}")

                    resp_data = {
                        "type": "done",
                        "success": True,
                        "text": full_text,
                        "duration": round(info.duration, 2),
                        "elapsed_seconds": round(elapsed, 2),
                        "language": info.language,
                        "language_probability": round(info.language_probability, 4),
                        "device": DEVICE_USED,
                        "segments": seg_list
                    }

                    if stream_mode:
                        done_evt = json.dumps(resp_data, ensure_ascii=False) + "\n"
                        try:
                            self.wfile.write(done_evt.encode("utf-8"))
                            self.wfile.flush()
                        except Exception:
                            pass
                    else:
                        self._send_json(200, resp_data)

            except Exception as e:
                print(f"[-] Transcription error: {e}")
                if not stream_mode:
                    self.send_error_response(500, f"Transcription failed: {str(e)}")
                else:
                    err_evt = json.dumps({"type": "done", "success": False, "error": str(e)}) + "\n"
                    try:
                        self.wfile.write(err_evt.encode("utf-8"))
                        self.wfile.flush()
                    except Exception:
                        pass
        else:
            self._send_json(404, {"error": "Not found"})

    def send_error_response(self, code, message):
        resp = {"success": False, "error": message}
        self._send_json(code, resp)

def run_server(port=5005, model="large-v3-turbo"):
    init_model(model)
    server_address = ("127.0.0.1", port)
    httpd = ThreadingHTTPServer(server_address, WhisperRequestHandler)
    print(f"[+] Local Faster-Whisper Service listening at http://127.0.0.1:{port}/")
    print(f"[+] Health check: http://127.0.0.1:{port}/health")
    print(f"[+] Ready to transcribe.")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n[*] Shutting down Whisper service...")
        httpd.server_close()

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Local Faster-Whisper Server")
    parser.add_argument("--port", type=int, default=5005, help="Port to listen on (default: 5005)")
    parser.add_argument("--model", type=str, default=MODEL_NAME, help="Model name (default: large-v3-turbo)")
    args = parser.parse_args()

    run_server(port=args.port, model=args.model)
