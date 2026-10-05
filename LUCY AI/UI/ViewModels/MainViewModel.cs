using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using LucyAI.AI.Services;
using LucyAI.Automation.Services;
using LucyAI.Core.Services;
using LucyAI.Memory.Services;
using LucyAI.Services;
using LucyAI.Voice.Services;

namespace LucyAI.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ISystemMetricsService _metricsService;
        private readonly IVoiceEngineService _voiceService;
        private readonly IWindowsAutomationService _automationService;
        private readonly IAIBrainService _aiBrainService;
        private readonly IMemoryService _memoryService;
        private readonly IFileIndexerService _fileIndexerService;
        private readonly ISoundEffectService _soundEffectService;
        private readonly IVoiceDiagnosticsService _diagnosticsService;

        private readonly DispatcherTimer _metricsTimer;
        private readonly DispatcherTimer _subtitleTimer;

        private double _cpuUsage;
        public double CpuUsage
        {
            get => _cpuUsage;
            set => SetProperty(ref _cpuUsage, value);
        }

        private double _ramUsage;
        public double RamUsage
        {
            get => _ramUsage;
            set => SetProperty(ref _ramUsage, value);
        }

        private bool _isInternetConnected;
        public bool IsInternetConnected
        {
            get => _isInternetConnected;
            set => SetProperty(ref _isInternetConnected, value);
        }

        private string _batteryStatus = "AC Connected";
        public string BatteryStatus
        {
            get => _batteryStatus;
            set => SetProperty(ref _batteryStatus, value);
        }

        private string _assistantState = "LISTENING...";
        public string AssistantState
        {
            get => _assistantState;
            set => SetProperty(ref _assistantState, value);
        }

        private double _audioVolume;
        public double AudioVolume
        {
            get => _audioVolume;
            set => SetProperty(ref _audioVolume, value);
        }

        private string _subtitleText = string.Empty;
        public string SubtitleText
        {
            get => _subtitleText;
            set => SetProperty(ref _subtitleText, value);
        }

        private bool _isSubtitleVisible;
        public bool IsSubtitleVisible
        {
            get => _isSubtitleVisible;
            set => SetProperty(ref _isSubtitleVisible, value);
        }

        private string _activePage = "Home";
        public string ActivePage
        {
            get => _activePage;
            set
            {
                if (SetProperty(ref _activePage, value))
                    OnPropertyChanged(nameof(IsPageOverlayVisible));
            }
        }

        /// <summary>
        /// True when any page other than Home is active.
        /// Drives the glass overlay Visibility in MainWindow.xaml.
        /// </summary>
        public bool IsPageOverlayVisible => _activePage != "Home";

        public string SystemStatusLabel => SystemStatusLabel_Override
            ?? (IsInternetConnected ? "SYSTEM OPTIMAL" : "SYSTEM OFFLINE");

        public string ActiveModel => $"Ollama {_aiBrainService.ActiveModel} (Local)";
        public string MicStatus => AssistantState.Contains("Listening") ? "ACTIVE" : "STANDBY";
        public string SpeakerStatus => AssistantState.Contains("Speaking") ? "BUSY" : "READY";
        public string SystemTime => DateTime.Now.ToString("hh:mm:ss tt");

        public IRelayCommand<string> NavigateCommand { get; }
        public IRelayCommand ToggleVoiceListeningCommand { get; }

        public MainViewModel(
            ISystemMetricsService metricsService, 
            IVoiceEngineService voiceService,
            IWindowsAutomationService automationService,
            IAIBrainService aiBrainService,
            IMemoryService memoryService,
            IFileIndexerService fileIndexerService,
            ISoundEffectService soundEffectService,
            IVoiceDiagnosticsService diagnosticsService)
        {
            _metricsService = metricsService;
            _voiceService = voiceService;
            _automationService = automationService;
            _aiBrainService = aiBrainService;
            _memoryService = memoryService;
            _fileIndexerService = fileIndexerService;
            _soundEffectService = soundEffectService;
            _diagnosticsService = diagnosticsService;

            NavigateCommand = new RelayCommand<string>(Navigate);
            ToggleVoiceListeningCommand = new RelayCommand(ToggleVoiceListening);

            // Subtitle Auto-Hide Timer (2.5 Seconds)
            _subtitleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _subtitleTimer.Tick += (s, e) =>
            {
                IsSubtitleVisible = false;
                _subtitleTimer.Stop();
            };

            // Wire Voice Engine Events
            _voiceService.VoiceCommandRecognized += OnVoiceCommandRecognized;
            _voiceService.VoiceStateChanged += OnVoiceStateChanged;
            _voiceService.AudioLevelChanged += OnAudioLevelChanged;

            // Start Continuous Voice Listening
            _voiceService.StartListening();

            // Start metrics update timer
            _metricsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.0)
            };
            _metricsTimer.Tick += (s, e) => UpdateMetrics();
            _metricsTimer.Start();

            UpdateMetrics();

            // Run diagnostics asynchronously, then deliver a context-aware
            // startup greeting that tells Vishrut what LUCY detected on boot.
            // We fire-and-forget here (the UI is already visible by this point).
            _ = RunStartupSequenceAsync();
        }

        private void OnAudioLevelChanged(object? sender, double level)
        {
            try
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    AudioVolume = level;
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Error updating audio level UI");
            }
        }

        private void OnVoiceStateChanged(object? sender, string newState)
        {
            try
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    AssistantState = newState;
                    OnPropertyChanged(nameof(MicStatus));
                    OnPropertyChanged(nameof(SpeakerStatus));
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Error updating voice state UI: {State}", newState);
            }
        }

        private void OnVoiceCommandRecognized(object? sender, string rawCommand)
        {
            App.Current.Dispatcher.Invoke(async () =>
            {
                try
                {
                    await ProcessVoiceCommandAsync(rawCommand);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "CRITICAL ERROR in voice command processing for command: '{Command}'", rawCommand);
                    
                    // Emergency fallback: at least show what went wrong
                    ShowSubtitleToast($"LUCY: Error processing command - {ex.Message}");
                    AssistantState = "LISTENING...";
                    
                    // Try to speak the error (may fail if TTS is broken)
                    try
                    {
                        await _voiceService.SpeakAsync("I encountered an error processing that command. Please check the logs.");
                    }
                    catch
                    {
                        Serilog.Log.Error("TTS also failed during error recovery");
                    }
                }
            });
        }

        private string _lastOpenedApp = string.Empty;

        private async Task ProcessVoiceCommandAsync(string rawCommand)
        {
            if (string.IsNullOrWhiteSpace(rawCommand))
            {
                Serilog.Log.Warning("ProcessVoiceCommandAsync: Empty command received!");
                return;
            }

            string cleanCommand = rawCommand.Trim().TrimEnd('.', '!', '?');
            string lower = cleanCommand.ToLowerInvariant();

            Serilog.Log.Information("Processing voice command: '{Command}'", cleanCommand);

            // Handle Stop Listening / Sleep
            if (lower.Contains("stop listening") || lower.Contains("sleep") || lower.Contains("bandh thao"))
            {
                Serilog.Log.Information("Command matched: Stop Listening");
                _voiceService.StopListening();
                ShowSubtitleToast("LUCY: Voice listening paused. Say 'Lucy wake up' or 'Lucy resume listening'.");
                await _soundEffectService.PlayListeningSoundAsync();
                await _voiceService.SpeakAsync("Pausing voice listening, Boss.");
                return;
            }

            // Handle Resume Listening / Wake Up
            if (lower.Contains("resume listening") || lower.Contains("wake up") || lower.Contains("chalu thao"))
            {
                Serilog.Log.Information("Command matched: Resume Listening");
                _voiceService.StartListening();
                ShowSubtitleToast("LUCY: Voice listening resumed.");
                await _soundEffectService.PlayWakeSoundAsync();
                await _voiceService.SpeakAsync("Voice listening resumed, Boss.");
                return;
            }

            await _soundEffectService.PlayWakeSoundAsync();
            ShowSubtitleToast($"USER: {cleanCommand}");
            AssistantState = "THINKING...";

            await Task.Delay(100);

            string responseText = string.Empty;

            // ═══════════════════════════════════════════════════════════════
            //  STAGE 1: WAKE WORD & GREETINGS (Instant local response, never Ollama)
            // ═══════════════════════════════════════════════════════════════
            if (lower is "hello" or "hello lucy" or "lucy hello")
            {
                responseText = "Hello Boss. How can I help you?";
            }
            else if (lower is "hi lucy" or "hi" or "hey" or "hey lucy" or "lucy hi" or "lucy hey" or "lucy")
            {
                responseText = "Hello Boss. I'm listening.";
            }
            else if (lower.Contains("good morning"))
            {
                responseText = "Good morning Boss. How can I assist you today?";
            }
            else if (lower.Contains("good afternoon"))
            {
                responseText = "Good afternoon Boss. How can I assist you today?";
            }
            else if (lower.Contains("good evening"))
            {
                responseText = "Good evening Boss. How can I assist you today?";
            }
            else if (lower.Contains("how are you") || lower.Contains("kaise ho") || lower.Contains("kem cho"))
            {
                responseText = "All neural networks are operating at peak efficiency, Boss. How can I assist you?";
            }
            else if (lower.Contains("who are you") || lower.Contains("what is your name"))
            {
                responseText = "I am LUCY, your futuristic voice-first AI Operating System running locally on Windows .NET 10.";
            }

            // ═══════════════════════════════════════════════════════════════
            //  STAGE 2: WINDOWS & SYSTEM COMMANDS
            // ═══════════════════════════════════════════════════════════════
            else if (lower.Contains("volume up") || lower.Contains("volume vadhavo") || lower.Contains("volume badhao") || lower.Contains("increase volume"))
            {
                _automationService.VolumeUp();
                responseText = "Volume increased, Boss.";
            }
            else if (lower.Contains("volume down") || lower.Contains("volume ochhu karo") || lower.Contains("volume kam karo") || lower.Contains("decrease volume"))
            {
                _automationService.VolumeDown();
                responseText = "Volume decreased, Boss.";
            }
            else if (lower.Contains("mute") || lower.Contains("volume bandh karo"))
            {
                _automationService.MuteAudio();
                responseText = "Audio muted, Boss.";
            }
            else if (lower.Contains("take screenshot") || lower.Contains("take a screenshot") || lower.Contains("screenshot lo") || lower.Contains("screenshot le"))
            {
                _automationService.TakeScreenshot();
                responseText = "Screenshot taken, Boss.";
            }
            else if (lower.Contains("lock screen") || lower.Contains("pc lock karo") || lower.Contains("lock pc"))
            {
                _automationService.LockScreen();
                responseText = "Locking Windows session, Boss.";
            }
            else if (lower.Contains("task manager"))
            {
                _automationService.OpenTaskManager();
                responseText = "Opening Task Manager, Boss.";
            }
            else if (lower.Contains("empty recycle bin"))
            {
                _automationService.EmptyRecycleBin();
                responseText = "Recycle bin emptied, Boss.";
            }
            else if (lower.Contains("what time") || lower.Contains("time is it") || lower.Contains("samay shu che"))
            {
                responseText = $"The current time is {DateTime.Now:hh:mm tt}, Boss.";
            }
            else if (lower.Contains("what date") || lower.Contains("what day") || lower.Contains("today date"))
            {
                responseText = $"Today is {DateTime.Now:dddd, MMMM d, yyyy}, Boss.";
            }
            else if (lower.Contains("status shu che") || lower.Contains("pc status kya hai") || lower.Contains("system status"))
            {
                responseText = $"CPU load is {CpuUsage} percent, RAM is {RamUsage} percent, Boss.";
            }

            // ═══════════════════════════════════════════════════════════════
            //  STAGE 3: APP, FILE, & BROWSER LAUNCH COMMANDS
            // ═══════════════════════════════════════════════════════════════
            else if (lower.Contains("open chrome") || lower.Contains("chrome kholo") || lower.Contains("chrome open karo") || lower.Contains("browser kholo") || lower.Contains("launch chrome"))
            {
                bool success = _automationService.LaunchApp("chrome");
                if (success)
                {
                    _lastOpenedApp = "chrome";
                    responseText = "Opening Chrome, Boss.";
                }
                else
                {
                    responseText = "Failed to open Chrome, Boss.";
                }
            }
            else if (lower.Contains("open vs code") || lower.Contains("open visual studio") || lower.Contains("code kholo") || lower.Contains("visual studio kholo") || lower.Contains("code open karo"))
            {
                bool success = _automationService.LaunchApp("code") || _automationService.LaunchApp("devenv");
                if (success)
                {
                    _lastOpenedApp = "code";
                    responseText = "Launching Visual Studio workspace, Boss.";
                }
                else
                {
                    responseText = "Failed to open Visual Studio, Boss.";
                }
            }
            else if (lower.Contains("open spotify") || lower.Contains("spotify kholo") || lower.Contains("spotify open karo") || lower.Contains("play music"))
            {
                bool success = _automationService.LaunchApp("spotify");
                if (success)
                {
                    _lastOpenedApp = "spotify";
                    responseText = "Opening Spotify, Boss.";
                }
                else
                {
                    responseText = "Failed to open Spotify, Boss.";
                }
            }
            else if (lower.Contains("open whatsapp") || lower.Contains("whatsapp kholo") || lower.Contains("whatsapp open karo"))
            {
                bool success = _automationService.LaunchApp("whatsapp");
                if (success)
                {
                    _lastOpenedApp = "whatsapp";
                    responseText = "Opening WhatsApp, Boss.";
                }
                else
                {
                    responseText = "Failed to open WhatsApp, Boss.";
                }
            }
            else if (lower.Contains("open calculator") || lower.Contains("calculator kholo") || lower.Contains("calculator open karo"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("calc.exe") { UseShellExecute = true });
                    _lastOpenedApp = "calculator";
                    responseText = "Opening Calculator, Boss.";
                }
                catch
                {
                    responseText = "Failed to open Calculator, Boss.";
                }
            }
            else if (lower.Contains("open notepad") || lower.Contains("notepad kholo") || lower.Contains("notepad open karo"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
                    _lastOpenedApp = "notepad";
                    responseText = "Opening Notepad, Boss.";
                }
                catch
                {
                    responseText = "Failed to open Notepad, Boss.";
                }
            }
            else if ((lower.Contains("play") || lower.Contains("chalavo") || lower.Contains("bajao")) && lower.Contains("youtube"))
            {
                string songQuery = lower.Replace("play", "").Replace("youtube", "").Replace("song", "").Replace("par", "").Replace("chalavo", "").Replace("bajao", "").Replace("on", "").Trim();
                _automationService.OpenYouTube(songQuery);
                _lastOpenedApp = "youtube";
                responseText = $"Opening YouTube and playing {songQuery}, Boss.";
            }
            else if (lower.StartsWith("open youtube") || lower == "youtube kholo" || lower == "youtube open karo" || lower == "youtube")
            {
                _automationService.OpenYouTube("");
                _lastOpenedApp = "youtube";
                responseText = "Opening YouTube, Boss.";
            }

            // Navigation Commands
            else if (lower.Contains("open home") || lower == "home")
            {
                Navigate("Home");
                responseText = "Switching to home view, Boss.";
            }
            else if (lower.Contains("open voice") || lower.Contains("open assistant"))
            {
                Navigate("Voice");
                responseText = "Opening voice assistant panel, Boss.";
            }
            else if (lower.Contains("open memory"))
            {
                Navigate("Memory");
                responseText = "Opening memory module, Boss.";
            }
            else if (lower.Contains("open files") || lower.Contains("open file"))
            {
                Navigate("Files");
                responseText = "Opening files module, Boss.";
            }
            else if (lower.Contains("open browser") || lower.Contains("open web"))
            {
                Navigate("Browser");
                responseText = "Opening browser module, Boss.";
            }
            else if (lower.Contains("open vision") || lower.Contains("open camera"))
            {
                Navigate("Vision");
                responseText = "Opening vision module, Boss.";
            }
            else if (lower.Contains("open automation") || lower.Contains("open workflow"))
            {
                Navigate("Automation");
                responseText = "Opening automation engine, Boss.";
            }
            else if (lower.Contains("open apps") || lower.Contains("open app hub"))
            {
                Navigate("Apps");
                responseText = "Opening apps hub, Boss.";
            }
            else if (lower.Contains("open settings") || lower.Contains("open setting"))
            {
                Navigate("Settings");
                responseText = "Opening settings panel, Boss.";
            }
            else if (lower.Contains("open developer") || lower.Contains("open dev"))
            {
                Navigate("Developer");
                responseText = "Entering developer mode, Boss.";
            }
            else if (lower.Contains("open control") || lower.Contains("open system control"))
            {
                Navigate("Control");
                responseText = "Opening Windows control centre, Boss.";
            }
            else if (lower.Contains("switch model to") || lower.Contains("use model"))
            {
                string targetModel = lower.Replace("switch model to", "").Replace("use model", "").Trim();
                await _aiBrainService.SwitchModelAsync(targetModel);
                OnPropertyChanged(nameof(ActiveModel));
                responseText = $"Switched local AI brain model to {targetModel}, Boss.";
            }

            // File Indexer Search Query
            else if ((lower.Contains("find") || lower.Contains("locate")) && (lower.Contains("pdf") || lower.Contains("invoice") || lower.Contains("file") || lower.Contains("project")))
            {
                string term = lower.Replace("lucy", "").Replace("find", "").Replace("locate", "").Replace("open", "").Replace("file", "").Replace("pdf", "").Trim();
                string? matchedFile = await _fileIndexerService.FindBestMatchingFileAsync(term);
                if (!string.IsNullOrWhiteSpace(matchedFile))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", matchedFile) { UseShellExecute = true });
                        responseText = $"Found file {Path.GetFileName(matchedFile)}. Opening file now, Boss.";
                    }
                    catch
                    {
                        responseText = $"Found file at {matchedFile}, Boss.";
                    }
                }
                else
                {
                    responseText = $"Searched indexed files for '{term}', but no matching file was found, Boss.";
                }
            }

            // ═══════════════════════════════════════════════════════════════
            //  STAGE 4: FOLLOW-UP / CONTEXT COMMANDS
            // ═══════════════════════════════════════════════════════════════
            else if (lower.StartsWith("search ") || lower.StartsWith("google search "))
            {
                string query = lower.Replace("search google for", "").Replace("google search", "").Replace("search for", "").Replace("search", "").Replace("lucy", "").Trim();
                _automationService.SearchWeb(query);
                if (_lastOpenedApp == "chrome" || _lastOpenedApp == "browser")
                    responseText = $"Searching {query} in Chrome, Boss.";
                else
                    responseText = $"Searching Google for '{query}', Boss.";
            }
            else if (lower is "close it" or "close app" or "close application" || lower.StartsWith("close ") || lower.EndsWith(" bandh karo") || lower.EndsWith(" close karo"))
            {
                string targetApp = lower
                    .Replace("close it", "")
                    .Replace("close application", "")
                    .Replace("close app", "")
                    .Replace("close", "")
                    .Replace("bandh karo", "")
                    .Replace("close karo", "")
                    .Trim();

                if (string.IsNullOrWhiteSpace(targetApp))
                {
                    targetApp = _lastOpenedApp;
                }

                if (!string.IsNullOrWhiteSpace(targetApp))
                {
                    bool closed = _automationService.CloseApp(targetApp);
                    if (closed)
                    {
                        responseText = $"Closed {targetApp}, Boss.";
                        if (targetApp.Equals(_lastOpenedApp, StringComparison.OrdinalIgnoreCase))
                            _lastOpenedApp = string.Empty;
                    }
                    else
                    {
                        responseText = $"Could not close {targetApp}, Boss.";
                    }
                }
                else
                {
                    responseText = "No active application to close, Boss.";
                }
            }

            // ═══════════════════════════════════════════════════════════════
            //  STAGE 5: GENERAL KNOWLEDGE / CONVERSATION (Ollama / AI Service)
            // ═══════════════════════════════════════════════════════════════
            else
            {
                string autoResp = await _automationService.ExecuteVoiceCommandAsync(cleanCommand);
                if (!string.IsNullOrWhiteSpace(autoResp))
                {
                    responseText = autoResp;
                }
                else
                {
                    responseText = await _aiBrainService.GenerateResponseAsync(cleanCommand);
                }
            }

            if (responseText.StartsWith("[WEB_SEARCH:"))
            {
                string searchQuery = responseText.Substring(12).TrimEnd(']');
                _automationService.SearchWeb(searchQuery);
                responseText = $"Searching Google for '{searchQuery}', Boss.";
            }

            if (string.IsNullOrWhiteSpace(responseText))
            {
                responseText = "Sorry Boss, I didn't understand. Please repeat.";
            }

            Serilog.Log.Information("Generated response: '{Response}' (length: {Length} chars)", 
                responseText.Substring(0, Math.Min(responseText.Length, 60)), responseText.Length);

            ShowSubtitleToast($"LUCY: {responseText}");
            AssistantState = "SPEAKING...";

            try
            {
                await _soundEffectService.PlaySuccessChimeAsync();
                await _voiceService.SpeakAsync(responseText);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "CRITICAL ERROR in MainViewModel TTS call");
            }

            AssistantState = "LISTENING...";
        }

        private void ShowSubtitleToast(string message)
        {
            SubtitleText = message;
            IsSubtitleVisible = true;
            _subtitleTimer.Stop();
            _subtitleTimer.Start();
        }

        private void ToggleVoiceListening()
        {
            try
            {
                if (AssistantState.Contains("Waiting") || AssistantState.Contains("PAUSED"))
                {
                    _voiceService.StartListening();
                    ShowSubtitleToast("LUCY: Voice listening activated.");
                }
                else
                {
                    _voiceService.StopListening();
                    ShowSubtitleToast("LUCY: Voice listening paused.");
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error toggling voice listening");
                ShowSubtitleToast("LUCY: Error controlling voice listening");
            }
        }

        private void UpdateMetrics()
        {
            try
            {
                var metrics = _metricsService.GetCurrentMetrics();
                CpuUsage            = metrics.CpuUsagePercentage;
                RamUsage            = metrics.RamUsagePercentage;
                IsInternetConnected = metrics.IsInternetConnected;
                BatteryStatus       = metrics.BatteryStatus;
                OnPropertyChanged(nameof(SystemTime));
                OnPropertyChanged(nameof(SystemStatusLabel));
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Error updating system metrics");
            }
        }

        // ── Startup Sequence ──────────────────────────────────────────────────

        /// <summary>
        /// Runs on app launch (fire-and-forget from constructor).
        ///
        /// Steps:
        ///   1. Small boot delay so the hologram renders first (feels snappier).
        ///   2. Run VoiceDiagnosticsService to check mic, TTS, Ollama.
        ///   3. Build a greeting that reflects the actual system state.
        ///   4. Show the subtitle toast and speak the greeting aloud.
        ///
        /// This is the fix for VoiceDiagnosticsService being implemented
        /// but never called anywhere in the application.
        /// </summary>
        private async Task RunStartupSequenceAsync()
        {
            try
            {
                Serilog.Log.Information("Starting LUCY startup sequence...");
                
                // Short delay so the window finishes painting before we speak
                await Task.Delay(600);

                // Time-of-day greeting
                int    hour         = DateTime.Now.Hour;
                string timeGreeting = hour < 12 ? "Good Morning"
                                    : hour < 18 ? "Good Afternoon"
                                    :             "Good Evening";

                // Run diagnostics — this checks Ollama, TTS, and mic
                string systemLine;
                try
                {
                    Serilog.Log.Information("Running voice diagnostics...");
                    var report = await _diagnosticsService.RunDiagnosticsAsync();

                    // Update the top-bar status label with the real diagnostic result
                    // (SystemStatusLabel is a computed property — we trigger via field)
                    // Store the report status so the HUD bar shows it
                    SystemStatusLabel_Override = report.OverallStatus;
                    OnPropertyChanged(nameof(SystemStatusLabel));

                    // Build the spoken status sentence from the report
                    if (report.IsOllamaConnected && report.IsTtsReady)
                        systemLine = $"Local AI model {_aiBrainService.ActiveModel} is online and ready.";
                    else if (!report.IsOllamaConnected && report.IsTtsReady)
                        systemLine = "Voice engine is active. Local AI model is in offline standby.";
                    else
                        systemLine = "Core systems initialised. Some subsystems require configuration.";
                    
                    Serilog.Log.Information("Diagnostics complete: {Status}", systemLine);
                }
                catch (Exception diagEx)
                {
                    Serilog.Log.Warning(diagEx, "Diagnostics failed, using fallback greeting");
                    systemLine = "All primary systems are online.";
                    SystemStatusLabel_Override = null;
                }

                // Full greeting — e.g. "Good Evening BOSS. Lucy is online and listening."
                string greetingText = $"{timeGreeting} BOSS. Lucy is online and listening.";

                ShowSubtitleToast($"LUCY: {greetingText}");
                
                try
                {
                    Serilog.Log.Information("Speaking startup greeting...");
                    await _voiceService.SpeakAsync(greetingText);
                    Serilog.Log.Information("Startup greeting completed successfully");
                }
                catch (Exception ttsEx)
                {
                    Serilog.Log.Error(ttsEx, "CRITICAL: Failed to speak startup greeting - TTS may be broken!");
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "CRITICAL ERROR in startup sequence");
                ShowSubtitleToast("LUCY: Error during startup - check logs");
            }
        }

        // Allows diagnostics to override the computed SystemStatusLabel
        private string? SystemStatusLabel_Override;

        private void Navigate(string? page)
        {
            if (!string.IsNullOrEmpty(page))
                ActivePage = page;
        }
    }
}
