import subprocess
import urllib.request
import json
import base64
import os

print("Generating synthetic Turkish speech via Windows SpeechSynthesizer...")
ps_script = """
Add-Type -AssemblyName System.Speech
$speak = New-Object System.Speech.Synthesis.SpeechSynthesizer
$speak.SetOutputToWaveFile('sample_speech.wav')
$speak.Speak('Adalet, toplumun huzuru ve bireylerin temel haklarının güvencesidir.')
$speak.Dispose()
"""
subprocess.run(["powershell", "-Command", ps_script], check=True)

if not os.path.exists("sample_speech.wav"):
    print("Failed to generate sample_speech.wav")
    exit(1)

print(f"Generated sample_speech.wav ({os.path.getsize('sample_speech.wav')} bytes).")

with open(".env", "r") as f:
    for line in f:
        if line.startswith("GEMINI_API_KEY="):
            api_key = line.strip().split("=", 1)[1]
            break

with open("sample_speech.wav", "rb") as f:
    audio_b64 = base64.b64encode(f.read()).decode("ascii")

prompt_text = (
    "Aşağıdaki ses kaydını dikkatle dinle ve içindeki tüm konuşmaları eksiksiz, kelimesi kelimesine Türkçe olarak yazıya dök (deşifre et). "
    "Sadece ve sadece deşifre edilmiş konuşma metnini döndür. Başka hiçbir açıklama, giriş veya yorum ekleme."
)

payload = {
    "model": "gemini-3.8-flash-tiered",
    "messages": [
        {
            "role": "user",
            "content": [
                {"type": "text", "text": prompt_text},
                {"type": "input_audio", "input_audio": {"data": audio_b64, "format": "wav"}}
            ]
        }
    ]
}

print("Sending audio to GeminiAudioSttProvider via proxy (http://localhost:8045)...")
req = urllib.request.Request(
    "http://localhost:8045/v1/chat/completions",
    data=json.dumps(payload).encode("utf-8"),
    headers={
        "Authorization": f"Bearer {api_key}",
        "Content-Type": "application/json"
    }
)

with urllib.request.urlopen(req) as resp:
    data = json.loads(resp.read().decode("utf-8"))
    transcription = data["choices"][0]["message"]["content"]
    print("\n==========================================")
    print("GEMINI AUDIO STT TRANSCRIPTION RESULT:")
    print("==========================================")
    print(transcription.strip())
    print("==========================================\n")

if os.path.exists("sample_speech.wav"):
    os.remove("sample_speech.wav")
