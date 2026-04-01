using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public sealed class GoogleDriveLocalMirrorState
    {
        public List<string> DirectoryPaths { get; set; } = new List<string>();
        public List<GoogleDriveLocalMirrorFileState> Files { get; set; } = new List<GoogleDriveLocalMirrorFileState>();
    }

    public sealed class GoogleDriveLocalMirrorFileState
    {
        public string RelativePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public long LastWriteUtcTicks { get; set; }
    }

    public static class GoogleDriveLocalMirrorStateSupport
    {
        public static GoogleDriveLocalMirrorState Capture(string? rootDirectory)
        {
            string normalizedRoot = rootDirectory?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                return new GoogleDriveLocalMirrorState();
            }

            normalizedRoot = Path.GetFullPath(normalizedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(normalizedRoot))
            {
                return new GoogleDriveLocalMirrorState();
            }

            GoogleDriveLocalMirrorState state = new GoogleDriveLocalMirrorState
            {
                DirectoryPaths = Directory.EnumerateDirectories(normalizedRoot, "*", SearchOption.AllDirectories)
                    .Select(directoryPath => GoogleDriveLocalSnapshotBuilder.NormalizeRelativePath(
                        Path.GetRelativePath(normalizedRoot, directoryPath)))
                    .Where(relativePath => !string.IsNullOrWhiteSpace(relativePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(relativePath => relativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Files = Directory.EnumerateFiles(normalizedRoot, "*", SearchOption.AllDirectories)
                    .Select(filePath =>
                    {
                        FileInfo info = new FileInfo(filePath);
                        return new GoogleDriveLocalMirrorFileState
                        {
                            RelativePath = GoogleDriveLocalSnapshotBuilder.NormalizeRelativePath(
                                Path.GetRelativePath(normalizedRoot, filePath)),
                            Size = info.Length,
                            LastWriteUtcTicks = File.GetLastWriteTimeUtc(filePath).Ticks
                        };
                    })
                    .Where(file => !string.IsNullOrWhiteSpace(file.RelativePath))
                    .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            return Normalize(state);
        }

        public static GoogleDriveLocalMirrorState Normalize(GoogleDriveLocalMirrorState? state)
        {
            GoogleDriveLocalMirrorState normalized = state ?? new GoogleDriveLocalMirrorState();
            normalized.DirectoryPaths = (normalized.DirectoryPaths ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => GoogleDriveLocalSnapshotBuilder.NormalizeRelativePath(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            normalized.Files = (normalized.Files ?? new List<GoogleDriveLocalMirrorFileState>())
                .Where(file => file != null)
                .Select(file => new GoogleDriveLocalMirrorFileState
                {
                    RelativePath = GoogleDriveLocalSnapshotBuilder.NormalizeRelativePath(file.RelativePath),
                    Size = Math.Max(0L, file.Size),
                    LastWriteUtcTicks = Math.Max(0L, file.LastWriteUtcTicks)
                })
                .Where(file => !string.IsNullOrWhiteSpace(file.RelativePath))
                .GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return normalized;
        }

        public static bool HasDifferences(GoogleDriveLocalMirrorState? baseline, string? rootDirectory)
        {
            try
            {
                GoogleDriveLocalMirrorState current = Capture(rootDirectory);
                return HasDifferences(baseline, current);
            }
            catch
            {
                return true;
            }
        }

        public static bool HasDifferences(
            GoogleDriveLocalMirrorState? baseline,
            GoogleDriveLocalMirrorState? current)
        {
            GoogleDriveLocalMirrorState normalizedBaseline = Normalize(baseline);
            GoogleDriveLocalMirrorState normalizedCurrent = Normalize(current);

            if (!normalizedBaseline.DirectoryPaths.SequenceEqual(
                    normalizedCurrent.DirectoryPaths,
                    StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedBaseline.Files.Count != normalizedCurrent.Files.Count)
            {
                return true;
            }

            Dictionary<string, GoogleDriveLocalMirrorFileState> baselineByPath = normalizedBaseline.Files
                .ToDictionary(file => file.RelativePath, file => file, StringComparer.OrdinalIgnoreCase);
            foreach (GoogleDriveLocalMirrorFileState currentFile in normalizedCurrent.Files)
            {
                if (!baselineByPath.TryGetValue(currentFile.RelativePath, out GoogleDriveLocalMirrorFileState? baselineFile))
                {
                    return true;
                }

                if (baselineFile.Size != currentFile.Size
                    || baselineFile.LastWriteUtcTicks != currentFile.LastWriteUtcTicks)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
