using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OceanyaClient
{
    public static class EffectsFolderCatalog
    {
        private static readonly object Sync = new object();
        private static List<string> cachedFolders = new List<string>();
        private static string cachedSignature = string.Empty;

        public static IReadOnlyList<string> GetEffectFolders(bool forceRefresh = false)
        {
            string signature = BuildSignature();
            lock (Sync)
            {
                if (!forceRefresh && cachedFolders.Count > 0 && string.Equals(signature, cachedSignature, StringComparison.Ordinal))
                {
                    return cachedFolders;
                }

                cachedFolders = BuildList();
                cachedSignature = signature;
                return cachedFolders;
            }
        }

        public static IReadOnlyList<string> Refresh()
        {
            return GetEffectFolders(forceRefresh: true);
        }

        private static string BuildSignature()
        {
            List<string> baseFolders = Globals.BaseFolders ?? new List<string>();
            return $"{Globals.PathToConfigINI}|{string.Join("|", baseFolders)}";
        }

        private static List<string> BuildList()
        {
            HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string miscRoot = Path.Combine(baseFolder, "misc");
                if (!Directory.Exists(miscRoot))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(miscRoot, "effects.ini", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (string effectsIniPath in files)
                {
                    string folder = Path.GetDirectoryName(effectsIniPath) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(folder))
                    {
                        continue;
                    }

                    string relative = Path.GetRelativePath(miscRoot, folder).Replace('\\', '/').Trim('/');
                    if (!string.IsNullOrWhiteSpace(relative))
                    {
                        values.Add(relative);
                    }
                }
            }

            return values
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
