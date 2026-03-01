using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Common;

namespace OceanyaClient.Features.ChatPreview
{
    /// <summary>
    /// AO2_SYNC_CHECK:
    /// This resolver intentionally mirrors AO2 chatbox asset/font lookup concepts (misc chat profiles + courtroom fonts/chat config).
    /// If AO2 changes how chatbox assets/fonts/colors are resolved, this file should be reviewed first.
    /// Reference AO2-Client sources:
    /// - src/text_file_functions.cpp (get_chat_markup/get_chat_color/get_chat)
    /// - src/courtroom.cpp (initialize_chatbox/set_font)
    /// </summary>
    public static class AO2ChatPreviewResolver
    {
        private static readonly string[] ImageExtensions = { ".webp", ".apng", ".gif", ".png", ".jpg", ".jpeg" };
        private static readonly string[] PreferredThemeNames = { "default", "CC", "CCDefault", "CCBig", "CC1080p" };

        public static AO2ChatPreviewStyle Resolve(string? chatToken, bool hasShowname)
        {
            string token = NormalizeChatToken(chatToken);
            Dictionary<string, string> fontValues = LoadMergedConfig(token, "courtroom_fonts.ini");
            Dictionary<string, string> chatMarkupValues = LoadMergedConfig(token, "chat_config.ini");

            Color shownameColor = TryParseColor(GetValue(fontValues, "showname_color"), Colors.White);
            Color messageColor = TryParseColor(GetValue(fontValues, "message_color"), TryParseColor(GetValue(chatMarkupValues, "c0"), Colors.White));
            Color shownameOutlineColor = TryParseColor(GetValue(fontValues, "showname_outline_color"), Colors.Black);

            int shownameSize = TryParseInt(GetValue(fontValues, "showname"), 14);
            int messageSize = TryParseInt(GetValue(fontValues, "message"), 14);

            string shownameFont = GetValue(fontValues, "showname_font");
            string messageFont = GetValue(fontValues, "message_font");

            bool shownameBold = TryParseBool(GetValue(fontValues, "showname_bold"));
            bool messageBold = TryParseBool(GetValue(fontValues, "message_bold"));
            bool shownameOutlined = TryParseBool(GetValue(fontValues, "showname_outlined"));
            int shownameOutlineWidth = Math.Max(1, TryParseInt(GetValue(fontValues, "showname_outline_width"), 1));

            string? chatboxImagePath = ResolveChatboxImagePath(token, hasShowname);

            return new AO2ChatPreviewStyle
            {
                ChatToken = token,
                ChatboxImagePath = chatboxImagePath,
                ShownameColor = shownameColor,
                MessageColor = messageColor,
                ShownameOutlineColor = shownameOutlineColor,
                ShownameFontFamily = string.IsNullOrWhiteSpace(shownameFont) ? "Arial" : shownameFont,
                MessageFontFamily = string.IsNullOrWhiteSpace(messageFont) ? "Arial" : messageFont,
                ShownameFontSize = Math.Max(8, shownameSize),
                MessageFontSize = Math.Max(8, messageSize),
                ShownameBold = shownameBold,
                MessageBold = messageBold,
                ShownameOutlined = shownameOutlined,
                ShownameOutlineWidth = shownameOutlineWidth
            };
        }

        private static string NormalizeChatToken(string? chatToken)
        {
            string token = (chatToken ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            return string.IsNullOrWhiteSpace(token) ? "default" : token;
        }

        private static Dictionary<string, string> LoadMergedConfig(string chatToken, string fileName)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> candidates = EnumerateCandidateFiles(chatToken, fileName).ToList();
            foreach (string filePath in candidates)
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                try
                {
                    foreach ((string key, string value) in ParseIniFile(filePath))
                    {
                        values[key] = value;
                    }
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning("Chat preview could not parse config: " + filePath, ex);
                }
            }

            return values;
        }

        private static IEnumerable<string> EnumerateCandidateFiles(string chatToken, string fileName)
        {
            List<string> baseFolders = Globals.BaseFolders ?? new List<string>();
            for (int i = baseFolders.Count - 1; i >= 0; i--)
            {
                string baseFolder = baseFolders[i];
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                foreach (string root in EnumerateThemeRoots(baseFolder))
                {
                    yield return Path.Combine(root, fileName);
                    yield return Path.Combine(root, "misc", "default", fileName);
                    yield return Path.Combine(root, "misc", chatToken, fileName);
                }
            }
        }

        private static IEnumerable<string> EnumerateThemeRoots(string baseFolder)
        {
            yield return baseFolder;

            string themesRoot = Path.Combine(baseFolder, "themes");
            if (!Directory.Exists(themesRoot))
            {
                yield break;
            }

            List<string> themeDirectories = new List<string>();
            try
            {
                themeDirectories.AddRange(Directory.EnumerateDirectories(themesRoot));
            }
            catch
            {
                yield break;
            }

            foreach (string preferred in PreferredThemeNames)
            {
                string? path = themeDirectories.FirstOrDefault(dir =>
                    string.Equals(Path.GetFileName(dir), preferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }

            foreach (string path in themeDirectories.OrderBy(static dir => dir, StringComparer.OrdinalIgnoreCase))
            {
                if (PreferredThemeNames.Any(name =>
                    string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                yield return path;
            }
        }

        private static IEnumerable<(string Key, string Value)> ParseIniFile(string filePath)
        {
            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                string key = line[..equalsIndex].Trim();
                string value = line[(equalsIndex + 1)..].Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                yield return (key, value);
            }
        }

        private static string GetValue(IReadOnlyDictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out string? value) ? value : string.Empty;
        }

        private static int TryParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }

        private static bool TryParseBool(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized == "1"
                || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static Color TryParseColor(string value, Color fallback)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            if (parts.Length < 3)
            {
                return fallback;
            }

            if (!byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r))
            {
                return fallback;
            }

            if (!byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g))
            {
                return fallback;
            }

            if (!byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
            {
                return fallback;
            }

            return Color.FromRgb(r, g, b);
        }

        private static string? ResolveChatboxImagePath(string chatToken, bool hasShowname)
        {
            string[] preferredStems = hasShowname
                ? new[] { "chat", "chatbox", "chatblank" }
                : new[] { "chatblank", "chat", "chatbox" };

            List<string> baseFolders = Globals.BaseFolders ?? new List<string>();
            for (int i = 0; i < baseFolders.Count; i++)
            {
                string baseFolder = baseFolders[i];
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                foreach (string root in EnumerateThemeRoots(baseFolder))
                {
                    foreach (string stem in preferredStems)
                    {
                        string? match = ResolveStem(root, chatToken, stem);
                        if (!string.IsNullOrWhiteSpace(match))
                        {
                            return match;
                        }
                    }
                }
            }

            return null;
        }

        private static string? ResolveStem(string root, string chatToken, string stem)
        {
            foreach (string extension in ImageExtensions)
            {
                string tokenCandidate = Path.Combine(root, "misc", chatToken, stem + extension);
                if (File.Exists(tokenCandidate))
                {
                    return tokenCandidate;
                }

                string defaultCandidate = Path.Combine(root, "misc", "default", stem + extension);
                if (File.Exists(defaultCandidate))
                {
                    return defaultCandidate;
                }

                string rootCandidate = Path.Combine(root, stem + extension);
                if (File.Exists(rootCandidate))
                {
                    return rootCandidate;
                }
            }

            return null;
        }
    }

    public sealed class AO2ChatPreviewStyle
    {
        public string ChatToken { get; set; } = "default";
        public string? ChatboxImagePath { get; set; }
        public Color ShownameColor { get; set; } = Colors.White;
        public Color MessageColor { get; set; } = Colors.White;
        public Color ShownameOutlineColor { get; set; } = Colors.Black;
        public string ShownameFontFamily { get; set; } = "Arial";
        public string MessageFontFamily { get; set; } = "Arial";
        public int ShownameFontSize { get; set; } = 14;
        public int MessageFontSize { get; set; } = 14;
        public bool ShownameBold { get; set; }
        public bool MessageBold { get; set; }
        public bool ShownameOutlined { get; set; }
        public int ShownameOutlineWidth { get; set; } = 1;
    }
}
