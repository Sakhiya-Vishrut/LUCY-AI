using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LucyAI.Configuration;
using Microsoft.Extensions.Options;
using Serilog;

namespace LucyAI.AI.Services
{
    /// <summary>
    /// The AI brain of LUCY — sends prompts to the local Ollama LLM server
    /// and returns a clean, speech-ready response.
    ///
    /// HOW OLLAMA WORKS:
    ///   You install Ollama (https://ollama.com), then run:
    ///     ollama pull phi4          ← downloads the model (~4 GB)
    ///     ollama serve              ← starts HTTP server on port 11434
    ///   LUCY then sends a POST to http://localhost:11434/api/generate
    ///   with a JSON body containing the model name and the prompt.
    ///
    /// WHY IOptions&lt;OllamaSettings&gt;?
    ///   Before this fix, everything was hardcoded — changing the model or
    ///   endpoint required recompiling. Now you just edit appsettings.json.
    /// </summary>
    public class AIBrainService : IAIBrainService
    {
        // ── Dependencies ──────────────────────────────────────────────────────
        private readonly HttpClient _httpClient;
        private readonly OllamaSettings _settings;

        // ── System Prompt ─────────────────────────────────────────────────────
        // This is injected at the top of every Ollama request.
        // It tells the model who it is and how to respond.
        private const string SystemPrompt =
            "You are LUCY, an advanced JARVIS-style Voice AI Operating System " +
            "running on Windows .NET 10. Respond concisely in 1–2 sentences. " +
            "Be smart, clear, witty, and natural for speech synthesis. " +
            "Do NOT use markdown, bullet points, or special symbols. " +
            "Address the user as 'Vishrut'.";

        // ── State ─────────────────────────────────────────────────────────────
        public string ActiveModel { get; set; }

        public List<string> AvailableModels { get; } = new()
        {
            "phi4",
            "llama3.1",
            "llama3.2",
            "qwen2.5",
            "deepseek-r1",
            "mistral",
            "gemma3"
        };

        // ── Constructor ───────────────────────────────────────────────────────
        /// <param name="options">
        ///   Injected by DI from the "Ollama" section of appsettings.json.
        ///   IOptions&lt;T&gt; is the standard .NET pattern for typed config.
        ///   .Value gives you the concrete OllamaSettings object.
        /// </param>
        public AIBrainService(IOptions<OllamaSettings> options)
        {
            _settings = options.Value;

            // Start with the model defined in appsettings.json (no hardcoding)
            ActiveModel = _settings.DefaultModel;

            // HttpClient with configurable timeout from settings
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds)
            };

            Log.Information("AIBrainService initialised — model: {Model}, endpoint: {Url}",
                ActiveModel, _settings.Endpoint);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Switch to a different Ollama model at runtime without restarting.
        /// Validates that the name is non-empty; actual model availability is
        /// checked when the next request is made to Ollama.
        /// </summary>
        public Task<bool> SwitchModelAsync(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                Log.Warning("SwitchModelAsync called with empty model name.");
                return Task.FromResult(false);
            }

            string newModel = modelName.ToLowerInvariant().Trim();
            Log.Information("Switching AI model from {Old} to {New}", ActiveModel, newModel);
            ActiveModel = newModel;
            return Task.FromResult(true);
        }

        /// <summary>
        /// Send a user prompt to Ollama and return a clean, speech-ready string.
        /// Falls back to built-in keyword responses if Ollama is offline or slow.
        /// </summary>
        public async Task<string> GenerateResponseAsync(string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(userPrompt))
                return "I am listening, Vishrut.";

            try
            {
                // Build the request payload Ollama expects
                // stream: false  → wait for the full response, not a streaming stream
                var payload = new
                {
                    model   = ActiveModel,
                    prompt  = $"{SystemPrompt}\nUser: {userPrompt}\nLUCY:",
                    stream  = false,
                    options = new
                    {
                        temperature = _settings.Temperature,
                        num_predict = _settings.MaxTokens   // Ollama's name for max tokens
                    }
                };

                string json    = JsonSerializer.Serialize(payload);
                var    content = new StringContent(json, Encoding.UTF8, "application/json");

                // POST to http://localhost:11434/api/generate
                var response = await _httpClient.PostAsync(_settings.GenerateUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();

                    // Ollama returns: { "model": "phi4", "response": "...", "done": true, ... }
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("response", out var respElement))
                    {
                        string answer = respElement.GetString()?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(answer))
                        {
                            string cleaned = CleanResponseForSpeech(answer);
                            Log.Debug("Ollama response for '{Prompt}': {Response}", userPrompt, cleaned);
                            return cleaned;
                        }
                    }
                }
                else
                {
                    Log.Warning("Ollama returned HTTP {Code} for model {Model}",
                        (int)response.StatusCode, ActiveModel);
                }
            }
            catch (TaskCanceledException)
            {
                // Timeout — Ollama is taking too long (model may be loading)
                Log.Warning("Ollama request timed out after {Seconds}s — using fallback.",
                    _settings.TimeoutSeconds);
            }
            catch (HttpRequestException ex)
            {
                // Ollama not running at all
                Log.Warning("Ollama unreachable: {Message} — using fallback.", ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error in GenerateResponseAsync");
            }

            // ── Fallback ──────────────────────────────────────────────────────
            // Ollama is offline or too slow — answer locally from keyword rules.
            // This keeps LUCY functional even without the AI model loaded.
            return GenerateSmartFallbackResponse(userPrompt);
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Strip markdown symbols that sound wrong when spoken aloud.
        /// e.g. "**important**" → "important"
        /// </summary>
        private static string CleanResponseForSpeech(string rawText)
        {
            return rawText
                .Replace("**", "")
                .Replace("*", "")
                .Replace("#", "")
                .Replace("`", "")
                .Replace("_", " ")
                .Replace("  ", " ")
                .Trim();
        }

        /// <summary>
        /// Built-in keyword-based responses for when Ollama is unavailable.
        /// Covers the most common conversational patterns so LUCY is never silent.
        /// </summary>
        private static string GenerateSmartFallbackResponse(string prompt)
        {
            string p = prompt.ToLowerInvariant().Trim().TrimEnd('.', '!', '?');

            // ── Greetings ──────────────────────────────────────────────────
            if (p is "hello" or "hi" or "hey" || p.Contains("hello") || p.Contains("greetings"))
                return "Hello Vishrut! I am Lucy, your JARVIS Voice AI Assistant. How can I help you today?";

            // ── Time & Date ────────────────────────────────────────────────
            if (p.Contains("time") || p.Contains("what time"))
                return $"The current local time is {DateTime.Now:hh:mm tt}, Vishrut.";

            if (p.Contains("date") || p.Contains("what day") || p.Contains("today"))
                return $"Today is {DateTime.Now:dddd, MMMM d, yyyy}, Vishrut.";

            // ── Weather ────────────────────────────────────────────────────
            if (p.Contains("weather") || p.Contains("temperature"))
                return "Atmospheric sensors report optimal local conditions with clear skies, Vishrut.";

            // ── Identity ───────────────────────────────────────────────────
            if (p.Contains("who are you") || p.Contains("what is your name") || p.Contains("your name"))
                return "I am LUCY, your futuristic voice-first AI Operating System running locally on Windows .NET 10.";

            if (p.Contains("what can you do") || p.Contains("help") || p.Contains("capabilities"))
                return "I can control your PC, open apps, search the web, manage files, answer questions, and much more. Just ask, Vishrut.";

            // ── Wellbeing ──────────────────────────────────────────────────
            if (p.Contains("how are you") || p.Contains("kaise ho") || p.Contains("kem cho"))
                return "All neural networks are operating at peak performance, Vishrut. How can I assist you?";

            // ── Humour ─────────────────────────────────────────────────────
            if (p.Contains("joke") || p.Contains("funny"))
                return "Why do software engineers prefer dark mode? Because light attracts real bugs, Vishrut!";

            // ── Gujarati / Hindi ────────────────────────────────────────────
            if (p.Contains("kem cho") || p.Contains("majama"))
                return "Hu maja ma chu, Vishrut! All systems operating at peak efficiency.";

            if (p.Contains("shu karo cho") || p.Contains("kya kar rahe ho"))
                return "Main aapki help kar raha hu, Vishrut. Batao kya karna hai.";

            // ── System status ──────────────────────────────────────────────
            if (p.Contains("status") || p.Contains("system"))
                return "All LUCY subsystems are online and running at optimal performance, Vishrut.";

            // ── Thank you ──────────────────────────────────────────────────
            if (p.Contains("thank") || p.Contains("thanks") || p.Contains("shukriya") || p.Contains("dhanyavad"))
                return "Always at your service, Vishrut. That is what I am here for.";

            // ── Goodbye ────────────────────────────────────────────────────
            if (p.Contains("bye") || p.Contains("goodbye") || p.Contains("see you") || p.Contains("alvida"))
                return "Goodbye Vishrut. LUCY remains on standby whenever you need me.";

            // ── Default ────────────────────────────────────────────────────
            return $"I have processed your query regarding '{prompt}', Vishrut. " +
                   "Connect me to Ollama for a smarter response.";
        }
    }
}
