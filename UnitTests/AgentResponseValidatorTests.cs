using AO2AIBot.Controller;
using AOBot_Testing.Structures;
using NUnit.Framework;

namespace UnitTests
{
    [TestFixture]
    public class AgentResponseValidatorTests
    {
        private AOClientControlSnapshot CreateSnapshot()
        {
            return new AOClientControlSnapshot
            {
                CurrentCharacter = "Phoenix",
                CurrentEmote = "normal",
                CurrentArea = "Courtroom No. 3",
                CurrentPosition = "def",
                IcShowname = "Phoenix Wright",
                OocShowname = "Usuario",
                AvailableCharacters = new[] { "Phoenix", "Miles", "Franziska" },
                AvailableEmotes = new[] { "normal", "thinking", "angry", "pointing", "happy" },
                AvailablePositions = new[] { "def", "pro", "wit", "jud", "sea" },
                AvailableAreas = new[] { "Courtroom No. 3", "Lobby", "Defense Lobby No. 1" },
                AvailableSfx = new[] { "Default", "Nothing", "dramatic/whoosh" },
                AvailableIniPuppets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Phoenix Wright", true },
                    { "Miles Edgeworth", true }
                }
            };
        }

        [Test]
        public void Validate_ICSpeakWithoutEmote_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "IC",
                        Message = "Hello",
                        Emote = ""
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("IC speak requires an explicit 'emote' field"));
        }

        [Test]
        public void Validate_ICSpeakWithValidEmote_Passes()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "IC",
                        Message = "Hello",
                        Emote = "normal"
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validate_ICSpeakWithInvalidEmote_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "IC",
                        Message = "Hello",
                        Emote = "dancing"
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Emote 'dancing' is not in the available emotes list"));
        }

        [Test]
        public void Validate_ShouldRespondTrueEmptyActions_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>()
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("shouldRespond is true but actions array is empty"));
        }

        [Test]
        public void Validate_UnknownActionType_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = "fly_away", Value = "moon" }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Unknown action type"));
        }

        [Test]
        public void Validate_SetCharacterNotInList_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetCharacter, Value = "Gumshoe" }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Character 'Gumshoe' is not in the available characters list"));
        }

        [Test]
        public void Validate_SetPositionWithAlias_NormalizesAndPasses()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetPosition, Value = "defense" }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.True);
            Assert.That(response.Actions[0].Value, Is.EqualTo("def"));
        }

        [Test]
        public void Validate_SetTextColorInvalid_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetTextColor, Value = "purple" }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Text color 'purple' is not a valid color"));
        }

        [Test]
        public void Validate_SetShoutModifierWithAlias_NormalizesAndPasses()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetShoutModifier, Value = "hold it" }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.True);
            Assert.That(response.Actions[0].Value, Is.EqualTo("holdIt"));
        }

        [Test]
        public void Validate_SetAreaNotInList_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetArea, Value = "Secret Room" }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Area 'Secret Room' is not in the available areas list"));
        }

        [Test]
        public void Validate_SetIniPuppetNotInList_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetIniPuppet, Value = "Larry Butz" }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("INI puppet 'Larry Butz' is not in the available puppets list"));
        }

        [Test]
        public void Validate_SpeakWithInvalidChannel_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "PRIVATE",
                        Message = "hello"
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Invalid channel 'PRIVATE'"));
        }

        [Test]
        public void Validate_OOCSpeakWithoutEmote_Passes()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "OOC",
                        Message = "brb",
                        Emote = ""
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validate_SetFlipBoolean_Passes()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetFlip, BoolValue = true }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validate_MultipleActions_AllValidated()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetTextColor, Value = "red" },
                    new AgentAction { Type = AgentActionType.SetEmote, Value = "angry" },
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "IC",
                        Message = "Objection!",
                        Emote = "pointing",
                        ShoutModifier = "objection"
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validate_SpeakWithInvalidShoutModifier_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "IC",
                        Message = "Test",
                        Emote = "normal",
                        ShoutModifier = "superShout"
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Shout modifier 'superShout' is not valid"));
        }

        [Test]
        public void Validate_SpeakWithInvalidEffect_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "IC",
                        Message = "Test",
                        Emote = "normal",
                        Effect = "explosion"
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Effect 'explosion' is not valid"));
        }

        [Test]
        public void Validate_SetEffectWithAlias_NormalizesFlashToRealization()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetEffect, Value = "flash" }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.True);
            Assert.That(response.Actions[0].Value, Is.EqualTo("realization"));
        }

        [Test]
        public void Validate_CaseInsensitiveEmoteMatch_Passes()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "IC",
                        Message = "Hello",
                        Emote = "Normal"
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.True);
            Assert.That(response.Actions[0].Emote, Is.EqualTo("normal"));
        }
    }
}
