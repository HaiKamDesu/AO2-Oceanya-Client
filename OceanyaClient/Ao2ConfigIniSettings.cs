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

        // Cache the parsed config.ini so hot paths (per IC message text-log writes) do not re-read and
        // re-parse the entire file from disk on every message. Invalidated by file path/size/timestamp
        // change and explicitly on Save(). See "message freeze" navigation-map entry.
        private static readonly object cacheLock = new object();
        private static string cachedPath = string.Empty;
        private static DateTime cachedWriteUtc = DateTime.MinValue;
        private static long cachedLength = -1;
        private static Dictionary<string, string>? cachedValues;

        public static Dictionary<string, string> Load()
        {
            string path = ConfigPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            DateTime writeUtc;
            long length;
            try
            {
                FileInfo info = new FileInfo(path);
                writeUtc = info.LastWriteTimeUtc;
                length = info.Length;
            }
            catch
            {
                // If we cannot stat the file, fall back to a fresh parse without caching.
                return ReadFromDisk(path);
            }

            lock (cacheLock)
            {
                if (cachedValues != null
                    && string.Equals(cachedPath, path, StringComparison.OrdinalIgnoreCase)
                    && cachedWriteUtc == writeUtc
                    && cachedLength == length)
                {
                    // Return a copy so callers (e.g. SetPercent + Save) can mutate freely without corrupting the cache.
                    return new Dictionary<string, string>(cachedValues, StringComparer.OrdinalIgnoreCase);
                }
            }

            Dictionary<string, string> values = ReadFromDisk(path);

            lock (cacheLock)
            {
                cachedPath = path;
                cachedWriteUtc = writeUtc;
                cachedLength = length;
                cachedValues = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            }

            return values;
        }

        private static Dictionary<string, string> ReadFromDisk(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        private static void InvalidateCache()
        {
            lock (cacheLock)
            {
                cachedValues = null;
                cachedPath = string.Empty;
                cachedWriteUtc = DateTime.MinValue;
                cachedLength = -1;
            }
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
            InvalidateCache();
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
