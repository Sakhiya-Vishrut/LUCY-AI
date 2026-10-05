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
            "Address the user as 'Boss'.";

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
        public AIBrainService(IOptions<OllamaSettings> options)
        {
            _settings = options.Value;
            ActiveModel = _settings.DefaultModel;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Min(_settings.TimeoutSeconds, 4))
            };

            Log.Information("AIBrainService initialised — model: {Model}, endpoint: {Url}",
                ActiveModel, _settings.Endpoint);
        }

        // ── Public API ────────────────────────────────────────────────────────

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

        public async Task<string> GenerateResponseAsync(string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(userPrompt))
                return "I am listening, Boss.";

            try
            {
                var payload = new
                {
                    model   = ActiveModel,
                    prompt  = $"{SystemPrompt}\nUser: {userPrompt}\nLUCY:",
                    stream  = false,
                    options = new
                    {
                        temperature = _settings.Temperature,
                        num_predict = _settings.MaxTokens
                    }
                };

                string json    = JsonSerializer.Serialize(payload);
                var    content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_settings.GenerateUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();

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
                Log.Warning("Ollama request timed out — using fallback.");
            }
            catch (HttpRequestException ex)
            {
                Log.Warning("Ollama unreachable: {Message} — using fallback.", ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error in GenerateResponseAsync");
            }

            return GenerateSmartFallbackResponse(userPrompt);
        }

        // ── Private Helpers ───────────────────────────────────────────────────

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

        private static string GenerateSmartFallbackResponse(string prompt)
        {
            string p = prompt.ToLowerInvariant().Trim().TrimEnd('.', '!', '?');

            // ── Greetings ──────────────────────────────────────────────────
            if (p is "hello" or "hi" or "hey" || p.Contains("hello") || p.Contains("greetings"))
                return "Hello Boss. How can I help you?";

            // ── Time & Date ────────────────────────────────────────────────
            if (p.Contains("time") || p.Contains("what time"))
                return $"The current local time is {DateTime.Now:hh:mm tt}, Boss.";

            if (p.Contains("date") || p.Contains("what day") || p.Contains("today"))
                return $"Today is {DateTime.Now:dddd, MMMM d, yyyy}, Boss.";

            // ── Identity & Capabilities ───────────────────────────────────
            if (p.Contains("who are you") || p.Contains("what is your name") || p.Contains("your name"))
                return "I am LUCY, your futuristic voice-first AI Operating System running locally on Windows .NET 10.";

            if (p.Contains("what can you do") || p.Contains("help") || p.Contains("capabilities"))
                return "I can control your PC, open apps, search the web, manage files, answer software and tech questions, and much more. Just ask, Boss.";

            // ── Wellbeing ──────────────────────────────────────────────────
            if (p.Contains("how are you") || p.Contains("kaise ho") || p.Contains("kem cho"))
                return "All neural networks are operating at peak performance, Boss. How can I assist you?";

            // ── Humour & Trivia ────────────────────────────────────────────
            if (p.Contains("joke") || p.Contains("funny"))
                return "Why do software engineers prefer dark mode? Because light attracts real bugs, Boss!";

            // ── Offline Software & Technical Knowledge Base ──────────────────
            if (p.Contains("dependency injection") || p.Contains("di"))
                return "Dependency Injection is a design pattern where an object receives its dependencies from an external source rather than creating them internally, Boss.";

            if (p.Contains("angular"))
                return "Angular is a TypeScript-based open-source web application framework developed by Google for building single-page web apps, Boss.";

            if (p.Contains(".net") || p.Contains("dotnet") || p.Contains("c#") || p.Contains("c sharp"))
                return ".NET is Microsoft's cross-platform developer platform, and C sharp is its modern object-oriented programming language, Boss.";

            if (p.Contains("object oriented") || p.Contains("oop"))
                return "Object-Oriented Programming is a paradigm based on objects containing data and methods, built on encapsulation, inheritance, polymorphism, and abstraction, Boss.";

            if (p.Contains("microservice") || p.Contains("microservices"))
                return "Microservices is an architectural style where an application is built as independent services communicating over APIs, Boss.";

            if (p.Contains("rest api") || p.Contains("restful") || p.Contains("rest"))
                return "REST is an architectural style for web services using standard HTTP methods like GET, POST, PUT, and DELETE, Boss.";

            if (p.Contains("docker") || p.Contains("container"))
                return "Docker is a platform for packaging applications into isolated containers that run consistently across any environment, Boss.";

            if (p.Contains("git") || p.Contains("github"))
                return "Git is a distributed version control system used to track changes in source code during software development, Boss.";

            if (p.Contains("sql") || p.Contains("database"))
                return "SQL is the standard language for querying, updating, and managing relational database systems, Boss.";

            if (p.Contains("async") || p.Contains("asynchronous") || p.Contains("await"))
                return "Async and await allow asynchronous code to execute without blocking the main thread, keeping user interfaces responsive, Boss.";

            if (p.Contains("python"))
                return "Python is a high-level interpreted programming language widely used for web development, automation, and AI, Boss.";

            if (p.Contains("artificial intelligence") || p.Contains("machine learning") || p.Contains(" ai "))
                return "Artificial Intelligence empowers systems to learn from data and perform cognitive tasks like speech recognition and decision making, Boss.";

            if (p.Contains("solid principle") || p.Contains("solid"))
                return "SOLID represents five object-oriented design principles that foster maintainable, scalable, and testable software, Boss.";

            if (p.Contains("react"))
                return "React is a popular JavaScript library developed by Meta for building dynamic user interfaces with component-based architecture, Boss.";

            if (p.Contains("typescript"))
                return "TypeScript is a strongly typed superset of JavaScript that compiles to plain JavaScript, adding static type safety, Boss.";

            // ── Thank you & Goodbye ─────────────────────────────────────────
            if (p.Contains("thank") || p.Contains("thanks") || p.Contains("shukriya"))
                return "Always at your service, Boss.";

            if (p.Contains("bye") || p.Contains("goodbye") || p.Contains("see you"))
                return "Goodbye Boss. LUCY remains on standby whenever you need me.";

            // ── Smart Search Fallback for Questions ────────────────────────
            if (p.StartsWith("what") || p.StartsWith("how") || p.StartsWith("why") || p.StartsWith("who") || p.StartsWith("explain") || p.StartsWith("tell me") || p.StartsWith("search"))
            {
                return $"[WEB_SEARCH:{prompt}]";
            }

            return $"I have processed your request regarding '{prompt}', Boss. System standing by.";
        }
    }
}
