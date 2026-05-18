using System;
using System.Collections.Generic;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace OceanyaClient.Components
{
    public readonly record struct LogTextOffsetMatch(int StartIndex, int Length);

    public static class LogDocumentSearch
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        internal sealed class TextSegment
        {
            public TextSegment(int start, int length, TextPointer pointer)
            {
                Start = start;
                Length = length;
                Pointer = pointer;
            }

            public int Start { get; }
            public int Length { get; }
            public int End => Start + Length;
            public TextPointer Pointer { get; }
        }

        public static IReadOnlyList<LogTextMatch> Find(
            FlowDocument document,
            string searchText,
            bool matchCase,
            bool wholeWord,
            bool useRegex)
        {
            if (document == null || string.IsNullOrEmpty(searchText))
            {
                return Array.Empty<LogTextMatch>();
            }

            DocumentTextIndex index = BuildIndex(document);
            if (index.Text.Length == 0)
            {
                return Array.Empty<LogTextMatch>();
            }

            return useRegex
                ? FindRegex(index, searchText, matchCase, wholeWord)
                : FindPlainText(index, searchText, matchCase, wholeWord);
        }

        public static IReadOnlyList<LogTextOffsetMatch> FindOffsets(
            DocumentTextIndex index,
            string searchText,
            bool matchCase,
            bool wholeWord,
            bool useRegex,
            CancellationToken cancellationToken = default)
        {
            if (index == null || string.IsNullOrEmpty(searchText) || index.Text.Length == 0)
            {
                return Array.Empty<LogTextOffsetMatch>();
            }

            return useRegex
                ? FindRegexOffsets(index.Text, searchText, matchCase, wholeWord, cancellationToken)
                : FindPlainTextOffsets(index.Text, searchText, matchCase, wholeWord, cancellationToken);
        }

        public static DocumentTextIndex CreateIndex(FlowDocument document)
        {
            return BuildIndex(document);
        }

        public static IReadOnlyList<LogTextMatch> ResolveMatches(
            DocumentTextIndex index,
            IReadOnlyList<LogTextOffsetMatch> matches)
        {
            if (index == null || matches.Count == 0)
            {
                return Array.Empty<LogTextMatch>();
            }

            List<LogTextMatch> results = new List<LogTextMatch>(matches.Count);
            foreach (LogTextOffsetMatch match in matches)
            {
                AddResult(index, match.StartIndex, match.Length, results);
            }

            return results;
        }

        private static IReadOnlyList<LogTextMatch> FindPlainText(
            DocumentTextIndex index,
            string searchText,
            bool matchCase,
            bool wholeWord)
        {
            List<LogTextMatch> results = new List<LogTextMatch>();
            StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int cursor = 0;

            while (cursor < index.Text.Length)
            {
                int matchIndex = index.Text.IndexOf(searchText, cursor, comparison);
                if (matchIndex < 0)
                {
                    break;
                }

                if (!wholeWord || IsWholeWordMatch(index.Text, matchIndex, searchText.Length))
                {
                    AddResult(index, matchIndex, searchText.Length, results);
                }

                cursor = matchIndex + Math.Max(1, searchText.Length);
            }

            return results;
        }

        private static IReadOnlyList<LogTextOffsetMatch> FindPlainTextOffsets(
            string text,
            string searchText,
            bool matchCase,
            bool wholeWord,
            CancellationToken cancellationToken)
        {
            List<LogTextOffsetMatch> results = new List<LogTextOffsetMatch>();
            StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int cursor = 0;

            while (cursor < text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int matchIndex = text.IndexOf(searchText, cursor, comparison);
                if (matchIndex < 0)
                {
                    break;
                }

                if (!wholeWord || IsWholeWordMatch(text, matchIndex, searchText.Length))
                {
                    results.Add(new LogTextOffsetMatch(matchIndex, searchText.Length));
                }

                cursor = matchIndex + Math.Max(1, searchText.Length);
            }

            return results;
        }

        private static IReadOnlyList<LogTextMatch> FindRegex(
            DocumentTextIndex index,
            string pattern,
            bool matchCase,
            bool wholeWord)
        {
            List<LogTextMatch> results = new List<LogTextMatch>();
            RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (!matchCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            Regex regex;
            try
            {
                regex = new Regex(pattern, options, RegexTimeout);
            }
            catch
            {
                return results;
            }

            try
            {
                foreach (Match match in regex.Matches(index.Text))
                {
                    if (!match.Success || match.Length == 0)
                    {
                        continue;
                    }

                    if (!wholeWord || IsWholeWordMatch(index.Text, match.Index, match.Length))
                    {
                        AddResult(index, match.Index, match.Length, results);
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return results;
            }

            return results;
        }

        private static IReadOnlyList<LogTextOffsetMatch> FindRegexOffsets(
            string text,
            string pattern,
            bool matchCase,
            bool wholeWord,
            CancellationToken cancellationToken)
        {
            List<LogTextOffsetMatch> results = new List<LogTextOffsetMatch>();
            RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (!matchCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            Regex regex;
            try
            {
                regex = new Regex(pattern, options, RegexTimeout);
            }
            catch
            {
                return results;
            }

            try
            {
                foreach (Match match in regex.Matches(text))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!match.Success || match.Length == 0)
                    {
                        continue;
                    }

                    if (!wholeWord || IsWholeWordMatch(text, match.Index, match.Length))
                    {
                        results.Add(new LogTextOffsetMatch(match.Index, match.Length));
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return results;
            }

            return results;
        }

        private static DocumentTextIndex BuildIndex(FlowDocument document)
        {
            StringBuilder text = new StringBuilder();
            List<TextSegment> segments = new List<TextSegment>();
            TextPointer? current = document.ContentStart;

            while (current != null && current.CompareTo(document.ContentEnd) < 0)
            {
                if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string runText = current.GetTextInRun(LogicalDirection.Forward);
                    if (runText.Length > 0)
                    {
                        segments.Add(new TextSegment(text.Length, runText.Length, current));
                        text.Append(runText);
                    }
                }

                current = current.GetNextContextPosition(LogicalDirection.Forward);
            }

            return new DocumentTextIndex(text.ToString(), segments);
        }

        private static void AddResult(DocumentTextIndex index, int startIndex, int length, List<LogTextMatch> results)
        {
            if (length <= 0)
            {
                return;
            }

            TextPointer? start = index.GetPointerAtTextOffset(startIndex, isEndOffset: false);
            TextPointer? end = index.GetPointerAtTextOffset(startIndex + length, isEndOffset: true);
            if (start != null && end != null && start.CompareTo(end) < 0)
            {
                results.Add(new LogTextMatch(start, end));
            }
        }

        private static bool IsWholeWordMatch(string text, int index, int length)
        {
            bool beforeOk = index == 0 || !IsWordCharacter(text[index - 1]);
            int afterIndex = index + length;
            bool afterOk = afterIndex >= text.Length || !IsWordCharacter(text[afterIndex]);
            return beforeOk && afterOk;
        }

        private static bool IsWordCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        public sealed class DocumentTextIndex
        {
            internal DocumentTextIndex(string text, List<TextSegment> segments)
            {
                Text = text;
                Segments = segments;
            }

            public string Text { get; }
            private List<TextSegment> Segments { get; }

            public TextPointer? GetPointerAtTextOffset(int offset, bool isEndOffset)
            {
                if (Segments.Count == 0)
                {
                    return null;
                }

                int lookupOffset = isEndOffset ? Math.Max(0, offset - 1) : offset;
                TextSegment? segment = FindSegment(lookupOffset);
                if (segment == null)
                {
                    return null;
                }

                int segmentOffset = offset - segment.Start;
                segmentOffset = Math.Clamp(segmentOffset, 0, segment.Length);
                return segment.Pointer.GetPositionAtOffset(segmentOffset, LogicalDirection.Forward);
            }

            private TextSegment? FindSegment(int offset)
            {
                int low = 0;
                int high = Segments.Count - 1;
                while (low <= high)
                {
                    int mid = low + ((high - low) / 2);
                    TextSegment segment = Segments[mid];
                    if (offset < segment.Start)
                    {
                        high = mid - 1;
                    }
                    else if (offset >= segment.End)
                    {
                        low = mid + 1;
                    }
                    else
                    {
                        return segment;
                    }
                }

                return null;
            }
        }
    }
}
