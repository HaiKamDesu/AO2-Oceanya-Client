namespace AO2AIBot.Clients
{
    /// <summary>
    /// Represents a prompt-oriented chat completion client.
    /// </summary>
    public interface IChatPromptClient
    {
        /// <summary>
        /// Gets the system variables used for prompt templating.
        /// </summary>
        IDictionary<string, string> SystemVariables { get; }

        /// <summary>
        /// Replaces the active system instructions.
        /// </summary>
        IChatPromptClient SetSystemInstructions(IEnumerable<string> instructions);

        /// <summary>
        /// Requests a completion from the target backend.
        /// </summary>
        Task<string> GetResponseAsync(
            string prompt,
            string model,
            double temperature = 0.2,
            int maxTokens = 150,
            Action<string>? partialResponseHandler = null,
            CancellationToken cancellationToken = default);
    }
}
