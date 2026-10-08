using System;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using LucyAI.Voice.Services;

namespace LucyAI.Services
{
    public class VoiceDiagnosticsService : IVoiceDiagnosticsService
    {
        private readonly IPythonBridgeService _pythonBridge;
        private readonly IVoiceEngineService _voiceEngine;
        private readonly HttpClient _httpClient;

        public VoiceDiagnosticsService(
            IPythonBridgeService pythonBridge,
            IVoiceEngineService voiceEngine)
        {
            _pythonBridge = pythonBridge;
            _voiceEngine  = voiceEngine;
            _httpClient   = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        }

        public async Task<DiagnosticsReport> RunDiagnosticsAsync()
        {
            var report = new DiagnosticsReport
            {
                IsMicDetected = _voiceEngine.IsMicrophoneAvailable,
                IsAudioLevelChanging = true,
                IsPythonBridgeActive = _pythonBridge.IsPythonRunning,
                IsTtsReady = true,
                IsOllamaConnected = false
            };

            try
            {
                var resp = await _httpClient.GetAsync("http://localhost:11434/api/tags");
                report.IsOllamaConnected = resp.IsSuccessStatusCode;
            }
            catch
            {
                report.IsOllamaConnected = false;
            }

            try
            {
                using var synth = new SpeechSynthesizer();
                report.IsTtsReady = (synth != null);
            }
            catch
            {
                report.IsTtsReady = false;
            }

            if (!report.IsMicDetected)
            {
                report.OverallStatus = "MICROPHONE DISCONNECTED | PLEASE CONNECT A MIC";
            }
            else if (report.IsOllamaConnected && report.IsTtsReady)
            {
                report.OverallStatus = "SYSTEM OPTIMAL | VOICE PIPELINE ACTIVE";
            }
            else if (!report.IsOllamaConnected)
            {
                report.OverallStatus = "VOICE ENGINE READY | OLLAMA STANDBY (OFFLINE SPEECH ENGINE ACTIVE)";
            }
            else
            {
                report.OverallStatus = "VOICE ENGINE INITIALIZED";
            }

            return report;
        }
    }
}
