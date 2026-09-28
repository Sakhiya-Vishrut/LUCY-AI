using System;
using System.Threading.Tasks;

namespace LucyAI.Voice.Services
{
    public interface IPythonBridgeService
    {
        event EventHandler<string>? CommandRecognized;
        event EventHandler<string>? WakeWordDetected;
        event EventHandler<string>? VoiceStateChanged;
        event EventHandler<double>? AudioLevelChanged;
        event EventHandler<string>? PythonLogReceived;

        bool IsPythonRunning { get; }
        void StartPythonEngine();
        void StopPythonEngine();
        Task SpeakAsync(string text);
    }
}
