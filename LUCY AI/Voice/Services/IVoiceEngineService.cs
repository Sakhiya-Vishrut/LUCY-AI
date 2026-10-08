using System;
using System.Threading.Tasks;

namespace LucyAI.Voice.Services
{
    public interface IVoiceEngineService
    {
        event EventHandler<string>? VoiceCommandRecognized;
        event EventHandler<string>? VoiceStateChanged;
        event EventHandler<double>? AudioLevelChanged;

        bool IsMicrophoneAvailable { get; }
        void StartListening();
        void StopListening();
        Task SpeakAsync(string text);
    }
}
