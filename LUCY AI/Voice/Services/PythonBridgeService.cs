using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LucyAI.Voice.Services
{
    public class PythonBridgeService : IPythonBridgeService, IDisposable
    {
        private Process? _pythonProcess;
        private bool _disposed;
        private bool _shouldRun = true;

        public event EventHandler<string>? CommandRecognized;
        public event EventHandler<string>? WakeWordDetected;
        public event EventHandler<string>? VoiceStateChanged;
        public event EventHandler<double>? AudioLevelChanged;
        public event EventHandler<string>? PythonLogReceived;

        public bool IsPythonRunning => _pythonProcess != null && !_pythonProcess.HasExited;

        public PythonBridgeService()
        {
            StartPythonEngine();
        }

        public void StartPythonEngine()
        {
            if (IsPythonRunning) return;

            try
            {
                string scriptPath = Path.Combine(AppContext.BaseDirectory, "Voice", "PythonEngine", "voice_engine.py");
                if (!File.Exists(scriptPath))
                {
                    // Fallback search in project root if running under Visual Studio / dotnet run
                    string projPath = Path.Combine(Directory.GetCurrentDirectory(), "Voice", "PythonEngine", "voice_engine.py");
                    if (File.Exists(projPath)) scriptPath = projPath;
                }

                if (!File.Exists(scriptPath))
                {
                    PythonLogReceived?.Invoke(this, $"Python script not found at {scriptPath}");
                    return;
                }

                string pythonExe = "python";
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? string.Empty
                };

                _pythonProcess = new Process { StartInfo = psi };
                _pythonProcess.EnableRaisingEvents = true;

                _pythonProcess.OutputDataReceived += OnOutputDataReceived;
                _pythonProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        PythonLogReceived?.Invoke(this, $"PyErr: {e.Data}");
                    }
                };

                _pythonProcess.Exited += (s, e) =>
                {
                    PythonLogReceived?.Invoke(this, "Python voice engine exited.");
                    if (_shouldRun)
                    {
                        Task.Delay(2000).ContinueWith(_ => StartPythonEngine());
                    }
                };

                _pythonProcess.Start();
                _pythonProcess.BeginOutputReadLine();
                _pythonProcess.BeginErrorReadLine();

                PythonLogReceived?.Invoke(this, "Python voice engine process launched successfully.");
            }
            catch (Exception ex)
            {
                PythonLogReceived?.Invoke(this, $"Failed to start Python process: {ex.Message}");
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            try
            {
                using var doc = JsonDocument.Parse(e.Data);
                var root = doc.RootElement;
                if (!root.TryGetProperty("event", out var evProp)) return;

                string evType = evProp.GetString() ?? string.Empty;

                switch (evType)
                {
                    case "wake_detected":
                        string wakeText = root.TryGetProperty("text", out var wt) ? wt.GetString() ?? "Lucy" : "Lucy";
                        WakeWordDetected?.Invoke(this, wakeText);
                        break;

                    case "command_recognized":
                        string cmdText = root.TryGetProperty("text", out var ct) ? ct.GetString() ?? string.Empty : string.Empty;
                        if (!string.IsNullOrWhiteSpace(cmdText))
                        {
                            CommandRecognized?.Invoke(this, cmdText);
                        }
                        break;

                    case "state_changed":
                        string st = root.TryGetProperty("state", out var sp) ? sp.GetString() ?? "Listening" : "Listening";
                        VoiceStateChanged?.Invoke(this, st);
                        break;

                    case "audio_level":
                        if (root.TryGetProperty("level", out var lv))
                        {
                            AudioLevelChanged?.Invoke(this, lv.GetDouble());
                        }
                        break;

                    case "log":
                    case "status":
                        string msg = root.TryGetProperty("message", out var mp) ? mp.GetString() ?? string.Empty : string.Empty;
                        PythonLogReceived?.Invoke(this, msg);
                        break;
                }
            }
            catch
            {
                // Raw log output
                PythonLogReceived?.Invoke(this, e.Data);
            }
        }

        public async Task SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (IsPythonRunning && _pythonProcess != null)
            {
                try
                {
                    var payload = new { command = "speak", text = text };
                    string json = JsonSerializer.Serialize(payload);
                    await _pythonProcess.StandardInput.WriteLineAsync(json);
                    await _pythonProcess.StandardInput.FlushAsync();
                }
                catch { }
            }
        }

        public void StopPythonEngine()
        {
            _shouldRun = false;
            if (IsPythonRunning && _pythonProcess != null)
            {
                try
                {
                    var payload = new { command = "stop" };
                    string json = JsonSerializer.Serialize(payload);
                    _pythonProcess.StandardInput.WriteLine(json);
                    _pythonProcess.StandardInput.Flush();
                    _pythonProcess.Kill();
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopPythonEngine();
                _pythonProcess?.Dispose();
                _disposed = true;
            }
        }
    }
}
