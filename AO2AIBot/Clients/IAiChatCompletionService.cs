using System.Text.Json.Nodes;

namespace AO2AIBot.Clients
{
    /// <summary>
    /// Provides a unified entry point for AI model completions.
    /// </summary>
    public interface IAiChatCompletionService
    {
        /// <summary>
        /// Requests a model completion with the provided prompt and settings.
        /// </summary>
        /// <param name="jsonSchema">
        /// Optional JSON schema node. When provided to an Ollama client, enables grammar-constrained
        /// decoding so the model is forced to produce output that matches the schema exactly.
        /// Ignored by the OpenAI client.
        /// </param>
        Task<string> GetResponseAsync(
            string prompt,
            AiChatProviderSettings settings,
            IEnumerable<string> systemInstructions,
            IReadOnlyDictionary<string, string>? systemVariables = null,
            Action<string>? partialResponseHandler = null,
            CancellationToken cancellationToken = default,
            JsonNode? jsonSchema = null);
    }
}
