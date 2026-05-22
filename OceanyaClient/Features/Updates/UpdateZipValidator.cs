using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace OceanyaClient.Features.Updates
{
    public static class UpdateZipValidator
    {
        public static string ExtractValidatedPackage(string zipPath, string extractionRoot)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                throw new FileNotFoundException("The update package was not found.", zipPath);
            }

            if (Directory.Exists(extractionRoot))
            {
                Directory.Delete(extractionRoot, recursive: true);
            }

            Directory.CreateDirectory(extractionRoot);
            string fullExtractionRoot = EnsureTrailingSeparator(Path.GetFullPath(extractionRoot));
            HashSet<string> topLevelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ValidateEntry(entry, fullExtractionRoot, topLevelNames);
            }

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.GetFullPath(Path.Combine(extractionRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? extractionRoot);
                entry.ExtractToFile(destinationPath, overwrite: false);
            }

            string packageRoot = ResolvePackageRoot(extractionRoot, topLevelNames);
            ValidateExtractedTree(packageRoot, fullExtractionRoot);
            if (!File.Exists(Path.Combine(packageRoot, "OceanyaClient.exe")))
            {
                throw new InvalidOperationException("The update package does not contain OceanyaClient.exe.");
            }

            return packageRoot;
        }

        private static string ResolvePackageRoot(string extractionRoot, HashSet<string> topLevelNames)
        {
            List<string> candidates = new List<string>();
            if (File.Exists(Path.Combine(extractionRoot, "OceanyaClient.exe")))
            {
                candidates.Add(extractionRoot);
            }

            foreach (string topLevelName in topLevelNames)
            {
                string topLevelPath = Path.Combine(extractionRoot, topLevelName);
                if (Directory.Exists(topLevelPath) && File.Exists(Path.Combine(topLevelPath, "OceanyaClient.exe")))
                {
                    candidates.Add(topLevelPath);
                }
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count > 1)
            {
                throw new InvalidOperationException("The update package contains multiple possible OceanyaClient.exe roots.");
            }

            throw new InvalidOperationException(
                "The update package must contain OceanyaClient.exe either at the zip root or inside one top-level application folder.");
        }

        private static void ValidateEntry(ZipArchiveEntry entry, string fullExtractionRoot, HashSet<string> topLevelNames)
        {
            string name = entry.FullName?.Replace('\\', '/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("The update package contains an empty path.");
            }

            if (name.StartsWith("/", StringComparison.Ordinal)
                || name.StartsWith("\\", StringComparison.Ordinal)
                || Path.IsPathFullyQualified(name)
                || name.Contains("../", StringComparison.Ordinal)
                || name.Contains("/..", StringComparison.Ordinal)
                || name.Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The update package contains an unsafe path: " + name);
            }

            string[] parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Any(part => part == "." || part == ".." || part.Contains(':', StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("The update package contains an unsafe path: " + name);
            }

            if (IsSymlinkEntry(entry))
            {
                throw new InvalidOperationException("The update package contains a symbolic link: " + name);
            }

            string destinationPath = Path.GetFullPath(Path.Combine(fullExtractionRoot, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!destinationPath.StartsWith(fullExtractionRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The update package contains a path traversal entry: " + name);
            }

            topLevelNames.Add(parts[0]);
        }

        private static bool IsSymlinkEntry(ZipArchiveEntry entry)
        {
            int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            return unixMode == 0xA000;
        }

        private static void ValidateExtractedTree(string packageRoot, string fullExtractionRoot)
        {
            string fullPackageRoot = EnsureTrailingSeparator(Path.GetFullPath(packageRoot));
            if (!fullPackageRoot.StartsWith(fullExtractionRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The extracted package root is outside the staging folder.");
            }

            foreach (string path in Directory.EnumerateFileSystemEntries(packageRoot, "*", SearchOption.AllDirectories))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("The extracted package contains a reparse point: " + path);
                }
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
