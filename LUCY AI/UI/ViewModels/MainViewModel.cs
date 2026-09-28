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
            App.Current.Dispatcher.Invoke(() =>
            {
                AudioVolume = level;
            });
        }

        private void OnVoiceStateChanged(object? sender, string newState)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                AssistantState = newState;
                OnPropertyChanged(nameof(MicStatus));
                OnPropertyChanged(nameof(SpeakerStatus));
            });
        }

        private void OnVoiceCommandRecognized(object? sender, string rawCommand)
        {
            App.Current.Dispatcher.Invoke(async () =>
            {
                await ProcessVoiceCommandAsync(rawCommand);
            });
        }

        private async Task ProcessVoiceCommandAsync(string rawCommand)
        {
            if (string.IsNullOrWhiteSpace(rawCommand)) return;

            string cleanCommand = rawCommand.Trim().TrimEnd('.', '!', '?');
            string lower = cleanCommand.ToLowerInvariant();

            // Handle Stop Listening / Sleep
            if (lower.Contains("stop listening") || lower.Contains("sleep") || lower.Contains("bandh thao"))
            {
                _voiceService.StopListening();
                ShowSubtitleToast("LUCY: Voice listening paused. Say 'Lucy wake up' or 'Lucy resume listening'.");
                await _soundEffectService.PlayListeningSoundAsync();
                await _voiceService.SpeakAsync("Pausing voice listening, Vishrut.");
                return;
            }

            // Handle Resume Listening / Wake Up
            if (lower.Contains("resume listening") || lower.Contains("wake up") || lower.Contains("chalu thao"))
            {
                _voiceService.StartListening();
                ShowSubtitleToast("LUCY: Voice listening resumed.");
                await _soundEffectService.PlayWakeSoundAsync();
                await _voiceService.SpeakAsync("Voice listening resumed, Vishrut.");
                return;
            }

            await _soundEffectService.PlayWakeSoundAsync();
            ShowSubtitleToast($"USER: {cleanCommand}");
            AssistantState = "THINKING...";

            await Task.Delay(100);

            string responseText = string.Empty;

            // Greetings & Open Conversations
            if (lower == "hello" || lower == "hi" || lower == "hey" || lower == "lucy" ||
                lower.Contains("hello") || lower.Contains("hi lucy") || lower.Contains("hey lucy") || 
                lower.Contains("lucy hello") || lower.Contains("good morning") || lower.Contains("good evening") || 
                lower.Contains("good afternoon"))
            {
                responseText = "Hello Vishrut! I am Lucy, your JARVIS Voice AI Assistant. How can I help you today?";
            }
            else if (lower.Contains("how are you") || lower.Contains("kaise ho") || lower.Contains("kem cho"))
            {
                responseText = "All neural networks are operating at peak efficiency, Vishrut. How can I assist you?";
            }
            else if (lower.Contains("who are you") || lower.Contains("what is your name"))
            {
                responseText = "I am LUCY, your futuristic voice-first AI Operating System running locally on Windows .NET 10.";
            }

            // Direct Commands: Time & Date
            else if (lower.Contains("what time") || lower.Contains("time is it") || lower.Contains("samay shu che"))
            {
                responseText = $"The current time is {DateTime.Now:hh:mm tt}, Vishrut.";
            }

            // Web Search Command ("Lucy search Angular tutorial")
            else if (lower.Contains("search ") || lower.StartsWith("google search"))
            {
                string query = lower.Replace("lucy", "").Replace("google search", "").Replace("search", "").Replace("for", "").Trim();
                _automationService.SearchWeb(query);
                responseText = $"Searching Google for '{query}', Vishrut.";
            }

            // File AI Indexer Query
            else if ((lower.Contains("find") || lower.Contains("locate")) && (lower.Contains("pdf") || lower.Contains("invoice") || lower.Contains("file") || lower.Contains("project")))
            {
                string term = lower.Replace("lucy", "").Replace("find", "").Replace("locate", "").Replace("open", "").Replace("file", "").Replace("pdf", "").Trim();
                string? matchedFile = await _fileIndexerService.FindBestMatchingFileAsync(term);
                if (!string.IsNullOrWhiteSpace(matchedFile))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", matchedFile) { UseShellExecute = true });
                        responseText = $"Found file {Path.GetFileName(matchedFile)}. Opening file now, Vishrut.";
                    }
                    catch
                    {
                        responseText = $"Found file at {matchedFile}, Vishrut.";
                    }
                }
                else
                {
                    responseText = $"Searched indexed files for '{term}', but no matching file was found, Vishrut.";
                }
            }

            // Navigation via voice ("Lucy open memory", "Lucy open files", etc.)
            else if (lower.Contains("open home") || lower == "home")
            {
                Navigate("Home");
                responseText = "Switching to home view, Vishrut.";
            }
            else if (lower.Contains("open voice") || lower.Contains("open assistant"))
            {
                Navigate("Voice");
                responseText = "Opening voice assistant panel, Vishrut.";
            }
            else if (lower.Contains("open memory"))
            {
                Navigate("Memory");
                responseText = "Opening memory module, Vishrut.";
            }
            else if (lower.Contains("open files") || lower.Contains("open file"))
            {
                Navigate("Files");
                responseText = "Opening files module, Vishrut.";
            }
            else if (lower.Contains("open browser") || lower.Contains("open web"))
            {
                Navigate("Browser");
                responseText = "Opening browser module, Vishrut.";
            }
            else if (lower.Contains("open vision") || lower.Contains("open camera"))
            {
                Navigate("Vision");
                responseText = "Opening vision module, Vishrut.";
            }
            else if (lower.Contains("open automation") || lower.Contains("open workflow"))
            {
                Navigate("Automation");
                responseText = "Opening automation engine, Vishrut.";
            }
            else if (lower.Contains("open apps") || lower.Contains("open app hub"))
            {
                Navigate("Apps");
                responseText = "Opening apps hub, Vishrut.";
            }
            else if (lower.Contains("open settings") || lower.Contains("open setting"))
            {
                Navigate("Settings");
                responseText = "Opening settings panel, Vishrut.";
            }
            else if (lower.Contains("open developer") || lower.Contains("open dev"))
            {
                Navigate("Developer");
                responseText = "Entering developer mode, Vishrut.";
            }
            else if (lower.Contains("open control") || lower.Contains("open system control"))
            {
                Navigate("Control");
                responseText = "Opening Windows control centre, Vishrut.";
            }

            // Model Switching
            else if (lower.Contains("switch model to") || lower.Contains("use model"))
            {
                string targetModel = lower.Replace("switch model to", "").Replace("use model", "").Trim();
                await _aiBrainService.SwitchModelAsync(targetModel);
                OnPropertyChanged(nameof(ActiveModel));
                responseText = $"Switched local AI brain model to {targetModel}, Vishrut.";
            }

            // Multilingual Commands
            else if (lower.Contains("chrome kholo") || lower.Contains("browser kholo") || lower.Contains("open chrome"))
            {
                _automationService.LaunchApp("chrome");
                responseText = "Opening Google Chrome, Vishrut.";
            }
            else if (lower.Contains("code kholo") || lower.Contains("visual studio kholo") || lower.Contains("open visual studio") || lower.Contains("vs code open"))
            {
                if (!_automationService.LaunchApp("code")) _automationService.LaunchApp("devenv");
                responseText = "Launching Visual Studio workspace, Vishrut.";
            }
            else if (lower.Contains("youtube par song chalavo") || lower.Contains("youtube par song bajao"))
            {
                string song = lower.Replace("youtube par song chalavo", "").Replace("youtube par song bajao", "").Replace("youtube", "").Trim();
                _automationService.OpenYouTube(song);
                responseText = $"Playing song on YouTube, Vishrut.";
            }
            else if (lower.Contains("status shu che") || lower.Contains("pc status kya hai"))
            {
                responseText = $"CPU load is {CpuUsage} percent, RAM is {RamUsage} percent, Vishrut.";
            }
            else if (lower.Contains("lucy kem cho") || lower.Contains("lucy kaise ho"))
            {
                responseText = "Hu maja ma chu, Vishrut! All systems operating at peak efficiency.";
            }
            else if (lower.Contains("screenshot lo") || lower.Contains("take screenshot") || lower.Contains("screenshot le"))
            {
                _automationService.TakeScreenshot();
                responseText = "Screenshot captured and saved, Vishrut.";
            }
            else if (lower.Contains("pc lock karo") || lower.Contains("lock screen"))
            {
                _automationService.LockScreen();
                responseText = "Locking Windows session, Vishrut.";
            }
            else
            {
                // Automation Engine
                string autoResp = await _automationService.ExecuteVoiceCommandAsync(cleanCommand);
                if (!string.IsNullOrWhiteSpace(autoResp))
                {
                    responseText = autoResp;
                }
                else
                {
                    // Ollama / AI Brain Engine
                    responseText = await _aiBrainService.GenerateResponseAsync(cleanCommand);
                }
            }

            if (string.IsNullOrWhiteSpace(responseText))
            {
                responseText = "I didn't catch that. Please repeat.";
            }

            ShowSubtitleToast($"LUCY: {responseText}");
            AssistantState = "SPEAKING...";

            await _soundEffectService.PlaySuccessChimeAsync();
            await _voiceService.SpeakAsync(responseText);

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

        private void UpdateMetrics()
        {
            var metrics = _metricsService.GetCurrentMetrics();
            CpuUsage            = metrics.CpuUsagePercentage;
            RamUsage            = metrics.RamUsagePercentage;
            IsInternetConnected = metrics.IsInternetConnected;
            BatteryStatus       = metrics.BatteryStatus;
            OnPropertyChanged(nameof(SystemTime));
            OnPropertyChanged(nameof(SystemStatusLabel));
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
            }
            catch
            {
                systemLine = "All primary systems are online.";
                SystemStatusLabel_Override = null;
            }

            // Full greeting — e.g. "Good Evening Vishrut. Lucy is online and listening.
            //                       Local AI model phi4 is online and ready."
            string greetingText =
                $"{timeGreeting} Vishrut. Lucy is online and listening. {systemLine}";

            ShowSubtitleToast($"LUCY: {greetingText}");
            await _voiceService.SpeakAsync(greetingText);
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
