import sys
import json
import time
import threading
import math
import random

# Try importing speech recognition & audio modules; fall back gracefully if missing
try:
    import speech_recognition as sr
    HAS_SR = True
except ImportError:
    HAS_SR = False

try:
    import pyttsx3
    HAS_PYTTS = True
except ImportError:
    HAS_PYTTS = False

def send_event(event_type, **kwargs):
    payload = {"event": event_type}
    payload.update(kwargs)
    print(json.dumps(payload), flush=True)

def log_msg(msg):
    send_event("log", message=msg)

class VoiceEngine:
    def __init__(self):
        self.is_listening = True
        self.recognizer = None
        self.microphone = None
        self.tts_engine = None
        
        if HAS_PYTTS:
            try:
                self.tts_engine = pyttsx3.init()
                voices = self.tts_engine.getProperty('voices')
                for v in voices:
                    if "female" in v.name.lower() or "zira" in v.name.lower():
                        self.tts_engine.setProperty('voice', v.id)
                        break
                self.tts_engine.setProperty('rate', 165)
            except Exception as e:
                log_msg(f"PyTTSInitException: {e}")

        if HAS_SR:
            try:
                self.recognizer = sr.Recognizer()
                self.recognizer.energy_threshold = 300
                self.recognizer.dynamic_energy_threshold = True
                self.microphone = sr.Microphone()
            except Exception as e:
                log_msg(f"MicrophoneInitException: {e}")

    def speak(self, text):
        if not text:
            return
        send_event("state_changed", state="Speaking")
        log_msg(f"Python Speaking: {text}")
        if self.tts_engine:
            try:
                self.tts_engine.say(text)
                self.tts_engine.runAndWait()
            except Exception as e:
                log_msg(f"TTS Exception: {e}")
        else:
            time.sleep(1.0)
        send_event("state_changed", state="Listening")

    def listen_loop(self):
        send_event("status", message="Python Voice Engine Initialized")
        send_event("state_changed", state="Listening")
        
        if not HAS_SR or not self.microphone:
            log_msg("SpeechRecognition / Microphone not installed in Python env. Running in bridge fallback mode.")
            # Emit simulated audio level pulse
            while self.is_listening:
                level = random.uniform(5, 25)
                send_event("audio_level", level=level)
                time.sleep(0.3)
            return

        with self.microphone as source:
            try:
                self.recognizer.adjust_for_ambient_noise(source, duration=0.8)
            except Exception as e:
                log_msg(f"Ambient noise adjustment error: {e}")

            while self.is_listening:
                try:
                    send_event("audio_level", level=random.uniform(10, 45))
                    audio = self.recognizer.listen(source, timeout=4, phrase_time_limit=8)
                    send_event("state_changed", state="Thinking")
                    
                    try:
                        recognized_text = self.recognizer.recognize_google(audio)
                        if recognized_text:
                            log_msg(f"Recognized: {recognized_text}")
                            lower = recognized_text.lower()
                            if "lucy" in lower:
                                send_event("wake_detected", text=recognized_text)
                            send_event("command_recognized", text=recognized_text)
                    except sr.UnknownValueError:
                        pass
                    except sr.RequestError as re:
                        log_msg(f"STT Request Error: {re}")
                    
                    send_event("state_changed", state="Listening")
                except sr.WaitTimeoutError:
                    pass
                except Exception as e:
                    log_msg(f"Listen loop exception: {e}")
                    time.sleep(0.5)

    def process_stdin(self):
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            try:
                cmd = json.loads(line)
                action = cmd.get("command")
                if action == "speak":
                    text = cmd.get("text", "")
                    threading.Thread(target=self.speak, args=(text,), daemon=True).start()
                elif action == "stop":
                    self.is_listening = False
                    break
            except Exception as e:
                log_msg(f"JSON stdin error: {e}")

if __name__ == "__main__":
    engine = VoiceEngine()
    stdin_thread = threading.Thread(target=engine.process_stdin, daemon=True)
    stdin_thread.start()
    engine.listen_loop()
