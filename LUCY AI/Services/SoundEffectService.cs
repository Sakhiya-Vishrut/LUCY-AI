using System;
using System.Media;
using System.Threading.Tasks;
using Serilog;

namespace LucyAI.Services
{
    /// <summary>
    /// Plays short audio feedback sounds during voice interaction.
    ///
    /// SOUND MAPPING:
    ///   Wake / recognition start → Asterisk  (ding)
    ///   Listening stopped        → Hand      (error-style click)
    ///   Processing / thinking    → Beep      (PC speaker short tone)
    ///   Success / response ready → Exclamation (Windows notify chime)
    ///   Error                    → Hand      (error click)
    ///
    /// WHY SystemSounds?
    ///   No audio files to ship or manage — Windows plays these sounds
    ///   through whatever the user has set in Control Panel → Sound.
    ///   They respect the user's volume and can be muted per-app.
    ///
    ///   In a later phase we will replace these with custom WAV files
    ///   (loaded via SoundPlayer) for the full JARVIS experience.
    ///   The interface stays the same — only this class changes.
    /// </summary>
    public class SoundEffectService : ISoundEffectService
    {
        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Played when LUCY hears the wake word and starts processing.
        /// A short "ding" lets the user know their command was received.
        /// </summary>
        public Task PlayWakeSoundAsync()
            => PlayOnBackgroundThread(() => SystemSounds.Asterisk.Play(), nameof(PlayWakeSoundAsync));

        /// <summary>
        /// Played when voice listening is paused or stopped.
        /// </summary>
        public Task PlayListeningSoundAsync()
            => PlayOnBackgroundThread(() => SystemSounds.Hand.Play(), nameof(PlayListeningSoundAsync));

        /// <summary>
        /// Played while LUCY is thinking / calling the AI model.
        /// Previously a no-op stub — now plays Console.Beep (short, non-blocking).
        ///
        /// Console.Beep uses the PC speaker directly, so it works even when
        /// Windows audio is muted. Frequency 880 Hz = musical A5 note (calm, techy).
        /// Duration 120 ms is short enough not to feel intrusive.
        /// </summary>
        public Task PlayProcessingHumAsync()
            => PlayOnBackgroundThread(() =>
            {
                try
                {
                    // 880 Hz for 120 ms — a brief, subtle "thinking" tone
                    Console.Beep(880, 120);
                }
                catch
                {
                    // Console.Beep may throw in some terminal environments — safe to swallow
                    SystemSounds.Asterisk.Play();
                }
            }, nameof(PlayProcessingHumAsync));

        /// <summary>
        /// Played just before LUCY speaks her response.
        /// The Windows "Exclamation" chime signals the start of TTS output.
        /// </summary>
        public Task PlaySuccessChimeAsync()
            => PlayOnBackgroundThread(() => SystemSounds.Exclamation.Play(), nameof(PlaySuccessChimeAsync));

        /// <summary>
        /// Played when a command fails or LUCY cannot process the request.
        /// </summary>
        public Task PlayErrorBeepAsync()
            => PlayOnBackgroundThread(() =>
            {
                try   { Console.Beep(300, 200); }   // low, short error tone
                catch { SystemSounds.Hand.Play(); }
            }, nameof(PlayErrorBeepAsync));

        // ── Private helper ────────────────────────────────────────────────────

        /// <summary>
        /// All sounds run on a background thread so the UI thread is never blocked.
        /// Returns immediately — the sound plays asynchronously.
        /// </summary>
        private static Task PlayOnBackgroundThread(Action play, string soundName)
        {
            _ = Task.Run(() =>
            {
                try   { play(); }
                catch (Exception ex) { Log.Warning(ex, "SoundEffectService: {Sound} failed.", soundName); }
            });
            return Task.CompletedTask;   // caller doesn't need to await the sound
        }
    }
}
