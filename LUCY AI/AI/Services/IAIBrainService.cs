using System.Collections.Generic;
using System.Threading.Tasks;

namespace LucyAI.AI.Services
{
    public interface IAIBrainService
    {
        string ActiveModel { get; set; }
        List<string> AvailableModels { get; }
        Task<string> GenerateResponseAsync(string userPrompt);
        Task<bool> SwitchModelAsync(string modelName);
    }
}
