using Common;

namespace AO2AIBot.Chat
{
    /// <summary>
    /// Represents one transcript entry captured from an AO2 client.
    /// </summary>
    public sealed class ChatLogEntry
    {
        /// <summary>
        /// Gets or sets the UTC timestamp for the entry.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the chat log type.
        /// </summary>
        public string ChatLogType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the character name for IC messages.
        /// </summary>
        public string CharacterName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the visible showname.
        /// </summary>
        public string ShowName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the raw message content.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the character id from the server when present.
        /// </summary>
        public int IniPuppetId { get; set; } = -1;

        /// <summary>
        /// Gets or sets a value indicating whether the message originated from the controlled client.
        /// </summary>
        public bool IsFromSelf { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the message came from the server rather than a player.
        /// </summary>
        public bool IsFromServer { get; set; }
    }

    /// <summary>
    /// Stores and formats AO2 transcript history for AI prompting.
    /// </summary>
    public sealed class ChatLogManager
    {
        private readonly List<ChatLogEntry> chatHistory = new List<ChatLogEntry>();
        private readonly int maxChatHistory;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChatLogManager"/> class.
        /// </summary>
        /// <param name="maxChatHistory">
        /// Maximum number of stored entries. Use <c>-1</c> to store the full session transcript.
        /// </param>
        public ChatLogManager(int maxChatHistory)
        {
            this.maxChatHistory = maxChatHistory;
        }

        /// <summary>
        /// Adds a new message to the transcript.
        /// </summary>
        public void AddMessage(
            string chatLogType,
            string characterName,
            string showName,
            string message,
            int iniPuppetId = -1,
            bool isFromSelf = false)
        {
            ChatLogEntry entry = new ChatLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                ChatLogType = chatLogType?.Trim() ?? string.Empty,
                CharacterName = characterName?.Trim() ?? string.Empty,
                ShowName = showName?.Trim() ?? string.Empty,
                Message = message ?? string.Empty,
                IniPuppetId = iniPuppetId,
                IsFromSelf = isFromSelf
            };

            AddMessage(entry);
        }

        /// <summary>
        /// Adds a transcript entry to the history.
        /// </summary>
        public void AddMessage(ChatLogEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            ChatLogEntry normalizedEntry = new ChatLogEntry
            {
                TimestampUtc = entry.TimestampUtc == default ? DateTime.UtcNow : entry.TimestampUtc,
                ChatLogType = entry.ChatLogType?.Trim() ?? string.Empty,
                CharacterName = entry.CharacterName?.Trim() ?? string.Empty,
                ShowName = entry.ShowName?.Trim() ?? string.Empty,
                Message = entry.Message ?? string.Empty,
                IniPuppetId = entry.IniPuppetId,
                IsFromSelf = entry.IsFromSelf,
                IsFromServer = entry.IsFromServer
            };

            chatHistory.Add(normalizedEntry);
            TrimHistoryIfNeeded();

            CustomConsole.Info(FormatEntry(normalizedEntry));
        }

        /// <summary>
        /// Returns a snapshot of the stored transcript entries.
        /// </summary>
        public IReadOnlyList<ChatLogEntry> GetHistorySnapshot()
        {
            return chatHistory
                .Select(entry => new ChatLogEntry
                {
                    TimestampUtc = entry.TimestampUtc,
                    ChatLogType = entry.ChatLogType,
                    CharacterName = entry.CharacterName,
                    ShowName = entry.ShowName,
                    Message = entry.Message,
                    IniPuppetId = entry.IniPuppetId,
                    IsFromSelf = entry.IsFromSelf,
                    IsFromServer = entry.IsFromServer
                })
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Returns the transcript in prompt-ready text form.
        /// </summary>
        public string GetFormattedChatHistory()
        {
            return string.Join(Environment.NewLine, chatHistory.Select(FormatEntry));
        }

        /// <summary>
        /// Formats an entry for prompt or debug output.
        /// </summary>
        public static string FormatEntry(ChatLogEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            string timestamp = entry.TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            string selfMarker = entry.IsFromSelf ? "[SELF]" : "[OTHER]";
            if (string.Equals(entry.ChatLogType, "OOC", StringComparison.OrdinalIgnoreCase))
            {
                return $"[OOC][{timestamp}]{selfMarker} {entry.ShowName}: {entry.Message}";
            }

            return $"[IC][{timestamp}]{selfMarker} {entry.ShowName} ({entry.CharacterName}): {entry.Message}";
        }

        private void TrimHistoryIfNeeded()
        {
            if (maxChatHistory < 0)
            {
                return;
            }

            while (chatHistory.Count > maxChatHistory)
            {
                chatHistory.RemoveAt(0);
            }
        }
    }
}
