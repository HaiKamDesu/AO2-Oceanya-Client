using System.Text.Json.Nodes;

namespace AO2AIBot.Clients
{
    /// <summary>
    /// Default provider resolver for AO2 AI Bot.
    /// </summary>
    public sealed class AiChatCompletionService : IAiChatCompletionService
    {
        private readonly Func<string, string?> environmentReader;

        /// <summary>
        /// Initializes a new instance of the <see cref="AiChatCompletionService"/> class.
        /// </summary>
        public AiChatCompletionService(Func<string, string?>? environmentReader = null)
        {
            this.environmentReader = environmentReader
                ?? (name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
                    ?? Environment.GetEnvironmentVariable(name));
        }

        /// <inheritdoc/>
        public async Task<string> GetResponseAsync(
            string prompt,
            AiChatProviderSettings settings,
            IEnumerable<string> systemInstructions,
            IReadOnlyDictionary<string, string>? systemVariables = null,
            Action<string>? partialResponseHandler = null,
            CancellationToken cancellationToken = default,
            JsonNode? jsonSchema = null)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            IChatPromptClient client = CreateClient(settings, jsonSchema);
            client.SetSystemInstructions(systemInstructions);

            if (systemVariables != null)
            {
                foreach (KeyValuePair<string, string> variable in systemVariables)
                {
                    client.SystemVariables[variable.Key] = variable.Value ?? string.Empty;
                }
            }

            return await client.GetResponseAsync(
                prompt,
                settings.SelectedModel,
                settings.Temperature,
                settings.MaxTokens,
                partialResponseHandler,
                cancellationToken);
        }

        private IChatPromptClient CreateClient(AiChatProviderSettings settings, JsonNode? jsonSchema = null)
        {
            if (settings.Provider == AiProviderKind.OpenAI)
            {
                string variableName = string.IsNullOrWhiteSpace(settings.OpenAIApiKeyEnvironmentVariable)
                    ? "OPENAI_API_KEY"
                    : settings.OpenAIApiKeyEnvironmentVariable.Trim();
                string apiKey = environmentReader(variableName)?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException(
                        "OpenAI API key is not configured. Set " + variableName + " before enabling OpenAI mode.");
                }

                return new GPTClient(apiKey);
            }

            return new OllamaClient(settings.OllamaEndpoint, jsonSchema: jsonSchema);
        }
    }
}
