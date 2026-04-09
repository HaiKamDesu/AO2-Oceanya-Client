using AOBot_Testing.Structures;

namespace AO2AIBot.Controller
{
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

        private static readonly HashSet<string> ValidChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IC", "OOC" };

        private static readonly HashSet<string> ValidTextColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "white", "green", "red", "orange", "blue", "yellow", "magenta", "cyan", "gray"
        };

        private static readonly HashSet<string> ValidShoutModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "nothing", "holdIt", "objection", "takeThat", "custom"
        };

        private static readonly HashSet<string> ValidEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "none", "realization", "hearts", "reaction", "impact"
        };

        private static readonly HashSet<string> ValidDeskMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hidden", "shown"
        };

        /// <summary>
        /// Validates a parsed response against the current authoritative snapshot.
        /// Normalizes known aliases in-place where deterministically safe.
        /// </summary>
        public static ValidationResult Validate(AgentResponse response, AOClientControlSnapshot snapshot)
        {
            if (response == null)
            {
                return Fail("Response is null.");
            }

            if (snapshot == null)
            {
                return Fail("Snapshot is null.");
            }

            List<string> errors = new List<string>();

            // Structural checks already handled by parser, but double-check here.
            if (!response.ShouldRespond && response.Actions.Count > 0)
            {
                errors.Add("shouldRespond is false but actions array is non-empty.");
            }

            if (response.ShouldRespond && response.Actions.Count == 0)
            {
                errors.Add("shouldRespond is true but actions array is empty.");
            }

            for (int i = 0; i < response.Actions.Count; i++)
            {
                AgentAction action = response.Actions[i];
                string prefix = $"Action[{i}] ({action.Type}): ";

                if (!AgentActionType.All.Contains(action.Type))
                {
                    errors.Add(prefix + $"Unknown action type '{action.Type}'.");
                    continue;
                }

                ValidateAction(action, prefix, snapshot, errors);
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
            List<string> errors)
        {
            switch (action.Type)
            {
                case AgentActionType.Speak:
                    ValidateSpeak(action, prefix, snapshot, errors);
                    break;

                case AgentActionType.SetCharacter:
                    if (!TryResolveExact(action.Value, snapshot.AvailableCharacters, out string resolvedChar))
                    {
                        errors.Add(prefix + $"Character '{action.Value}' is not in the available characters list.");
                    }
                    else
                    {
                        action.Value = resolvedChar;
                    }

                    break;

                case AgentActionType.SetEmote:
                    if (!TryResolveExact(action.Value, snapshot.AvailableEmotes, out string resolvedEmote))
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
                    if (!ValidTextColors.Contains(action.Value))
                    {
                        errors.Add(prefix + $"Text color '{action.Value}' is not a valid color. Valid: {string.Join(", ", ValidTextColors)}.");
                    }

                    break;

                case AgentActionType.SetShoutModifier:
                    action.Value = NormalizeAlias(action.Value, ShoutAliases);
                    if (!ValidShoutModifiers.Contains(action.Value))
                    {
                        errors.Add(prefix + $"Shout modifier '{action.Value}' is not valid. Valid: {string.Join(", ", ValidShoutModifiers)}.");
                    }

                    break;

                case AgentActionType.SetEffect:
                    action.Value = NormalizeAlias(action.Value, EffectAliases);
                    if (!ValidEffects.Contains(action.Value))
                    {
                        errors.Add(prefix + $"Effect '{action.Value}' is not valid. Valid: {string.Join(", ", ValidEffects)}.");
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
                    // String value already validated by parser (non-empty required).
                    break;

                case AgentActionType.SetFlip:
                case AgentActionType.SetAdditive:
                case AgentActionType.SetImmediate:
                case AgentActionType.SetPreanimEnabled:
                case AgentActionType.SetScreenshake:
                    // Boolean value already validated by parser.
                    break;

                case AgentActionType.SetEmoteModifier:
                    // Accept any string for emote modifier — values are implementation-specific.
                    break;

                case AgentActionType.SetOffset:
                    // Integer values already validated by parser.
                    break;
            }
        }

        private static void ValidateSpeak(
            AgentAction action,
            string prefix,
            AOClientControlSnapshot snapshot,
            List<string> errors)
        {
            // Channel is required and must be IC or OOC.
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

            // Message is required.
            if (string.IsNullOrWhiteSpace(action.Message))
            {
                errors.Add(prefix + "Missing or empty 'message' field.");
                return;
            }

            // IC speak requires explicit emote.
            if (string.Equals(action.Channel, "IC", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(action.Emote))
                {
                    errors.Add(prefix + "IC speak requires an explicit 'emote' field. Choose from available emotes.");
                    return;
                }

                if (!TryResolveExact(action.Emote, snapshot.AvailableEmotes, out string resolvedEmote))
                {
                    errors.Add(prefix + $"Emote '{action.Emote}' is not in the available emotes list.");
                }
                else
                {
                    action.Emote = resolvedEmote;
                }
            }

            // Validate optional speak fields.
            if (!string.IsNullOrWhiteSpace(action.TextColor) && !ValidTextColors.Contains(action.TextColor))
            {
                errors.Add(prefix + $"Text color '{action.TextColor}' is not valid.");
            }

            if (!string.IsNullOrWhiteSpace(action.ShoutModifier))
            {
                action.ShoutModifier = NormalizeAlias(action.ShoutModifier, ShoutAliases);
                if (!ValidShoutModifiers.Contains(action.ShoutModifier))
                {
                    errors.Add(prefix + $"Shout modifier '{action.ShoutModifier}' is not valid.");
                }
            }

            if (!string.IsNullOrWhiteSpace(action.Effect))
            {
                action.Effect = NormalizeAlias(action.Effect, EffectAliases);
                if (!ValidEffects.Contains(action.Effect))
                {
                    errors.Add(prefix + $"Effect '{action.Effect}' is not valid.");
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

        private static ValidationResult Fail(string error)
        {
            return new ValidationResult(false, new[] { error });
        }
    }
}
