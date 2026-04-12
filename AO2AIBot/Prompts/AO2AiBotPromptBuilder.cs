using AO2AIBot.Chat;
using AO2AIBot.Controller;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AO2AIBot.Prompts
{
    /// <summary>
    /// Builds structured user prompts for the AO2 AI bot using the action-array contract.
    /// Emits authoritative state as structured JSON payloads and clearly separates
    /// known state from unavailable information.
    /// </summary>
    public static class AO2AiBotPromptBuilder
    {
        private const int ParticipantWindowSize = 30;
        private const int MaxAdjacentDuplicateEntries = 0;

        /// <summary>
        /// Builds a prompt using the current client snapshot, transcript, and persistent rules.
        /// </summary>
        public static string BuildPrompt(
            AOClientControlSnapshot snapshot,
            IReadOnlyList<ChatLogEntry> history,
            ChatLogEntry? latestEntry,
            string triggerReason,
            IReadOnlyList<PersistentRule>? activeRules = null)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            StringBuilder sb = new StringBuilder();

            // === AUTHORITATIVE SELF STATE (structured) ===
            AppendSelfState(sb, snapshot);

            // === AVAILABLE OPTIONS (structured) ===
            AppendAvailableOptions(sb, snapshot);

            // === OTHER PLAYERS (with known limits) ===
            AppendOtherPlayers(sb, snapshot);

            // === RULES ===
            AppendStandingRules(sb, activeRules, latestEntry);

            // === CHAT CONTEXT ===
            AppendChatContext(sb, snapshot, history, latestEntry);
            sb.AppendLine();

            // === TARGET CHARACTER CONTEXT ===
            AppendRequestedCharacterContext(sb, snapshot, latestEntry);

            // === CONVERSATION HISTORY ===
            sb.AppendLine("## Conversation History");
            IReadOnlyList<ChatLogEntry> compactHistory = CompactHistory(history);
            if (compactHistory.Count == 0)
            {
                sb.AppendLine("(no messages yet — this is the start of the session)");
            }
            else
            {
                foreach (ChatLogEntry entry in compactHistory)
                {
                    sb.AppendLine(FormatEntryForPrompt(entry));
                }
            }

            sb.AppendLine();

            // === TRIGGER ===
            if (latestEntry != null)
            {
                sb.AppendLine("## Message You Are Responding To");
                sb.AppendLine($"  {FormatEntryForPrompt(latestEntry)}");
                sb.AppendLine($"  Channel: {latestEntry.ChatLogType}");
                sb.AppendLine();
                sb.AppendLine(latestEntry.IsFromServer
                    ? "This is server output. Treat it as context, not as a player."
                    : "This is the latest player message. Respond if appropriate.");
            }
            else
            {
                sb.AppendLine("## Evaluation Context");
                sb.AppendLine("Manual evaluation. Review history and decide if a response is warranted.");
            }

            sb.AppendLine();

            // === RESPONSE INSTRUCTIONS ===
            sb.AppendLine("## Your Turn");
            sb.AppendLine("Output ONLY a single JSON object following the action-array contract.");
            sb.AppendLine("To respond: {\"shouldRespond\":true,\"actions\":[...]}");
            sb.AppendLine("To stay silent: {\"shouldRespond\":false,\"actions\":[]}");
            sb.AppendLine();
            sb.AppendLine("Every IC speak action MUST include an explicit \"emote\" field chosen from Available Emotes.");
            sb.AppendLine("If Chat Context gives an Expected response channel, use that channel. Otherwise use the latest message channel.");

            return sb.ToString();
        }

        /// <summary>
        /// Builds a correction prompt for the retry attempt after a validation failure.
        /// Includes the exact validation errors so the model can fix them.
        /// </summary>
        public static string BuildCorrectionPrompt(
            string originalPrompt,
            string failedResponse,
            IReadOnlyList<string>? validationErrors = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Your previous response failed validation. Fix the errors and output ONLY a valid JSON object.");
            sb.AppendLine();

            if (validationErrors != null && validationErrors.Count > 0)
            {
                sb.AppendLine("## Validation Errors");
                foreach (string error in validationErrors)
                {
                    sb.AppendLine($"  - {error}");
                }

                sb.AppendLine();
            }

            sb.AppendLine("## Required Format");
            sb.AppendLine("To respond: {\"shouldRespond\":true,\"actions\":[{\"type\":\"speak\",\"channel\":\"IC\",\"message\":\"...\",\"emote\":\"...\"}]}");
            sb.AppendLine("To stay silent: {\"shouldRespond\":false,\"actions\":[]}");
            sb.AppendLine();
            sb.AppendLine("Do NOT apologize, narrate retries, or say you will try again.");
            sb.AppendLine("If you cannot satisfy the validation errors exactly, output {\"shouldRespond\":false,\"actions\":[]}.");
            AppendSpecialCorrectionHint(sb, failedResponse, validationErrors);
            sb.AppendLine();

            sb.AppendLine("## Your Previous Invalid Response");
            sb.AppendLine("---");
            sb.AppendLine((failedResponse ?? string.Empty).Trim());
            sb.AppendLine("---");
            sb.AppendLine();

            sb.AppendLine("## Original Context");
            sb.AppendLine((originalPrompt ?? string.Empty).Trim());

            return sb.ToString();
        }

        private static void AppendSelfState(StringBuilder sb, AOClientControlSnapshot snapshot)
        {
            sb.AppendLine("## Self State");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine($"  \"serverName\": {JsonQuote(snapshot.ServerName)},");
            sb.AppendLine($"  \"connected\": {(snapshot.IsConnected ? "true" : "false")},");
            sb.AppendLine($"  \"currentCharacter\": {JsonQuote(snapshot.CurrentCharacter)},");
            sb.AppendLine($"  \"currentEmote\": {JsonQuote(snapshot.CurrentEmote)},");
            sb.AppendLine($"  \"currentArea\": {JsonQuote(snapshot.CurrentArea)},");
            sb.AppendLine($"  \"currentPosition\": {JsonQuote(snapshot.CurrentPosition)},");
            sb.AppendLine($"  \"currentBackground\": {JsonQuote(snapshot.CurrentBackground)},");
            sb.AppendLine($"  \"icShowname\": {JsonQuote(snapshot.IcShowname)},");
            sb.AppendLine($"  \"oocShowname\": {JsonQuote(snapshot.OocShowname)},");
            sb.AppendLine($"  \"textColor\": {JsonQuote(snapshot.TextColor)},");
            sb.AppendLine($"  \"flip\": {(snapshot.Flip ? "true" : "false")},");
            sb.AppendLine($"  \"additive\": {(snapshot.Additive ? "true" : "false")},");
            sb.AppendLine($"  \"immediate\": {(snapshot.Immediate ? "true" : "false")},");
            sb.AppendLine($"  \"preanimEnabled\": {(snapshot.PreanimEnabled ? "true" : "false")},");
            sb.AppendLine($"  \"currentIniPuppetName\": {JsonQuote(snapshot.CurrentIniPuppetName)},");
            sb.AppendLine($"  \"selfOffsetHorizontal\": {snapshot.SelfOffsetHorizontal},");
            sb.AppendLine($"  \"selfOffsetVertical\": {snapshot.SelfOffsetVertical}");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        private static void AppendAvailableOptions(StringBuilder sb, AOClientControlSnapshot snapshot)
        {
            sb.AppendLine("## Available Options");
            AppendCompactList(sb, "Emotes", snapshot.AvailableEmotes);
            AppendCompactList(sb, "Positions", snapshot.AvailablePositions);
            AppendCompactList(sb, "SFX", snapshot.AvailableSfx, maxItems: 60);
            AppendAreaInfoList(sb, snapshot);
            AppendIniPuppetList(sb, snapshot);
            AppendCharacterList(sb, snapshot);
            sb.AppendLine("  Text Colors: white, green, red, orange, blue, yellow, magenta, cyan, gray");
            sb.AppendLine("  Shout Modifiers: nothing, holdIt, objection, takeThat, custom");
            sb.AppendLine("  Effects: none, realization, hearts, reaction, impact");
            sb.AppendLine();
        }

        private static void AppendOtherPlayers(StringBuilder sb, AOClientControlSnapshot snapshot)
        {
            sb.AppendLine("## Other Players in Area");
            if (snapshot.CurrentAreaPlayers.Count == 0)
            {
                sb.AppendLine("  (no roster data available)");
            }
            else
            {
                foreach (AOBot_Testing.Structures.Player player in snapshot.CurrentAreaPlayers)
                {
                    string showname = string.IsNullOrWhiteSpace(player.OOCShowname) ? "unknown" : player.OOCShowname;
                    string cm = player.IsCM ? " [CM]" : "";
                    sb.AppendLine($"  - [{player.PlayerID}] {player.ICCharacterName} (OOC: {showname}){cm}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Known Limits About Other Players");
            sb.AppendLine("  You do NOT have access to other players' current emote, text color, position, flip state, or any other client-side state.");
            sb.AppendLine("  If asked about another player's emote or client state, say you do not have that information. Do NOT guess.");
            sb.AppendLine();
        }

        private static void AppendStandingRules(
            StringBuilder sb,
            IReadOnlyList<PersistentRule>? rules,
            ChatLogEntry? latestEntry)
        {
            IReadOnlyList<PersistentRule> persistentRules = rules == null
                ? Array.Empty<PersistentRule>()
                : rules.Where(rule => string.Equals(rule.Scope, "persistent", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                    .AsReadOnly();
            IReadOnlyList<PersistentRule> sessionRules = rules == null
                ? Array.Empty<PersistentRule>()
                : rules.Where(rule => string.Equals(rule.Scope, "session", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                    .AsReadOnly();

            sb.AppendLine("## Standing Rules");
            AppendRuleGroup(sb, "Persistent Rules", persistentRules);
            AppendRuleGroup(sb, "Session Rules", sessionRules);

            sb.AppendLine("## Transient Turn Instructions");
            if (latestEntry == null || latestEntry.IsFromServer || string.IsNullOrWhiteSpace(latestEntry.Message))
            {
                sb.AppendLine("  (none active)");
            }
            else if (PersistentRuleStore.ShouldPromoteRuleCommand(latestEntry.Message, out _))
            {
                sb.AppendLine("  Latest instruction was promoted into a standing rule above.");
            }
            else
            {
                sb.AppendLine($"  Latest turn-only instruction: {latestEntry.Message.Trim()}");
            }

            sb.AppendLine();
        }

        private static void AppendChatContext(
            StringBuilder sb,
            AOClientControlSnapshot snapshot,
            IReadOnlyList<ChatLogEntry> history,
            ChatLogEntry? latestEntry)
        {
            IEnumerable<ChatLogEntry> window = history.Count > ParticipantWindowSize
                ? history.Skip(history.Count - ParticipantWindowSize)
                : history;

            HashSet<string> participants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ChatLogEntry entry in window)
            {
                if (!entry.IsFromSelf && !entry.IsFromServer && !string.IsNullOrWhiteSpace(entry.ShowName))
                {
                    participants.Add(entry.ShowName);
                }
            }

            bool directAddress = false;
            if (latestEntry != null && !string.IsNullOrWhiteSpace(latestEntry.Message))
            {
                string msg = latestEntry.Message;
                directAddress =
                    ContainsName(msg, snapshot.IcShowname)
                    || ContainsName(msg, snapshot.OocShowname)
                    || ContainsName(msg, snapshot.CurrentCharacter);
            }

            sb.AppendLine("## Chat Context");

            if (participants.Count == 0)
            {
                sb.AppendLine("  Other participants: none visible yet");
            }
            else
            {
                string names = string.Join(", ", participants.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                sb.AppendLine($"  Other participants ({participants.Count}): {names}");
            }

            if (directAddress)
            {
                sb.AppendLine("  Direct address: YES — your name appears in the message");
            }
            else
            {
                sb.AppendLine("  Direct address: no");
            }

            if (latestEntry != null)
            {
                string expectedChannel = ResolveExpectedChannel(latestEntry);
                if (!string.IsNullOrWhiteSpace(expectedChannel))
                {
                    sb.AppendLine($"  Expected response channel: {expectedChannel}");
                }

                sb.AppendLine(latestEntry.IsFromServer
                    ? "  Latest source: SERVER output"
                    : "  Latest source: player message");
            }

            if (latestEntry?.IsFromServer == true)
            {
                sb.AppendLine("  → Do not reply to server output unless a player explicitly asks.");
            }
            else if (directAddress)
            {
                sb.AppendLine("  → You were directly addressed. You MUST respond.");
            }
            else if (participants.Count <= 1)
            {
                sb.AppendLine("  → 1-on-1 conversation. Usually respond unless told not to.");
            }
            else if (participants.Count <= 3)
            {
                sb.AppendLine("  → Small group. Respond if addressed or relevant.");
            }
            else
            {
                sb.AppendLine("  → Larger group. Respond only when directly addressed or clearly relevant.");
            }
        }

        private static void AppendRequestedCharacterContext(
            StringBuilder sb,
            AOClientControlSnapshot snapshot,
            ChatLogEntry? latestEntry)
        {
            if (latestEntry == null || string.IsNullOrWhiteSpace(latestEntry.Message))
            {
                return;
            }

            if (!TryExtractRequestedCharacter(latestEntry.Message, snapshot, out string requestedCharacter))
            {
                return;
            }

            sb.AppendLine("## Requested Character Switch");
            sb.AppendLine($"  Target character: {requestedCharacter}");
            if (snapshot.AvailableCharacterEmotes.TryGetValue(requestedCharacter, out IReadOnlyList<string>? emotes)
                && emotes != null
                && emotes.Count > 0)
            {
                const int maxDisplay = 25;
                IEnumerable<string> display = emotes.Count > maxDisplay ? emotes.Take(maxDisplay) : emotes;
                string suffix = emotes.Count > maxDisplay ? $" ... (+{emotes.Count - maxDisplay} more)" : string.Empty;
                sb.AppendLine($"  Valid emotes for {requestedCharacter}: {string.Join(", ", display)}{suffix}");
                sb.AppendLine("  If you switch character this turn, any later set_emote or IC speak emote MUST come from this target list.");
            }
            else
            {
                sb.AppendLine("  No emote list is currently available for that character. Avoid guessing.");
            }

            sb.AppendLine();
        }

        private static string FormatEntryForPrompt(ChatLogEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string sourceMarker = entry.IsFromServer ? "[SERVER] " : (entry.IsFromSelf ? "[SELF] " : "[OTHER] ");
            if (string.Equals(entry.ChatLogType, "OOC", StringComparison.OrdinalIgnoreCase))
            {
                return $"[OOC]{sourceMarker}{entry.ShowName}: {entry.Message}";
            }

            string characterSuffix = string.IsNullOrWhiteSpace(entry.CharacterName)
                ? string.Empty
                : $" ({entry.CharacterName})";
            return $"[IC]{sourceMarker}{entry.ShowName}{characterSuffix}: {entry.Message}";
        }

        private static void AppendCompactList(
            StringBuilder sb,
            string label,
            IReadOnlyList<string> items,
            int maxItems = 40)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            IEnumerable<string> displayItems = items.Count > maxItems
                ? items.Take(maxItems)
                : items;

            string suffix = items.Count > maxItems
                ? $" ... (+{items.Count - maxItems} more)"
                : string.Empty;

            sb.AppendLine($"  {label}: {string.Join(", ", displayItems)}{suffix}");
        }

        private static bool ContainsName(string message, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return message.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AppendRuleGroup(
            StringBuilder sb,
            string title,
            IReadOnlyList<PersistentRule> rules)
        {
            sb.AppendLine($"### {title}");
            if (rules.Count == 0)
            {
                sb.AppendLine("  (none active)");
                return;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. {rules[i].Text}");
            }
        }

        private static string ResolveExpectedChannel(ChatLogEntry latestEntry)
        {
            if (latestEntry == null)
            {
                return string.Empty;
            }

            if (TryResolveExplicitChannelRequest(latestEntry.Message, out string explicitChannel))
            {
                return explicitChannel;
            }

            return string.Equals(latestEntry.ChatLogType, "OOC", StringComparison.OrdinalIgnoreCase)
                ? "OOC"
                : "IC";
        }

        private static bool TryExtractRequestedCharacter(
            string message,
            AOClientControlSnapshot snapshot,
            out string requestedCharacter)
        {
            requestedCharacter = string.Empty;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            if (lower.IndexOf("character", StringComparison.OrdinalIgnoreCase) < 0
                && lower.IndexOf("switch to", StringComparison.OrdinalIgnoreCase) < 0
                && lower.IndexOf("change to", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            foreach (string character in snapshot.AvailableCharacters.OrderByDescending(value => value.Length))
            {
                if (lower.IndexOf(character, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    requestedCharacter = character;
                    return true;
                }
            }

            return false;
        }

        private static void AppendSpecialCorrectionHint(
            StringBuilder sb,
            string failedResponse,
            IReadOnlyList<string>? validationErrors)
        {
            if (!NeedsKeepShoutOrEffectHint(failedResponse, validationErrors))
            {
                return;
            }

            sb.AppendLine("Keep the same requested shoutModifier/effect/channel/message.");
            sb.AppendLine("Only add a valid IC emote from Available Emotes. Do NOT remove the shout or effect.");
        }

        private static bool NeedsKeepShoutOrEffectHint(
            string failedResponse,
            IReadOnlyList<string>? validationErrors)
        {
            if (validationErrors == null || validationErrors.Count == 0)
            {
                return false;
            }

            bool onlyMissingEmoteErrors = validationErrors.All(error =>
                error.IndexOf("IC speak requires an explicit 'emote' field", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!onlyMissingEmoteErrors)
            {
                return false;
            }

            AOClientAgentResponseParser.ParseResult parseResult = AOClientAgentResponseParser.Parse(failedResponse ?? string.Empty);
            if (!parseResult.Success || parseResult.Response == null)
            {
                return false;
            }

            return parseResult.Response.Actions.Any(action =>
                    string.Equals(action.Type, AgentActionType.Speak, StringComparison.Ordinal)
                    && (!string.IsNullOrWhiteSpace(action.ShoutModifier) || !string.IsNullOrWhiteSpace(action.Effect)))
                || parseResult.Response.Actions.Any(action =>
                    string.Equals(action.Type, AgentActionType.SetShoutModifier, StringComparison.Ordinal)
                    || string.Equals(action.Type, AgentActionType.SetEffect, StringComparison.Ordinal));
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

        private static void AppendAreaInfoList(StringBuilder sb, AOClientControlSnapshot snapshot)
        {
            if (snapshot.AvailableAreaInfos.Count > 0)
            {
                sb.Append("  Areas: ");
                sb.AppendLine(string.Join(", ", snapshot.AvailableAreaInfos.Select(a =>
                    $"{a.Name} ({a.Players} users, {a.Status}, {a.LockState})")));
            }
            else if (snapshot.AvailableAreas.Count > 0)
            {
                AppendCompactList(sb, "Areas", snapshot.AvailableAreas, maxItems: 20);
            }
        }

        private static void AppendIniPuppetList(StringBuilder sb, AOClientControlSnapshot snapshot)
        {
            if (snapshot.AvailableIniPuppets.Count == 0)
            {
                return;
            }

            List<string> available = snapshot.AvailableIniPuppets
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> taken = snapshot.AvailableIniPuppets
                .Where(kv => !kv.Value)
                .Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            sb.Append("  INI Puppets: ");
            if (available.Count > 0)
            {
                sb.Append("available: " + string.Join(", ", available.Take(10)));
            }

            if (taken.Count > 0)
            {
                if (available.Count > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append("taken: " + string.Join(", ", taken.Take(10)));
            }

            sb.AppendLine();
        }

        private static void AppendCharacterList(StringBuilder sb, AOClientControlSnapshot snapshot)
        {
            if (snapshot.AvailableCharacters.Count == 0)
            {
                return;
            }

            const int maxDisplay = 30;
            IEnumerable<string> display = snapshot.AvailableCharacters.Count > maxDisplay
                ? snapshot.AvailableCharacters.Take(maxDisplay)
                : snapshot.AvailableCharacters;

            string suffix = snapshot.AvailableCharacters.Count > maxDisplay
                ? $" ... (+{snapshot.AvailableCharacters.Count - maxDisplay} more)"
                : string.Empty;

            sb.AppendLine($"  Characters ({snapshot.AvailableCharacters.Count} total, exact name): {string.Join(", ", display)}{suffix}");
        }

        private static IReadOnlyList<ChatLogEntry> CompactHistory(IReadOnlyList<ChatLogEntry> history)
        {
            if (history.Count <= 1)
            {
                return history;
            }

            List<ChatLogEntry> compacted = new List<ChatLogEntry>(history.Count);
            ChatLogEntry? previous = null;
            int duplicateCount = 0;

            foreach (ChatLogEntry entry in history)
            {
                if (previous != null && AreEquivalent(previous, entry))
                {
                    duplicateCount++;
                    if (duplicateCount > MaxAdjacentDuplicateEntries)
                    {
                        continue;
                    }
                }
                else
                {
                    duplicateCount = 0;
                }

                compacted.Add(entry);
                previous = entry;
            }

            return compacted;
        }

        private static bool AreEquivalent(ChatLogEntry left, ChatLogEntry right)
        {
            return string.Equals(left.ChatLogType, right.ChatLogType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.CharacterName, right.CharacterName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.ShowName, right.ShowName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Message, right.Message, StringComparison.Ordinal)
                && left.IsFromSelf == right.IsFromSelf
                && left.IsFromServer == right.IsFromServer;
        }

        private static string JsonQuote(string value)
        {
            return JsonSerializer.Serialize(value ?? string.Empty);
        }
    }
}
