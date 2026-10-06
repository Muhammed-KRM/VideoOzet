import subprocess
import os
import time
from faster_whisper import WhisperModel

ps_script = """
Add-Type -AssemblyName System.Speech
$speak = New-Object System.Speech.Synthesis.SpeechSynthesizer
$speak.SetOutputToWaveFile('sample_test.wav')
$speak.Speak('Adalet, toplumun huzuru ve bireylerin temel haklarının güvencesidir.')
$speak.Dispose()
"""
print("Generating sample Turkish audio...")
subprocess.run(["powershell", "-Command", ps_script], check=True)

print("Loading WhisperModel 'large-v3' on CPU (int8, 8 threads)...")
t0 = time.time()
model = WhisperModel("large-v3", device="cpu", compute_type="int8", cpu_threads=8)
t_load = time.time() - t0
print(f"Model loaded in: {t_load:.2f}s")

print("Transcribing audio...")
t1 = time.time()
segments, info = model.transcribe("sample_test.wav", language="tr", beam_size=5)
transcription = " ".join([s.text for s in segments]).strip()
t_transcribe = time.time() - t1

print(f"Audio duration: {info.duration:.2f}s")
print(f"Transcribed in: {t_transcribe:.2f}s")
if t_transcribe > 0:
    print(f"Speedup: {info.duration/t_transcribe:.1f}x real-time")
print(f"Detected language: {info.language} ({info.language_probability*100:.1f}%)")
print("==========================================")
print(f"RESULT: {transcription}")
print("==========================================")

if os.path.exists("sample_test.wav"):
    os.remove("sample_test.wav")
