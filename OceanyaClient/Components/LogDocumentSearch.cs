using System;
using System.Collections.Generic;
using System.Threading;
using System.Text;
using System.Windows.Documents;

namespace OceanyaClient.Components
{
    public readonly record struct LogTextOffsetMatch(int StartIndex, int Length);

    public static class LogDocumentSearch
    {
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

            IReadOnlyList<LogTextOffsetMatch> offsetMatches = FindOffsets(index, searchText, matchCase, wholeWord, useRegex);
            return ResolveMatches(index, offsetMatches);
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

            return FindOffsetsInText(index.Text, searchText, matchCase, wholeWord, useRegex, cancellationToken);
        }

        public static IReadOnlyList<LogTextOffsetMatch> FindOffsetsInText(
            string text,
            string searchText,
            bool matchCase,
            bool wholeWord,
            bool useRegex,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchText))
            {
                return Array.Empty<LogTextOffsetMatch>();
            }

            return LogTextMatcher
                .Create(searchText, matchCase, wholeWord, useRegex)
                .FindOffsets(text, cancellationToken);
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
