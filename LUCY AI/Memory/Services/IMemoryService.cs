using System.Collections.Generic;
using System.Threading.Tasks;

namespace LucyAI.Memory.Services
{
    public interface IMemoryService
    {
        Task SaveMemoryAsync(string key, string value);
        Task<string?> GetMemoryAsync(string key);
        Task<Dictionary<string, string>> GetAllMemoriesAsync();
        Task<string> SearchMemoryAsync(string query);
        Task RememberUserPreferenceAsync(string category, string detail);
    }
}
