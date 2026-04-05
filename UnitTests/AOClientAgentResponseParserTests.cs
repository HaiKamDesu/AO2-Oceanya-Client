using AO2AIBot.Controller;
using AOBot_Testing.Structures;
using NUnit.Framework;

namespace UnitTests
{
    [TestFixture]
    public class AOClientAgentResponseParserTests
    {
        [Test]
        public void Test_Parse_SystemWait()
        {
            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse("SYSTEM_WAIT()");

            Assert.That(result.Success, Is.True);
            Assert.That(result.ShouldWait, Is.True);
            Assert.That(result.Decision, Is.Null);
        }

        [Test]
        public void Test_Parse_LegacyJsonResponse()
        {
            string response =
                """
                {
                  "message": "Hold it right there.",
                  "chatlog": "IC",
                  "showname": "Jarvis",
                  "current_character": "Phoenix",
                  "currentEmote": "normal",
                  "modifiers": {
                    "deskMod": 99,
                    "emoteMod": 0,
                    "shoutModifiers": 1,
                    "flip": 0,
                    "textColor": 0,
                    "immediate": 0,
                    "additive": 0
                  }
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.True);
            Assert.That(result.ShouldWait, Is.False);
            Assert.That(result.Decision, Is.Not.Null);
            Assert.That(result.Decision!.ShouldAct, Is.True);
            Assert.That(result.Decision.Channel, Is.EqualTo("IC"));
            Assert.That(result.Decision.Message, Is.EqualTo("Hold it right there."));
            Assert.That(result.Decision.IcShowname, Is.EqualTo("Jarvis"));
            Assert.That(result.Decision.Character, Is.EqualTo("Phoenix"));
            Assert.That(result.Decision.Emote, Is.EqualTo("normal"));
            Assert.That(result.Decision.ShoutModifier, Is.EqualTo(ICMessage.ShoutModifiers.HoldIt));
        }

        [Test]
        public void Test_Parse_JsonEmbeddedInProse()
        {
            string response =
                """
                Thinking through the reply now.

                {
                  "shouldRespond": true,
                  "channel": "OOC",
                  "message": "hello",
                  "showname": "Jarvis"
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.True);
            Assert.That(result.ShouldWait, Is.False);
            Assert.That(result.Decision, Is.Not.Null);
            Assert.That(result.Decision!.Channel, Is.EqualTo("OOC"));
            Assert.That(result.Decision.Message, Is.EqualTo("hello"));
            Assert.That(result.Decision.OocShowname, Is.EqualTo("Jarvis"));
        }

        [Test]
        public void Test_Parse_SystemWaitEmbeddedInProse()
        {
            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(
                "I should not respond here.\nSYSTEM_WAIT()");

            Assert.That(result.Success, Is.True);
            Assert.That(result.ShouldWait, Is.True);
            Assert.That(result.Decision, Is.Null);
        }
    }
}
