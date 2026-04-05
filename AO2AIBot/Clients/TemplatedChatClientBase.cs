using System.Text.Json.Serialization;

namespace AO2AIBot.Clients
{
    /// <summary>
    /// Base class for chat clients that support system instruction templating.
    /// </summary>
    public abstract class TemplatedChatClientBase : IChatPromptClient
    {
        private readonly List<string> systemInstructions = new List<string>();

        /// <summary>
        /// Gets the active system variables for templating.
        /// </summary>
        public IDictionary<string, string> SystemVariables { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <inheritdoc/>
        public IChatPromptClient SetSystemInstructions(IEnumerable<string> instructions)
        {
            systemInstructions.Clear();
            if (instructions == null)
            {
                return this;
            }

            foreach (string instruction in instructions)
            {
                if (string.IsNullOrWhiteSpace(instruction))
                {
                    continue;
                }

                systemInstructions.Add(instruction);
            }

            return this;
        }

        /// <summary>
        /// Creates a chat message list for the concrete backend.
        /// </summary>
        protected IReadOnlyList<ChatPromptMessage> BuildMessages(string prompt)
        {
            List<ChatPromptMessage> messages = new List<ChatPromptMessage>();
            foreach (string instruction in systemInstructions)
            {
                messages.Add(new ChatPromptMessage("system", ApplySystemVariables(instruction)));
            }

            messages.Add(new ChatPromptMessage("user", prompt ?? string.Empty));
            return messages;
        }

        /// <inheritdoc/>
        public abstract Task<string> GetResponseAsync(
            string prompt,
            string model,
            double temperature = 0.2,
            int maxTokens = 150,
            Action<string>? partialResponseHandler = null,
            CancellationToken cancellationToken = default);

        private string ApplySystemVariables(string input)
        {
            string resolved = input ?? string.Empty;
            foreach (KeyValuePair<string, string> variable in SystemVariables)
            {
                resolved = resolved.Replace(variable.Key, variable.Value ?? string.Empty, StringComparison.Ordinal);
            }

            return resolved;
        }

        /// <summary>
        /// Represents a chat message payload.
        /// </summary>
        protected sealed class ChatPromptMessage
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ChatPromptMessage"/> class.
            /// </summary>
            public ChatPromptMessage(string role, string content)
            {
                Role = role ?? string.Empty;
                Content = content ?? string.Empty;
            }

            /// <summary>
            /// Gets the chat role.
            /// </summary>
            [JsonPropertyName("role")]
            public string Role { get; }

            /// <summary>
            /// Gets the chat content.
            /// </summary>
            [JsonPropertyName("content")]
            public string Content { get; }
        }
    }
}
