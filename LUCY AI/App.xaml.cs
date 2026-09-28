using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using LucyAI.Configuration;
using LucyAI.Core.Services;
using LucyAI.Infrastructure.Services;
using LucyAI.Voice.Services;
using LucyAI.Automation.Services;
using LucyAI.AI.Services;
using LucyAI.Memory.Services;
using LucyAI.Services;
using LucyAI.UI.ViewModels;
using LucyAI.UI.Views;

namespace LucyAI
{
    public partial class App : Application
    {
        private readonly IHost _host;

        public App()
        {
            string basePath = AppContext.BaseDirectory;

            // ── Serilog: initialise before the host so early errors are captured ──
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(basePath, "Logs", "lucy-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,          // keep 30 days of logs
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("═══════════════════════════════════════════════════");
            Log.Information("  LUCY AI — JARVIS Desktop Assistant  (.NET 10)   ");
            Log.Information("═══════════════════════════════════════════════════");

            _host = Host.CreateDefaultBuilder()
                // ── 1. Configuration ─────────────────────────────────────────
                .ConfigureAppConfiguration((_, builder) =>
                {
                    builder.SetBasePath(basePath);
                    builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    // Future: builder.AddJsonFile("appsettings.secrets.json", optional: true);
                })

                // ── 2. Serilog provider ──────────────────────────────────────
                .UseSerilog()

                // ── 3. Services ──────────────────────────────────────────────
                .ConfigureServices((context, services) =>
                {
                    RegisterSettings(context.Configuration, services);
                    RegisterCoreServices(services);
                    RegisterUi(services);
                })
                .Build();
        }

        // ── Settings Registration ─────────────────────────────────────────────
        //
        // IOptions<T> EXPLAINED FOR BEGINNERS:
        //
        //   services.Configure<OllamaSettings>(config.GetSection("Ollama"))
        //   tells DI: "whenever someone asks for IOptions<OllamaSettings>,
        //   create an OllamaSettings object and fill it from the 'Ollama'
        //   section of appsettings.json."
        //
        //   The service then receives it like:
        //     public MyService(IOptions<OllamaSettings> opts)
        //     {   var settings = opts.Value;  }   ← typed, IntelliSense works
        //
        //   No more magic strings like config["Ollama:DefaultModel"] !
        //
        private static void RegisterSettings(IConfiguration config, IServiceCollection services)
        {
            services.Configure<ApplicationSettings>(
                config.GetSection(ApplicationSettings.SectionName));

            services.Configure<OllamaSettings>(
                config.GetSection(OllamaSettings.SectionName));

            services.Configure<VoiceSettings>(
                config.GetSection(VoiceSettings.SectionName));

            Log.Information("Configuration sections bound: Application, Ollama, Voice");
        }

        // ── Core Service Registration ─────────────────────────────────────────
        //
        // AddSingleton<I, T>() means:
        //   "Create T once, reuse the same instance everywhere I is requested."
        //   Good for services that hold state (voice engine, AI brain, memory).
        //
        private static void RegisterCoreServices(IServiceCollection services)
        {
            // ── Voice pipeline ────────────────────────────────────────────────
            services.AddSingleton<IPythonBridgeService,      PythonBridgeService>();
            services.AddSingleton<IVoiceEngineService,       VoiceEngineService>();
            services.AddSingleton<IVoiceDiagnosticsService,  VoiceDiagnosticsService>();

            // ── AI brain ──────────────────────────────────────────────────────
            services.AddSingleton<IAIBrainService,           AIBrainService>();

            // ── System & infrastructure ───────────────────────────────────────
            services.AddSingleton<ISystemMetricsService,     SystemMetricsService>();
            services.AddSingleton<IWindowsAutomationService, WindowsAutomationService>();

            // ── Memory & file indexing ────────────────────────────────────────
            services.AddSingleton<IMemoryService,            MemoryService>();
            services.AddSingleton<IFileIndexerService,       FileIndexerService>();

            // ── Audio feedback ────────────────────────────────────────────────
            services.AddSingleton<ISoundEffectService,       SoundEffectService>();

            Log.Information("All core services registered.");
        }

        // ── UI Registration ───────────────────────────────────────────────────
        private static void RegisterUi(IServiceCollection services)
        {
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        }

        // ── Startup ───────────────────────────────────────────────────────────
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            await _host.StartAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            Log.Information("MainWindow displayed — LUCY shell is live.");
        }

        // ── Shutdown ──────────────────────────────────────────────────────────
        protected override async void OnExit(ExitEventArgs e)
        {
            Log.Information("LUCY shutting down — releasing resources...");
            await _host.StopAsync();
            _host.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
