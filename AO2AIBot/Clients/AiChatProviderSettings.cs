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
        /// Gets or sets the model token budget.
        /// </summary>
        public int MaxTokens { get; set; } = 450;

        /// <summary>
        /// Gets or sets the max transcript entries included in the prompt. Use <c>0</c> or lower for full history.
        /// </summary>
        public int MaxPromptMessages { get; set; }

        /// <summary>
        /// Gets or sets an optional personality or role description appended to the system prompt.
        /// </summary>
        public string PersonalityPrompt { get; set; } = string.Empty;

        /// <summary>
        /// Gets the selected model for the active provider.
        /// </summary>
        public string SelectedModel =>
            Provider == AiProviderKind.OpenAI
                ? OpenAIModel?.Trim() ?? string.Empty
                : OllamaModel?.Trim() ?? string.Empty;
    }
}
