using System.Text.Json;

namespace AO2AIBot.Controller
{
    /// <summary>
    /// Strict parser for the AI agent action-array response contract.
    /// Rejects malformed, prose-wrapped, or legacy-shaped responses.
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
            /// Gets or sets the parsed response when successful.
            /// </summary>
            public AgentResponse? Response { get; set; }

            /// <summary>
            /// Gets or sets an error message for invalid responses.
            /// </summary>
            public string ErrorMessage { get; set; } = string.Empty;
        }

        /// <summary>
        /// Parses a raw model response into a strict <see cref="AgentResponse"/>.
        /// Only accepts clean JSON matching the action-array contract.
        /// </summary>
        public static ParseResult Parse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return CreateError("Response is empty.");
            }

            string trimmed = StripMarkdownFence(response.Trim());

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return CreateError("Response is empty after stripping markdown.");
            }

            // Reject prose-wrapped JSON: the response must start with '{'.
            if (trimmed[0] != '{')
            {
                return CreateError("Response must be a JSON object starting with '{'. Prose or text before JSON is not allowed.");
            }

            JsonDocument jsonDocument;
            try
            {
                jsonDocument = JsonDocument.Parse(trimmed);
            }
            catch (JsonException ex)
            {
                return CreateError("Response is not valid JSON: " + ex.Message);
            }

            using (jsonDocument)
            {
                JsonElement root = jsonDocument.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return CreateError("Response root must be a JSON object.");
                }

                // Reject legacy shapes: if root has "thinking", "message", "state", "modifiers",
                // "chatlog", "showname", "current_character", or "currentEmote" — it's the old format.
                if (HasProperty(root, "thinking"))
                {
                    return CreateError("Legacy format detected: 'thinking' field is no longer supported. Use the action-array contract.");
                }

                if (HasProperty(root, "modifiers"))
                {
                    return CreateError("Legacy format detected: 'modifiers' field is no longer supported. Use the action-array contract.");
                }

                if (HasProperty(root, "state"))
                {
                    return CreateError("Legacy format detected: 'state' field is no longer supported. Use the action-array contract.");
                }

                if (HasProperty(root, "chatlog") || HasProperty(root, "showname")
                    || HasProperty(root, "current_character") || HasProperty(root, "currentEmote"))
                {
                    return CreateError("Legacy format detected: root-level state fields are no longer supported. Use the action-array contract.");
                }

                // "shouldRespond" is required.
                if (!root.TryGetProperty("shouldRespond", out JsonElement shouldRespondElement))
                {
                    return CreateError("Missing required field 'shouldRespond'.");
                }

                bool shouldRespond;
                if (shouldRespondElement.ValueKind == JsonValueKind.True)
                {
                    shouldRespond = true;
                }
                else if (shouldRespondElement.ValueKind == JsonValueKind.False)
                {
                    shouldRespond = false;
                }
                else
                {
                    return CreateError("Field 'shouldRespond' must be a boolean.");
                }

                AgentResponse agentResponse = new AgentResponse
                {
                    ShouldRespond = shouldRespond
                };

                // Parse actions array.
                if (root.TryGetProperty("actions", out JsonElement actionsElement))
                {
                    if (actionsElement.ValueKind != JsonValueKind.Array)
                    {
                        return CreateError("Field 'actions' must be an array.");
                    }

                    int actionIndex = 0;
                    foreach (JsonElement actionElement in actionsElement.EnumerateArray())
                    {
                        if (actionElement.ValueKind != JsonValueKind.Object)
                        {
                            return CreateError($"Action at index {actionIndex} must be a JSON object.");
                        }

                        ParseResult actionResult = ParseAction(actionElement, actionIndex);
                        if (!actionResult.Success)
                        {
                            return actionResult;
                        }

                        if (actionResult.Response?.Actions.Count > 0)
                        {
                            agentResponse.Actions.Add(actionResult.Response.Actions[0]);
                        }

                        actionIndex++;
                    }
                }

                // Structural consistency: shouldRespond=false must have empty actions.
                if (!shouldRespond && agentResponse.Actions.Count > 0)
                {
                    return CreateError("shouldRespond is false but actions array is non-empty. Set shouldRespond to true or remove actions.");
                }

                // shouldRespond=true must have non-empty actions.
                if (shouldRespond && agentResponse.Actions.Count == 0)
                {
                    return CreateError("shouldRespond is true but actions array is empty. Add at least one action or set shouldRespond to false.");
                }

                return new ParseResult
                {
                    Success = true,
                    ShouldWait = !shouldRespond,
                    Response = agentResponse
                };
            }
        }

        private static ParseResult ParseAction(JsonElement element, int index)
        {
            string prefix = $"Action[{index}]: ";

            if (!element.TryGetProperty("type", out JsonElement typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                return CreateError(prefix + "Missing or non-string 'type' field.");
            }

            string type = typeElement.GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(type))
            {
                return CreateError(prefix + "'type' field is empty.");
            }

            if (!AgentActionType.All.Contains(type))
            {
                return CreateError(prefix + $"Unknown action type '{type}'.");
            }

            AgentAction action = new AgentAction { Type = type };

            switch (type)
            {
                case AgentActionType.Speak:
                    action.Channel = GetRequiredString(element, "channel", out string? channelErr);
                    if (channelErr != null)
                    {
                        return CreateError(prefix + channelErr);
                    }

                    action.Message = GetRequiredString(element, "message", out string? msgErr);
                    if (msgErr != null)
                    {
                        return CreateError(prefix + msgErr);
                    }

                    action.Emote = GetOptionalString(element, "emote");
                    action.TextColor = GetOptionalString(element, "textColor");
                    action.ShoutModifier = GetOptionalString(element, "shoutModifier");
                    action.Effect = GetOptionalString(element, "effect");
                    action.Screenshake = GetOptionalBoolean(element, "screenshake");
                    action.Sfx = GetOptionalString(element, "sfx");
                    break;

                case AgentActionType.SetIcShowname:
                case AgentActionType.SetOocShowname:
                case AgentActionType.SetCharacter:
                case AgentActionType.SetPosition:
                case AgentActionType.SetTextColor:
                case AgentActionType.SetArea:
                case AgentActionType.SetIniPuppet:
                case AgentActionType.SetEmote:
                case AgentActionType.SetSfx:
                case AgentActionType.SetDeskMod:
                case AgentActionType.SetEffect:
                case AgentActionType.SetShoutModifier:
                case AgentActionType.SetEmoteModifier:
                    action.Value = GetRequiredString(element, "value", out string? valErr);
                    if (valErr != null)
                    {
                        return CreateError(prefix + valErr);
                    }

                    break;

                case AgentActionType.SetFlip:
                case AgentActionType.SetAdditive:
                case AgentActionType.SetImmediate:
                case AgentActionType.SetPreanimEnabled:
                case AgentActionType.SetScreenshake:
                    action.BoolValue = GetRequiredBoolean(element, "value", out string? boolErr);
                    if (boolErr != null)
                    {
                        return CreateError(prefix + boolErr);
                    }

                    break;

                case AgentActionType.SetOffset:
                    action.Horizontal = GetOptionalInteger(element, "horizontal");
                    action.Vertical = GetOptionalInteger(element, "vertical");
                    if (!action.Horizontal.HasValue && !action.Vertical.HasValue)
                    {
                        return CreateError(prefix + "set_offset requires at least one of 'horizontal' or 'vertical'.");
                    }

                    break;
            }

            AgentResponse wrapper = new AgentResponse();
            wrapper.Actions.Add(action);
            return new ParseResult
            {
                Success = true,
                Response = wrapper
            };
        }

        private static string StripMarkdownFence(string input)
        {
            if (!input.StartsWith("```", StringComparison.Ordinal))
            {
                return input;
            }

            // Remove opening fence line.
            int firstNewline = input.IndexOf('\n');
            if (firstNewline < 0)
            {
                return input;
            }

            string body = input.Substring(firstNewline + 1);

            // Remove closing fence.
            int lastFence = body.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                body = body.Substring(0, lastFence);
            }

            return body.Trim();
        }

        private static bool HasProperty(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out _);
        }

        private static string GetRequiredString(JsonElement element, string propertyName, out string? error)
        {
            error = null;
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                error = $"Missing required field '{propertyName}'.";
                return string.Empty;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                string result = value.GetString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(result))
                {
                    error = $"Field '{propertyName}' is empty.";
                    return string.Empty;
                }

                return result;
            }

            if (value.ValueKind == JsonValueKind.Null)
            {
                error = $"Field '{propertyName}' is null.";
                return string.Empty;
            }

            error = $"Field '{propertyName}' must be a string.";
            return string.Empty;
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

            return null;
        }

        private static bool GetRequiredBoolean(JsonElement element, string propertyName, out string? error)
        {
            error = null;
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                error = $"Missing required field '{propertyName}'.";
                return false;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            error = $"Field '{propertyName}' must be a boolean.";
            return false;
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

            return null;
        }

        private static ParseResult CreateError(string message)
        {
            return new ParseResult
            {
                Success = false,
                ErrorMessage = message ?? string.Empty
            };
        }
    }
}
