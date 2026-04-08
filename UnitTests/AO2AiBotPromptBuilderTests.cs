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
            Assert.That(prompt, Does.Contain("Current area roster: [1] Adrian (Jarvis), [0] Franziska (Kam)"));
        }

        [Test]
        public void BuildPrompt_MarksServerMessagesAndCompactsAdjacentDuplicates()
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
            Assert.That(prompt, Does.Contain("Latest source: SERVER output or command response"));
            Assert.That(prompt.Split("[OOC][SERVER] $H: People in this area: 2").Length - 1, Is.EqualTo(2));
        }
    }
}
