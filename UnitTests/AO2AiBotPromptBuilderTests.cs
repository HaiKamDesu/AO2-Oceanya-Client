using AO2AIBot.Chat;
using AO2AIBot.Controller;
using AO2AIBot.Prompts;
using AOBot_Testing.Structures;
using NUnit.Framework;

namespace UnitTests
{
    [TestFixture]
    public class AO2AiBotPromptBuilderTests
    {
        [Test]
        public void BuildPrompt_IncludesAvailableSfxAndCurrentAreaRoster()
        {
            AOClientControlSnapshot snapshot = new AOClientControlSnapshot
            {
                CurrentCharacter = "KamLoremaster",
                IcShowname = "Jarvis",
                CurrentEmote = "Explaining_LeftHandUp",
                CurrentArea = "Basement",
                CurrentPosition = "jud",
                CurrentBackground = "basement",
                AvailableSfx = new[] { "Default", "Nothing", "dramatic/whoosh" },
                CurrentAreaPlayers = new[]
                {
                    new Player { PlayerID = 1, ICCharacterName = "Adrian", OOCShowname = "Jarvis" },
                    new Player { PlayerID = 0, ICCharacterName = "Franziska", OOCShowname = "Kam" }
                }
            };

            string prompt = AO2AiBotPromptBuilder.BuildPrompt(
                snapshot,
                new[]
                {
                    new ChatLogEntry
                    {
                        ChatLogType = "IC",
                        ShowName = "von Karma",
                        CharacterName = "Franziska",
                        Message = "hello"
                    }
                },
                latestEntry: null,
                triggerReason: "manual");

            Assert.That(prompt, Does.Contain("SFX: Default, Nothing, dramatic/whoosh"));
            Assert.That(prompt, Does.Contain("[1] Adrian (OOC: Jarvis)"));
            Assert.That(prompt, Does.Contain("[0] Franziska (OOC: Kam)"));
        }

        [Test]
        public void BuildPrompt_MarksServerMessages()
        {
            AOClientControlSnapshot snapshot = new AOClientControlSnapshot
            {
                CurrentCharacter = "KamLoremaster",
                IcShowname = "Jarvis"
            };

            ChatLogEntry serverEntry = new ChatLogEntry
            {
                ChatLogType = "OOC",
                ShowName = "$H",
                Message = "People in this area: 2",
                IsFromServer = true
            };

            string prompt = AO2AiBotPromptBuilder.BuildPrompt(
                snapshot,
                new[]
                {
                    serverEntry,
                    new ChatLogEntry
                    {
                        ChatLogType = "IC",
                        ShowName = "von Karma",
                        CharacterName = "Franziska",
                        Message = "DONT answer to this message."
                    }
                },
                latestEntry: serverEntry,
                triggerReason: "new message");

            Assert.That(prompt, Does.Contain("[OOC][SERVER] $H: People in this area: 2"));
            Assert.That(prompt, Does.Contain("Latest source: SERVER output"));
        }

        [Test]
        public void BuildPrompt_IncludesStructuredSelfState()
        {
            AOClientControlSnapshot snapshot = new AOClientControlSnapshot
            {
                CurrentCharacter = "Phoenix",
                CurrentEmote = "normal",
                CurrentPosition = "def",
                IcShowname = "Phoenix Wright",
                TextColor = "white",
                Flip = true
            };

            string prompt = AO2AiBotPromptBuilder.BuildPrompt(
                snapshot, Array.Empty<ChatLogEntry>(), null, "manual");

            Assert.That(prompt, Does.Contain("## Self State"));
            Assert.That(prompt, Does.Contain("\"currentCharacter\": \"Phoenix\""));
            Assert.That(prompt, Does.Contain("\"flip\": true"));
            Assert.That(prompt, Does.Contain("\"currentEmote\": \"normal\""));
        }

        [Test]
        public void BuildPrompt_IncludesKnownLimitsSection()
        {
            AOClientControlSnapshot snapshot = new AOClientControlSnapshot
            {
                CurrentCharacter = "Phoenix"
            };

            string prompt = AO2AiBotPromptBuilder.BuildPrompt(
                snapshot, Array.Empty<ChatLogEntry>(), null, "manual");

            Assert.That(prompt, Does.Contain("## Known Limits About Other Players"));
            Assert.That(prompt, Does.Contain("do NOT have access to other players' current emote"));
        }

        [Test]
        public void BuildPrompt_IncludesPersistentRules()
        {
            AOClientControlSnapshot snapshot = new AOClientControlSnapshot
            {
                CurrentCharacter = "Phoenix"
            };

            List<string> rules = new List<string>
            {
                "For every IC line, choose an emote by vibe.",
                "Always respond in red text."
            };

            string prompt = AO2AiBotPromptBuilder.BuildPrompt(
                snapshot, Array.Empty<ChatLogEntry>(), null, "manual", rules);

            Assert.That(prompt, Does.Contain("## Persistent Rules"));
            Assert.That(prompt, Does.Contain("1. For every IC line, choose an emote by vibe."));
            Assert.That(prompt, Does.Contain("2. Always respond in red text."));
        }

        [Test]
        public void BuildPrompt_EmptyPersistentRules_ShowsNoneActive()
        {
            AOClientControlSnapshot snapshot = new AOClientControlSnapshot
            {
                CurrentCharacter = "Phoenix"
            };

            string prompt = AO2AiBotPromptBuilder.BuildPrompt(
                snapshot, Array.Empty<ChatLogEntry>(), null, "manual");

            Assert.That(prompt, Does.Contain("(none active)"));
        }

        [Test]
        public void BuildPrompt_IncludesActionArrayContractInstructions()
        {
            AOClientControlSnapshot snapshot = new AOClientControlSnapshot
            {
                CurrentCharacter = "Phoenix"
            };

            string prompt = AO2AiBotPromptBuilder.BuildPrompt(
                snapshot, Array.Empty<ChatLogEntry>(), null, "manual");

            Assert.That(prompt, Does.Contain("action-array contract"));
            Assert.That(prompt, Does.Contain("\"shouldRespond\""));
            Assert.That(prompt, Does.Contain("\"actions\""));
        }

        [Test]
        public void BuildCorrectionPrompt_IncludesValidationErrors()
        {
            List<string> errors = new List<string>
            {
                "IC speak requires an explicit 'emote' field.",
                "Text color 'purple' is not valid."
            };

            string prompt = AO2AiBotPromptBuilder.BuildCorrectionPrompt(
                "original context", "bad response", errors);

            Assert.That(prompt, Does.Contain("## Validation Errors"));
            Assert.That(prompt, Does.Contain("IC speak requires an explicit 'emote' field."));
            Assert.That(prompt, Does.Contain("Text color 'purple' is not valid."));
            Assert.That(prompt, Does.Contain("bad response"));
        }
    }
}
