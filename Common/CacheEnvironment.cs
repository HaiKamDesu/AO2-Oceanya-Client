using System;
using System.IO;
using OceanyaClient;

namespace Common
{
    /// <summary>
    /// Resolves the root directory for per-config disk caches (character/background/folder-visualizer
    /// lookups), following the same production/dev/unit-test isolation as <see cref="SaveFile"/> so that
    /// test runs never read or write the real user's cache directory.
    /// </summary>
    public static class CacheEnvironment
    {
        public static string GetCacheRoot()
        {
            string saveFileDirectory = Path.GetDirectoryName(SaveFile.CurrentStoragePath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(saveFileDirectory))
            {
                return Path.Combine(saveFileDirectory, "cache");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OceanyaClient",
                "cache");
        }
    }
}
