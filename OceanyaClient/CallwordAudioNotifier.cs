using System;
using System.IO;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.Viewport;

namespace OceanyaClient
{
    /// <summary>
    /// Plays saved callword notification rules for incoming chat messages.
    /// </summary>
    internal sealed class CallwordAudioNotifier : IDisposable
    {
        private readonly AO2BlipPreviewPlayer player = new AO2BlipPreviewPlayer();
        private string currentPath = string.Empty;

        public void TryNotify(string message)
        {
            TryNotify(new CallwordNotificationContext(message, string.Empty, null));
        }

        public void TryNotify(CallwordNotificationContext context)
        {
            foreach (CallwordRule rule in SaveFile.Data.CallwordRules)
            {
                if (!rule.IsEnabled || !RuleMatches(rule, context))
                {
                    continue;
                }

                PlayRule(rule);
                return;
            }
        }

        private static bool RuleMatches(CallwordRule rule, CallwordNotificationContext context)
        {
            string match = string.IsNullOrWhiteSpace(rule.Match) ? rule.Word?.Trim() ?? string.Empty : rule.Match.Trim();
            return rule.TriggerType switch
            {
                CallwordTriggerType.Ao2Callword => MessageMatches(context.Message, match, rule.WholeWord),
                CallwordTriggerType.MessageContains => MessageMatches(context.Message, match, rule.WholeWord),
                CallwordTriggerType.MessageStartsWith => !string.IsNullOrWhiteSpace(match)
                    && StartsWith(context.Message, match, rule.WholeWord),
                CallwordTriggerType.CharacterSpeaks => context.IcMessage != null
                    && EqualsText(context.IcMessage.Character, rule.CharacterName),
                CallwordTriggerType.PlayerShownameSpeaks => EqualsText(context.Showname, match),
                CallwordTriggerType.CharacterEmoteUsed => context.IcMessage != null
                    && EqualsText(context.IcMessage.Character, rule.CharacterName)
                    && EqualsText(context.IcMessage.Emote, rule.EmoteName),
                _ => false
            };
        }

        private void PlayRule(CallwordRule rule)
        {
            string? path = ResolveRulePath(rule);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (!player.TrySetBlip(path))
                {
                    return;
                }

                currentPath = path;
            }

            player.Volume = (float)(AudioSettings.SfxVolume * rule.VolumePercent / 100.0);
            _ = player.PlayBlip();
        }

        private static string? ResolveRulePath(CallwordRule rule)
        {
            string customPath = rule.SoundPath?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                return customPath;
            }

            return CallwordRuleEditorWindow.ResolveDefaultNotificationPath();
        }

        private static bool Contains(string? value, string match)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(match)
                && value.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool MessageMatches(string? value, string match, bool wholeWord)
        {
            return wholeWord ? ContainsWholeWord(value, match) : Contains(value, match);
        }

        internal static bool ContainsWholeWord(string? value, string match)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(match))
            {
                return false;
            }

            string text = value;
            int startIndex = 0;
            while (startIndex < text.Length)
            {
                int matchIndex = text.IndexOf(match, startIndex, StringComparison.OrdinalIgnoreCase);
                if (matchIndex < 0)
                {
                    return false;
                }

                int afterMatchIndex = matchIndex + match.Length;
                if (IsWordBoundary(text, matchIndex - 1) && IsWordBoundary(text, afterMatchIndex))
                {
                    return true;
                }

                startIndex = matchIndex + 1;
            }

            return false;
        }

        private static bool StartsWith(string? value, string match, bool wholeWord)
        {
            string text = (value ?? string.Empty).TrimStart();
            if (!text.StartsWith(match, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !wholeWord || IsWordBoundary(text, match.Length);
        }

        private static bool IsWordBoundary(string text, int index)
        {
            if (index < 0 || index >= text.Length)
            {
                return true;
            }

            char value = text[index];
            return !char.IsLetterOrDigit(value) && value != '_';
        }

        private static bool EqualsText(string? value, string? match)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(match)
                && string.Equals(value.Trim(), match.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            player.Dispose();
        }
    }

    internal sealed class CallwordNotificationContext
    {
        public CallwordNotificationContext(string message, string showname, ICMessage? icMessage)
        {
            Message = message ?? string.Empty;
            Showname = showname ?? string.Empty;
            IcMessage = icMessage;
        }

        public string Message { get; }

        public string Showname { get; }

        public ICMessage? IcMessage { get; }
    }
}
