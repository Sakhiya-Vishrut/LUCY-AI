using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LucyAI.Automation.Services
{
    public class WindowsAutomationService : IWindowsAutomationService
    {
        private bool _pendingConfirmation = false;
        private string _pendingAction = string.Empty;

        // P/Invoke for Volume keys
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_VOLUME_MUTE = 0xAD;
        private const byte VK_VOLUME_DOWN = 0xAE;
        private const byte VK_VOLUME_UP = 0xAF;

        public async Task<string> ExecuteVoiceCommandAsync(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText)) return string.Empty;

            string lower = commandText.ToLowerInvariant().Trim().TrimEnd('.', '!', '?');

            // Strip "lucy" prefix if present
            if (lower.StartsWith("lucy "))
            {
                lower = lower.Substring(5).Trim();
            }

            // Voice Safety Confirmation Handler
            if (_pendingConfirmation)
            {
                if (lower.Contains("yes") || lower.Contains("confirm") || lower.Contains("ha") || lower.Contains("sahi"))
                {
                    string actionToRun = _pendingAction;
                    _pendingConfirmation = false;
                    _pendingAction = string.Empty;

                    if (actionToRun == "shutdown")
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 10") { CreateNoWindow = true });
                            return "Initiating computer shutdown in 10 seconds, Boss.";
                        }
                        catch
                        {
                            return "Failed to initiate computer shutdown, Boss.";
                        }
                    }
                    if (actionToRun == "restart")
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 10") { CreateNoWindow = true });
                            return "Initiating system restart, Boss.";
                        }
                        catch
                        {
                            return "Failed to initiate system restart, Boss.";
                        }
                    }
                    if (actionToRun == "delete_downloads")
                    {
                        return "Downloads cleanup command acknowledged, Boss.";
                    }
                }
                else
                {
                    _pendingConfirmation = false;
                    _pendingAction = string.Empty;
                    return "Action cancelled, Boss.";
                }
            }

            // Dangerous Action Triggers requiring Voice Confirmation
            if (lower.Contains("shutdown computer") || lower == "shutdown" || lower.Contains("bandh karo computer"))
            {
                _pendingConfirmation = true;
                _pendingAction = "shutdown";
                return "Shutdown requested, Boss. Please confirm by saying 'Yes confirm'.";
            }

            if (lower.Contains("restart computer") || lower == "restart")
            {
                _pendingConfirmation = true;
                _pendingAction = "restart";
                return "Restart requested, Boss. Please confirm by saying 'Yes confirm'.";
            }

            if (lower.Contains("delete downloads"))
            {
                _pendingConfirmation = true;
                _pendingAction = "delete_downloads";
                return "Deletion of Downloads requested, Boss. Please confirm deletion by saying 'Yes confirm'.";
            }

            // Volume & Mute Controls
            if (lower.Contains("volume up") || lower.Contains("volume vadhavo") || lower.Contains("volume badhao") || lower.Contains("increase volume"))
            {
                VolumeUp();
                return "Volume increased, Boss.";
            }

            if (lower.Contains("volume down") || lower.Contains("volume ochhu karo") || lower.Contains("volume kam karo") || lower.Contains("decrease volume"))
            {
                VolumeDown();
                return "Volume decreased, Boss.";
            }

            if (lower.Contains("mute") || lower.Contains("volume bandh karo"))
            {
                MuteAudio();
                return "Audio muted, Boss.";
            }

            // Task Manager
            if (lower.Contains("task manager") || lower.Contains("task manager open"))
            {
                OpenTaskManager();
                return "Opening Task Manager, Boss.";
            }

            // Recycle Bin
            if (lower.Contains("empty recycle bin") || lower.Contains("recycle bin khali karo"))
            {
                EmptyRecycleBin();
                return "Recycle bin emptied, Boss.";
            }

            // 1. YouTube Automation
            if ((lower.Contains("play") || lower.Contains("chalavo") || lower.Contains("bajao")) && lower.Contains("youtube"))
            {
                string songQuery = ExtractQuery(lower);
                OpenYouTube(songQuery);
                return $"Opening YouTube and playing {songQuery}, Boss.";
            }

            if (lower.StartsWith("open youtube") || lower == "youtube kholo" || lower == "youtube open karo" || lower == "youtube")
            {
                OpenYouTube("");
                return "Opening YouTube, Boss.";
            }

            // 2. Google Search Automation
            if (lower.Contains("search google for") || lower.StartsWith("google search") || lower.StartsWith("search for"))
            {
                string query = lower.Replace("search google for", "").Replace("google search", "").Replace("search for", "").Trim();
                SearchWeb(query);
                return $"Searching Google for '{query}', Boss.";
            }

            // 3. Application Launchers & System Folders
            if (lower.Contains("open chrome") || lower.Contains("chrome kholo") || lower.Contains("chrome open karo") || lower.Contains("browser kholo") || lower.Contains("launch chrome"))
            {
                bool success = LaunchApp("chrome");
                return success ? "Opening Chrome, Boss." : "Failed to open Chrome, Boss.";
            }

            if (lower.Contains("open vs code") || lower.Contains("open visual studio") || lower.Contains("code kholo") || lower.Contains("visual studio kholo") || lower.Contains("code open karo"))
            {
                bool success = LaunchApp("code") || LaunchApp("devenv");
                return success ? "Launching Visual Studio workspace, Boss." : "Failed to open Visual Studio, Boss.";
            }

            if (lower.Contains("open spotify") || lower.Contains("spotify kholo") || lower.Contains("spotify open karo") || lower.Contains("play music"))
            {
                bool success = LaunchApp("spotify");
                return success ? "Opening Spotify, Boss." : "Failed to open Spotify, Boss.";
            }

            if (lower.Contains("open whatsapp") || lower.Contains("whatsapp kholo") || lower.Contains("whatsapp open karo"))
            {
                bool success = LaunchApp("whatsapp");
                return success ? "Opening WhatsApp, Boss." : "Failed to open WhatsApp, Boss.";
            }

            if (lower.Contains("open calculator") || lower.Contains("calculator kholo") || lower.Contains("calculator open karo"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("calc.exe") { UseShellExecute = true });
                    return "Opening Calculator, Boss.";
                }
                catch
                {
                    return "Failed to open Calculator, Boss.";
                }
            }

            if (lower.Contains("open notepad") || lower.Contains("notepad kholo") || lower.Contains("notepad open karo"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
                    return "Opening Notepad, Boss.";
                }
                catch
                {
                    return "Failed to open Notepad, Boss.";
                }
            }

            if (lower.Contains("open downloads") || lower.Contains("my downloads"))
            {
                try
                {
                    string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    Process.Start(new ProcessStartInfo("explorer.exe", downloadsPath) { UseShellExecute = true });
                    return "Opening Downloads folder, Boss.";
                }
                catch
                {
                    return "Failed to open Downloads folder, Boss.";
                }
            }

            if (lower.Contains("open desktop"))
            {
                try
                {
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    Process.Start(new ProcessStartInfo("explorer.exe", desktopPath) { UseShellExecute = true });
                    return "Opening Desktop folder, Boss.";
                }
                catch
                {
                    return "Failed to open Desktop folder, Boss.";
                }
            }

            // Close App Commands
            if (lower.StartsWith("close ") || lower.EndsWith(" bandh karo") || lower.EndsWith(" close karo"))
            {
                string targetApp = lower
                    .Replace("close", "")
                    .Replace("bandh karo", "")
                    .Replace("close karo", "")
                    .Trim();

                if (!string.IsNullOrWhiteSpace(targetApp))
                {
                    bool closed = CloseApp(targetApp);
                    if (closed) return $"Closed {targetApp}, Boss.";
                    return $"Could not find running process for {targetApp}, Boss.";
                }
            }

            // 4. System Commands
            if (lower.Contains("lock screen") || lower.Contains("pc lock karo") || lower.Contains("lock pc"))
            {
                LockScreen();
                return "Locking Windows session, Boss.";
            }

            if (lower.Contains("take screenshot") || lower.Contains("take a screenshot") || lower.Contains("screenshot lo") || lower.Contains("screenshot le"))
            {
                TakeScreenshot();
                return "Screenshot taken, Boss.";
            }

            await Task.Delay(10);
            return string.Empty;
        }

        public bool LaunchApp(string appName)
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c start {appName}") { CreateNoWindow = true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool CloseApp(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return false;

            string target = appName.ToLowerInvariant().Trim();
            string processName = target switch
            {
                "chrome" or "google chrome" or "browser" => "chrome",
                "code" or "vs code" or "visual studio code" => "code",
                "visual studio" or "devenv" => "devenv",
                "spotify" => "spotify",
                "whatsapp" => "whatsapp",
                "calculator" or "calc" => "calculatorapp",
                "notepad" => "notepad",
                _ => target
            };

            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0 && processName == "calculatorapp")
                    processes = Process.GetProcessesByName("calc");

                if (processes.Length == 0) return false;

                foreach (var p in processes)
                {
                    try
                    {
                        if (!p.CloseMainWindow())
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void OpenYouTube(string query)
        {
            try
            {
                string url = string.IsNullOrWhiteSpace(query)
                    ? "https://www.youtube.com"
                    : $"https://www.youtube.com/results?search_query={WebUtility.UrlEncode(query)}";

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        public void SearchWeb(string query)
        {
            try
            {
                string url = $"https://www.google.com/search?q={WebUtility.UrlEncode(query)}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        public void LockScreen()
        {
            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true });
            }
            catch { }
        }

        public void OpenTaskManager()
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
            }
            catch { }
        }

        public void MuteAudio()
        {
            keybd_event(VK_VOLUME_MUTE, 0, 0, UIntPtr.Zero);
        }

        public void VolumeUp()
        {
            keybd_event(VK_VOLUME_UP, 0, 0, UIntPtr.Zero);
            keybd_event(VK_VOLUME_UP, 0, 0, UIntPtr.Zero);
        }

        public void VolumeDown()
        {
            keybd_event(VK_VOLUME_DOWN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_VOLUME_DOWN, 0, 0, UIntPtr.Zero);
        }

        public void EmptyRecycleBin()
        {
            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe", "-Command Clear-RecycleBin -Force") { CreateNoWindow = true });
            }
            catch { }
        }

        public void TakeScreenshot()
        {
            try
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string lucyFolder = Path.Combine(pictures, "LucyScreenshots");
                Directory.CreateDirectory(lucyFolder);

                string filePath = Path.Combine(lucyFolder, $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                int w = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
                int h = (int)System.Windows.SystemParameters.PrimaryScreenHeight;

                using var bmp = new System.Drawing.Bitmap(w, h);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(w, h));
                }
                bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

                Process.Start(new ProcessStartInfo("explorer.exe", filePath) { UseShellExecute = true });
            }
            catch { }
        }

        private static string ExtractQuery(string input)
        {
            return input
                .Replace("open youtube and play", "")
                .Replace("youtube par song chalavo", "")
                .Replace("youtube par song bajao", "")
                .Replace("play song on youtube", "")
                .Replace("play", "")
                .Replace("youtube", "")
                .Replace("song", "")
                .Replace("on", "")
                .Trim();
        }
    }
}
