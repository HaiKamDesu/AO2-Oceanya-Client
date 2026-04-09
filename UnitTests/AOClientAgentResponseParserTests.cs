using AO2AIBot.Controller;
using NUnit.Framework;

namespace UnitTests
{
    [TestFixture]
    public class AOClientAgentResponseParserTests
    {
        [Test]
        public void Parse_ValidShouldRespondFalse_ReturnsWait()
        {
            string response = """{"shouldRespond":false,"actions":[]}""";
            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.True);
            Assert.That(result.ShouldWait, Is.True);
            Assert.That(result.Response, Is.Not.Null);
            Assert.That(result.Response!.ShouldRespond, Is.False);
            Assert.That(result.Response.Actions, Is.Empty);
        }

        [Test]
        public void Parse_ValidSpeakAction_ReturnsResponse()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    {
                      "type": "speak",
                      "channel": "IC",
                      "message": "Hold it!",
                      "emote": "pointing"
                    }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.True);
            Assert.That(result.ShouldWait, Is.False);
            Assert.That(result.Response, Is.Not.Null);
            Assert.That(result.Response!.ShouldRespond, Is.True);
            Assert.That(result.Response.Actions.Count, Is.EqualTo(1));
            Assert.That(result.Response.Actions[0].Type, Is.EqualTo("speak"));
            Assert.That(result.Response.Actions[0].Channel, Is.EqualTo("IC"));
            Assert.That(result.Response.Actions[0].Message, Is.EqualTo("Hold it!"));
            Assert.That(result.Response.Actions[0].Emote, Is.EqualTo("pointing"));
        }

        [Test]
        public void Parse_MultipleOrderedActions_PreservesOrder()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    { "type": "set_text_color", "value": "red" },
                    { "type": "set_emote", "value": "angry" },
                    { "type": "speak", "channel": "IC", "message": "Objection!", "emote": "angry" }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Response!.Actions.Count, Is.EqualTo(3));
            Assert.That(result.Response.Actions[0].Type, Is.EqualTo("set_text_color"));
            Assert.That(result.Response.Actions[0].Value, Is.EqualTo("red"));
            Assert.That(result.Response.Actions[1].Type, Is.EqualTo("set_emote"));
            Assert.That(result.Response.Actions[2].Type, Is.EqualTo("speak"));
        }

        [Test]
        public void Parse_ProseWrappedJson_IsRejected()
        {
            string response = """
                Thinking through the reply now.

                {
                  "shouldRespond": true,
                  "actions": [
                    { "type": "speak", "channel": "IC", "message": "hello", "emote": "normal" }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("must be a JSON object starting with '{'"));
        }

        [Test]
        public void Parse_ShouldRespondTrueWithEmptyActions_IsRejected()
        {
            string response = """{"shouldRespond":true,"actions":[]}""";
            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("shouldRespond is true but actions array is empty"));
        }

        [Test]
        public void Parse_ShouldRespondFalseWithActions_IsRejected()
        {
            string response = """
                {
                  "shouldRespond": false,
                  "actions": [
                    { "type": "speak", "channel": "IC", "message": "hi", "emote": "normal" }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("shouldRespond is false but actions array is non-empty"));
        }

        [Test]
        public void Parse_UnknownActionType_IsRejected()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    { "type": "dance", "value": "moonwalk" }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Unknown action type 'dance'"));
        }

        [Test]
        public void Parse_LegacyThinkingField_IsRejected()
        {
            string response = """{"thinking":"reason","shouldRespond":false}""";
            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Legacy format detected"));
            Assert.That(result.ErrorMessage, Does.Contain("thinking"));
        }

        [Test]
        public void Parse_LegacyModifiersField_IsRejected()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "message": "hi",
                  "modifiers": { "textColor": "red" }
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Legacy format detected"));
        }

        [Test]
        public void Parse_LegacyStateField_IsRejected()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "channel": "IC",
                  "message": "hi",
                  "state": { "emote": "normal" }
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Legacy format detected"));
        }

        [Test]
        public void Parse_MissingShouldRespond_IsRejected()
        {
            string response = """{"actions":[]}""";
            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Missing required field 'shouldRespond'"));
        }

        [Test]
        public void Parse_SpeakWithoutChannel_IsRejected()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    { "type": "speak", "message": "hello", "emote": "normal" }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Missing required field 'channel'"));
        }

        [Test]
        public void Parse_SpeakWithoutMessage_IsRejected()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    { "type": "speak", "channel": "IC", "emote": "normal" }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Missing required field 'message'"));
        }

        [Test]
        public void Parse_BooleanActionWithoutValue_IsRejected()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    { "type": "set_flip" }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Missing required field 'value'"));
        }

        [Test]
        public void Parse_SetOffsetWithValues_Succeeds()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    { "type": "set_offset", "horizontal": 10, "vertical": -5 }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Response!.Actions[0].Horizontal, Is.EqualTo(10));
            Assert.That(result.Response.Actions[0].Vertical, Is.EqualTo(-5));
        }

        [Test]
        public void Parse_SetOffsetWithNoValues_IsRejected()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    { "type": "set_offset" }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("at least one of 'horizontal' or 'vertical'"));
        }

        [Test]
        public void Parse_MarkdownFencedJson_IsAccepted()
        {
            string response = "```json\n{\"shouldRespond\":false,\"actions\":[]}\n```";
            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.True);
            Assert.That(result.ShouldWait, Is.True);
        }

        [Test]
        public void Parse_SpeakWithOptionalTransientFields_Succeeds()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    {
                      "type": "speak",
                      "channel": "IC",
                      "message": "Take that!",
                      "emote": "pointing",
                      "textColor": "red",
                      "shoutModifier": "takeThat",
                      "effect": "realization",
                      "screenshake": true,
                      "sfx": "dramatic/whoosh"
                    }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.True);
            AgentAction speak = result.Response!.Actions[0];
            Assert.That(speak.TextColor, Is.EqualTo("red"));
            Assert.That(speak.ShoutModifier, Is.EqualTo("takeThat"));
            Assert.That(speak.Effect, Is.EqualTo("realization"));
            Assert.That(speak.Screenshake, Is.True);
            Assert.That(speak.Sfx, Is.EqualTo("dramatic/whoosh"));
        }

        [Test]
        public void Parse_EmptyResponse_IsRejected()
        {
            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse("");
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("empty"));
        }

        [Test]
        public void Parse_OOCSpeakAction_Succeeds()
        {
            string response = """
                {
                  "shouldRespond": true,
                  "actions": [
                    { "type": "speak", "channel": "OOC", "message": "brb" }
                  ]
                }
                """;

            AOClientAgentResponseParser.ParseResult result = AOClientAgentResponseParser.Parse(response);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Response!.Actions[0].Channel, Is.EqualTo("OOC"));
            Assert.That(result.Response.Actions[0].Message, Is.EqualTo("brb"));
        }
    }
}
