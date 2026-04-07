using AO2AIBot.Chat;
using AO2AIBot.Controller;
using System.Text;

namespace AO2AIBot.Prompts
{
    /// <summary>
    /// Builds user prompts for the AO2 AI bot.
    /// </summary>
    public static class AO2AiBotPromptBuilder
    {
        private const int ParticipantWindowSize = 30;

        /// <summary>
        /// Builds a prompt using the current client snapshot and transcript.
        /// Frames the model as an active participant in the conversation, not an analyst.
        /// </summary>
        public static string BuildPrompt(
            AOClientControlSnapshot snapshot,
            IReadOnlyList<ChatLogEntry> history,
            ChatLogEntry? latestEntry,
            string triggerReason)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            StringBuilder sb = new StringBuilder();

            // === WHO YOU ARE + SESSION STATE (single compact block) ===
            sb.AppendLine("## You");
            sb.AppendLine($"Character: {Fallback(snapshot.CurrentCharacter, "none")} | IC name: {Fallback(snapshot.IcShowname, "same")} | OOC name: {Fallback(snapshot.OocShowname, "none")}");
            sb.Append($"Server: {Fallback(snapshot.ServerName, "?")} | ");
            AppendCurrentAreaInline(sb, snapshot);
            sb.AppendLine($" | Pos: {Fallback(snapshot.CurrentPosition, "?")} | BG: {Fallback(snapshot.CurrentBackground, "?")}");
            sb.AppendLine();

            // === ACTIVE MODIFIERS — only show non-default values ===
            AppendActiveModifiers(sb, snapshot);

            // === CHAT CONTEXT — participant count and direct address ===
            AppendChatContext(sb, snapshot, history, latestEntry);
            sb.AppendLine();

            // === AVAILABLE OPTIONS — compact ===
            sb.AppendLine("## Options");
            AppendCompactList(sb, "Emotes", snapshot.AvailableEmotes);
            AppendCompactList(sb, "Positions", snapshot.AvailablePositions);
            AppendAreaInfoList(sb, snapshot);
            AppendIniPuppetList(sb, snapshot);
            AppendCharacterList(sb, snapshot);
            sb.AppendLine();

            // === CONVERSATION HISTORY ===
            sb.AppendLine("## Conversation History");
            if (history.Count == 0)
            {
                sb.AppendLine("(no messages yet — this is the start of the session)");
            }
            else
            {
                foreach (ChatLogEntry entry in history)
                {
                    sb.AppendLine(FormatEntryForPrompt(entry));
                }
            }

            sb.AppendLine();

            // === WHAT TRIGGERED THIS EVALUATION ===
            if (latestEntry != null)
            {
                sb.AppendLine("## Message You Are Responding To");
                sb.AppendLine("The following message just arrived and triggered this evaluation:");
                sb.AppendLine($"  {FormatEntryForPrompt(latestEntry)}");
                sb.AppendLine();
                sb.AppendLine("This is a real message from another player in your AO2 session. You may respond to it IC or OOC, or stay silent.");
            }
            else
            {
                sb.AppendLine("## Evaluation Context");
                sb.AppendLine("This evaluation was manually triggered. Review the conversation history above and decide if any response is warranted.");
            }

            sb.AppendLine();

            // === CLOSING DIRECTIVE — placed last so local models weight it most ===
            sb.AppendLine("## Your Turn");
            sb.AppendLine("Check Chat Context: how many participants, are you directly addressed?");
            sb.AppendLine("If in doubt: RESPOND. Only stay silent when the message clearly has nothing to do with you.");
            sb.AppendLine();
            sb.AppendLine("Output ONLY a single JSON object. Start with your reasoning in \"thinking\", then decide:");
            sb.AppendLine("  To respond:     {\"thinking\":\"reason\",\"shouldRespond\":true,\"channel\":\"IC\",\"message\":\"...\"}");
            sb.AppendLine("  To stay silent: {\"thinking\":\"reason\",\"shouldRespond\":false}");

            return sb.ToString();
        }

        /// <summary>
        /// Builds a correction prompt for the retry attempt when a previous model response was invalid.
        /// The schema-constrained format is already enforced, so this prompt focuses on clarifying intent.
        /// </summary>
        public static string BuildCorrectionPrompt(string originalPrompt, string failedResponse)
        {
            string trimmedFailed = (failedResponse ?? string.Empty).Trim();
            string trimmedOriginal = (originalPrompt ?? string.Empty).Trim();

            return
                "Your previous response could not be parsed. Output ONLY a valid JSON object.\n\n"
                + "To respond: {\"shouldRespond\":true,\"channel\":\"IC\",\"message\":\"your text\"}\n"
                + "To stay silent: {\"shouldRespond\":false}\n\n"
                + "Your previous invalid response:\n"
                + "---\n"
                + trimmedFailed
                + "\n---\n\n"
                + "Context (same as before):\n\n"
                + trimmedOriginal;
        }

        private static string FormatEntryForPrompt(ChatLogEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string selfMarker = entry.IsFromSelf ? "[SELF] " : "[OTHER] ";
            if (string.Equals(entry.ChatLogType, "OOC", StringComparison.OrdinalIgnoreCase))
            {
                return $"[OOC]{selfMarker}{entry.ShowName}: {entry.Message}";
            }

            string characterSuffix = string.IsNullOrWhiteSpace(entry.CharacterName)
                ? string.Empty
                : $" ({entry.CharacterName})";
            return $"[IC]{selfMarker}{entry.ShowName}{characterSuffix}: {entry.Message}";
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

        private static string Fallback(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        /// <summary>
        /// Appends a "Chat Context" block that tells the model how many other people are in the
        /// conversation and whether the latest message directly addresses it by name.
        /// </summary>
        private static void AppendChatContext(
            StringBuilder sb,
            AOClientControlSnapshot snapshot,
            IReadOnlyList<ChatLogEntry> history,
            ChatLogEntry? latestEntry)
        {
            // Collect distinct [OTHER] participants from the recent window.
            IEnumerable<ChatLogEntry> window = history.Count > ParticipantWindowSize
                ? history.Skip(history.Count - ParticipantWindowSize)
                : history;

            HashSet<string> participants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ChatLogEntry entry in window)
            {
                if (!entry.IsFromSelf && !string.IsNullOrWhiteSpace(entry.ShowName))
                {
                    participants.Add(entry.ShowName);
                }
            }

            // Check whether the triggering message directly names the bot.
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
                sb.AppendLine("  Direct address: YES — your name appears in the message you're evaluating");
            }
            else
            {
                sb.AppendLine("  Direct address: no");
            }

            // Give the model a plain-language hint calibrated to participant count.
            if (directAddress)
            {
                sb.AppendLine("  → You were directly addressed. You MUST respond.");
            }
            else if (participants.Count <= 1)
            {
                sb.AppendLine("  → Only one other person is present. Treat this like a private conversation — respond unless the message is clearly not meant for you.");
            }
            else if (participants.Count <= 3)
            {
                sb.AppendLine("  → Small group. Respond if the conversation includes you or you have something relevant to add.");
            }
            else
            {
                sb.AppendLine("  → Larger group. Respond when directly addressed or when you have a clear reason to contribute.");
            }
        }

        private static bool ContainsName(string message, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return message.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Appends only the non-default modifiers to save tokens when nothing has changed.
        /// </summary>
        private static void AppendActiveModifiers(StringBuilder sb, AOClientControlSnapshot snapshot)
        {
            List<string> parts = new List<string>();
            parts.Add($"emote={Fallback(snapshot.CurrentEmote, "none")}");

            string color = Fallback(snapshot.TextColor, "white");
            if (!string.Equals(color, "White", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"color={color}");
            }

            string shout = Fallback(snapshot.ShoutModifier, "nothing");
            if (!string.Equals(shout, "nothing", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"shout={shout}");
            }

            string effect = Fallback(snapshot.Effect, "None");
            if (!string.Equals(effect, "None", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"effect={effect}");
            }

            if (snapshot.Flip)
            {
                parts.Add("flip=true");
            }

            if (snapshot.PreanimEnabled)
            {
                parts.Add("preanim=true");
            }

            if (snapshot.Additive)
            {
                parts.Add("additive=true");
            }

            if (snapshot.Immediate)
            {
                parts.Add("immediate=true");
            }

            if (snapshot.Screenshake)
            {
                parts.Add("shake=true");
            }

            if (snapshot.SelfOffsetHorizontal != 0 || snapshot.SelfOffsetVertical != 0)
            {
                parts.Add($"offset={snapshot.SelfOffsetHorizontal},{snapshot.SelfOffsetVertical}");
            }

            sb.AppendLine($"## Active State: {string.Join(" | ", parts)}");
            sb.AppendLine("(Include a field in your JSON only if you want to CHANGE it from this state.)");
            sb.AppendLine();
        }

        /// <summary>
        /// Appends the current area inline (no trailing newline — caller adds it).
        /// </summary>
        private static void AppendCurrentAreaInline(StringBuilder sb, AOClientControlSnapshot snapshot)
        {
            string areaId = Fallback(snapshot.CurrentArea, "?");
            AOBot_Testing.Structures.AreaInfo? info = snapshot.AvailableAreaInfos
                .FirstOrDefault(a => string.Equals(a.Name, areaId, StringComparison.OrdinalIgnoreCase))
                ?? snapshot.AvailableAreaInfos.FirstOrDefault(a =>
                    a.Name.IndexOf(areaId, StringComparison.OrdinalIgnoreCase) >= 0);

            if (info != null)
            {
                sb.Append($"Area: {info.Name} ({info.Players} users, {info.Status}, {info.LockState})");
            }
            else
            {
                sb.Append($"Area: {areaId}");
            }
        }

        /// <summary>
        /// Appends the area list with player counts and status.
        /// </summary>
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

        /// <summary>
        /// Appends available INI puppets (server character slots) with availability.
        /// </summary>
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

            sb.Append("  INI Puppets (server slots): ");
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

        /// <summary>
        /// Appends the character list — limited to 30 to conserve context tokens.
        /// The substring-match fallback in the executor handles approximate names.
        /// </summary>
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

            sb.AppendLine($"  Characters ({snapshot.AvailableCharacters.Count} total, use exact name): {string.Join(", ", display)}{suffix}");
        }
    }
}
