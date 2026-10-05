using System.Threading.Tasks;

namespace LucyAI.Automation.Services
{
    public interface IWindowsAutomationService
    {
        Task<string> ExecuteVoiceCommandAsync(string commandText);
        bool LaunchApp(string appName);
        bool CloseApp(string appName);
        void OpenYouTube(string query);
        void SearchWeb(string query);
        void LockScreen();
        void TakeScreenshot();
        void OpenTaskManager();
        void MuteAudio();
        void VolumeUp();
        void VolumeDown();
        void EmptyRecycleBin();
    }
}
