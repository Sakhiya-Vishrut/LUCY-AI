using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LucyAI.Memory.Services
{
    public class MemoryService : IMemoryService
    {
        private readonly string _filePath;
        private Dictionary<string, string> _memories = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public MemoryService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string lucyDir = Path.Combine(appData, "LucyAI");
            Directory.CreateDirectory(lucyDir);
            _filePath = Path.Combine(lucyDir, "lucy_memory.json");
            LoadMemories();
        }

        private void LoadMemories()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        string json = File.ReadAllText(_filePath);
                        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (data != null)
                        {
                            _memories = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                catch { }

                // Default memory seeds if empty
                if (!_memories.ContainsKey("UserName")) _memories["UserName"] = "Vishrut";
                if (!_memories.ContainsKey("AI_Name")) _memories["AI_Name"] = "LUCY";
                if (!_memories.ContainsKey("Role")) _memories["Role"] = "JARVIS Voice Operating System";
                if (!_memories.ContainsKey("PreferredLanguage")) _memories["PreferredLanguage"] = "English / Gujarati / Hindi";
                if (!_memories.ContainsKey("PrimaryFramework")) _memories["PrimaryFramework"] = ".NET 10 & C#";
            }
        }

        private async Task SaveToFileAsync()
        {
            try
            {
                string json;
                lock (_lock)
                {
                    json = JsonSerializer.Serialize(_memories, new JsonSerializerOptions { WriteIndented = true });
                }
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch { }
        }

        public async Task SaveMemoryAsync(string key, string value)
        {
            lock (_lock)
            {
                _memories[key] = value;
            }
            await SaveToFileAsync();
        }

        public Task<string?> GetMemoryAsync(string key)
        {
            lock (_lock)
            {
                if (_memories.TryGetValue(key, out var val))
                {
                    return Task.FromResult<string?>(val);
                }
            }
            return Task.FromResult<string?>(null);
        }

        public Task<Dictionary<string, string>> GetAllMemoriesAsync()
        {
            lock (_lock)
            {
                return Task.FromResult(new Dictionary<string, string>(_memories));
            }
        }

        public Task<string> SearchMemoryAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Task.FromResult("Memory search query was empty.");

            lock (_lock)
            {
                var matches = _memories
                    .Where(k => k.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                k.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matches.Any())
                {
                    string summary = string.Join("; ", matches.Select(m => $"{m.Key}: {m.Value}"));
                    return Task.FromResult($"Found memory records for '{query}': {summary}");
                }
            }
            return Task.FromResult($"No memory record found for '{query}'.");
        }

        public async Task RememberUserPreferenceAsync(string category, string detail)
        {
            string key = $"Pref_{category}";
            await SaveMemoryAsync(key, detail);
        }
    }
}
