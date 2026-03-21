using System;
using System.Collections.Generic;
using System.IO;

namespace OceanyaClient
{
    /// <summary>
    /// Known config and support files that can legitimately exist inside character folders.
    /// </summary>
    public static class CharacterSupportFileCatalog
    {
        private static readonly string[] KnownSupportFileNamesArray =
        {
            "char.ini",
            "soundlist.ini",
            "sounds.ini",
            "iniswaps.ini",
            "char.txt",
            "design.txt",
            "soundlist.txt"
        };

        private static readonly HashSet<string> KnownSupportFileNamesSet = new HashSet<string>(
            KnownSupportFileNamesArray,
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the known character support file names.
        /// </summary>
        public static IReadOnlyCollection<string> KnownSupportFileNames => KnownSupportFileNamesArray;

        /// <summary>
        /// Returns whether the file name is a known character support file.
        /// </summary>
        public static bool IsKnownSupportFileName(string fileName)
        {
            string normalizedFileName = (fileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedFileName))
            {
                return false;
            }

            return KnownSupportFileNamesSet.Contains(normalizedFileName);
        }

        /// <summary>
        /// Returns whether the path points to a known character support file.
        /// </summary>
        public static bool IsKnownSupportFilePath(string path)
        {
            string normalizedPath = (path ?? string.Empty).Trim().TrimEnd('/', '\\');
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            string fileName = Path.GetFileName(normalizedPath);
            return IsKnownSupportFileName(fileName);
        }
    }
}
