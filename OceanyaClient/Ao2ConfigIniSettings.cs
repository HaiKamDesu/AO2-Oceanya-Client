using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Small config.ini reader/writer that preserves comments and unknown keys where practical.
    /// </summary>
    internal static class Ao2ConfigIniSettings
    {
        private static readonly Dictionary<string, string> DefaultValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["default_music"] = "50",
            ["default_sfx"] = "100",
            ["default_blip"] = "50",
            ["suppress_audio"] = "0",
            ["text_crawl"] = "40",
            ["blip_rate"] = "2",
            ["blank_blip"] = "false",
            ["shake"] = "true",
            ["chat_ratelimit"] = "0",
            ["stay_time"] = "200",
            ["log_maximum"] = "200",
            ["automatic_logging_enabled"] = "true",
            ["demo_logging_enabled"] = "true"
        };

        public static IReadOnlyDictionary<string, string> Defaults => DefaultValues;

        public static string ConfigPath => !string.IsNullOrWhiteSpace(Globals.PathToConfigINI)
            ? Globals.PathToConfigINI
            : SaveFile.Data.ConfigIniPath;

        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = ConfigPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return values;
            }

            foreach (string rawLine in File.ReadLines(path))
            {
                string line = (rawLine ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line)
                    || line.StartsWith(";", StringComparison.Ordinal)
                    || line.StartsWith("#", StringComparison.Ordinal)
                    || line.StartsWith("[", StringComparison.Ordinal))
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
                if (!string.IsNullOrWhiteSpace(key))
                {
                    values[key] = value;
                }
            }

            return values;
        }

        public static void Save(IDictionary<string, string> values)
        {
            string path = ConfigPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            List<string> lines = File.Exists(path)
                ? File.ReadAllLines(path).ToList()
                : new List<string>();
            HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                string line = (lines[i] ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line)
                    || line.StartsWith(";", StringComparison.Ordinal)
                    || line.StartsWith("#", StringComparison.Ordinal)
                    || line.StartsWith("[", StringComparison.Ordinal))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                string key = line[..equalsIndex].Trim();
                if (!values.TryGetValue(key, out string? value))
                {
                    continue;
                }

                lines[i] = $"{key}={value}";
                written.Add(key);
            }

            foreach (KeyValuePair<string, string> pair in values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (written.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                lines.Add($"{pair.Key.Trim()}={pair.Value?.Trim() ?? string.Empty}");
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(path, lines);
            Globals.UpdateConfigINI(path);
        }

        public static int GetInt(IDictionary<string, string> values, string key, int fallback)
        {
            return values.TryGetValue(key, out string? raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : fallback;
        }

        public static bool GetBool(IDictionary<string, string> values, string key, bool fallback)
        {
            if (!values.TryGetValue(key, out string? raw) || string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            if (bool.TryParse(raw, out bool parsedBool))
            {
                return parsedBool;
            }

            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt)
                ? parsedInt != 0
                : fallback;
        }

        public static void SetPercent(IDictionary<string, string> values, string key, double percent)
        {
            values[key] = Math.Clamp((int)Math.Round(percent), 0, 100).ToString(CultureInfo.InvariantCulture);
        }
    }
}
