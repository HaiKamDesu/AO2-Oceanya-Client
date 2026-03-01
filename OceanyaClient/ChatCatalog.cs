using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OceanyaClient
{
    public static class ChatCatalog
    {
        private static readonly object Sync = new object();
        private static List<string> cachedChats = new List<string>();
        private static string cachedSignature = string.Empty;

        public static IReadOnlyList<string> GetChats(bool forceRefresh = false)
        {
            string signature = BuildSignature();
            lock (Sync)
            {
                if (!forceRefresh && cachedChats.Count > 0 && string.Equals(signature, cachedSignature, StringComparison.Ordinal))
                {
                    return cachedChats;
                }

                cachedChats = BuildChatList();
                cachedSignature = signature;
                return cachedChats;
            }
        }

        public static IReadOnlyList<string> Refresh()
        {
            return GetChats(forceRefresh: true);
        }

        private static string BuildSignature()
        {
            List<string> baseFolders = Globals.BaseFolders ?? new List<string>();
            return $"{Globals.PathToConfigINI}|{string.Join("|", baseFolders)}";
        }

        private static List<string> BuildChatList()
        {
            HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "default"
            };

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

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(miscRoot, "*", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (string directory in directories)
                {
                    string configPath = Path.Combine(directory, "config.ini");
                    if (!File.Exists(configPath))
                    {
                        continue;
                    }

                    string relative = Path.GetRelativePath(miscRoot, directory);
                    string normalized = relative.Replace('\\', '/').Trim('/');
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
