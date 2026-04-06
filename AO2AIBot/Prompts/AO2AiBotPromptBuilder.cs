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

            // === WHO YOU ARE ===
            sb.AppendLine("## Who You Are Right Now");
            sb.AppendLine($"You are an AI agent actively playing in an Attorney Online 2 session as:");
            sb.AppendLine($"  Character  : {Fallback(snapshot.CurrentCharacter, "(none selected)")}");
            sb.AppendLine($"  IC showname: {Fallback(snapshot.IcShowname, "(same as character)")}");
            sb.AppendLine($"  OOC name   : {Fallback(snapshot.OocShowname, "(not set)")}");
            sb.AppendLine($"  Area       : {Fallback(snapshot.CurrentArea, "(unknown)")}");
            sb.AppendLine($"  Position   : {Fallback(snapshot.CurrentPosition, "(none)")}");
            sb.AppendLine($"  Emote      : {Fallback(snapshot.CurrentEmote, "(none)")}");
            sb.AppendLine($"  Connected  : {(snapshot.IsConnected ? "Yes" : "No")}");
            sb.AppendLine();

            // === AVAILABLE OPTIONS (compact, only what the AI needs) ===
            sb.AppendLine("## Your Available Options");
            AppendCompactList(sb, "Emotes for your character", snapshot.AvailableEmotes);
            AppendCompactList(sb, "Positions", snapshot.AvailablePositions);
            AppendCompactList(sb, "Areas", snapshot.AvailableAreas, maxItems: 20);
            sb.AppendLine();

            // === HOW TO READ THE TRANSCRIPT ===
            sb.AppendLine("## How to Read the Transcript");
            sb.AppendLine("Each line in the transcript uses this format:");
            sb.AppendLine("  [CHANNEL][SELF or OTHER] DisplayName (CharacterName): message text");
            sb.AppendLine("Key:");
            sb.AppendLine("  [IC]    = In-Character message (roleplay dialogue, shown with character sprites)");
            sb.AppendLine("  [OOC]   = Out-Of-Character message (player-to-player chat, no sprites)");
            sb.AppendLine("  [SELF]  = YOU sent this message (your own past responses)");
            sb.AppendLine("  [OTHER] = Another player sent this message (these are what you react to)");
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
            sb.AppendLine("## Your Decision");
            sb.AppendLine("You are a live participant in this AO2 session. Read the conversation above and decide what your character does next.");
            sb.AppendLine();
            sb.AppendLine("Output ONLY a single JSON object — no other text:");
            sb.AppendLine("  To respond:     {\"shouldRespond\":true,\"channel\":\"IC\",\"message\":\"your response here\"}");
            sb.AppendLine("  To stay silent: {\"shouldRespond\":false}");

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
    }
}
