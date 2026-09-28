using System.Collections.Generic;
using System.Threading.Tasks;

namespace LucyAI.Services
{
    public interface IFileIndexerService
    {
        Task IndexSystemFoldersAsync();
        Task<List<string>> SearchFilesAsync(string searchTerm);
        Task<string?> FindBestMatchingFileAsync(string query);
        int TotalIndexedFilesCount { get; }
    }
}
