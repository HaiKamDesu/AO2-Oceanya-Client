using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OceanyaClient
{
    public static class BlipCatalog
    {
        private static readonly object Sync = new object();
        private static List<string> cachedBlips = new List<string>();
        private static string cachedSignature = string.Empty;
        private static readonly string[] AllowedExtensions = { ".opus", ".ogg", ".mp3", ".wav" };

        public static IReadOnlyList<string> GetBlips(bool forceRefresh = false)
        {
            string signature = BuildSignature();
            lock (Sync)
            {
                if (!forceRefresh && cachedBlips.Count > 0 && string.Equals(signature, cachedSignature, StringComparison.Ordinal))
                {
                    return cachedBlips;
                }

                cachedBlips = BuildBlipList();
                cachedSignature = signature;
                return cachedBlips;
            }
        }

        public static IReadOnlyList<string> Refresh()
        {
            return GetBlips(forceRefresh: true);
        }

        private static string BuildSignature()
        {
            List<string> baseFolders = Globals.BaseFolders ?? new List<string>();
            return $"{Globals.PathToConfigINI}|{string.Join("|", baseFolders)}";
        }

        private static List<string> BuildBlipList()
        {
            HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string blipsRoot = Path.Combine(baseFolder, "sounds", "blips");
                if (!Directory.Exists(blipsRoot))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(blipsRoot, "*", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (string filePath in files)
                {
                    string extension = Path.GetExtension(filePath);
                    if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relative = Path.GetRelativePath(blipsRoot, filePath);
                    string noExtension = Path.ChangeExtension(relative, null) ?? string.Empty;
                    string normalized = noExtension.Replace('\\', '/').Trim('/');
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        values.Add(normalized);
                    }
                }
            }

            return values
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
