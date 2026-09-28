namespace LucyAI.Configuration
{
    /// <summary>
    /// Strongly-typed model for the "Application" section in appsettings.json.
    /// This lets us inject IOptions&lt;ApplicationSettings&gt; into any service
    /// instead of calling IConfiguration["Application:Name"] with magic strings.
    ///
    /// WHY: magic string keys crash silently at runtime; typed options fail at
    /// startup with a clear error message, and you get IntelliSense autocompletion.
    /// </summary>
    public sealed class ApplicationSettings
    {
        /// <summary>Config section key — must match appsettings.json exactly.</summary>
        public const string SectionName = "Application";

        public string Name { get; set; } = "LUCY AI Desktop Assistant";
        public string Version { get; set; } = "1.0.0";
        public string Environment { get; set; } = "Development";

        /// <summary>The assistant's spoken name (used in TTS greetings).</summary>
        public string AssistantName { get; set; } = "LUCY";

        /// <summary>How LUCY addresses you in speech (e.g. "Sir", "Vishrut", "Boss").</summary>
        public string UserNickname { get; set; } = "Sir";
    }

    /// <summary>
    /// Strongly-typed model for the "Ollama" section in appsettings.json.
    /// Ollama is the local LLM server that runs models like phi4, llama3, mistral, etc.
    /// We call it via a simple HTTP POST — no special SDK needed.
    /// </summary>
    public sealed class OllamaSettings
    {
        public const string SectionName = "Ollama";

        /// <summary>Base URL of the Ollama HTTP server (default: http://localhost:11434).</summary>
        public string Endpoint { get; set; } = "http://localhost:11434";

        /// <summary>Which model LUCY uses by default on startup.</summary>
        public string DefaultModel { get; set; } = "phi4";

        /// <summary>Fallback model if the primary model fails to load.</summary>
        public string FallbackModel { get; set; } = "llama3.2";

        /// <summary>
        /// Controls creativity: 0.0 = deterministic, 1.0 = very creative.
        /// 0.7 is a good balance for an assistant.
        /// </summary>
        public double Temperature { get; set; } = 0.7;

        /// <summary>Maximum number of tokens in a single AI response.</summary>
        public int MaxTokens { get; set; } = 2048;

        /// <summary>HTTP request timeout in seconds for Ollama calls.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>Full URL for the generate endpoint, built from Endpoint.</summary>
        public string GenerateUrl => $"{Endpoint.TrimEnd('/')}/api/generate";

        /// <summary>Full URL for the tags endpoint (used to list available models).</summary>
        public string TagsUrl => $"{Endpoint.TrimEnd('/')}/api/tags";
    }

    /// <summary>
    /// Strongly-typed model for the "Voice" section in appsettings.json.
    /// Controls both STT (Speech-to-Text) and TTS (Text-to-Speech) behaviour.
    /// </summary>
    public sealed class VoiceSettings
    {
        public const string SectionName = "Voice";

        /// <summary>
        /// The wake word LUCY listens for before processing a command.
        /// Case-insensitive at runtime. Default: "Lucy".
        /// </summary>
        public string WakeWord { get; set; } = "Lucy";

        /// <summary>
        /// Which STT engine to use: "SystemSpeech" or "Whisper".
        /// SystemSpeech = built-in Windows recognition (no install needed).
        /// Whisper     = offline AI model (more accurate, needs Python + whisper).
        /// </summary>
        public string SttEngine { get; set; } = "SystemSpeech";

        /// <summary>Whisper model size: tiny | base | small | medium | large.</summary>
        public string WhisperModel { get; set; } = "base";

        /// <summary>
        /// Which TTS engine to use: "SystemSpeech" or "Piper".
        /// SystemSpeech = Windows built-in voices (Zira, David).
        /// Piper        = offline neural voices (more natural, needs piper.exe).
        /// </summary>
        public string TtsEngine { get; set; } = "SystemSpeech";

        /// <summary>Preferred voice gender: "Female" or "Male".</summary>
        public string VoiceGender { get; set; } = "Female";

        /// <summary>
        /// Speech rate: -10 (very slow) to +10 (very fast).
        /// 0 = normal pace. Mapped to SpeechSynthesizer.Rate.
        /// </summary>
        public int SpeechRate { get; set; } = 0;

        /// <summary>TTS volume: 0–100.</summary>
        public int VoiceVolume { get; set; } = 100;

        /// <summary>
        /// Minimum STT confidence (0.0–1.0) to accept a recognised phrase.
        /// Below this threshold the recognition is silently discarded.
        /// 0.2 is permissive; raise to 0.5 for fewer false positives.
        /// </summary>
        public float SttConfidenceThreshold { get; set; } = 0.2f;

        /// <summary>
        /// Milliseconds between accepted speech events (debounce).
        /// Prevents the same utterance firing twice.
        /// </summary>
        public int SttDebounceMs { get; set; } = 800;
    }
}
