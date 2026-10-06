import subprocess
import os
import json
import urllib.request

ps_script = """
Add-Type -AssemblyName System.Speech
$speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
$speaker.SetOutputToWaveFile('test_voice.wav')
$speaker.Speak('Yapay zeka modelleri yerel bilgisayarda kotasiz ve basarili calisir.')
$speaker.Dispose()
"""
print("Generating test voice...")
subprocess.run(["powershell", "-Command", ps_script], check=True)

wav_path = os.path.abspath("test_voice.wav")
print(f"Testing audio: {wav_path} ({os.path.getsize(wav_path)} bytes)")

payload = {"file_path": wav_path, "language": "tr"}
req = urllib.request.Request(
    "http://127.0.0.1:5005/transcribe",
    data=json.dumps(payload).encode("utf-8"),
    headers={"Content-Type": "application/json"}
)

try:
    print("Sending request to http://127.0.0.1:5005/transcribe...")
    with urllib.request.urlopen(req) as resp:
        res = json.loads(resp.read().decode("utf-8"))
        print("\n==========================================")
        print("LOCAL FASTER-WHISPER RESPONSE:")
        print("==========================================")
        print("Text:", res.get("text"))
        print(f"Device: {res.get('device')}")
        print(f"Audio Duration: {res.get('duration')}s")
        print(f"Elapsed: {res.get('elapsed_seconds')}s")
        print("==========================================\n")
except urllib.error.HTTPError as e:
    print(f"HTTP Error {e.code}: {e.read().decode('utf-8')}")
except Exception as e:
    print(f"Error: {e}")
finally:
    if os.path.exists("test_voice.wav"):
        os.remove("test_voice.wav")
