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
                AvailableCharacterEmotes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Phoenix", new[] { "normal", "thinking", "angry", "pointing", "happy" } },
                    { "Miles", new[] { "normal", "smirk", "desk-slam" } },
                    { "Franziska", new[] { "normal", "mad", "whip" } }
                },
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

        [Test]
        public void Validate_CommandIntentMissingTextColorAction_IsRejected()
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
                        Message = "done"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "set text color to cyan",
                TriggeringChannel = "OOC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("set_text_color='cyan'"));
        }

        [Test]
        public void Validate_ExplicitOocRequest_NormalizesSpeakChannel()
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
                        Message = "brb"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "say it in OOC",
                TriggeringChannel = "IC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.True);
            Assert.That(response.Actions[0].Channel, Is.EqualTo("OOC"));
        }

        [Test]
        public void Validate_TriggerChannelDefaultsSpeakChannel()
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
                        Message = "brb"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "brb?",
                TriggeringChannel = "OOC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.True);
            Assert.That(response.Actions[0].Channel, Is.EqualTo("OOC"));
        }

        [Test]
        public void Validate_CharacterSwitchUsesTargetCharacterEmotes()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetCharacter, Value = "Miles" },
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "IC",
                        Message = "interesting",
                        Emote = "smirk"
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validate_CharacterSwitchRejectsOldCharacterEmotes()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetCharacter, Value = "Miles" },
                    new AgentAction
                    {
                        Type = AgentActionType.Speak,
                        Channel = "IC",
                        Message = "interesting",
                        Emote = "thinking"
                    }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Emote 'thinking' is not in the available emotes list"));
        }

        [Test]
        public void Validate_ShoutKeywordInSetEmote_IsRejected()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetEmote, Value = "objection" }
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot());

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("shout modifier"));
        }

        [Test]
        public void Validate_RetryLoopPhraseOnSecondAttempt_IsRejected()
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
                        Message = "I think I understand now"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "say hi in OOC",
                TriggeringChannel = "OOC",
                AttemptNumber = 2
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Retry/meta filler"));
        }

        [Test]
        public void Validate_ActiveLowercaseRule_IsEnforced()
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
                        Message = "HELLO"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "reply in OOC",
                TriggeringChannel = "OOC",
                ActiveRules = new[] { "always speak in lowercase" }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("lowercase"));
        }

        [Test]
        public void Validate_ActiveNoSymbolsRule_IsEnforced()
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
                        Message = "hello!"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "reply in OOC",
                TriggeringChannel = "OOC",
                ActiveRules = new[] { "always use no symbols" }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("symbols"));
        }

        [Test]
        public void Validate_ExplicitFromOocPhrase_NormalizesSpeakChannel()
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
                        Message = "here"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "tell me something from ooc",
                TriggeringChannel = "IC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.True);
            Assert.That(response.Actions[0].Channel, Is.EqualTo("OOC"));
        }

        [Test]
        public void Validate_ExplicitInIcPhrase_NormalizesSpeakChannel()
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
                        Message = "here",
                        Emote = "normal"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "reply in ic",
                TriggeringChannel = "OOC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.True);
            Assert.That(response.Actions[0].Channel, Is.EqualTo("IC"));
        }

        [Test]
        public void Validate_HardSilenceDirective_RequiresShouldRespondFalse()
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
                        Message = "hello",
                        Emote = "normal"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "stay completely silent and dont respond",
                TriggeringChannel = "IC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("required silence"));
        }

        [Test]
        public void Validate_ExplicitActionCommand_CannotReturnShouldRespondFalse()
        {
            AgentResponse response = new AgentResponse
            {
                ShouldRespond = false,
                Actions = new List<AgentAction>()
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "set text color to cyan",
                TriggeringChannel = "OOC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("explicit control request"));
        }

        [Test]
        public void Validate_ImmediatelySpeak_DoesNotRequireImmediateCheckbox()
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
                        Message = "right now"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "immediately speak in OOC",
                TriggeringChannel = "OOC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validate_UseValidNewEmote_IsSatisfiedByIcSpeakEmote()
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
                        Message = "testing",
                        Emote = "thinking"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "use a valid new emote",
                TriggeringChannel = "IC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validate_TakeThatShout_DoesNotRequireSeparateEmoteChange()
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
                        Message = "Take that!",
                        Emote = "pointing",
                        ShoutModifier = "takeThat"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "use shout take that",
                TriggeringChannel = "IC"
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validate_RecentSelfNearDuplicate_IsRejected()
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
                        Message = "i am feeling anxious about this situation"
                    }
                }
            };

            ValidationContext context = new ValidationContext
            {
                TriggeringMessage = "reply in OOC",
                TriggeringChannel = "OOC",
                RecentSelfMessages = new[]
                {
                    "I am feeling anxious about this situation..."
                }
            };

            AgentResponseValidator.ValidationResult result =
                AgentResponseValidator.Validate(response, CreateSnapshot(), context);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("near-duplicate"));
        }
    }
}
