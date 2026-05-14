using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AOBot_Testing.Structures;
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
        private const double WpfPixelsPerQtPoint = 96d / 72d;

        public static AO2ChatPreviewStyle Resolve(string? chatToken, bool hasShowname)
        {
            return Resolve(chatToken, hasShowname, preferViewportTheme: false);
        }

        public static AO2ChatPreviewStyle Resolve(string? chatToken, bool hasShowname, bool preferViewportTheme)
        {
            string token = NormalizeChatToken(chatToken);
            AO2ChatPreviewThemeContext themeContext = ResolveThemeContext(preferViewportTheme);
            Dictionary<string, string> fontValues = LoadMergedConfig(token, "courtroom_fonts.ini", preferViewportTheme);
            Dictionary<string, string> chatMarkupValues = LoadMergedConfig(token, "chat_config.ini", preferViewportTheme);
            Dictionary<string, string> designValues = LoadMergedConfig(token, "courtroom_design.ini", preferViewportTheme);

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
            TextAlignment shownameTextAlignment = ParseShownameTextAlignment(GetValue(designValues, "showname_align"));
            int shownameExtraWidth = Math.Max(0, TryParseInt(GetValue(designValues, "showname_extra_width"), 0));

            string? chatboxImagePath = ResolveChatboxImagePath(token, hasShowname, preferViewportTheme);

            AO2ChatPreviewStyle style = new AO2ChatPreviewStyle
            {
                ChatToken = token,
                ChatboxImagePath = chatboxImagePath,
                ShownameColor = shownameColor,
                MessageColor = messageColor,
                ShownameOutlineColor = shownameOutlineColor,
                ShownameFontFamily = string.IsNullOrWhiteSpace(shownameFont) ? "Arial" : shownameFont,
                MessageFontFamily = string.IsNullOrWhiteSpace(messageFont) ? "Arial" : messageFont,
                ShownameFontSize = ConvertQtPointSizeToWpfFontSize(shownameSize * themeContext.ScalingFactor),
                MessageFontSize = ConvertQtPointSizeToWpfFontSize(messageSize * themeContext.ScalingFactor),
                ShownameBold = shownameBold,
                MessageBold = messageBold,
                ShownameOutlined = shownameOutlined,
                ShownameOutlineWidth = shownameOutlineWidth * themeContext.ScalingFactor,
                ShownameTextAlignment = shownameTextAlignment,
                ShownameExtraWidth = shownameExtraWidth * themeContext.ScalingFactor,
                ChatboxBounds = TryParseBounds(GetValue(designValues, "ao2_chatbox"), themeContext.ScalingFactor)
                    ?? TryParseBounds(GetValue(designValues, "chatbox"), themeContext.ScalingFactor)
                    ?? new AO2ChatPreviewBounds(0, 0, 256, 104),
                ShownameBounds = TryParseBounds(GetValue(designValues, "showname"), themeContext.ScalingFactor)
                    ?? new AO2ChatPreviewBounds(1, 0, 46, 15),
                MessageBounds = TryParseBounds(GetValue(designValues, "message"), themeContext.ScalingFactor)
                    ?? new AO2ChatPreviewBounds(6, 12, 238, 60),
                ChatArrowBounds = TryParseBounds(GetValue(designValues, "chat_arrow"), themeContext.ScalingFactor)
                    ?? new AO2ChatPreviewBounds(245, 84, 11, 9)
            };
            for (int i = 0; i < style.ChatColors.Length; i++)
            {
                Color fallback = i == 0
                    ? messageColor
                    : ToWpfColor(ICMessage.GetColorFromTextColor((ICMessage.TextColors)i));
                style.ChatColors[i] = TryParseColor(GetValue(chatMarkupValues, "c" + i.ToString(CultureInfo.InvariantCulture)), fallback);
                style.ChatMarkupStart[i] = GetValue(chatMarkupValues, "c" + i.ToString(CultureInfo.InvariantCulture) + "_start");
                style.ChatMarkupEnd[i] = GetValue(chatMarkupValues, "c" + i.ToString(CultureInfo.InvariantCulture) + "_end");
                style.ChatMarkupRemove[i] = TryParseBool(GetValue(chatMarkupValues, "c" + i.ToString(CultureInfo.InvariantCulture) + "_remove"));
                style.ChatMarkupTalking[i] = !string.Equals(
                    GetValue(chatMarkupValues, "c" + i.ToString(CultureInfo.InvariantCulture) + "_talking"),
                    "0",
                    StringComparison.Ordinal);
            }

            return style;
        }

        private static string NormalizeChatToken(string? chatToken)
        {
            string token = (chatToken ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            return string.IsNullOrWhiteSpace(token) ? string.Empty : token;
        }

        private static Dictionary<string, string> LoadMergedConfig(string chatToken, string fileName, bool preferViewportTheme)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string filePath in EnumerateConfigFilesLowToHigh(chatToken, fileName, preferViewportTheme))
            {
                string? resolvedPath = ResolveFileCaseInsensitive(filePath);
                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    continue;
                }

                try
                {
                    foreach ((string key, string value) in ParseIniFile(resolvedPath))
                    {
                        values[key] = value;
                    }
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning("Chat preview could not parse config: " + resolvedPath, ex);
                }
            }

            return values;
        }

        private static IEnumerable<string> EnumerateConfigFilesLowToHigh(string chatToken, string fileName, bool preferViewportTheme)
        {
            List<string> candidates = EnumerateConfigFilesHighToLow(chatToken, fileName, preferViewportTheme)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            candidates.Reverse();
            return candidates;
        }

        private static IEnumerable<string> EnumerateConfigFilesHighToLow(string chatToken, string fileName, bool preferViewportTheme)
        {
            List<string> baseFolders = Globals.BaseFolders ?? new List<string>();
            AO2ChatPreviewThemeContext themeContext = ResolveThemeContext(preferViewportTheme);
            foreach (string baseFolder in baseFolders)
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                bool hasChatToken = !string.IsNullOrWhiteSpace(chatToken);
                List<string> activeThemeRoots = EnumerateActiveThemeRoots(baseFolder, themeContext, includeSubtheme: true)
                    .ToList();
                if (hasChatToken)
                {
                    foreach (string root in activeThemeRoots)
                    {
                        yield return Path.Combine(root, "misc", chatToken, fileName);
                    }
                }

                if (activeThemeRoots.Count > 1)
                {
                    yield return Path.Combine(activeThemeRoots[0], fileName);
                }

                if (hasChatToken)
                {
                    yield return Path.Combine(baseFolder, "misc", chatToken, fileName);
                }

                string activeThemeRoot = activeThemeRoots.Count > 0
                    ? activeThemeRoots[^1]
                    : BuildThemeRoot(baseFolder, themeContext.Theme);
                if (activeThemeRoots.Count == 1
                    || !string.Equals(activeThemeRoots[0], activeThemeRoot, StringComparison.OrdinalIgnoreCase))
                {
                    yield return Path.Combine(activeThemeRoot, fileName);
                }

                yield return Path.Combine(BuildThemeRoot(baseFolder, "default"), fileName);
                yield return Path.Combine(baseFolder, fileName);

                if (hasChatToken && string.Equals(fileName, "chat_config.ini", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Path.Combine(BuildThemeRoot(baseFolder, themeContext.Theme), "misc", chatToken, "config.ini");
                    yield return Path.Combine(baseFolder, "misc", chatToken, "config.ini");
                    yield return Path.Combine(BuildThemeRoot(baseFolder, themeContext.Theme), "misc", "default", "config.ini");
                    yield return Path.Combine(baseFolder, "misc", "default", "config.ini");
                }
            }
        }

        private static IEnumerable<string> EnumerateThemeRoots(string baseFolder, bool preferViewportTheme)
        {
            AO2ChatPreviewThemeContext themeContext = ResolveThemeContext(preferViewportTheme);
            yield return baseFolder;

            foreach (string root in EnumerateActiveThemeRoots(baseFolder, themeContext, includeSubtheme: true))
            {
                yield return root;
            }

            string defaultRoot = BuildThemeRoot(baseFolder, "default");
            if (!string.Equals(defaultRoot, BuildThemeRoot(baseFolder, themeContext.Theme), StringComparison.OrdinalIgnoreCase))
            {
                yield return defaultRoot;
            }
        }

        private static IEnumerable<string> EnumerateActiveThemeRoots(
            string baseFolder,
            AO2ChatPreviewThemeContext themeContext,
            bool includeSubtheme)
        {
            if (includeSubtheme && !string.IsNullOrWhiteSpace(themeContext.Subtheme))
            {
                yield return Path.Combine(baseFolder, "themes", themeContext.Theme, themeContext.Subtheme);
            }

            yield return BuildThemeRoot(baseFolder, themeContext.Theme);
        }

        private static string BuildThemeRoot(string baseFolder, string theme)
        {
            return Path.Combine(baseFolder, "themes", string.IsNullOrWhiteSpace(theme) ? "default" : theme);
        }

        private static AO2ChatPreviewThemeContext ResolveThemeContext(bool preferViewportTheme)
        {
            Dictionary<string, string> configValues = Ao2ConfigIniSettings.Load();
            string fallbackTheme = preferViewportTheme ? "(714x688) FullChar" : "AceAttorney2x";
            string theme = configValues.TryGetValue("theme", out string? configuredTheme)
                && !string.IsNullOrWhiteSpace(configuredTheme)
                    ? configuredTheme.Trim()
                    : fallbackTheme;
            string subtheme = configValues.TryGetValue("subtheme", out string? configuredSubtheme)
                && !string.IsNullOrWhiteSpace(configuredSubtheme)
                    ? configuredSubtheme.Trim()
                    : string.Empty;
            int scalingFactor = configValues.TryGetValue("theme_scaling_factor", out string? configuredScale)
                && int.TryParse(configuredScale, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedScale)
                && parsedScale > 0
                    ? parsedScale
                    : 1;

            if (string.Equals(subtheme, "server", StringComparison.OrdinalIgnoreCase))
            {
                subtheme = string.Empty;
            }

            return new AO2ChatPreviewThemeContext(theme, subtheme, scalingFactor);
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

        private static double ConvertQtPointSizeToWpfFontSize(int pointSize)
        {
            return Math.Max(8, pointSize) * WpfPixelsPerQtPoint;
        }

        private static bool TryParseBool(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized == "1"
                || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static TextAlignment ParseShownameTextAlignment(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Equals("right", StringComparison.OrdinalIgnoreCase))
            {
                return TextAlignment.Right;
            }

            if (normalized.Equals("center", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("justify", StringComparison.OrdinalIgnoreCase))
            {
                return TextAlignment.Center;
            }

            return TextAlignment.Left;
        }

        private static AO2ChatPreviewBounds? TryParseBounds(string value, int scalingFactor)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            if (parts.Length < 4)
            {
                return null;
            }

            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
                || !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
                || width < 0
                || height < 0)
            {
                return null;
            }

            int scale = Math.Max(1, scalingFactor);
            return new AO2ChatPreviewBounds(x * scale, y * scale, width * scale, height * scale);
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

        private static Color ToWpfColor(System.Drawing.Color color)
        {
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        private static string? ResolveChatboxImagePath(string chatToken, bool hasShowname, bool preferViewportTheme)
        {
            string[] preferredStems = hasShowname
                ? new[] { "chat", "chatbox" }
                : new[] { "chatblank" };

            List<string> baseFolders = Globals.BaseFolders ?? new List<string>();
            AO2ChatPreviewThemeContext themeContext = ResolveThemeContext(preferViewportTheme);
            for (int i = 0; i < baseFolders.Count; i++)
            {
                string baseFolder = baseFolders[i];
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                foreach (string stem in preferredStems)
                {
                    foreach (string directory in EnumerateAo2AssetDirectoriesHighToLow(baseFolder, themeContext, chatToken))
                    {
                        string? match = ResolveStemInDirectory(directory, stem);
                        if (!string.IsNullOrWhiteSpace(match))
                        {
                            return match;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves the AO2 misc folder that owns the active chatbox, if one exists on disk.
        /// </summary>
        public static string? ResolveChatboxDirectoryPath(string? chatToken, bool preferViewportTheme)
        {
            string token = NormalizeChatToken(chatToken);
            List<string> baseFolders = Globals.BaseFolders ?? new List<string>();
            AO2ChatPreviewThemeContext themeContext = ResolveThemeContext(preferViewportTheme);
            foreach (string baseFolder in baseFolders)
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                foreach (string directory in EnumerateAo2MiscDirectoriesHighToLow(baseFolder, themeContext, token))
                {
                    if (Directory.Exists(directory))
                    {
                        return directory;
                    }
                }
            }

            string? imagePath = ResolveChatboxImagePath(token, hasShowname: true, preferViewportTheme);
            return string.IsNullOrWhiteSpace(imagePath) ? null : Path.GetDirectoryName(imagePath);
        }

        private static IEnumerable<string> EnumerateAo2AssetDirectoriesHighToLow(
            string baseFolder,
            AO2ChatPreviewThemeContext themeContext,
            string chatToken)
        {
            bool hasChatToken = !string.IsNullOrWhiteSpace(chatToken);
            List<string> activeThemeRoots = EnumerateActiveThemeRoots(baseFolder, themeContext, includeSubtheme: true)
                .ToList();
            if (hasChatToken)
            {
                foreach (string root in activeThemeRoots)
                {
                    yield return Path.Combine(root, "misc", chatToken);
                }
            }

            if (activeThemeRoots.Count > 1)
            {
                yield return activeThemeRoots[0];
            }

            if (hasChatToken)
            {
                yield return Path.Combine(baseFolder, "misc", chatToken);
            }

            string activeThemeRoot = activeThemeRoots.Count > 0
                ? activeThemeRoots[^1]
                : BuildThemeRoot(baseFolder, themeContext.Theme);
            if (activeThemeRoots.Count == 1
                || !string.Equals(activeThemeRoots[0], activeThemeRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield return activeThemeRoot;
            }

            yield return BuildThemeRoot(baseFolder, "default");
            yield return baseFolder;
        }

        private static IEnumerable<string> EnumerateAo2MiscDirectoriesHighToLow(
            string baseFolder,
            AO2ChatPreviewThemeContext themeContext,
            string chatToken)
        {
            if (string.IsNullOrWhiteSpace(chatToken))
            {
                yield break;
            }

            foreach (string root in EnumerateActiveThemeRoots(baseFolder, themeContext, includeSubtheme: true))
            {
                yield return Path.Combine(root, "misc", chatToken);
            }

            yield return Path.Combine(baseFolder, "misc", chatToken);
        }

        private static string? ResolveStemInDirectory(string directory, string stem)
        {
            foreach (string extension in ImageExtensions)
            {
                string candidate = Path.Combine(directory, stem + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return ResolveStemCaseInsensitive(directory, stem);
        }

        private static string? ResolveStemCaseInsensitive(string directory, string stem)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            foreach (string filePath in Directory.EnumerateFiles(directory))
            {
                string fileStem = Path.GetFileNameWithoutExtension(filePath);
                string extension = Path.GetExtension(filePath);
                if (string.Equals(fileStem, stem, StringComparison.OrdinalIgnoreCase)
                    && ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    return filePath;
                }
            }

            return null;
        }

        private static string? ResolveFileCaseInsensitive(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            if (File.Exists(filePath))
            {
                return filePath;
            }

            string? directory = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
            {
                return null;
            }

            try
            {
                return Directory.EnumerateFiles(directory)
                    .FirstOrDefault(candidate =>
                        string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        public static string? ResolveSiblingImageVariant(string? imagePath, string suffix)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(suffix))
            {
                return null;
            }

            string? directory = Path.GetDirectoryName(imagePath);
            string stem = Path.GetFileNameWithoutExtension(imagePath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stem))
            {
                return null;
            }

            return ResolveStemInDirectory(directory, stem + suffix);
        }
    }

    internal sealed record AO2ChatPreviewThemeContext(string Theme, string Subtheme, int ScalingFactor);

    public sealed record AO2ChatPreviewBounds(int X, int Y, int Width, int Height);

    public sealed class AO2ChatPreviewStyle
    {
        public string ChatToken { get; set; } = "default";
        public string? ChatboxImagePath { get; set; }
        public Color ShownameColor { get; set; } = Colors.White;
        public Color MessageColor { get; set; } = Colors.White;
        public Color ShownameOutlineColor { get; set; } = Colors.Black;
        public string ShownameFontFamily { get; set; } = "Arial";
        public string MessageFontFamily { get; set; } = "Arial";
        public double ShownameFontSize { get; set; } = 14;
        public double MessageFontSize { get; set; } = 14;
        public bool ShownameBold { get; set; }
        public bool MessageBold { get; set; }
        public bool ShownameOutlined { get; set; }
        public int ShownameOutlineWidth { get; set; } = 1;
        public TextAlignment ShownameTextAlignment { get; set; } = TextAlignment.Left;
        public int ShownameExtraWidth { get; set; }
        public AO2ChatPreviewBounds ChatboxBounds { get; set; } = new AO2ChatPreviewBounds(0, 0, 256, 104);
        public AO2ChatPreviewBounds ShownameBounds { get; set; } = new AO2ChatPreviewBounds(1, 0, 46, 15);
        public AO2ChatPreviewBounds MessageBounds { get; set; } = new AO2ChatPreviewBounds(6, 12, 238, 60);
        public AO2ChatPreviewBounds ChatArrowBounds { get; set; } = new AO2ChatPreviewBounds(245, 84, 11, 9);
        public Color[] ChatColors { get; } = new Color[9];
        public string[] ChatMarkupStart { get; } = new string[9];
        public string[] ChatMarkupEnd { get; } = new string[9];
        public bool[] ChatMarkupRemove { get; } = new bool[9];
        public bool[] ChatMarkupTalking { get; } = new bool[9];
    }
}
