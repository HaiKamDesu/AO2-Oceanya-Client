namespace AO2AIBot.Controller
{
    /// <summary>
    /// Identifies the type of UI status update emitted by the AI controller.
    /// </summary>
    public enum AOClientAgentStatusKind
    {
        /// <summary>
        /// Sets or updates the primary transient status line.
        /// </summary>
        TransientPrimary,

        /// <summary>
        /// Sets or updates the transient thinking/preview line.
        /// </summary>
        TransientPreview,

        /// <summary>
        /// Clears transient status lines for the active evaluation.
        /// </summary>
        ClearTransient,

        /// <summary>
        /// Emits a permanent result or error message.
        /// </summary>
        FinalMessage,

        /// <summary>
        /// The AI evaluated the transcript and decided no action was needed (SYSTEM_WAIT).
        /// </summary>
        WaitDecision
    }

    /// <summary>
    /// Represents a UI-facing status update for one controller evaluation.
    /// </summary>
    public sealed class AOClientAgentStatusUpdate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AOClientAgentStatusUpdate"/> class.
        /// </summary>
        public AOClientAgentStatusUpdate(
            AOClientAgentStatusKind kind,
            string message = "",
            bool isError = false,
            string rawResponse = "",
            AOClientAgentDecision? decision = null)
        {
            Kind = kind;
            Message = message ?? string.Empty;
            IsError = isError;
            RawResponse = rawResponse ?? string.Empty;
            Decision = decision;
        }

        /// <summary>
        /// Gets the update type.
        /// </summary>
        public AOClientAgentStatusKind Kind { get; }

        /// <summary>
        /// Gets the human-readable status text.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets a value indicating whether the update represents an error.
        /// </summary>
        public bool IsError { get; }

        /// <summary>
        /// Gets the raw model response associated with the update when available.
        /// </summary>
        public string RawResponse { get; }

        /// <summary>
        /// Gets the parsed decision associated with the update when available.
        /// </summary>
        public AOClientAgentDecision? Decision { get; }
    }
}
