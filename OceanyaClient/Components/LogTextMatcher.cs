using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace OceanyaClient.Components
{
    internal sealed class LogTextMatcher
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        private readonly string searchText;
        private readonly bool matchCase;
        private readonly bool wholeWord;
        private readonly Regex? regex;
        private readonly bool invalidRegex;

        private LogTextMatcher(string searchText, bool matchCase, bool wholeWord, Regex? regex, bool invalidRegex)
        {
            this.searchText = searchText;
            this.matchCase = matchCase;
            this.wholeWord = wholeWord;
            this.regex = regex;
            this.invalidRegex = invalidRegex;
        }

        public static LogTextMatcher Create(string searchText, bool matchCase, bool wholeWord, bool useRegex)
        {
            if (!useRegex)
            {
                return new LogTextMatcher(searchText, matchCase, wholeWord, null, invalidRegex: false);
            }

            RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (!matchCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            try
            {
                return new LogTextMatcher(
                    searchText,
                    matchCase,
                    wholeWord,
                    new Regex(searchText, options, RegexTimeout),
                    invalidRegex: false);
            }
            catch
            {
                return new LogTextMatcher(searchText, matchCase, wholeWord, null, invalidRegex: true);
            }
        }

        public IReadOnlyList<LogTextOffsetMatch> FindOffsets(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(searchText) || string.IsNullOrEmpty(text) || invalidRegex)
            {
                return Array.Empty<LogTextOffsetMatch>();
            }

            return regex == null
                ? FindPlainTextOffsets(text, cancellationToken)
                : FindRegexOffsets(text, cancellationToken);
        }

        private IReadOnlyList<LogTextOffsetMatch> FindPlainTextOffsets(string text, CancellationToken cancellationToken)
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

        private IReadOnlyList<LogTextOffsetMatch> FindRegexOffsets(string text, CancellationToken cancellationToken)
        {
            List<LogTextOffsetMatch> results = new List<LogTextOffsetMatch>();

            try
            {
                foreach (Match match in regex!.Matches(text))
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
                return Array.Empty<LogTextOffsetMatch>();
            }

            return results;
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
    }
}
