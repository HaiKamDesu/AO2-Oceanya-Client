using System.Text.RegularExpressions;

namespace AO2AIBot.Controller
{
    /// <summary>
    /// Additional context for validation beyond the snapshot.
    /// Carries the triggering message and channel for command-intent and channel-obedience checks.
    /// </summary>
    public sealed class ValidationContext
    {
        /// <summary>
        /// Gets or sets the raw text of the message that triggered this evaluation.
        /// </summary>
        public string TriggeringMessage { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the channel of the triggering message ("IC" or "OOC").
        /// </summary>
        public string TriggeringChannel { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the 1-based retry attempt number for the current evaluation.
        /// </summary>
        public int AttemptNumber { get; set; } = 1;

        /// <summary>
        /// Gets or sets the currently active standing rules.
        /// </summary>
        public IReadOnlyList<string> ActiveRules { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets recent messages previously sent by the controlled client.
        /// Used to block repetitive fallback output.
        /// </summary>
        public IReadOnlyList<string> RecentSelfMessages { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Validates a parsed <see cref="AgentResponse"/> against authoritative runtime state
    /// before execution. Rejects invalid, incomplete, or hallucinated actions.
    /// </summary>
    public static class AgentResponseValidator
    {
        /// <summary>
        /// Result of a validation pass.
        /// </summary>
        public sealed class ValidationResult
        {
            /// <summary>
            /// Gets a value indicating whether the response passed validation.
            /// </summary>
            public bool IsValid { get; }

            /// <summary>
            /// Gets the list of validation errors, empty when valid.
            /// </summary>
            public IReadOnlyList<string> Errors { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="ValidationResult"/> class.
            /// </summary>
            public ValidationResult(bool isValid, IReadOnlyList<string> errors)
            {
                IsValid = isValid;
                Errors = errors ?? Array.Empty<string>();
            }

            /// <summary>
            /// A successful validation result.
            /// </summary>
            public static ValidationResult Valid { get; } = new ValidationResult(true, Array.Empty<string>());
        }

        private sealed class ValidationState
        {
            public string CurrentCharacter { get; set; } = string.Empty;

            public IReadOnlyList<string> AvailableEmotes { get; set; } = Array.Empty<string>();
        }

        private sealed class CommandRequirement
        {
            public CommandRequirement(string description, Func<AgentResponse, bool> isSatisfied, string failureMessage)
            {
                Description = description;
                IsSatisfied = isSatisfied;
                FailureMessage = failureMessage;
            }

            public string Description { get; }

            public Func<AgentResponse, bool> IsSatisfied { get; }

            public string FailureMessage { get; }
        }

        /// <summary>
        /// Deterministic alias map for known safe value normalizations.
        /// Key = common alias (lowercase), Value = canonical value.
        /// </summary>
        private static readonly Dictionary<string, string> PositionAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "defense", "def" },
            { "defence", "def" },
            { "prosecution", "pro" },
            { "witness", "wit" },
            { "judge", "jud" },
            { "seance", "sea" },
            { "gallery", "sea" },
            { "audience", "sea" }
        };

        private static readonly Dictionary<string, string> ShoutAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "hold it", "holdIt" },
            { "hold_it", "holdIt" },
            { "holdit", "holdIt" },
            { "take that", "takeThat" },
            { "take_that", "takeThat" },
            { "takethat", "takeThat" },
            { "objection!", "objection" },
            { "none", "nothing" },
            { "off", "nothing" },
            { "no shout", "nothing" }
        };

        private static readonly Dictionary<string, string> EffectAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "flash", "realization" },
            { "off", "none" }
        };

        private static readonly HashSet<string> ValidChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IC",
            "OOC"
        };

        private static readonly HashSet<string> ValidTextColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "white",
            "green",
            "red",
            "orange",
            "blue",
            "yellow",
            "magenta",
            "cyan",
            "gray"
        };

        private static readonly HashSet<string> ValidShoutModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "nothing",
            "holdIt",
            "objection",
            "takeThat",
            "custom"
        };

        private static readonly HashSet<string> ValidEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "none",
            "realization",
            "hearts",
            "reaction",
            "impact"
        };

        private static readonly HashSet<string> ValidDeskMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hidden",
            "shown"
        };

        private static readonly string[] CommandVerbs =
        {
            "set",
            "change",
            "switch",
            "make",
            "use",
            "turn",
            "enable",
            "disable",
            "move",
            "go",
            "flip",
            "mirror",
            "respond",
            "reply",
            "say",
            "speak",
            "write",
            "type"
        };

        private static readonly Regex RetryLoopPhrasePattern = new Regex(
            @"\b(?:i(?:'| a)?ll try again|let me try again|i think i understand now|i understand now|glad i could finally|get it working|finally got it working|thanks for your patience|sorry about that)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NoSymbolsPattern = new Regex(@"[^\p{L}\p{Nd}\s]", RegexOptions.Compiled);
        private static readonly Regex SilenceDirectivePattern = new Regex(
            @"\b(?:stay(?:\s+completely)?\s+silent|be\s+silent|stay\s+quiet|say\s+nothing|do\s+not\s+(?:respond|reply|answer)|don't\s+(?:respond|reply|answer)|dont\s+(?:respond|reply|answer)|no\s+response)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Validates a parsed response against the current authoritative snapshot.
        /// Normalizes known aliases in-place where deterministically safe.
        /// </summary>
        public static ValidationResult Validate(AgentResponse response, AOClientControlSnapshot snapshot, ValidationContext? context = null)
        {
            if (response == null)
            {
                return Fail("Response is null.");
            }

            if (snapshot == null)
            {
                return Fail("Snapshot is null.");
            }

            if (context != null)
            {
                EnforceExpectedChannel(response, context);
            }

            List<string> errors = new List<string>();

            if (!response.ShouldRespond && response.Actions.Count > 0)
            {
                errors.Add("shouldRespond is false but actions array is non-empty.");
            }

            if (response.ShouldRespond && response.Actions.Count == 0)
            {
                errors.Add("shouldRespond is true but actions array is empty.");
            }

            if (context != null && ShouldForceSilence(context))
            {
                if (response.ShouldRespond)
                {
                    errors.Add("Latest player message explicitly required silence, so shouldRespond must be false.");
                }

                if (errors.Count > 0)
                {
                    return new ValidationResult(false, errors);
                }

                return ValidationResult.Valid;
            }

            ValidationState validationState = new ValidationState
            {
                CurrentCharacter = snapshot.CurrentCharacter,
                AvailableEmotes = snapshot.AvailableEmotes
            };

            for (int i = 0; i < response.Actions.Count; i++)
            {
                AgentAction action = response.Actions[i];
                string prefix = $"Action[{i}] ({action.Type}): ";

                if (!AgentActionType.All.Contains(action.Type))
                {
                    errors.Add(prefix + $"Unknown action type '{action.Type}'.");
                    continue;
                }

                ValidateAction(action, prefix, snapshot, validationState.AvailableEmotes, errors);

                if (string.Equals(action.Type, AgentActionType.SetCharacter, StringComparison.Ordinal)
                    && TryResolveCharacter(action.Value, snapshot, out string resolvedCharacter))
                {
                    validationState.CurrentCharacter = resolvedCharacter;
                    validationState.AvailableEmotes = GetAvailableEmotesForCharacter(snapshot, resolvedCharacter);
                }
            }

            if (context != null)
            {
                ValidateCommandIntent(response, snapshot, context, errors);
                ValidateActiveRuleConstraints(response, context, errors);
                ValidateRetryLoopPhrases(response, context, errors);
                ValidateAgainstRecentSelfMessages(response, context, errors);
            }

            if (errors.Count > 0)
            {
                return new ValidationResult(false, errors);
            }

            return ValidationResult.Valid;
        }

        private static void ValidateAction(
            AgentAction action,
            string prefix,
            AOClientControlSnapshot snapshot,
            IReadOnlyList<string> availableEmotes,
            List<string> errors)
        {
            switch (action.Type)
            {
                case AgentActionType.Speak:
                    ValidateSpeak(action, prefix, snapshot, availableEmotes, errors);
                    break;

                case AgentActionType.SetCharacter:
                    if (!TryResolveCharacter(action.Value, snapshot, out string resolvedChar))
                    {
                        errors.Add(prefix + $"Character '{action.Value}' is not in the available characters list.");
                    }
                    else
                    {
                        action.Value = resolvedChar;
                    }

                    break;

                case AgentActionType.SetEmote:
                    if (TryNormalizeShoutModifier(action.Value, out string shoutAsEmote))
                    {
                        errors.Add(
                            prefix
                            + $"'{action.Value}' is a shout modifier, not an emote. Use set_shout_modifier or speak.shoutModifier='{shoutAsEmote}'.");
                        break;
                    }

                    if (!TryResolveExact(action.Value, availableEmotes, out string resolvedEmote))
                    {
                        errors.Add(prefix + $"Emote '{action.Value}' is not in the available emotes list.");
                    }
                    else
                    {
                        action.Value = resolvedEmote;
                    }

                    break;

                case AgentActionType.SetPosition:
                    action.Value = NormalizeAlias(action.Value, PositionAliases);
                    if (!TryResolveExact(action.Value, snapshot.AvailablePositions, out string resolvedPos))
                    {
                        errors.Add(prefix + $"Position '{action.Value}' is not in the available positions list.");
                    }
                    else
                    {
                        action.Value = resolvedPos;
                    }

                    break;

                case AgentActionType.SetArea:
                    if (!TryResolveExact(action.Value, snapshot.AvailableAreas, out string resolvedArea))
                    {
                        errors.Add(prefix + $"Area '{action.Value}' is not in the available areas list.");
                    }
                    else
                    {
                        action.Value = resolvedArea;
                    }

                    break;

                case AgentActionType.SetIniPuppet:
                    if (!TryResolveExact(action.Value, snapshot.AvailableIniPuppets.Keys, out string resolvedPuppet))
                    {
                        errors.Add(prefix + $"INI puppet '{action.Value}' is not in the available puppets list.");
                    }
                    else
                    {
                        action.Value = resolvedPuppet;
                    }

                    break;

                case AgentActionType.SetSfx:
                    if (!TryResolveExact(action.Value, snapshot.AvailableSfx, out string resolvedSfx))
                    {
                        errors.Add(prefix + $"SFX '{action.Value}' is not in the available SFX list.");
                    }
                    else
                    {
                        action.Value = resolvedSfx;
                    }

                    break;

                case AgentActionType.SetTextColor:
                    if (!TryResolveTextColor(action.Value, out string resolvedTextColor))
                    {
                        errors.Add(prefix + $"Text color '{action.Value}' is not a valid color. Valid: {string.Join(", ", ValidTextColors)}.");
                    }
                    else
                    {
                        action.Value = resolvedTextColor;
                    }

                    break;

                case AgentActionType.SetShoutModifier:
                    if (!TryNormalizeShoutModifier(action.Value, out string resolvedShout))
                    {
                        errors.Add(prefix + $"Shout modifier '{action.Value}' is not valid. Valid: {string.Join(", ", ValidShoutModifiers)}.");
                    }
                    else
                    {
                        action.Value = resolvedShout;
                    }

                    break;

                case AgentActionType.SetEffect:
                    if (!TryNormalizeEffect(action.Value, out string resolvedEffect))
                    {
                        errors.Add(prefix + $"Effect '{action.Value}' is not valid. Valid: {string.Join(", ", ValidEffects)}.");
                    }
                    else
                    {
                        action.Value = resolvedEffect;
                    }

                    break;

                case AgentActionType.SetDeskMod:
                    if (!ValidDeskMods.Contains(action.Value))
                    {
                        errors.Add(prefix + $"Desk mod '{action.Value}' is not valid. Valid: {string.Join(", ", ValidDeskMods)}.");
                    }

                    break;

                case AgentActionType.SetIcShowname:
                case AgentActionType.SetOocShowname:
                case AgentActionType.SetFlip:
                case AgentActionType.SetAdditive:
                case AgentActionType.SetImmediate:
                case AgentActionType.SetPreanimEnabled:
                case AgentActionType.SetScreenshake:
                case AgentActionType.SetEmoteModifier:
                case AgentActionType.SetOffset:
                    break;
            }
        }

        private static void ValidateSpeak(
            AgentAction action,
            string prefix,
            AOClientControlSnapshot snapshot,
            IReadOnlyList<string> availableEmotes,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(action.Channel))
            {
                errors.Add(prefix + "Missing 'channel' field. Must be 'IC' or 'OOC'.");
                return;
            }

            if (!ValidChannels.Contains(action.Channel))
            {
                errors.Add(prefix + $"Invalid channel '{action.Channel}'. Must be 'IC' or 'OOC'.");
                return;
            }

            if (string.IsNullOrWhiteSpace(action.Message))
            {
                errors.Add(prefix + "Missing or empty 'message' field.");
                return;
            }

            if (string.Equals(action.Channel, "IC", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(action.Emote))
                {
                    errors.Add(prefix + "IC speak requires an explicit 'emote' field. Choose from available emotes.");
                    return;
                }

                if (TryNormalizeShoutModifier(action.Emote, out string shoutAsEmote)
                    && !TryResolveExact(action.Emote, availableEmotes, out _))
                {
                    errors.Add(
                        prefix
                        + $"'{action.Emote}' is a shout modifier, not an IC emote. Use shoutModifier='{shoutAsEmote}' and choose a valid emote.");
                    return;
                }

                if (!TryResolveExact(action.Emote, availableEmotes, out string resolvedEmote))
                {
                    errors.Add(prefix + $"Emote '{action.Emote}' is not in the available emotes list.");
                }
                else
                {
                    action.Emote = resolvedEmote;
                }
            }

            if (!string.IsNullOrWhiteSpace(action.TextColor))
            {
                if (!TryResolveTextColor(action.TextColor, out string resolvedColor))
                {
                    errors.Add(prefix + $"Text color '{action.TextColor}' is not valid.");
                }
                else
                {
                    action.TextColor = resolvedColor;
                }
            }

            if (!string.IsNullOrWhiteSpace(action.ShoutModifier))
            {
                if (!TryNormalizeShoutModifier(action.ShoutModifier, out string resolvedShout))
                {
                    errors.Add(prefix + $"Shout modifier '{action.ShoutModifier}' is not valid.");
                }
                else
                {
                    action.ShoutModifier = resolvedShout;
                }
            }

            if (!string.IsNullOrWhiteSpace(action.Effect))
            {
                if (!TryNormalizeEffect(action.Effect, out string resolvedEffect))
                {
                    errors.Add(prefix + $"Effect '{action.Effect}' is not valid.");
                }
                else
                {
                    action.Effect = resolvedEffect;
                }
            }

            if (!string.IsNullOrWhiteSpace(action.Sfx) && snapshot.AvailableSfx.Count > 0)
            {
                if (!TryResolveExact(action.Sfx, snapshot.AvailableSfx, out string resolvedSfx))
                {
                    errors.Add(prefix + $"SFX '{action.Sfx}' is not in the available SFX list.");
                }
                else
                {
                    action.Sfx = resolvedSfx;
                }
            }
        }

        private static void EnforceExpectedChannel(AgentResponse response, ValidationContext context)
        {
            string expectedChannel = ResolveExpectedChannel(context);
            if (string.IsNullOrWhiteSpace(expectedChannel))
            {
                return;
            }

            foreach (AgentAction action in response.Actions)
            {
                if (string.Equals(action.Type, AgentActionType.Speak, StringComparison.Ordinal))
                {
                    action.Channel = expectedChannel;
                }
            }
        }

        private static void ValidateCommandIntent(
            AgentResponse response,
            AOClientControlSnapshot snapshot,
            ValidationContext context,
            List<string> errors)
        {
            List<CommandRequirement> requirements = BuildCommandRequirements(snapshot, context);
            if (requirements.Count == 0)
            {
                return;
            }

            if (!response.ShouldRespond)
            {
                errors.Add("Latest player message contained an explicit control request, but the response stayed silent.");
                return;
            }

            foreach (CommandRequirement requirement in requirements)
            {
                if (!requirement.IsSatisfied(response))
                {
                    errors.Add(requirement.FailureMessage);
                }
            }
        }

        private static void ValidateActiveRuleConstraints(
            AgentResponse response,
            ValidationContext context,
            List<string> errors)
        {
            if (context.ActiveRules == null || context.ActiveRules.Count == 0)
            {
                return;
            }

            bool requiresLowercase = context.ActiveRules.Any(rule =>
                rule.IndexOf("lowercase", StringComparison.OrdinalIgnoreCase) >= 0);
            bool disallowsSymbols = context.ActiveRules.Any(rule =>
                rule.IndexOf("no symbols", StringComparison.OrdinalIgnoreCase) >= 0
                || rule.IndexOf("without symbols", StringComparison.OrdinalIgnoreCase) >= 0
                || rule.IndexOf("no punctuation", StringComparison.OrdinalIgnoreCase) >= 0
                || rule.IndexOf("without punctuation", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!requiresLowercase && !disallowsSymbols)
            {
                return;
            }

            foreach (AgentAction action in response.Actions.Where(a => string.Equals(a.Type, AgentActionType.Speak, StringComparison.Ordinal)))
            {
                if (requiresLowercase
                    && action.Message.Any(char.IsLetter)
                    && !string.Equals(action.Message, action.Message.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    errors.Add("Active rules require lowercase output, but the speak message contains uppercase characters.");
                }

                if (disallowsSymbols && NoSymbolsPattern.IsMatch(action.Message))
                {
                    errors.Add("Active rules disallow symbols/punctuation, but the speak message contains them.");
                }
            }
        }

        private static void ValidateRetryLoopPhrases(
            AgentResponse response,
            ValidationContext context,
            List<string> errors)
        {
            if (context.AttemptNumber <= 1)
            {
                return;
            }

            foreach (AgentAction action in response.Actions.Where(a => string.Equals(a.Type, AgentActionType.Speak, StringComparison.Ordinal)))
            {
                if (RetryLoopPhrasePattern.IsMatch(action.Message))
                {
                    errors.Add("Retry/meta filler is not allowed after a validation failure. Emit the required actions or stay silent.");
                    return;
                }
            }
        }

        private static List<CommandRequirement> BuildCommandRequirements(AOClientControlSnapshot snapshot, ValidationContext context)
        {
            string message = context.TriggeringMessage ?? string.Empty;
            string lower = message.ToLowerInvariant();
            List<CommandRequirement> requirements = new List<CommandRequirement>();

            if (string.IsNullOrWhiteSpace(lower) || ShouldForceSilence(context))
            {
                return requirements;
            }

            if (TryExtractRequestedTextColor(lower, out string requestedColor))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set text color",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetTextColor, StringComparison.Ordinal)
                            && string.Equals(action.Value, requestedColor, StringComparison.OrdinalIgnoreCase)),
                        $"Player explicitly requested text color '{requestedColor}', so the response must include set_text_color='{requestedColor}'."));
            }

            if (TryExtractRequestedFlip(lower, out bool requestedFlip))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set flip",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetFlip, StringComparison.Ordinal)
                            && action.BoolValue == requestedFlip),
                        $"Player explicitly requested flip={requestedFlip.ToString().ToLowerInvariant()}, so the response must include set_flip."));
            }

            if (TryExtractRequestedAdditive(lower, out bool additiveValue))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set additive",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetAdditive, StringComparison.Ordinal)
                            && action.BoolValue == additiveValue),
                        $"Player explicitly requested additive={additiveValue.ToString().ToLowerInvariant()}, so the response must include set_additive."));
            }

            if (TryExtractRequestedImmediate(lower, out bool immediateValue))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set immediate",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetImmediate, StringComparison.Ordinal)
                            && action.BoolValue == immediateValue),
                        $"Player explicitly requested immediate={immediateValue.ToString().ToLowerInvariant()}, so the response must include set_immediate."));
            }

            if (TryExtractRequestedPreanim(lower, out bool preanimValue))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set preanimation",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetPreanimEnabled, StringComparison.Ordinal)
                            && action.BoolValue == preanimValue),
                        $"Player explicitly requested preanim_enabled={preanimValue.ToString().ToLowerInvariant()}, so the response must include set_preanim_enabled."));
            }

            if (TryExtractRequestedPosition(lower, snapshot, out string requestedPosition))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set position",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetPosition, StringComparison.Ordinal)
                            && string.Equals(action.Value, requestedPosition, StringComparison.OrdinalIgnoreCase)),
                        $"Player explicitly requested position '{requestedPosition}', so the response must include set_position='{requestedPosition}'."));
            }
            else if (IsGenericPositionCommand(lower))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set position",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetPosition, StringComparison.Ordinal)),
                        "Player explicitly requested a position change, so the response must include set_position."));
            }

            if (TryExtractRequestedCharacter(lower, snapshot.AvailableCharacters, out string requestedCharacter))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set character",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetCharacter, StringComparison.Ordinal)
                            && string.Equals(action.Value, requestedCharacter, StringComparison.OrdinalIgnoreCase)),
                        $"Player explicitly requested character '{requestedCharacter}', so the response must include set_character='{requestedCharacter}'."));
            }
            else if (IsGenericCharacterCommand(lower))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set character",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetCharacter, StringComparison.Ordinal)),
                        "Player explicitly requested a character switch, so the response must include set_character."));
            }

            if (TryExtractRequestedIniPuppet(lower, snapshot.AvailableIniPuppets.Keys, out string requestedPuppet))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set INI puppet",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetIniPuppet, StringComparison.Ordinal)
                            && string.Equals(action.Value, requestedPuppet, StringComparison.OrdinalIgnoreCase)),
                        $"Player explicitly requested INI puppet '{requestedPuppet}', so the response must include set_ini_puppet='{requestedPuppet}'."));
            }
            else if (IsGenericIniPuppetCommand(lower))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set INI puppet",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.SetIniPuppet, StringComparison.Ordinal)),
                        "Player explicitly requested an INI puppet change, so the response must include set_ini_puppet."));
            }

            if (TryExtractRequestedEffect(lower, out string requestedEffect))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set effect",
                        response => response.Actions.Any(action =>
                                string.Equals(action.Type, AgentActionType.SetEffect, StringComparison.Ordinal)
                                && string.Equals(action.Value, requestedEffect, StringComparison.OrdinalIgnoreCase))
                            || response.Actions.Any(action =>
                                string.Equals(action.Type, AgentActionType.Speak, StringComparison.Ordinal)
                                && string.Equals(action.Effect, requestedEffect, StringComparison.OrdinalIgnoreCase)),
                        $"Player explicitly requested effect '{requestedEffect}', so the response must include set_effect or speak.effect='{requestedEffect}'."));
            }

            if (TryExtractRequestedShoutModifier(lower, out string requestedShout))
            {
                requirements.Add(
                    new CommandRequirement(
                        "set shout modifier",
                        response => response.Actions.Any(action =>
                                string.Equals(action.Type, AgentActionType.SetShoutModifier, StringComparison.Ordinal)
                                && string.Equals(action.Value, requestedShout, StringComparison.OrdinalIgnoreCase))
                            || response.Actions.Any(action =>
                                string.Equals(action.Type, AgentActionType.Speak, StringComparison.Ordinal)
                                && string.Equals(action.ShoutModifier, requestedShout, StringComparison.OrdinalIgnoreCase)),
                        $"Player explicitly requested shout modifier '{requestedShout}', so the response must include set_shout_modifier or speak.shoutModifier='{requestedShout}'."));
            }

            if (TryExtractRequestedEmote(lower, snapshot.AvailableEmotes, out string requestedEmote))
            {
                requirements.Add(
                    new CommandRequirement(
                        "use requested emote",
                        response => HasRequestedEmote(response, requestedEmote),
                        $"Player explicitly requested emote '{requestedEmote}', so the response must include set_emote='{requestedEmote}' or an IC speak action with emote='{requestedEmote}'."));
            }
            else if (IsGenericEmoteCommand(lower))
            {
                bool requireDifferentEmote = RequestsDifferentEmote(lower, snapshot.CurrentEmote);
                requirements.Add(
                    new CommandRequirement(
                        "use a valid emote",
                        response => HasAnyRequestedEmote(response, requireDifferentEmote, snapshot.CurrentEmote),
                        requireDifferentEmote
                            ? "Player explicitly requested a valid new emote, so the response must include set_emote or an IC speak action with a valid emote different from the current one."
                            : "Player explicitly requested an emote change, so the response must include set_emote or an IC speak action with a valid explicit emote."));
            }

            if (!string.IsNullOrWhiteSpace(ResolveExpectedChannel(context)) && IsExplicitSpeechRequest(lower))
            {
                string expectedChannel = ResolveExpectedChannel(context);
                requirements.Add(
                    new CommandRequirement(
                        "speak on the requested channel",
                        response => response.Actions.Any(action =>
                            string.Equals(action.Type, AgentActionType.Speak, StringComparison.Ordinal)
                            && string.Equals(action.Channel, expectedChannel, StringComparison.OrdinalIgnoreCase)),
                        $"Player explicitly requested a spoken reply on channel '{expectedChannel}', so the response must include a speak action on that channel."));
            }

            return requirements;
        }

        /// <summary>
        /// Attempts exact case-insensitive match against an available list.
        /// No fuzzy/substring matching.
        /// </summary>
        private static bool TryResolveExact(string requested, IEnumerable<string> available, out string resolved)
        {
            resolved = string.Empty;
            if (string.IsNullOrWhiteSpace(requested))
            {
                return false;
            }

            string trimmed = requested.Trim();
            foreach (string item in available)
            {
                if (string.Equals(item.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    resolved = item;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveCharacter(string requested, AOClientControlSnapshot snapshot, out string resolvedCharacter)
        {
            return TryResolveExact(requested, snapshot.AvailableCharacters, out resolvedCharacter);
        }

        private static IReadOnlyList<string> GetAvailableEmotesForCharacter(AOClientControlSnapshot snapshot, string characterName)
        {
            if (!string.IsNullOrWhiteSpace(characterName)
                && snapshot.AvailableCharacterEmotes.TryGetValue(characterName, out IReadOnlyList<string>? emotes)
                && emotes != null
                && emotes.Count > 0)
            {
                return emotes;
            }

            return snapshot.AvailableEmotes;
        }

        /// <summary>
        /// Applies a deterministic alias normalization if the value matches a known alias.
        /// </summary>
        private static string NormalizeAlias(string value, Dictionary<string, string> aliases)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (aliases.TryGetValue(value.Trim(), out string? canonical))
            {
                return canonical;
            }

            return value.Trim();
        }

        private static bool TryResolveTextColor(string value, out string resolvedColor)
        {
            resolvedColor = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (!ValidTextColors.Contains(trimmed))
            {
                return false;
            }

            resolvedColor = ValidTextColors.First(color => string.Equals(color, trimmed, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        private static bool TryNormalizeShoutModifier(string value, out string resolvedShout)
        {
            resolvedShout = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = NormalizeAlias(value, ShoutAliases);
            if (!ValidShoutModifiers.Contains(normalized))
            {
                return false;
            }

            resolvedShout = ValidShoutModifiers.First(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        private static bool TryNormalizeEffect(string value, out string resolvedEffect)
        {
            resolvedEffect = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = NormalizeAlias(value, EffectAliases);
            if (!ValidEffects.Contains(normalized))
            {
                return false;
            }

            resolvedEffect = ValidEffects.First(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        private static string ResolveExpectedChannel(ValidationContext context)
        {
            if (TryResolveExplicitChannelRequest(context.TriggeringMessage, out string explicitChannel))
            {
                return explicitChannel;
            }

            if (ValidChannels.Contains(context.TriggeringChannel ?? string.Empty))
            {
                return string.Equals(context.TriggeringChannel, "OOC", StringComparison.OrdinalIgnoreCase)
                    ? "OOC"
                    : "IC";
            }

            return string.Empty;
        }

        private static bool TryExtractRequestedTextColor(string lowerMessage, out string requestedColor)
        {
            requestedColor = string.Empty;
            if (!ContainsCommandVerb(lowerMessage)
                || (lowerMessage.IndexOf("color", StringComparison.OrdinalIgnoreCase) < 0
                    && lowerMessage.IndexOf("text", StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            foreach (string color in ValidTextColors)
            {
                if (ContainsPhrase(lowerMessage, color))
                {
                    requestedColor = color;
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractRequestedFlip(string lowerMessage, out bool requestedFlip)
        {
            requestedFlip = true;
            if (lowerMessage.IndexOf("flip", StringComparison.OrdinalIgnoreCase) < 0
                && lowerMessage.IndexOf("mirror", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            requestedFlip =
                lowerMessage.IndexOf("unflip", StringComparison.OrdinalIgnoreCase) < 0
                && lowerMessage.IndexOf("stop flipping", StringComparison.OrdinalIgnoreCase) < 0
                && lowerMessage.IndexOf("disable flip", StringComparison.OrdinalIgnoreCase) < 0
                && lowerMessage.IndexOf("turn off flip", StringComparison.OrdinalIgnoreCase) < 0;
            return true;
        }

        private static bool TryExtractRequestedAdditive(string lowerMessage, out bool requestedValue)
        {
            return TryExtractRequestedControlToggle(lowerMessage, "additive", out requestedValue);
        }

        private static bool TryExtractRequestedImmediate(string lowerMessage, out bool requestedValue)
        {
            return TryExtractRequestedControlToggle(lowerMessage, "immediate", out requestedValue);
        }

        private static bool TryExtractRequestedPreanim(string lowerMessage, out bool requestedValue)
        {
            return TryExtractRequestedControlToggle(
                lowerMessage,
                "(?:preanim|pre animation|pre-animation)",
                out requestedValue,
                useRawPattern: true);
        }

        private static bool TryExtractRequestedControlToggle(
            string lowerMessage,
            string controlName,
            out bool requestedValue,
            bool useRawPattern = false)
        {
            requestedValue = true;
            if (string.IsNullOrWhiteSpace(lowerMessage))
            {
                return false;
            }

            string controlPattern = useRawPattern ? controlName : Regex.Escape(controlName);
            Regex requestPattern = new Regex(
                $@"\b(?:set|turn|switch|make|enable|disable)\b(?:\s+\w+){{0,4}}\s+(?:the\s+)?{controlPattern}\b(?:\s+(?:checkbox|mode|setting|toggle))?(?:\s+to)?(?:\s+(?:on|off|true|false|enabled|disabled))?|\b{controlPattern}\b(?:\s+(?:checkbox|mode|setting|toggle))\b(?:\s+(?:is|to))?\s*(?:on|off|true|false|enabled|disabled)?",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            if (!requestPattern.IsMatch(lowerMessage))
            {
                return false;
            }

            Regex negativePattern = new Regex(
                $@"\b(?:disable|turn off|off|false|disabled)\b(?:\s+\w+){{0,4}}\s+{controlPattern}\b|\b{controlPattern}\b(?:\s+\w+){{0,3}}\s+\b(?:off|false|disabled)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            requestedValue = !negativePattern.IsMatch(lowerMessage);
            return true;
        }

        private static bool TryExtractRequestedPosition(string lowerMessage, AOClientControlSnapshot snapshot, out string requestedPosition)
        {
            requestedPosition = string.Empty;
            if (!IsGenericPositionCommand(lowerMessage))
            {
                return false;
            }

            foreach (string position in snapshot.AvailablePositions)
            {
                if (ContainsPhrase(lowerMessage, position))
                {
                    requestedPosition = position;
                    return true;
                }
            }

            foreach (KeyValuePair<string, string> alias in PositionAliases)
            {
                if (ContainsPhrase(lowerMessage, alias.Key)
                    && snapshot.AvailablePositions.Any(position =>
                        string.Equals(position, alias.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    requestedPosition = snapshot.AvailablePositions.First(position =>
                        string.Equals(position, alias.Value, StringComparison.OrdinalIgnoreCase));
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractRequestedCharacter(
            string lowerMessage,
            IEnumerable<string> availableCharacters,
            out string requestedCharacter)
        {
            requestedCharacter = string.Empty;
            if (!IsGenericCharacterCommand(lowerMessage))
            {
                return false;
            }

            foreach (string character in availableCharacters.OrderByDescending(value => value.Length))
            {
                if (ContainsPhrase(lowerMessage, character))
                {
                    requestedCharacter = character;
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractRequestedIniPuppet(
            string lowerMessage,
            IEnumerable<string> availablePuppets,
            out string requestedPuppet)
        {
            requestedPuppet = string.Empty;
            if (!IsGenericIniPuppetCommand(lowerMessage))
            {
                return false;
            }

            foreach (string puppet in availablePuppets.OrderByDescending(value => value.Length))
            {
                if (ContainsPhrase(lowerMessage, puppet))
                {
                    requestedPuppet = puppet;
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractRequestedEffect(string lowerMessage, out string requestedEffect)
        {
            requestedEffect = string.Empty;
            if (!ContainsCommandVerb(lowerMessage)
                && lowerMessage.IndexOf("effect", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            foreach (string effect in ValidEffects)
            {
                if (string.Equals(effect, "none", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ContainsPhrase(lowerMessage, effect))
                {
                    requestedEffect = effect;
                    return true;
                }
            }

            if (ContainsPhrase(lowerMessage, "flash"))
            {
                requestedEffect = "realization";
                return true;
            }

            if (ContainsPhrase(lowerMessage, "turn off effect")
                || ContainsPhrase(lowerMessage, "disable effect")
                || ContainsPhrase(lowerMessage, "no effect"))
            {
                requestedEffect = "none";
                return true;
            }

            return false;
        }

        private static bool TryExtractRequestedShoutModifier(string lowerMessage, out string requestedShout)
        {
            requestedShout = string.Empty;
            bool soundsLikeShoutCommand =
                lowerMessage.IndexOf("shout", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf("hold it", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf("holdit", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf("objection", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf("take that", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf("takethat", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!soundsLikeShoutCommand || !ContainsCommandVerb(lowerMessage))
            {
                return false;
            }

            if (ContainsPhrase(lowerMessage, "hold it") || ContainsPhrase(lowerMessage, "holdit"))
            {
                requestedShout = "holdIt";
                return true;
            }

            if (ContainsPhrase(lowerMessage, "objection"))
            {
                requestedShout = "objection";
                return true;
            }

            if (ContainsPhrase(lowerMessage, "take that") || ContainsPhrase(lowerMessage, "takethat"))
            {
                requestedShout = "takeThat";
                return true;
            }

            if (ContainsPhrase(lowerMessage, "no shout")
                || ContainsPhrase(lowerMessage, "disable shout")
                || ContainsPhrase(lowerMessage, "turn off shout"))
            {
                requestedShout = "nothing";
                return true;
            }

            return false;
        }

        private static bool TryExtractRequestedEmote(
            string lowerMessage,
            IEnumerable<string> availableEmotes,
            out string requestedEmote)
        {
            requestedEmote = string.Empty;
            if (!IsGenericEmoteCommand(lowerMessage))
            {
                return false;
            }

            foreach (string emote in availableEmotes.OrderByDescending(value => value.Length))
            {
                if (ContainsPhrase(lowerMessage, emote))
                {
                    requestedEmote = emote;
                    return true;
                }
            }

            return false;
        }

        private static bool IsGenericPositionCommand(string lowerMessage)
        {
            return ContainsCommandVerb(lowerMessage)
                && (lowerMessage.IndexOf("position", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf(" pos", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf("move to", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf("stand at", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsGenericCharacterCommand(string lowerMessage)
        {
            return ContainsCommandVerb(lowerMessage)
                && (lowerMessage.IndexOf("character", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf(" char", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf("switch to", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf("become", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsGenericIniPuppetCommand(string lowerMessage)
        {
            return ContainsCommandVerb(lowerMessage)
                && (lowerMessage.IndexOf("puppet", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf("ini", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsGenericEmoteCommand(string lowerMessage)
        {
            return ContainsCommandVerb(lowerMessage)
                && (lowerMessage.IndexOf("emote", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf("expression", StringComparison.OrdinalIgnoreCase) >= 0
                    || lowerMessage.IndexOf("pose", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsExplicitSpeechRequest(string lowerMessage)
        {
            return lowerMessage.StartsWith("say ", StringComparison.OrdinalIgnoreCase)
                || lowerMessage.StartsWith("tell ", StringComparison.OrdinalIgnoreCase)
                || lowerMessage.StartsWith("reply ", StringComparison.OrdinalIgnoreCase)
                || lowerMessage.StartsWith("respond ", StringComparison.OrdinalIgnoreCase)
                || lowerMessage.StartsWith("answer ", StringComparison.OrdinalIgnoreCase)
                || lowerMessage.StartsWith("speak ", StringComparison.OrdinalIgnoreCase)
                || lowerMessage.IndexOf(" tell me", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf(" say it", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf(" reply in", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf(" respond in", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf(" answer in", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsCommandVerb(string lowerMessage)
        {
            return CommandVerbs.Any(verb => ContainsPhrase(lowerMessage, verb));
        }

        private static bool ShouldForceSilence(ValidationContext context)
        {
            string message = context.TriggeringMessage ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            return SilenceDirectivePattern.IsMatch(lower) && !HasExplicitResponseRequest(lower);
        }

        private static bool HasExplicitResponseRequest(string lowerMessage)
        {
            if (string.IsNullOrWhiteSpace(lowerMessage))
            {
                return false;
            }

            return IsExplicitSpeechRequest(lowerMessage)
                || lowerMessage.IndexOf("tell me", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf("answer me", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryResolveExplicitChannelRequest(string? message, out string channel)
        {
            channel = string.Empty;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            if (LooksLikeExplicitChannelRequest(lower, "ooc"))
            {
                channel = "OOC";
                return true;
            }

            if (LooksLikeExplicitChannelRequest(lower, "ic"))
            {
                channel = "IC";
                return true;
            }

            return false;
        }

        private static bool LooksLikeExplicitChannelRequest(string lowerMessage, string channelToken)
        {
            if (!ContainsWholeWord(lowerMessage, channelToken))
            {
                return false;
            }

            Regex directedRequestPattern = new Regex(
                $@"\b(?:tell|say|reply|respond|answer|speak|talk|write|type)\b(?:\s+\w+){{0,5}}\s+(?:in|from|via|on|to)?\s*{channelToken}\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            if (directedRequestPattern.IsMatch(lowerMessage))
            {
                return true;
            }

            Regex channelThenVerbPattern = new Regex(
                $@"\b{channelToken}\b(?:\s+\w+){{0,3}}\s+\b(?:reply|respond|say|speak|talk|write|type|answer)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            return channelThenVerbPattern.IsMatch(lowerMessage);
        }

        private static bool HasRequestedEmote(AgentResponse response, string requestedEmote)
        {
            return response.Actions.Any(action =>
                    string.Equals(action.Type, AgentActionType.SetEmote, StringComparison.Ordinal)
                    && string.Equals(action.Value, requestedEmote, StringComparison.OrdinalIgnoreCase))
                || response.Actions.Any(action =>
                    string.Equals(action.Type, AgentActionType.Speak, StringComparison.Ordinal)
                    && string.Equals(action.Channel, "IC", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(action.Emote, requestedEmote, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasAnyRequestedEmote(AgentResponse response, bool requireDifferentEmote, string currentEmote)
        {
            foreach (AgentAction action in response.Actions)
            {
                if (string.Equals(action.Type, AgentActionType.SetEmote, StringComparison.Ordinal))
                {
                    if (!requireDifferentEmote
                        || !string.Equals(action.Value, currentEmote, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                if (string.Equals(action.Type, AgentActionType.Speak, StringComparison.Ordinal)
                    && string.Equals(action.Channel, "IC", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(action.Emote)
                    && (!requireDifferentEmote
                        || !string.Equals(action.Emote, currentEmote, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RequestsDifferentEmote(string lowerMessage, string currentEmote)
        {
            return !string.IsNullOrWhiteSpace(currentEmote)
                && (ContainsWholeWord(lowerMessage, "new")
                    || ContainsWholeWord(lowerMessage, "different")
                    || ContainsPhrase(lowerMessage, "another emote")
                    || ContainsPhrase(lowerMessage, "valid new emote"));
        }

        private static void ValidateAgainstRecentSelfMessages(
            AgentResponse response,
            ValidationContext context,
            List<string> errors)
        {
            if (context.RecentSelfMessages == null || context.RecentSelfMessages.Count == 0)
            {
                return;
            }

            if (ContainsRepeatRequest(context.TriggeringMessage))
            {
                return;
            }

            foreach (AgentAction action in response.Actions.Where(a => string.Equals(a.Type, AgentActionType.Speak, StringComparison.Ordinal)))
            {
                foreach (string previousSelfMessage in context.RecentSelfMessages)
                {
                    if (AreNearDuplicateMessages(action.Message, previousSelfMessage))
                    {
                        errors.Add("Speak message is a near-duplicate of a recent self message. Choose a materially different reply or stay silent.");
                        return;
                    }
                }
            }
        }

        private static bool ContainsRepeatRequest(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            return lower.IndexOf("repeat", StringComparison.OrdinalIgnoreCase) >= 0
                || lower.IndexOf("say that again", StringComparison.OrdinalIgnoreCase) >= 0
                || lower.IndexOf("same thing again", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool AreNearDuplicateMessages(string left, string right)
        {
            string normalizedLeft = NormalizeForComparison(left);
            string normalizedRight = NormalizeForComparison(right);
            if (normalizedLeft.Length < 8 || normalizedRight.Length < 8)
            {
                return false;
            }

            if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
            {
                return true;
            }

            if (normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal)
                || normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal))
            {
                return true;
            }

            HashSet<string> leftTokens = normalizedLeft
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 2)
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> rightTokens = normalizedRight
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 2)
                .ToHashSet(StringComparer.Ordinal);

            if (leftTokens.Count < 3 || rightTokens.Count < 3)
            {
                return false;
            }

            int overlap = leftTokens.Intersect(rightTokens).Count();
            int minimumCount = Math.Min(leftTokens.Count, rightTokens.Count);
            return overlap >= minimumCount - 1;
        }

        private static string NormalizeForComparison(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{Nd}\s]", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private static bool ContainsPhrase(string haystack, string phrase)
        {
            if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(phrase))
            {
                return false;
            }

            return haystack.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsWholeWord(string haystack, string word)
        {
            if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(word))
            {
                return false;
            }

            return Regex.IsMatch(haystack, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
        }

        private static ValidationResult Fail(string error)
        {
            return new ValidationResult(false, new[] { error });
        }
    }
}
