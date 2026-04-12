using System.Text.Json.Nodes;
using AO2AIBot.Chat;
using AO2AIBot.Clients;
using AO2AIBot.Controller;
using NUnit.Framework;

namespace UnitTests
{
    [TestFixture]
    public class AOClientAgentControllerTests
    {
        [Test]
        public async Task RecordMessage_InvalidRetriesAreSuppressedWithoutExecutingActions()
        {
            QueueCompletionService completionService = new QueueCompletionService(
                """
                {"shouldRespond":true,"actions":[{"type":"speak","channel":"OOC","message":"done"}]}
                """,
                """
                {"shouldRespond":true,"actions":[{"type":"speak","channel":"OOC","message":"I think I understand now"}]}
                """);

            AOClientControlSnapshot snapshot = new AOClientControlSnapshot
            {
                CurrentCharacter = "Phoenix",
                CurrentEmote = "normal",
                AvailableEmotes = new[] { "normal", "thinking" },
                AvailableCharacterEmotes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Phoenix", new[] { "normal", "thinking" } }
                },
                AvailableCharacters = new[] { "Phoenix" }
            };

            int executionCount = 0;
            TaskCompletionSource<bool> waitDecisionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using AOClientAgentController controller = new AOClientAgentController(
                completionService,
                () => new AiChatProviderSettings
                {
                    Provider = AiProviderKind.Ollama,
                    OllamaModel = "llama3.1:8b",
                    MaxPromptMessages = 10
                },
                () => snapshot,
                (response, _, _) =>
                {
                    executionCount++;
                    return Task.FromResult("executed");
                });

            controller.StatusChanged += update =>
            {
                if (update.Kind == AOClientAgentStatusKind.WaitDecision)
                {
                    waitDecisionTcs.TrySetResult(true);
                }
            };

            controller.SetEnabled(true);
            controller.RecordMessage(
                new ChatLogEntry
                {
                    ChatLogType = "OOC",
                    ShowName = "Judge",
                    Message = "set text color to cyan and say hi",
                    IsFromSelf = false
                });

            Task completed = await Task.WhenAny(waitDecisionTcs.Task, Task.Delay(TimeSpan.FromSeconds(4)));

            Assert.That(completed, Is.SameAs(waitDecisionTcs.Task), "Controller should suppress invalid retries and settle without executing.");
            Assert.That(executionCount, Is.EqualTo(0));
            Assert.That(completionService.CallCount, Is.EqualTo(2));
        }

        [Test]
        public async Task RecordMessage_ExplicitControlRequestDoesNotAcceptShouldRespondFalse()
        {
            QueueCompletionService completionService = new QueueCompletionService(
                """
                {"shouldRespond":false,"actions":[]}
                """,
                """
                {"shouldRespond":true,"actions":[{"type":"set_text_color","value":"cyan"}]}
                """);

            AOClientControlSnapshot snapshot = new AOClientControlSnapshot
            {
                CurrentCharacter = "Phoenix",
                CurrentEmote = "normal",
                AvailableEmotes = new[] { "normal", "thinking" },
                AvailableCharacterEmotes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Phoenix", new[] { "normal", "thinking" } }
                },
                AvailableCharacters = new[] { "Phoenix" }
            };

            int executionCount = 0;
            TaskCompletionSource<bool> executedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using AOClientAgentController controller = new AOClientAgentController(
                completionService,
                () => new AiChatProviderSettings
                {
                    Provider = AiProviderKind.Ollama,
                    OllamaModel = "llama3.1:8b",
                    MaxPromptMessages = 10
                },
                () => snapshot,
                (response, _, _) =>
                {
                    executionCount++;
                    executedTcs.TrySetResult(true);
                    return Task.FromResult("executed");
                });

            controller.SetEnabled(true);
            controller.RecordMessage(
                new ChatLogEntry
                {
                    ChatLogType = "OOC",
                    ShowName = "Judge",
                    Message = "set text color to cyan",
                    IsFromSelf = false
                });

            Task completed = await Task.WhenAny(executedTcs.Task, Task.Delay(TimeSpan.FromSeconds(4)));

            Assert.That(completed, Is.SameAs(executedTcs.Task), "Controller should retry after invalid silent output and execute the corrected control action.");
            Assert.That(executionCount, Is.EqualTo(1));
            Assert.That(completionService.CallCount, Is.EqualTo(2));
        }

        private sealed class QueueCompletionService : IAiChatCompletionService
        {
            private readonly Queue<string> responses;

            public QueueCompletionService(params string[] responses)
            {
                this.responses = new Queue<string>(responses ?? Array.Empty<string>());
            }

            public int CallCount { get; private set; }

            public Task<string> GetResponseAsync(
                string prompt,
                AiChatProviderSettings settings,
                IEnumerable<string> systemInstructions,
                IReadOnlyDictionary<string, string>? systemVariables = null,
                Action<string>? partialResponseHandler = null,
                CancellationToken cancellationToken = default,
                JsonNode? jsonSchema = null)
            {
                CallCount++;
                return Task.FromResult(responses.Dequeue());
            }
        }
    }
}
