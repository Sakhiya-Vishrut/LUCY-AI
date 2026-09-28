using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LucyAI.Services
{
    public class FileIndexerService : IFileIndexerService
    {
        private readonly List<string> _indexedFiles = new();
        private readonly object _lock = new();

        public int TotalIndexedFilesCount
        {
            get
            {
                lock (_lock) return _indexedFiles.Count;
            }
        }

        public FileIndexerService()
        {
            // Initial asynchronous indexing in background
            Task.Run(() => IndexSystemFoldersAsync());
        }

        public Task IndexSystemFoldersAsync()
        {
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] searchDirs = new[]
                {
                    Path.Combine(userProfile, "Downloads"),
                    Path.Combine(userProfile, "Documents"),
                    Path.Combine(userProfile, "Desktop"),
                    @"D:\.NET",
                    @"C:\Projects"
                };

                var found = new List<string>();

                foreach (var dir in searchDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        try
                        {
                            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                                .Where(f => !f.Contains(@"\.git\") && !f.Contains(@"\obj\") && !f.Contains(@"\bin\"))
                                .Take(2000);
                            found.AddRange(files);
                        }
                        catch { }
                    }
                }

                lock (_lock)
                {
                    _indexedFiles.Clear();
                    _indexedFiles.AddRange(found);
                }
            }
            catch { }

            return Task.CompletedTask;
        }

        public Task<List<string>> SearchFilesAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return Task.FromResult(new List<string>());

            lock (_lock)
            {
                var matches = _indexedFiles
                    .Where(f => Path.GetFileName(f).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    .Take(25)
                    .ToList();

                return Task.FromResult(matches);
            }
        }

        public Task<string?> FindBestMatchingFileAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Task.FromResult<string?>(null);

            string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            lock (_lock)
            {
                var match = _indexedFiles.FirstOrDefault(f =>
                {
                    string fileName = Path.GetFileName(f).ToLowerInvariant();
                    return tokens.All(t => fileName.Contains(t.ToLowerInvariant()));
                });

                if (match == null && tokens.Length > 0)
                {
                    match = _indexedFiles.FirstOrDefault(f => Path.GetFileName(f).Contains(tokens[0], StringComparison.OrdinalIgnoreCase));
                }

                return Task.FromResult(match);
            }
        }
    }
}
