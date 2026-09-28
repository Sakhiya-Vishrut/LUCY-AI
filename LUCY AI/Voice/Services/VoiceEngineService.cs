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
        private bool _disposed;
        private DateTime _lastSpeechTime = DateTime.MinValue;

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
                if (_synthesizer?.State != SynthesizerState.Speaking)
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
                _synthesizer = new SpeechSynthesizer();

                // Prefer the voice gender specified in appsettings.json
                try
                {
                    if (_voiceSettings.VoiceGender.Equals("Female", StringComparison.OrdinalIgnoreCase))
                        _synthesizer.SelectVoice("Microsoft Zira Desktop");
                    else
                        _synthesizer.SelectVoice("Microsoft David Desktop");
                }
                catch
                {
                    // Named voice not installed — fall back to any voice with correct gender
                    try
                    {
                        var gender = _voiceSettings.VoiceGender.Equals("Female", StringComparison.OrdinalIgnoreCase)
                            ? VoiceGender.Female
                            : VoiceGender.Male;
                        _synthesizer.SelectVoiceByHints(gender, VoiceAge.Adult);
                    }
                    catch { /* use whatever voice Windows has */ }
                }

                // Apply rate and volume from config (-10..+10 and 0..100)
                _synthesizer.Rate   = Math.Clamp(_voiceSettings.SpeechRate, -10, 10);
                _synthesizer.Volume = Math.Clamp(_voiceSettings.VoiceVolume, 0, 100);

                // Relay speaking/ready state to MainViewModel
                _synthesizer.StateChanged += (_, e) =>
                {
                    string state = e.State == SynthesizerState.Speaking ? "Speaking" : "Listening";
                    VoiceStateChanged?.Invoke(this, state);
                };

                Log.Information("TTS engine ready — voice: {Voice}, rate: {Rate}, vol: {Vol}",
                    _synthesizer.Voice?.Name ?? "default",
                    _voiceSettings.SpeechRate,
                    _voiceSettings.VoiceVolume);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialise SpeechSynthesizer — TTS will use Python fallback.");
                _synthesizer = null;
            }
        }

        // ── STT Initialisation ────────────────────────────────────────────────

        private void InitializeSttEngine()
        {
            try
            {
                // Always use en-US for the grammar engine
                // (multi-language via Whisper is handled in a later phase)
                var culture = new CultureInfo("en-US");

                try   { _recognizer = new SpeechRecognitionEngine(culture); }
                catch { _recognizer = new SpeechRecognitionEngine(); }

                _recognizer.SetInputToDefaultAudioDevice();

                // ═══════════════════════════════════════════════════════════════
                //  FREE-FORM SPEECH RECOGNITION (DictationGrammar only)
                // ═══════════════════════════════════════════════════════════════
                //
                //  WHY THIS FIX SOLVES YOUR PROBLEM:
                //
                //    You said: "Hello Lucy open Chrome"
                //    Old grammar had: ["Hello Lucy", "Open Chrome"] as separate items
                //    Result: REJECTED because "Hello Lucy open Chrome" didn't match
                //
                //    New approach: Load ONLY DictationGrammar (free-form speech).
                //    Windows Speech recognises ANYTHING you say, then passes the
                //    full text to MainViewModel which does flexible keyword matching.
                //
                //    Now these ALL work:
                //      ✓ "open chrome"
                //      ✓ "lucy open chrome"
                //      ✓ "hello lucy open chrome"
                //      ✓ "hey lucy can you please open chrome"
                //
                //    The ViewModel checks `if (lower.Contains("open chrome"))`, so
                //    extra words don't break the command — just like Alexa/Siri.
                //
                //  TRADE-OFF:
                //    DictationGrammar is slightly less accurate than a fixed phrase
                //    list, but it never rejects valid commands due to extra words.
                //    For a JARVIS assistant, flexibility > 100% perfect accuracy.
                // ═══════════════════════════════════════════════════════════════

                try
                {
                    // Load free-form dictation grammar ONLY
                    var dictationGrammar = new DictationGrammar();
                    _recognizer.LoadGrammar(dictationGrammar);
                    Log.Information("DictationGrammar loaded — free-form speech recognition active.");
                }
                catch (Exception ex)
                {
                    // Rare: DictationGrammar unavailable on this Windows install
                    Log.Warning(ex, "DictationGrammar unavailable — using minimal fallback grammar.");

                    // Minimal fallback so LUCY isn't completely silent
                    var choices = new Choices(new string[]
                    {
                        "Lucy", "Hello", "Hi", "Open", "Close", "Chrome",
                        "What time", "Status", "Help", "Yes", "No"
                    });
                    var gb = new GrammarBuilder(choices) { Culture = _recognizer.RecognizerInfo.Culture };
                    _recognizer.LoadGrammar(new Grammar(gb));
                }

                // Audio level → drives the particle hologram animation
                _recognizer.AudioLevelUpdated += (_, e) =>
                    AudioLevelChanged?.Invoke(this, e.AudioLevel);

                // Confidence threshold from appsettings.json (default 0.2)
                _recognizer.SpeechRecognized += (_, e) =>
                {
                    if (e.Result != null
                        && !string.IsNullOrWhiteSpace(e.Result.Text)
                        && e.Result.Confidence >= _voiceSettings.SttConfidenceThreshold)
                    {
                        OnRecognizedText(e.Result.Text);
                    }
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
                Log.Error(ex, "Failed to initialise SpeechRecognitionEngine — STT disabled.");
                _recognizer = null;
            }
        }

        // ── Core Speech Processing ────────────────────────────────────────────

        /// <summary>
        /// Called whenever text is recognised (from either C# or Python engine).
        /// Applies debounce, cancels active TTS, then fires VoiceCommandRecognized.
        /// </summary>
        private void OnRecognizedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return;

            // Debounce — ignore duplicates within the configured window
            if ((DateTime.Now - _lastSpeechTime).TotalMilliseconds < _voiceSettings.SttDebounceMs)
                return;

            _lastSpeechTime = DateTime.Now;

            // If LUCY is speaking, stop her so she doesn't talk over the user
            if (_synthesizer?.State == SynthesizerState.Speaking)
            {
                try { _synthesizer.SpeakAsyncCancelAll(); }
                catch { /* already stopped */ }
            }

            VoiceStateChanged?.Invoke(this, "Thinking");
            VoiceCommandRecognized?.Invoke(this, text.Trim().TrimEnd('.', '!', '?'));
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
            if (string.IsNullOrWhiteSpace(text)) return;

            if (_synthesizer != null)
            {
                await Task.Run(() =>
                {
                    try   { _synthesizer.Speak(text); }
                    catch (Exception ex) { Log.Error(ex, "SpeechSynthesizer error during Speak."); }
                });
            }
            else if (_pythonBridge.IsPythonRunning)
            {
                // Python fallback TTS (pyttsx3 or espeak)
                await _pythonBridge.SpeakAsync(text);
            }
            else
            {
                Log.Warning("No TTS engine available — cannot speak: {Text}", text);
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
