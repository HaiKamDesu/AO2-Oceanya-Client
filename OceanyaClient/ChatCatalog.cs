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

                foreach (string miscRoot in EnumerateMiscRoots(baseFolder))
                {
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
                        if (!LooksLikeChatboxFolder(directory))
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
            }

            return values
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> EnumerateMiscRoots(string baseFolder)
        {
            string rootMisc = Path.Combine(baseFolder, "misc");
            if (Directory.Exists(rootMisc))
            {
                yield return rootMisc;
            }

            string themesRoot = Path.Combine(baseFolder, "themes");
            if (!Directory.Exists(themesRoot))
            {
                yield break;
            }

            IEnumerable<string> themeDirectories;
            try
            {
                themeDirectories = Directory.EnumerateDirectories(themesRoot, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                yield break;
            }

            foreach (string themeDirectory in themeDirectories)
            {
                string themeMisc = Path.Combine(themeDirectory, "misc");
                if (Directory.Exists(themeMisc))
                {
                    yield return themeMisc;
                }

                IEnumerable<string> subthemeDirectories;
                try
                {
                    subthemeDirectories = Directory.EnumerateDirectories(themeDirectory, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (string subthemeDirectory in subthemeDirectories)
                {
                    string subthemeMisc = Path.Combine(subthemeDirectory, "misc");
                    if (Directory.Exists(subthemeMisc))
                    {
                        yield return subthemeMisc;
                    }
                }
            }
        }

        private static bool LooksLikeChatboxFolder(string directory)
        {
            if (File.Exists(Path.Combine(directory, "config.ini"))
                || File.Exists(Path.Combine(directory, "chat_config.ini"))
                || File.Exists(Path.Combine(directory, "courtroom_design.ini"))
                || File.Exists(Path.Combine(directory, "courtroom_fonts.ini")))
            {
                return true;
            }

            return HasImageStem(directory, "chat")
                || HasImageStem(directory, "chatbox")
                || HasImageStem(directory, "chatblank");
        }

        private static bool HasImageStem(string directory, string stem)
        {
            foreach (string extension in new[] { ".webp", ".apng", ".gif", ".png", ".jpg", ".jpeg" })
            {
                if (File.Exists(Path.Combine(directory, stem + extension)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
