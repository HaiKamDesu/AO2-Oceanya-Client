using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OceanyaClient.Features.Updates
{
    public static class ReleaseNotesSanitizer
    {
        private static readonly Regex HtmlTagPattern = new Regex("<[^>]+>", RegexOptions.CultureInvariant);
        private static readonly Regex MarkdownLinkPattern = new Regex(@"\[([^\]]{1,200})\]\((https://[^)\s]+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string ToSafePlainText(string? markdown)
        {
            string input = markdown ?? string.Empty;
            input = input.Replace("\r\n", "\n").Replace('\r', '\n');
            input = WebUtility.HtmlDecode(input);
            input = HtmlTagPattern.Replace(input, string.Empty);
            input = MarkdownLinkPattern.Replace(input, match => $"{match.Groups[1].Value} ({match.Groups[2].Value})");

            List<string> lines = new List<string>();
            foreach (string rawLine in input.Split('\n'))
            {
                string line = rawLine.TrimEnd();
                line = StripCommonMarkdown(line);
                if (line.Length > 1000)
                {
                    line = line[..1000] + "...";
                }

                lines.Add(line);
                if (lines.Count >= 220)
                {
                    lines.Add("...");
                    break;
                }
            }

            string result = string.Join(Environment.NewLine, lines).Trim();
            return string.IsNullOrWhiteSpace(result) ? "No release notes were provided." : result;
        }

        private static string StripCommonMarkdown(string line)
        {
            string trimmed = line.TrimStart();
            while (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..].TrimStart();
            }

            trimmed = trimmed.Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("__", string.Empty, StringComparison.Ordinal)
                .Replace("`", string.Empty, StringComparison.Ordinal);

            StringBuilder builder = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                if (!char.IsControl(c) || c == '\t')
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
