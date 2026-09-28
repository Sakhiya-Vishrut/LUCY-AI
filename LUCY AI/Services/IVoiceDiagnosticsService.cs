using System.Threading.Tasks;

namespace LucyAI.Services
{
    public struct DiagnosticsReport
    {
        public bool IsMicDetected;
        public bool IsAudioLevelChanging;
        public bool IsPythonBridgeActive;
        public bool IsOllamaConnected;
        public bool IsTtsReady;
        public string OverallStatus;
    }

    public interface IVoiceDiagnosticsService
    {
        Task<DiagnosticsReport> RunDiagnosticsAsync();
    }
}
