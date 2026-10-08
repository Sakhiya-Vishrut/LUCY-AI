using System;
using System.Globalization;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using LucyAI.Configuration;
using Microsoft.Extensions.Options;
using Serilog;

namespace LucyAI.Voice.Services
{
    /// <summary>
    /// Manages all voice I/O for LUCY:
    ///   • STT  — listens to the microphone and fires VoiceCommandRecognized
    ///   • TTS  — speaks text aloud via Windows Zira voice (or Python fallback)
    ///
    /// DUAL-ENGINE DESIGN:
    ///   Primary  → System.Speech (built-in Windows, no install needed)
    ///   Fallback → PythonBridgeService (pyttsx3 / Whisper via subprocess)
    ///
    /// Settings (wake word, confidence, debounce, rate, volume) are now read
    /// from appsettings.json via IOptions&lt;VoiceSettings&gt; instead of hardcoded.
    /// </summary>
    public class VoiceEngineService : IVoiceEngineService, IDisposable
    {
        // ── Dependencies ──────────────────────────────────────────────────────
        private readonly IPythonBridgeService _pythonBridge;
        private readonly VoiceSettings _voiceSettings;

        // ── Voice Engines ─────────────────────────────────────────────────────
        private SpeechSynthesizer? _synthesizer;
        private SpeechRecognitionEngine? _recognizer;

        // ── State ─────────────────────────────────────────────────────────────
        private bool _isListening;
        private bool _isSpeaking;
        private bool _disposed;
        private DateTime _lastSpeechTime = DateTime.MinValue;

        public bool IsMicrophoneAvailable { get; private set; } = true;

        // ── Events (consumed by MainViewModel) ────────────────────────────────
        public event EventHandler<string>? VoiceCommandRecognized;
        public event EventHandler<string>? VoiceStateChanged;
        public event EventHandler<double>? AudioLevelChanged;

        // ── Constructor ───────────────────────────────────────────────────────
        public VoiceEngineService(
            IPythonBridgeService pythonBridge,
            IOptions<VoiceSettings> voiceOptions)
        {
            _pythonBridge  = pythonBridge;
            _voiceSettings = voiceOptions.Value;

            // Forward Python bridge events so either engine can drive LUCY
            _pythonBridge.CommandRecognized  += (_, cmd)  => OnRecognizedText(cmd);
            _pythonBridge.WakeWordDetected   += (_, txt)  => OnRecognizedText(txt);
            _pythonBridge.VoiceStateChanged  += (_, st)   =>
            {
                // Only relay Python's state if C# synthesizer is not already speaking
                if (_synthesizer?.State != SynthesizerState.Speaking && !_isSpeaking)
                    VoiceStateChanged?.Invoke(this, st);
            };
            _pythonBridge.AudioLevelChanged  += (_, lvl) => AudioLevelChanged?.Invoke(this, lvl);

            InitializeTtsEngine();
            InitializeSttEngine();

            Log.Information("VoiceEngineService initialised — WakeWord: '{WakeWord}', TTS: {Tts}, STT: {Stt}",
                _voiceSettings.WakeWord, _voiceSettings.TtsEngine, _voiceSettings.SttEngine);
        }

        // ── TTS Initialisation ────────────────────────────────────────────────

        private void InitializeTtsEngine()
        {
            try
            {
                Log.Information("Initializing TTS engine...");
                _synthesizer = new SpeechSynthesizer();

                if (_synthesizer == null)
                {
                    Log.Error("CRITICAL: SpeechSynthesizer constructor returned null!");
                    return;
                }

                Log.Debug("SpeechSynthesizer created successfully");

                // Prefer the voice gender specified in appsettings.json
                try
                {
                    if (_voiceSettings.VoiceGender.Equals("Female", StringComparison.OrdinalIgnoreCase))
                        _synthesizer.SelectVoice("Microsoft Zira Desktop");
                    else
                        _synthesizer.SelectVoice("Microsoft David Desktop");
                    
                    Log.Debug("Selected voice: {Voice}", _synthesizer.Voice.Name);
                }
                catch (Exception voiceEx)
                {
                    Log.Warning(voiceEx, "Named voice selection failed, trying gender fallback");
                    // Named voice not installed — fall back to any voice with correct gender
                    try
                    {
                        var gender = _voiceSettings.VoiceGender.Equals("Female", StringComparison.OrdinalIgnoreCase)
                            ? VoiceGender.Female
                            : VoiceGender.Male;
                        _synthesizer.SelectVoiceByHints(gender, VoiceAge.Adult);
                        Log.Debug("Selected voice by gender: {Voice}", _synthesizer.Voice.Name);
                    }
                    catch (Exception genderEx)
                    {
                        Log.Warning(genderEx, "Gender voice selection failed, using system default");
                        /* use whatever voice Windows has */
                    }
                }

                // Apply rate and volume from config (-10..+10 and 0..100)
                _synthesizer.Rate   = Math.Clamp(_voiceSettings.SpeechRate, -10, 10);
                _synthesizer.Volume = Math.Clamp(_voiceSettings.VoiceVolume, 0, 100);

                // Relay speaking/ready state to MainViewModel
                _synthesizer.StateChanged += (_, e) =>
                {
                    string state = e.State == SynthesizerState.Speaking ? "Speaking" : "Listening";
                    VoiceStateChanged?.Invoke(this, state);
                    Log.Debug("TTS state changed: {State}", state);
                };

                Log.Information("TTS engine ready — voice: {Voice}, rate: {Rate}, vol: {Vol}",
                    _synthesizer.Voice?.Name ?? "default",
                    _voiceSettings.SpeechRate,
                    _voiceSettings.VoiceVolume);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CRITICAL: Failed to initialise SpeechSynthesizer — TTS will use Python fallback.");
                _synthesizer = null;
            }
        }

        // ── STT Initialisation ────────────────────────────────────────────────

        private void InitializeSttEngine()
        {
            try
            {
                // Always use en-US for the grammar engine
                var culture = new CultureInfo("en-US");

                try   { _recognizer = new SpeechRecognitionEngine(culture); }
                catch { _recognizer = new SpeechRecognitionEngine(); }

                try
                {
                    _recognizer.SetInputToDefaultAudioDevice();
                    _recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(5);
                    _recognizer.BabbleTimeout = TimeSpan.FromSeconds(0);
                    _recognizer.EndSilenceTimeout = TimeSpan.FromSeconds(1.2);
                    _recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

                    IsMicrophoneAvailable = true;
                    Log.Information("STT Engine bound successfully to default audio recording device (VAD EndSilenceTimeout: 1.2s).");
                }
                catch (Exception micEx)
                {
                    IsMicrophoneAvailable = false;
                    Log.Error(micEx, "MICROPHONE INPUT NOT DETECTED — Default audio device unavailable or disconnected.");
                    VoiceStateChanged?.Invoke(this, "MICROPHONE INPUT NOT DETECTED");
                }

                // ═══════════════════════════════════════════════════════════════
                //  HYBRID SPEECH RECOGNITION ENGINE (CommandChoices + Dictation)
                // ═══════════════════════════════════════════════════════════════
                //  1. High-priority CommandChoices grammar matches key phrases
                //     (Hello, Open Google Chrome, What time is it, Open Notepad, etc.) with ~95% confidence.
                //  2. Fallback DictationGrammar captures free-form custom sentences.
                // ═══════════════════════════════════════════════════════════════

                // 1. High-Priority Command Choices Grammar
                try
                {
                    var choices = new Choices(new string[]
                    {
                        "Hello", "Hi", "Hey", "Hello Lucy", "Hi Lucy", "Hey Lucy", "Lucy hello", "Lucy hi", "Lucy hey",
                        "Good morning", "Good afternoon", "Good evening", "Good morning Lucy", "Good afternoon Lucy", "Good evening Lucy",
                        "How are you", "Lucy how are you", "Lucy kem cho", "Lucy kaise ho", "Who are you", "What is your name",
                        "What can you do", "Help", "Lucy help", "Tell me a joke", "Joke",
                        "Lucy", "Lucy wake up", "Lucy resume listening", "Lucy stop listening", "Lucy sleep",
                        "Open Google Chrome", "Open Chrome", "Launch Google Chrome", "Launch Chrome", "Chrome kholo", "Google Chrome kholo", "Browser kholo", "Lucy open Chrome",
                        "Open VS Code", "Open Visual Studio", "Code kholo", "Visual Studio kholo", "Lucy open Visual Studio",
                        "Open Spotify", "Spotify kholo", "Play music", "Lucy open Spotify",
                        "Open WhatsApp", "WhatsApp kholo", "Lucy open WhatsApp",
                        "Open Calculator", "Calculator kholo", "Open Notepad", "Launch Notepad", "Notepad kholo", "Lucy open Notepad",
                        "Open YouTube", "YouTube kholo", "Lucy open YouTube",
                        "Play song on YouTube", "YouTube par song chalavo",
                        "System Status", "Status shu che", "PC status kya hai", "Check status",
                        "Take screenshot", "Screenshot lo", "Lucy take screenshot",
                        "Lock screen", "PC lock karo", "Lock PC", "Lucy lock screen",
                        "Shutdown computer", "Restart computer", "Delete downloads",
                        "Volume up", "Volume down", "Mute audio", "Volume 50 percent", "Task manager", "Empty recycle bin",
                        "Search Angular tutorial", "Lucy search Angular tutorial", "Search google",
                        "What time is it", "What is the time", "Tell me the time", "Lucy what time is it",
                        "What is the date", "What day is it", "Today date",
                        "Close it", "Close app", "Close application", "Close Chrome", "Close Google Chrome", "Close Visual Studio", "Close Notepad",
                        "What is dependency injection", "What is Angular", "What is dot net", "What is C sharp",
                        "What is OOP", "What is microservices", "What is REST API", "What is Docker", "What is Git", "What is SQL",
                        "Open home", "Open voice", "Open memory", "Open files", "Open browser", "Open vision", "Open automation", "Open apps", "Open settings", "Open developer", "Open control",
                        "Yes confirm"
                    });

                    var gb = new GrammarBuilder(choices) { Culture = _recognizer.RecognizerInfo.Culture };
                    var commandGrammar = new Grammar(gb) { Name = "CommandChoices", Weight = 1.0f };
                    _recognizer.LoadGrammar(commandGrammar);
                    Log.Information("CommandChoices grammar loaded successfully (Weight: 1.0).");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load CommandChoices grammar.");
                }

                // 2. Free-form Dictation Grammar Fallback
                try
                {
                    var dictationGrammar = new DictationGrammar { Name = "Dictation", Weight = 0.6f };
                    _recognizer.LoadGrammar(dictationGrammar);
                    Log.Information("DictationGrammar loaded successfully (Weight: 0.6).");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "DictationGrammar unavailable on this system.");
                }

                // Live Audio level → drives particle hologram animation
                _recognizer.AudioLevelUpdated += (_, e) =>
                {
                    if (!_isSpeaking && _synthesizer?.State != SynthesizerState.Speaking)
                    {
                        AudioLevelChanged?.Invoke(this, e.AudioLevel);
                    }
                };

                // Diagnostic Audio Events
                _recognizer.SpeechDetected += (_, e) =>
                {
                    if (!_isSpeaking)
                    {
                        Log.Information("STT MICROPHONE AUDIO SIGNAL DETECTED — User started speaking (Position: {Position})", e.AudioPosition);
                    }
                };

                _recognizer.SpeechHypothesized += (_, e) =>
                {
                    if (!_isSpeaking && e.Result != null && !string.IsNullOrWhiteSpace(e.Result.Text))
                    {
                        Log.Information("STT RAW HYPOTHESIS: '{Text}'", e.Result.Text);
                    }
                };

                // Speech Recognized with detailed diagnostic logging & transcript tracking
                _recognizer.SpeechRecognized += (_, e) =>
                {
                    if (e.Result == null || string.IsNullOrWhiteSpace(e.Result.Text)) return;

                    // Crucial: Ignore STT when LUCY is speaking to prevent self-listening feedback loop
                    if (_isSpeaking || _synthesizer?.State == SynthesizerState.Speaking)
                    {
                        Log.Debug("STT SpeechRecognized suppressed while LUCY is speaking: '{Text}'", e.Result.Text);
                        return;
                    }

                    float confidence = e.Result.Confidence;
                    string text = e.Result.Text;
                    string grammarName = e.Result.Grammar?.Name ?? "Unknown";

                    // RAW TRANSCRIPT LOGGING: Show exact transcript LUCY heard
                    Log.Information("STT RAW TRANSCRIPT CAPTURED: '{Text}' [Grammar: {Grammar}, Confidence: {Confidence:F2}]",
                        text, grammarName, confidence);

                    float minThreshold = Math.Min(_voiceSettings.SttConfidenceThreshold, 0.15f);

                    // For Dictation (free-form), require higher confidence and ignore short noise fragments
                    if (grammarName == "Dictation")
                    {
                        minThreshold = Math.Max(minThreshold, 0.35f);
                        string[] words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (words.Length == 1 && words[0].Length < 4 && confidence < 0.50f)
                        {
                            Log.Debug("STT Dictation noise fragment ignored: '{Text}' (Confidence: {Confidence:F2})", text, confidence);
                            return;
                        }
                    }

                    if (confidence >= minThreshold)
                    {
                        OnRecognizedText(text);
                    }
                    else
                    {
                        Log.Warning("STT Transcript ignored due to low confidence: {Confidence:F2} < {Threshold:F2} (Text: '{Text}')",
                            confidence, minThreshold, text);
                    }
                };

                _recognizer.SpeechRecognitionRejected += (_, e) =>
                {
                    if (_isSpeaking) return;
                    string text = e.Result != null ? e.Result.Text : "unrecognized audio";
                    float confidence = e.Result != null ? e.Result.Confidence : 0.0f;
                    Log.Debug("STT Audio noise rejected by recognizer: '{Text}' (Confidence: {Confidence:F2})", text, confidence);
                };

                _recognizer.AudioSignalProblemOccurred += (_, e) =>
                {
                    Log.Warning("STT Audio Signal Problem: {Problem} (Position: {Position})", e.AudioSignalProblem, e.AudioPosition);
                };

                // Auto-restart after each completed recognition cycle
                _recognizer.RecognizeCompleted += (_, _) =>
                {
                    if (_isListening)
                    {
                        try { _recognizer.RecognizeAsync(RecognizeMode.Multiple); }
                        catch { /* recognizer may have been disposed */ }
                    }
                };

                Log.Information("STT engine ready — confidence threshold: {Threshold}, debounce: {Debounce}ms",
                    _voiceSettings.SttConfidenceThreshold, _voiceSettings.SttDebounceMs);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "STT FAILED: Unable to initialize Speech Recognition Engine.");
                VoiceStateChanged?.Invoke(this, "STT FAILED");
                _recognizer = null;
            }
        }

        // ── Core Speech Processing ────────────────────────────────────────────

        /// <summary>
        /// Called whenever text is recognised (from either C# or Python engine).
        /// Applies debounce and fires VoiceCommandRecognized.
        /// </summary>
        private void OnRecognizedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 2) return;

            // Ignore recognition if LUCY is currently speaking
            if (_isSpeaking || _synthesizer?.State == SynthesizerState.Speaking)
            {
                Log.Debug("STT OnRecognizedText ignored while speaking: '{Text}'", text);
                return;
            }

            string cleaned = text.Trim().TrimEnd('.', '!', '?');

            // Debounce — ignore duplicates within configured window
            if ((DateTime.Now - _lastSpeechTime).TotalMilliseconds < _voiceSettings.SttDebounceMs)
            {
                Log.Debug("STT OnRecognizedText debounced: '{Text}' (Window: {Ms}ms)", cleaned, _voiceSettings.SttDebounceMs);
                return;
            }

            _lastSpeechTime = DateTime.Now;

            Log.Information("STT Command Accepted: '{Text}' — forwarding to MainViewModel", cleaned);

            VoiceStateChanged?.Invoke(this, "Thinking");
            VoiceCommandRecognized?.Invoke(this, cleaned);
        }

        // ── Public Methods ────────────────────────────────────────────────────

        public void StartListening()
        {
            if (_recognizer == null || _isListening) return;
            try
            {
                _isListening = true;
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                VoiceStateChanged?.Invoke(this, "Listening");
                Log.Information("Voice listening started.");
            }
            catch (Exception ex)
            {
                _isListening = false;
                Log.Error(ex, "Failed to start voice recognition.");
            }
        }

        public void StopListening()
        {
            if (_recognizer == null || !_isListening) return;
            try
            {
                _isListening = false;
                _recognizer.RecognizeAsyncStop();
                VoiceStateChanged?.Invoke(this, "Standby");
                Log.Information("Voice listening stopped.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping voice recognition.");
            }
        }

        /// <summary>
        /// Speak text using the Windows synthesizer.
        /// Runs on a background thread so the UI stays responsive.
        /// Falls back to Python pyttsx3 if the Windows synthesizer failed to load.
        /// </summary>
        public async Task SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Log.Warning("SpeakAsync called with empty text!");
                return;
            }

            Log.Information("TTS speaking: '{Text}' (length: {Length} chars)", text.Substring(0, Math.Min(text.Length, 50)), text.Length);

            _isSpeaking = true;
            try
            {
                if (_synthesizer != null)
                {
                    await Task.Run(() =>
                    {
                        try
                        {
                            _synthesizer.Speak(text);
                            Log.Debug("TTS completed successfully via SpeechSynthesizer");
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "SpeechSynthesizer error during Speak.");
                        }
                    });
                }
                else if (_pythonBridge.IsPythonRunning)
                {
                    Log.Information("Using Python fallback TTS");
                    await _pythonBridge.SpeakAsync(text);
                }
                else
                {
                    Log.Error("CRITICAL: No TTS engine available — cannot speak: {Text}", text);
                }
            }
            finally
            {
                // Small buffer delay to allow speaker audio in the room to dissipate before STT resumes
                await Task.Delay(400);
                _isSpeaking = false;
            }
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _synthesizer?.Dispose();
            _recognizer?.Dispose();
            _disposed = true;
        }
    }
}
