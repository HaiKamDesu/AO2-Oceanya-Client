using AOBot_Testing.Structures;
using System.Text.Json;

namespace AO2AIBot.Controller
{
    /// <summary>
    /// Parses model responses into AO client control decisions.
    /// </summary>
    public static class AOClientAgentResponseParser
    {
        /// <summary>
        /// Represents the result of parsing a model response.
        /// </summary>
        public sealed class ParseResult
        {
            /// <summary>
            /// Gets or sets a value indicating whether the parse succeeded.
            /// </summary>
            public bool Success { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the response explicitly requested no action.
            /// </summary>
            public bool ShouldWait { get; set; }

            /// <summary>
            /// Gets or sets the parsed decision when successful.
            /// </summary>
            public AOClientAgentDecision? Decision { get; set; }

            /// <summary>
            /// Gets or sets an error message for invalid responses.
            /// </summary>
            public string ErrorMessage { get; set; } = string.Empty;
        }

        /// <summary>
        /// Parses a raw model response into a normalized decision.
        /// </summary>
        public static ParseResult Parse(string response)
        {
            string normalized = NormalizeResponse(response);
            if (string.IsNullOrWhiteSpace(normalized) || ContainsSystemWaitDirective(normalized))
            {
                return new ParseResult
                {
                    Success = true,
                    ShouldWait = true
                };
            }

            ParseResult directParseResult = ParseJsonCandidate(normalized);
            if (directParseResult.Success)
            {
                return directParseResult;
            }

            if (TryExtractFirstJsonObject(normalized, out string extractedJson)
                && !string.Equals(extractedJson, normalized, StringComparison.Ordinal))
            {
                ParseResult extractedParseResult = ParseJsonCandidate(extractedJson);
                if (extractedParseResult.Success)
                {
                    return extractedParseResult;
                }

                return extractedParseResult;
            }

            return directParseResult;
        }

        private static ParseResult ParseJsonCandidate(string json)
        {
            try
            {
                using JsonDocument jsonDocument = JsonDocument.Parse(json);
                JsonElement root = jsonDocument.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return CreateError("AI response root must be a JSON object.");
                }

                AOClientAgentDecision decision = new AOClientAgentDecision();

                bool? explicitShouldRespond = GetOptionalBoolean(root, "shouldRespond");
                string channel = GetOptionalString(root, "channel");
                if (string.IsNullOrWhiteSpace(channel))
                {
                    channel = GetOptionalString(root, "chatlog");
                }

                string message = GetOptionalString(root, "message");
                string showname = GetOptionalString(root, "showname");
                string currentCharacter = GetOptionalString(root, "current_character");
                string currentEmote = GetOptionalString(root, "currentEmote");

                decision.Channel = channel;
                decision.Message = message;
                decision.Character = currentCharacter;
                decision.Emote = currentEmote;

                JsonElement stateElement = root;
                if (root.TryGetProperty("state", out JsonElement nestedState)
                    && nestedState.ValueKind == JsonValueKind.Object)
                {
                    stateElement = nestedState;
                }

                decision.IcShowname = GetOptionalString(stateElement, "icShowname");
                decision.OocShowname = GetOptionalString(stateElement, "oocShowname");
                if (string.IsNullOrWhiteSpace(decision.IcShowname) && !string.IsNullOrWhiteSpace(showname))
                {
                    if (string.Equals(decision.Channel, "OOC", StringComparison.OrdinalIgnoreCase))
                    {
                        decision.OocShowname = showname;
                    }
                    else
                    {
                        decision.IcShowname = showname;
                    }
                }

                if (string.IsNullOrWhiteSpace(decision.Character))
                {
                    decision.Character = GetOptionalString(stateElement, "character");
                }

                if (string.IsNullOrWhiteSpace(decision.Emote))
                {
                    decision.Emote = GetOptionalString(stateElement, "emote");
                }

                decision.Area = GetOptionalString(stateElement, "area");
                decision.Position = GetOptionalString(stateElement, "position");
                decision.IniPuppetName = GetOptionalString(stateElement, "iniPuppetName");
                decision.Sfx = GetOptionalString(stateElement, "sfx");

                decision.PreanimEnabled = GetOptionalBoolean(stateElement, "preanimEnabled");
                decision.Flip = GetOptionalBoolean(stateElement, "flip");
                decision.Additive = GetOptionalBoolean(stateElement, "additive");
                decision.Immediate = GetOptionalBoolean(stateElement, "immediate");
                decision.Screenshake = GetOptionalBoolean(stateElement, "screenshake");
                decision.SelfOffsetHorizontal = GetOptionalInteger(stateElement, "selfOffsetHorizontal");
                decision.SelfOffsetVertical = GetOptionalInteger(stateElement, "selfOffsetVertical");

                decision.DeskMod = ParseEnumValue<ICMessage.DeskMods>(stateElement, "deskMod");
                decision.EmoteModifier = ParseEnumValue<ICMessage.EmoteModifiers>(stateElement, "emoteModifier");
                decision.ShoutModifier = ParseEnumValue<ICMessage.ShoutModifiers>(stateElement, "shoutModifier");
                decision.TextColor = ParseEnumValue<ICMessage.TextColors>(stateElement, "textColor");
                decision.Effect = ParseEnumValue<ICMessage.Effects>(stateElement, "effect");

                if (root.TryGetProperty("modifiers", out JsonElement modifiersElement)
                    && modifiersElement.ValueKind == JsonValueKind.Object)
                {
                    decision.DeskMod ??= ParseEnumValue<ICMessage.DeskMods>(modifiersElement, "deskMod");
                    decision.EmoteModifier ??= ParseEnumValue<ICMessage.EmoteModifiers>(modifiersElement, "emoteMod");
                    decision.ShoutModifier ??= ParseEnumValue<ICMessage.ShoutModifiers>(modifiersElement, "shoutModifiers");
                    decision.TextColor ??= ParseEnumValue<ICMessage.TextColors>(modifiersElement, "textColor");
                    decision.Flip ??= GetOptionalBoolean(modifiersElement, "flip");
                    decision.Immediate ??= GetOptionalBoolean(modifiersElement, "immediate");
                    decision.Additive ??= GetOptionalBoolean(modifiersElement, "additive");
                    decision.PreanimEnabled ??= GetOptionalBoolean(modifiersElement, "preanimEnabled");
                    decision.Screenshake ??= GetOptionalBoolean(modifiersElement, "screenshake");

                    int? realizationValue = GetOptionalInteger(modifiersElement, "realization");
                    if (decision.Effect == null && realizationValue.HasValue)
                    {
                        decision.Effect = realizationValue.Value == 1
                            ? ICMessage.Effects.Realization
                            : ICMessage.Effects.None;
                    }
                }

                bool shouldAct = explicitShouldRespond
                    ?? !string.IsNullOrWhiteSpace(decision.Message)
                    || decision.HasStateChanges;
                decision.ShouldAct = shouldAct;

                return new ParseResult
                {
                    Success = true,
                    ShouldWait = !decision.ShouldAct,
                    Decision = decision
                };
            }
            catch (Exception ex)
            {
                return CreateError("AI response was not valid JSON: " + ex.Message);
            }
        }

        private static bool ContainsSystemWaitDirective(string response)
        {
            return response?.IndexOf("SYSTEM_WAIT()", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ParseResult CreateError(string message)
        {
            return new ParseResult
            {
                Success = false,
                ErrorMessage = message ?? string.Empty
            };
        }

        private static string NormalizeResponse(string response)
        {
            string normalized = response?.Trim() ?? string.Empty;
            if (normalized.StartsWith("```", StringComparison.Ordinal))
            {
                normalized = normalized.Trim('`').Trim();
                if (normalized.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(4).Trim();
                }
            }

            return normalized;
        }

        private static bool TryExtractFirstJsonObject(string input, out string extractedJson)
        {
            extractedJson = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            bool insideString = false;
            bool escaping = false;
            int depth = 0;
            int startIndex = -1;

            for (int index = 0; index < input.Length; index++)
            {
                char current = input[index];

                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (insideString)
                {
                    if (current == '\\')
                    {
                        escaping = true;
                    }
                    else if (current == '"')
                    {
                        insideString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    insideString = true;
                    continue;
                }

                if (current == '{')
                {
                    if (depth == 0)
                    {
                        startIndex = index;
                    }

                    depth++;
                    continue;
                }

                if (current != '}')
                {
                    continue;
                }

                if (depth == 0)
                {
                    continue;
                }

                depth--;
                if (depth == 0 && startIndex >= 0)
                {
                    extractedJson = input.Substring(startIndex, index - startIndex + 1);
                    return true;
                }
            }

            return false;
        }

        private static string GetOptionalString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                return string.Empty;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()?.Trim() ?? string.Empty;
            }

            return value.ValueKind == JsonValueKind.Null ? string.Empty : value.ToString().Trim();
        }

        private static bool? GetOptionalBoolean(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int intValue))
            {
                return intValue != 0;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                string text = value.GetString()?.Trim() ?? string.Empty;
                if (bool.TryParse(text, out bool boolValue))
                {
                    return boolValue;
                }

                if (int.TryParse(text, out int numericValue))
                {
                    return numericValue != 0;
                }
            }

            return null;
        }

        private static int? GetOptionalInteger(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int intValue))
            {
                return intValue;
            }

            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString()?.Trim(), out int parsed))
            {
                return parsed;
            }

            return null;
        }

        private static TEnum? ParseEnumValue<TEnum>(JsonElement element, string propertyName)
            where TEnum : struct, Enum
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int intValue))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), intValue);
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                string text = value.GetString()?.Trim() ?? string.Empty;
                if (int.TryParse(text, out int parsedInt))
                {
                    return (TEnum)Enum.ToObject(typeof(TEnum), parsedInt);
                }

                if (Enum.TryParse(text, ignoreCase: true, out TEnum enumValue))
                {
                    return enumValue;
                }
            }

            return null;
        }
    }
}
