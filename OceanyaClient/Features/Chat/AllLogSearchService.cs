using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OceanyaClient.Components;

namespace OceanyaClient.Features.Chat
{
    internal sealed record AllLogSearchOptions(
        string SearchText,
        bool IncludeIc,
        bool IncludeOoc,
        bool MatchCase,
        bool WholeWord,
        bool UseRegex);

    internal sealed record AllLogSearchResult(
        string FilePath,
        string DisplayName,
        DateTime LastWriteTime,
        long FileSizeBytes,
        int MatchCount);

    internal sealed record AllLogSearchSummary(
        IReadOnlyList<AllLogSearchResult> Results,
        int TotalLogFiles,
        long TotalTextLines,
        TimeSpan SearchElapsed);

    internal sealed record AllLogSearchProgress(int FilesScanned, int TotalFiles, string? CurrentFile);

    internal sealed class AllLogSearchService
    {
        private static readonly EnumerationOptions LogEnumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        private readonly record struct FileSearchCounts(int MatchCount, long LineCount);

        public Task<AllLogSearchSummary> SearchAsync(
            string logRoot,
            AllLogSearchOptions options,
            IProgress<AllLogSearchProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() => Search(logRoot, options, progress, cancellationToken), cancellationToken);
        }

        private static AllLogSearchSummary Search(
            string logRoot,
            AllLogSearchOptions options,
            IProgress<AllLogSearchProgress>? progress,
            CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(logRoot)
                || string.IsNullOrWhiteSpace(options.SearchText)
                || (!options.IncludeIc && !options.IncludeOoc)
                || !Directory.Exists(logRoot))
            {
                stopwatch.Stop();
                return new AllLogSearchSummary(Array.Empty<AllLogSearchResult>(), 0, 0, stopwatch.Elapsed);
            }

            List<FileInfo> files = Directory
                .EnumerateFiles(logRoot, "*.log", LogEnumerationOptions)
                .Select(path =>
                {
                    try
                    {
                        return new FileInfo(path);
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(file => file != null)
                .OrderByDescending(file => file!.LastWriteTimeUtc)
                .ThenByDescending(file => file!.Name, StringComparer.OrdinalIgnoreCase)
                .Cast<FileInfo>()
                .ToList();

            LogTextMatcher matcher = LogTextMatcher.Create(
                options.SearchText,
                options.MatchCase,
                options.WholeWord,
                options.UseRegex);
            List<AllLogSearchResult> results = new List<AllLogSearchResult>();
            long totalTextLines = 0;

            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo file = files[i];
                progress?.Report(new AllLogSearchProgress(i, files.Count, file.FullName));

                FileSearchCounts counts = CountMatchesInFile(file.FullName, matcher, options, cancellationToken);
                totalTextLines += counts.LineCount;
                if (counts.MatchCount > 0)
                {
                    results.Add(new AllLogSearchResult(
                        file.FullName,
                        BuildDisplayName(logRoot, file.FullName),
                        file.LastWriteTime,
                        file.Length,
                        counts.MatchCount));
                }

                progress?.Report(new AllLogSearchProgress(i + 1, files.Count, file.FullName));
            }

            stopwatch.Stop();
            IReadOnlyList<AllLogSearchResult> orderedResults = results
                .OrderByDescending(result => result.LastWriteTime)
                .ThenByDescending(result => result.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new AllLogSearchSummary(orderedResults, files.Count, totalTextLines, stopwatch.Elapsed);
        }

        private static FileSearchCounts CountMatchesInFile(
            string filePath,
            LogTextMatcher matcher,
            AllLogSearchOptions options,
            CancellationToken cancellationToken)
        {
            int matchCount = 0;
            long lineCount = 0;
            try
            {
                using FileStream stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    FileOptions.SequentialScan);
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

                while (reader.ReadLine() is { } line)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lineCount++;
                    if (!ShouldSearchLine(line, options))
                    {
                        continue;
                    }

                    matchCount += matcher.FindOffsets(line, cancellationToken).Count;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new FileSearchCounts(0, lineCount);
            }

            return new FileSearchCounts(matchCount, lineCount);
        }

        public static string ReadFileText(string filePath)
        {
            using FileStream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);
            return reader.ReadToEnd();
        }

        public static IReadOnlyList<LogTextOffsetMatch> FindOffsetsInText(
            string text,
            AllLogSearchOptions options,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(options.SearchText))
            {
                return Array.Empty<LogTextOffsetMatch>();
            }

            LogTextMatcher matcher = LogTextMatcher.Create(
                options.SearchText,
                options.MatchCase,
                options.WholeWord,
                options.UseRegex);
            List<LogTextOffsetMatch> results = new List<LogTextOffsetMatch>();
            int lineStart = 0;

            while (lineStart <= text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int lineEnd = lineStart;
                while (lineEnd < text.Length && text[lineEnd] != '\r' && text[lineEnd] != '\n')
                {
                    lineEnd++;
                }

                string line = text.Substring(lineStart, lineEnd - lineStart);
                if (ShouldSearchLine(line, options))
                {
                    foreach (LogTextOffsetMatch match in matcher.FindOffsets(line, cancellationToken))
                    {
                        results.Add(new LogTextOffsetMatch(lineStart + match.StartIndex, match.Length));
                    }
                }

                if (lineEnd >= text.Length)
                {
                    break;
                }

                lineStart = lineEnd + 1;
                if (lineEnd + 1 < text.Length && text[lineEnd] == '\r' && text[lineEnd + 1] == '\n')
                {
                    lineStart++;
                }
            }

            return results;
        }

        private static bool ShouldSearchLine(string line, AllLogSearchOptions options)
        {
            bool isOoc = line.StartsWith("[OOC]", StringComparison.OrdinalIgnoreCase);
            return isOoc ? options.IncludeOoc : options.IncludeIc;
        }

        private static string BuildDisplayName(string logRoot, string filePath)
        {
            try
            {
                return Path.GetRelativePath(logRoot, filePath);
            }
            catch
            {
                return Path.GetFileName(filePath);
            }
        }
    }
}
