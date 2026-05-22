using System;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace OceanyaClient.Features.Updates
{
    public static class ReleaseNotesMarkdownRenderer
    {
        private static readonly Regex HeadingPattern = new Regex(@"^(#{1,6})\s+(.+)$", RegexOptions.CultureInvariant);
        private static readonly Regex BulletPattern = new Regex(@"^\s*[-*+]\s+(.+)$", RegexOptions.CultureInvariant);
        private static readonly Regex NumberPattern = new Regex(@"^\s*\d+[.)]\s+(.+)$", RegexOptions.CultureInvariant);
        private static readonly Regex LinkPattern = new Regex(@"\[([^\]]{1,200})\]\((https?://[^)\s]+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HtmlTagPattern = new Regex("<[^>]+>", RegexOptions.CultureInvariant);

        public static FlowDocument BuildDocument(string? markdown)
        {
            FlowDocument document = new FlowDocument
            {
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 232)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                PagePadding = new Thickness(0)
            };

            string input = Normalize(markdown);
            if (string.IsNullOrWhiteSpace(input))
            {
                document.Blocks.Add(CreateParagraph("No release notes were provided."));
                return document;
            }

            bool inCodeBlock = false;
            Paragraph? codeParagraph = null;
            int renderedLines = 0;
            foreach (string rawLine in input.Split('\n'))
            {
                if (renderedLines++ > 260)
                {
                    document.Blocks.Add(CreateParagraph("..."));
                    break;
                }

                string line = rawLine.TrimEnd();
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeParagraph = CreateCodeParagraph();
                        document.Blocks.Add(codeParagraph);
                    }
                    else
                    {
                        inCodeBlock = false;
                        codeParagraph = null;
                    }

                    continue;
                }

                if (inCodeBlock)
                {
                    codeParagraph?.Inlines.Add(new Run(line + Environment.NewLine));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 3, 0, 3) });
                    continue;
                }

                Match heading = HeadingPattern.Match(line);
                if (heading.Success)
                {
                    Paragraph paragraph = CreateParagraph(string.Empty);
                    paragraph.FontWeight = FontWeights.SemiBold;
                    paragraph.FontSize = heading.Groups[1].Value.Length <= 2 ? 18 : 15;
                    paragraph.Margin = new Thickness(0, 8, 0, 5);
                    AddInlineMarkdown(paragraph, heading.Groups[2].Value);
                    document.Blocks.Add(paragraph);
                    continue;
                }

                Match bullet = BulletPattern.Match(line);
                if (bullet.Success)
                {
                    document.Blocks.Add(CreateListItemParagraph("• ", bullet.Groups[1].Value));
                    continue;
                }

                Match numbered = NumberPattern.Match(line);
                if (numbered.Success)
                {
                    string marker = line.TrimStart().Split(' ', 2)[0];
                    document.Blocks.Add(CreateListItemParagraph(marker + " ", numbered.Groups[1].Value));
                    continue;
                }

                Paragraph normal = CreateParagraph(string.Empty);
                AddInlineMarkdown(normal, line);
                document.Blocks.Add(normal);
            }

            return document;
        }

        private static string Normalize(string? markdown)
        {
            string input = markdown ?? string.Empty;
            input = input.Replace("\r\n", "\n").Replace('\r', '\n');
            input = WebUtility.HtmlDecode(input);
            input = HtmlTagPattern.Replace(input, string.Empty);
            return input;
        }

        private static Paragraph CreateParagraph(string text)
        {
            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 5),
                LineHeight = 18
            };
            if (!string.IsNullOrEmpty(text))
            {
                paragraph.Inlines.Add(new Run(text));
            }

            return paragraph;
        }

        private static Paragraph CreateCodeParagraph()
        {
            return new Paragraph
            {
                Margin = new Thickness(0, 6, 0, 8),
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };
        }

        private static Paragraph CreateListItemParagraph(string marker, string text)
        {
            Paragraph paragraph = CreateParagraph(string.Empty);
            paragraph.Margin = new Thickness(12, 2, 0, 5);
            paragraph.Inlines.Add(new Run(marker) { FontWeight = FontWeights.SemiBold });
            AddInlineMarkdown(paragraph, text);
            return paragraph;
        }

        private static void AddInlineMarkdown(Paragraph paragraph, string text)
        {
            int index = 0;
            foreach (Match linkMatch in LinkPattern.Matches(text))
            {
                AddStyledText(paragraph, text[index..linkMatch.Index]);
                string label = StripInlineMarkers(linkMatch.Groups[1].Value);
                string url = linkMatch.Groups[2].Value;
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                    && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
                {
                    Hyperlink link = new Hyperlink(new Run(label))
                    {
                        NavigateUri = uri,
                        Foreground = new SolidColorBrush(Color.FromRgb(116, 185, 255))
                    };
                    link.RequestNavigate += (_, _) => OpenExternalLink(uri);
                    paragraph.Inlines.Add(link);
                }
                else
                {
                    paragraph.Inlines.Add(new Run(label));
                }

                index = linkMatch.Index + linkMatch.Length;
            }

            AddStyledText(paragraph, text[index..]);
        }

        private static void AddStyledText(Paragraph paragraph, string text)
        {
            int index = 0;
            while (index < text.Length)
            {
                int codeStart = text.IndexOf('`', index);
                if (codeStart < 0)
                {
                    AddEmphasisText(paragraph, text[index..]);
                    return;
                }

                AddEmphasisText(paragraph, text[index..codeStart]);
                int codeEnd = text.IndexOf('`', codeStart + 1);
                if (codeEnd < 0)
                {
                    paragraph.Inlines.Add(new Run("`"));
                    index = codeStart + 1;
                    continue;
                }

                Run code = new Run(text[(codeStart + 1)..codeEnd])
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45))
                };
                paragraph.Inlines.Add(code);
                index = codeEnd + 1;
            }
        }

        private static void AddEmphasisText(Paragraph paragraph, string text)
        {
            int index = 0;
            while (index < text.Length)
            {
                int boldStart = text.IndexOf("**", index, StringComparison.Ordinal);
                int italicStart = text.IndexOf('*', index);
                if (boldStart >= 0 && (italicStart < 0 || boldStart <= italicStart))
                {
                    paragraph.Inlines.Add(new Run(text[index..boldStart]));
                    int boldEnd = text.IndexOf("**", boldStart + 2, StringComparison.Ordinal);
                    if (boldEnd < 0)
                    {
                        paragraph.Inlines.Add(new Run("**"));
                        index = boldStart + 2;
                        continue;
                    }

                    paragraph.Inlines.Add(new Run(text[(boldStart + 2)..boldEnd]) { FontWeight = FontWeights.SemiBold });
                    index = boldEnd + 2;
                    continue;
                }

                if (italicStart >= 0)
                {
                    paragraph.Inlines.Add(new Run(text[index..italicStart]));
                    int italicEnd = text.IndexOf('*', italicStart + 1);
                    if (italicEnd < 0)
                    {
                        paragraph.Inlines.Add(new Run("*"));
                        index = italicStart + 1;
                        continue;
                    }

                    paragraph.Inlines.Add(new Run(text[(italicStart + 1)..italicEnd]) { FontStyle = FontStyles.Italic });
                    index = italicEnd + 1;
                    continue;
                }

                paragraph.Inlines.Add(new Run(text[index..]));
                return;
            }
        }

        private static string StripInlineMarkers(string value)
        {
            return value.Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("*", string.Empty, StringComparison.Ordinal)
                .Replace("`", string.Empty, StringComparison.Ordinal);
        }

        private static void OpenExternalLink(Uri uri)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });
        }
    }
}
