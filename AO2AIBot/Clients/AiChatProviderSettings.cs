namespace AO2AIBot.Clients
{
    /// <summary>
    /// Supported model backends for AO2 AI Bot.
    /// </summary>
    public enum AiProviderKind
    {
        Ollama,
        OpenAI
    }

    /// <summary>
    /// Runtime model settings used for each AI evaluation.
    /// </summary>
    public sealed class AiChatProviderSettings
    {
        /// <summary>
        /// Gets or sets the active provider.
        /// </summary>
        public AiProviderKind Provider { get; set; } = AiProviderKind.Ollama;

        /// <summary>
        /// Gets or sets the Ollama base URL.
        /// </summary>
        public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";

        /// <summary>
        /// Gets or sets the Ollama model name.
        /// </summary>
        public string OllamaModel { get; set; } = "llama3.1:8b";

        /// <summary>
        /// Gets or sets the OpenAI model name.
        /// </summary>
        public string OpenAIModel { get; set; } = "gpt-4o-mini";

        /// <summary>
        /// Gets or sets the OpenAI API key environment variable name.
        /// </summary>
        public string OpenAIApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";

        /// <summary>
        /// Gets or sets the model temperature.
        /// </summary>
        public double Temperature { get; set; } = 0.2;

        /// <summary>
        /// Gets or sets the model token budget (max output tokens).
        /// </summary>
        public int MaxTokens { get; set; } = 450;

        /// <summary>
        /// Gets or sets the Ollama context window size in tokens (num_ctx).
        /// Ollama defaults to 4K which is far too small — context overflow causes the model to
        /// output {"shouldRespond":false} as the shortest valid fallback.
        /// Gemma 4 supports 128K natively. Recommended values:
        ///   16384 — conservative (good starting point, low VRAM overhead)
        ///   32768 — recommended balance (best for most desktop GPUs)
        ///  131072 — full 128K (needs significant VRAM, not recommended for e2b/e4b)
        /// </summary>
        public int OllamaContextSize { get; set; } = 16384;

        /// <summary>
        /// Gets or sets the max transcript entries included in the prompt. Use <c>0</c> or lower for full history.
        /// </summary>
        public int MaxPromptMessages { get; set; }

        /// <summary>
        /// Gets or sets an optional personality or role description appended to the system prompt.
        /// </summary>
        public string PersonalityPrompt { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether to send the JSON grammar schema to Ollama.
        /// When true, Ollama uses token masking to force valid JSON — this guarantees valid output but
        /// introduces a shortest-path bias toward <c>{"shouldRespond":false}</c> (the minimum valid token sequence).
        /// When false, the model generates freely and the parser extracts JSON post-hoc.
        /// Recommended: false for better decision quality. True only if the model frequently outputs non-JSON.
        /// </summary>
        public bool UseOllamaJsonSchema { get; set; } = false;

        /// <summary>
        /// Gets the selected model for the active provider.
        /// </summary>
        public string SelectedModel =>
            Provider == AiProviderKind.OpenAI
                ? OpenAIModel?.Trim() ?? string.Empty
                : OllamaModel?.Trim() ?? string.Empty;
    }
}
