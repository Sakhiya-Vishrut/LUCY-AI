using System.Threading.Tasks;

namespace LucyAI.Services
{
    public interface ISoundEffectService
    {
        Task PlayWakeSoundAsync();
        Task PlayListeningSoundAsync();
        Task PlayProcessingHumAsync();
        Task PlaySuccessChimeAsync();
        Task PlayErrorBeepAsync();
    }
}
