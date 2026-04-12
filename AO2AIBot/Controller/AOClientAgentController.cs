using AO2AIBot.Chat;
using AO2AIBot.Clients;
using AO2AIBot.Prompts;
using Common;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AO2AIBot.Controller
{
    /// <summary>
    /// Coordinates transcript capture, prompting, validation, and action execution for one AO client persona.
    /// Uses the strict action-array response contract with a validator stage between parse and execute.
    /// </summary>
    public sealed class AOClientAgentController : IDisposable
    {
        private readonly IAiChatCompletionService completionService;
        private readonly Func<AiChatProviderSettings> settingsProvider;
        private readonly Func<AOClientControlSnapshot> snapshotProvider;
        private readonly Func<AgentResponse, AOClientControlSnapshot, CancellationToken, Task<string>> actionExecutor;
        private readonly ChatLogManager chatLogManager = new ChatLogManager(-1);
        private readonly PersistentRuleStore ruleStore = new PersistentRuleStore();
        private readonly SemaphoreSlim evaluationGate = new SemaphoreSlim(1, 1);
        private readonly object evaluationSync = new object();
        private bool pendingEvaluationRequested;
        private bool pendingForceEvaluation;
        private string pendingReason = string.Empty;
        private ChatLogEntry? pendingLatestEntry;
        private CancellationTokenSource? inFlightCts;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="AOClientAgentController"/> class.
        /// </summary>
        public AOClientAgentController(
            IAiChatCompletionService completionService,
            Func<AiChatProviderSettings> settingsProvider,
            Func<AOClientControlSnapshot> snapshotProvider,
            Func<AgentResponse, AOClientControlSnapshot, CancellationToken, Task<string>> actionExecutor)
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
        /// Gets the persistent rule store for this controller instance.
        /// </summary>
        public PersistentRuleStore RuleStore => ruleStore;

        /// <summary>
        /// Enables or disables autopilot execution.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
        }

        /// <summary>
        /// Records a transcript entry, detects persistent rules, and schedules evaluation when appropriate.
        /// </summary>
        public void RecordMessage(ChatLogEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            chatLogManager.AddMessage(entry);

            // Detect persistent rule instructions from player messages.
            if (!entry.IsFromSelf && !entry.IsFromServer && !string.IsNullOrWhiteSpace(entry.Message))
            {
                ProcessPotentialRuleCommand(entry.Message);
            }

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

        private void ProcessPotentialRuleCommand(string message)
        {
            if (PersistentRuleStore.ContainsRevocationCue(message))
            {
                ruleStore.TryRevokeLatestRule();
                CustomConsole.Info("[PersistentRules] Revoked most recent rule due to: " + message);
            }
            else if (PersistentRuleStore.ShouldPromoteRuleCommand(message, out string scope))
            {
                ruleStore.AddRule(message, scope: scope);
                CustomConsole.Info("[PersistentRules] Added new " + scope + " rule: " + message);
            }
        }

        private void QueueEvaluation(string reason, ChatLogEntry? latestEntry, bool forceEvaluation)
        {
            lock (evaluationSync)
            {
                pendingEvaluationRequested = true;
                pendingForceEvaluation = pendingForceEvaluation || forceEvaluation;
                pendingReason = reason ?? string.Empty;
                pendingLatestEntry = latestEntry;

                inFlightCts?.Cancel();
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

                    CancellationTokenSource cts = new CancellationTokenSource();
                    lock (evaluationSync)
                    {
                        inFlightCts = cts;
                    }

                    try
                    {
                        await EvaluateOnceAsync(reason, latestEntry, forceEvaluation, cts.Token);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        PublishClearTransient();
                    }
                    finally
                    {
                        lock (evaluationSync)
                        {
                            if (ReferenceEquals(inFlightCts, cts))
                            {
                                inFlightCts = null;
                            }
                        }

                        cts.Dispose();
                    }
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
            IReadOnlyList<PersistentRule> activeRules = ruleStore.GetActiveRules();
            IReadOnlyList<string> activeRuleTexts = activeRules
                .Select(rule => rule.Text)
                .ToList()
                .AsReadOnly();

            Dictionary<string, string> systemVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "[[[current_character]]]", snapshot.CurrentCharacter },
                { "[[[current_emote]]]", snapshot.CurrentEmote }
            };

            string originalPrompt = AO2AiBotPromptBuilder.BuildPrompt(
                snapshot, promptHistory, latestEntry, reason, activeRules);
            IReadOnlyList<string> systemInstructions = AO2AiBotPromptCatalog.GetSystemInstructions(settings);

            const int maxAttempts = 2;
            string lastFailedResponse = string.Empty;
            IReadOnlyList<string>? lastValidationErrors = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                string prompt = attempt == 1
                    ? originalPrompt
                    : AO2AiBotPromptBuilder.BuildCorrectionPrompt(originalPrompt, lastFailedResponse, lastValidationErrors);

                string response = string.Empty;
                try
                {
                    PublishTransientPrimary(
                        BuildEvaluationStartMessage(settings.Provider.ToString(), settings.SelectedModel, attempt, maxAttempts),
                        isError: false);
                    PublishTransientPreview("Thinking...");

                    JsonNode? schema = (settings.Provider == AiProviderKind.Ollama && settings.UseOllamaJsonSchema)
                        ? AOClientResponseSchema.Schema
                        : null;

                    DateTime lastPreviewUpdateUtc = DateTime.MinValue;
                    string lastPreviewMessage = string.Empty;
                    response = await completionService.GetResponseAsync(
                        prompt,
                        settings,
                        systemInstructions,
                        systemVariables,
                        partialResponse =>
                        {
                            string previewMessage = BuildStreamPreviewMessage(partialResponse);
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

                    // === PARSE ===
                    AOClientAgentResponseParser.ParseResult parseResult = AOClientAgentResponseParser.Parse(response);
                    if (!parseResult.Success)
                    {
                        lastFailedResponse = response;
                        lastValidationErrors = new[] { parseResult.ErrorMessage };
                        CustomConsole.Warning(
                            "AI response parse failed (attempt " + attempt + "): "
                            + parseResult.ErrorMessage
                            + "\nRaw:\n" + response.Trim());
                        if (attempt < maxAttempts)
                        {
                            PublishTransientPrimary(
                                "AI response parse failed on attempt " + attempt + ". Retrying...",
                                isError: true);
                            continue;
                        }

                        PublishClearTransient();
                        PublishWaitDecision();
                        PublishFinalMessage(
                            "AI response suppressed after " + maxAttempts + " invalid attempt(s): " + parseResult.ErrorMessage,
                            isError: true,
                            rawResponse: response);
                        return;
                    }

                    // === VALIDATE ===
                    // Re-fetch snapshot for validation in case state changed during LLM call.
                    AOClientControlSnapshot validationSnapshot = snapshotProvider();
                    ValidationContext? validationContext = BuildValidationContext(
                        latestEntry,
                        attempt,
                        activeRuleTexts,
                        fullHistory);
                    AgentResponseValidator.ValidationResult validation =
                        parseResult.Response == null
                            ? new AgentResponseValidator.ValidationResult(false, new[] { "Parsed response was unexpectedly null." })
                            : AgentResponseValidator.Validate(parseResult.Response, validationSnapshot, validationContext);

                    if (!validation.IsValid)
                    {
                        lastFailedResponse = response;
                        lastValidationErrors = validation.Errors;
                        string errorSummary = string.Join("; ", validation.Errors);
                        CustomConsole.Warning(
                            "AI response validation failed (attempt " + attempt + "): " + errorSummary
                            + "\nRaw:\n" + response.Trim());
                        if (attempt < maxAttempts)
                        {
                            PublishTransientPrimary(
                                "AI response validation failed on attempt " + attempt + ". Retrying...",
                                isError: true);
                            continue;
                        }

                        PublishClearTransient();
                        PublishWaitDecision();
                        PublishFinalMessage(
                            "AI response suppressed after " + maxAttempts + " invalid attempt(s): " + errorSummary,
                            isError: true,
                            rawResponse: response);
                        return;
                    }

                    if (parseResult.ShouldWait || parseResult.Response == null || !parseResult.Response.ShouldRespond)
                    {
                        PublishClearTransient();
                        PublishWaitDecision();
                        return;
                    }

                    // === EXECUTE ===
                    string executionSummary = await actionExecutor(parseResult.Response, validationSnapshot, cancellationToken);
                    PublishClearTransient();
                    PublishFinalMessage(
                        string.IsNullOrWhiteSpace(executionSummary)
                            ? "AI action applied."
                            : executionSummary,
                        isError: false,
                        rawResponse: response);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
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

        private static ValidationContext? BuildValidationContext(
            ChatLogEntry? latestEntry,
            int attemptNumber,
            IReadOnlyList<string> activeRuleTexts,
            IReadOnlyList<ChatLogEntry> fullHistory)
        {
            if (latestEntry == null)
            {
                return null;
            }

            IReadOnlyList<string> recentSelfMessages = fullHistory
                .Where(entry => entry.IsFromSelf && !entry.IsFromServer && !string.IsNullOrWhiteSpace(entry.Message))
                .Select(entry => entry.Message.Trim())
                .TakeLast(4)
                .ToList()
                .AsReadOnly();

            return new ValidationContext
            {
                TriggeringMessage = latestEntry.IsFromServer ? string.Empty : latestEntry.Message,
                TriggeringChannel = latestEntry.ChatLogType?.Trim() ?? string.Empty,
                AttemptNumber = attemptNumber,
                ActiveRules = activeRuleTexts ?? Array.Empty<string>(),
                RecentSelfMessages = recentSelfMessages
            };
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
            string rawResponse = "")
        {
            StatusChanged?.Invoke(
                new AOClientAgentStatusUpdate(
                    AOClientAgentStatusKind.FinalMessage,
                    message,
                    isError,
                    rawResponse));
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

        private static string BuildStreamPreviewMessage(string partialResponse)
        {
            string normalized = Regex.Replace(partialResponse ?? string.Empty, "\\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "Generating...";
            }

            const int prefixLength = 140;
            const int suffixLength = 80;
            if (normalized.Length <= prefixLength + suffixLength + 5)
            {
                return normalized;
            }

            string prefix = normalized.Substring(0, prefixLength).TrimEnd();
            string suffix = normalized.Substring(normalized.Length - suffixLength).TrimStart();
            return prefix + " ... " + suffix;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lock (evaluationSync)
            {
                inFlightCts?.Cancel();
                inFlightCts = null;
            }

            evaluationGate.Dispose();
        }
    }
}
