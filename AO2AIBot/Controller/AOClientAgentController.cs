using AO2AIBot.Chat;
using AO2AIBot.Clients;
using AO2AIBot.Prompts;
using Common;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AO2AIBot.Controller
{
    /// <summary>
    /// Coordinates transcript capture, prompting, and action execution for one AO client persona.
    /// </summary>
    public sealed class AOClientAgentController : IDisposable
    {
        private readonly IAiChatCompletionService completionService;
        private readonly Func<AiChatProviderSettings> settingsProvider;
        private readonly Func<AOClientControlSnapshot> snapshotProvider;
        private readonly Func<AOClientAgentDecision, CancellationToken, Task<string>> actionExecutor;
        private readonly ChatLogManager chatLogManager = new ChatLogManager(-1);
        private readonly SemaphoreSlim evaluationGate = new SemaphoreSlim(1, 1);
        private readonly object evaluationSync = new object();
        private bool pendingEvaluationRequested;
        private bool pendingForceEvaluation;
        private string pendingReason = string.Empty;
        private ChatLogEntry? pendingLatestEntry;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="AOClientAgentController"/> class.
        /// </summary>
        public AOClientAgentController(
            IAiChatCompletionService completionService,
            Func<AiChatProviderSettings> settingsProvider,
            Func<AOClientControlSnapshot> snapshotProvider,
            Func<AOClientAgentDecision, CancellationToken, Task<string>> actionExecutor)
        {
            this.completionService = completionService ?? throw new ArgumentNullException(nameof(completionService));
            this.settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            this.snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            this.actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        }

        /// <summary>
        /// Raised whenever the controller status text changes.
        /// </summary>
        public event Action<AOClientAgentStatusUpdate>? StatusChanged;

        /// <summary>
        /// Gets a value indicating whether autopilot is active.
        /// </summary>
        public bool IsEnabled { get; private set; }

        /// <summary>
        /// Enables or disables autopilot execution.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
        }

        /// <summary>
        /// Records a transcript entry and schedules evaluation when appropriate.
        /// </summary>
        public void RecordMessage(ChatLogEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            chatLogManager.AddMessage(entry);
            if (!IsEnabled || entry.IsFromSelf)
            {
                return;
            }

            QueueEvaluation("A new message arrived in the transcript.", entry, forceEvaluation: false);
        }

        /// <summary>
        /// Triggers a manual evaluation using the current transcript and state snapshot.
        /// </summary>
        public Task TriggerManualEvaluationAsync(CancellationToken cancellationToken = default)
        {
            QueueEvaluation("Manual evaluation requested from the UI.", latestEntry: null, forceEvaluation: true);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns the current transcript snapshot.
        /// </summary>
        public IReadOnlyList<ChatLogEntry> GetTranscriptSnapshot()
        {
            return chatLogManager.GetHistorySnapshot();
        }

        private void QueueEvaluation(string reason, ChatLogEntry? latestEntry, bool forceEvaluation)
        {
            lock (evaluationSync)
            {
                pendingEvaluationRequested = true;
                pendingForceEvaluation = pendingForceEvaluation || forceEvaluation;
                pendingReason = reason ?? string.Empty;
                pendingLatestEntry = latestEntry;
            }

            _ = RunEvaluationLoopAsync();
        }

        private async Task RunEvaluationLoopAsync()
        {
            if (!await evaluationGate.WaitAsync(0))
            {
                return;
            }

            try
            {
                while (true)
                {
                    string reason;
                    ChatLogEntry? latestEntry;
                    bool forceEvaluation;
                    lock (evaluationSync)
                    {
                        if (!pendingEvaluationRequested || disposed)
                        {
                            return;
                        }

                        pendingEvaluationRequested = false;
                        forceEvaluation = pendingForceEvaluation;
                        pendingForceEvaluation = false;
                        reason = pendingReason;
                        latestEntry = pendingLatestEntry;
                    }

                    await EvaluateOnceAsync(reason, latestEntry, forceEvaluation, CancellationToken.None);
                }
            }
            finally
            {
                evaluationGate.Release();
            }
        }

        private async Task EvaluateOnceAsync(
            string reason,
            ChatLogEntry? latestEntry,
            bool forceEvaluation,
            CancellationToken cancellationToken)
        {
            if (!IsEnabled && !forceEvaluation)
            {
                return;
            }

            AiChatProviderSettings settings = settingsProvider();
            AOClientControlSnapshot snapshot = snapshotProvider();
            IReadOnlyList<ChatLogEntry> fullHistory = chatLogManager.GetHistorySnapshot();
            IReadOnlyList<ChatLogEntry> promptHistory = SelectPromptHistory(fullHistory, settings.MaxPromptMessages);

            Dictionary<string, string> systemVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "[[[current_character]]]", snapshot.CurrentCharacter },
                { "[[[current_emote]]]", snapshot.CurrentEmote }
            };

            string originalPrompt = AO2AiBotPromptBuilder.BuildPrompt(snapshot, promptHistory, latestEntry, reason);
            IReadOnlyList<string> systemInstructions = AO2AiBotPromptCatalog.GetSystemInstructions(settings);

            const int maxAttempts = 2;
            string lastFailedResponse = string.Empty;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                string prompt = attempt == 1
                    ? originalPrompt
                    : AO2AiBotPromptBuilder.BuildCorrectionPrompt(originalPrompt, lastFailedResponse);

                string response = string.Empty;
                try
                {
                    PublishTransientPrimary(
                        BuildEvaluationStartMessage(settings.Provider.ToString(), settings.SelectedModel, attempt, maxAttempts),
                        isError: false);
                    PublishTransientPreview("Thinking...");

                    // Always apply grammar-constrained schema so the model cannot produce
                    // invalid JSON or out-of-range enum values on any attempt.
                    JsonNode? schema = AOClientResponseSchema.Schema;

                    DateTime lastPreviewUpdateUtc = DateTime.MinValue;
                    string lastPreviewMessage = string.Empty;
                    response = await completionService.GetResponseAsync(
                        prompt,
                        settings,
                        systemInstructions,
                        systemVariables,
                        partialResponse =>
                        {
                            string previewMessage = BuildThinkingPreviewMessage(partialResponse);
                            DateTime nowUtc = DateTime.UtcNow;
                            if (string.Equals(previewMessage, lastPreviewMessage, StringComparison.Ordinal))
                            {
                                return;
                            }

                            if (lastPreviewUpdateUtc != DateTime.MinValue
                                && nowUtc - lastPreviewUpdateUtc < TimeSpan.FromMilliseconds(120))
                            {
                                return;
                            }

                            lastPreviewMessage = previewMessage;
                            lastPreviewUpdateUtc = nowUtc;
                            PublishTransientPreview(previewMessage);
                        },
                        cancellationToken,
                        jsonSchema: schema);

                    AOClientAgentResponseParser.ParseResult parseResult = AOClientAgentResponseParser.Parse(response);
                    if (!parseResult.Success)
                    {
                        lastFailedResponse = response;
                        CustomConsole.Warning(
                            "AI response was invalid (attempt "
                            + attempt
                            + "). Parse error: "
                            + parseResult.ErrorMessage
                            + "\nRaw response:\n"
                            + response.Trim());
                        if (attempt < maxAttempts)
                        {
                            PublishTransientPrimary(
                                "AI response was invalid on attempt " + attempt + ". Retrying with correction prompt...",
                                isError: true);
                            continue;
                        }

                        PublishClearTransient();
                        PublishFinalMessage(
                            "AI response was invalid after "
                            + maxAttempts
                            + " attempts: "
                            + parseResult.ErrorMessage,
                            isError: true,
                            rawResponse: response);
                        continue;
                    }

                    if (parseResult.ShouldWait || parseResult.Decision == null || !parseResult.Decision.ShouldAct)
                    {
                        PublishClearTransient();
                        PublishWaitDecision();
                        return;
                    }

                    string executionSummary = await actionExecutor(parseResult.Decision, cancellationToken);
                    PublishClearTransient();
                    PublishFinalMessage(
                        string.IsNullOrWhiteSpace(executionSummary)
                            ? "AI action applied."
                            : executionSummary,
                        isError: false,
                        rawResponse: response,
                        decision: parseResult.Decision);
                    return;
                }
                catch (Exception ex)
                {
                    CustomConsole.Error("AO2 AI Bot evaluation failed.", ex);
                    if (attempt < maxAttempts)
                    {
                        PublishTransientPrimary(
                            "AI evaluation failed on attempt " + attempt + ". Retrying...",
                            isError: true);
                        continue;
                    }

                    PublishClearTransient();
                    PublishFinalMessage(
                        "AI evaluation failed after " + maxAttempts + " attempts: " + ex.Message,
                        isError: true,
                        rawResponse: response);
                }
            }
        }

        private static IReadOnlyList<ChatLogEntry> SelectPromptHistory(
            IReadOnlyList<ChatLogEntry> history,
            int maxPromptMessages)
        {
            if (maxPromptMessages <= 0 || history.Count <= maxPromptMessages)
            {
                return history;
            }

            return history.Skip(history.Count - maxPromptMessages).ToList().AsReadOnly();
        }

        private void PublishTransientPrimary(string message, bool isError)
        {
            StatusChanged?.Invoke(
                new AOClientAgentStatusUpdate(
                    AOClientAgentStatusKind.TransientPrimary,
                    message,
                    isError));
        }

        private void PublishTransientPreview(string message)
        {
            StatusChanged?.Invoke(
                new AOClientAgentStatusUpdate(
                    AOClientAgentStatusKind.TransientPreview,
                    message,
                    isError: false));
        }

        private void PublishClearTransient()
        {
            StatusChanged?.Invoke(new AOClientAgentStatusUpdate(AOClientAgentStatusKind.ClearTransient));
        }

        private void PublishWaitDecision()
        {
            StatusChanged?.Invoke(new AOClientAgentStatusUpdate(AOClientAgentStatusKind.WaitDecision));
        }

        private void PublishFinalMessage(
            string message,
            bool isError,
            string rawResponse = "",
            AOClientAgentDecision? decision = null)
        {
            StatusChanged?.Invoke(
                new AOClientAgentStatusUpdate(
                    AOClientAgentStatusKind.FinalMessage,
                    message,
                    isError,
                    rawResponse,
                    decision));
        }

        private static string BuildEvaluationStartMessage(
            string providerName,
            string modelName,
            int attempt,
            int maxAttempts)
        {
            string baseMessage =
                "AI evaluation started using "
                + providerName
                + " (model: "
                + modelName
                + ").";
            return attempt <= 1
                ? baseMessage
                : baseMessage + " Retry " + attempt + " of " + maxAttempts + ".";
        }

        private static string BuildThinkingPreviewMessage(string partialResponse)
        {
            string normalized = Regex.Replace(partialResponse ?? string.Empty, "\\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "Thinking...";
            }

            const int prefixLength = 140;
            const int suffixLength = 80;
            if (normalized.Length <= prefixLength + suffixLength + 5)
            {
                return "Thinking... " + normalized;
            }

            string prefix = normalized.Substring(0, prefixLength).TrimEnd();
            string suffix = normalized.Substring(normalized.Length - suffixLength).TrimStart();
            return "Thinking... " + prefix + " ... " + suffix;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            evaluationGate.Dispose();
        }
    }
}
