using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OceanyaClient.Features.ChatPreview
{
    /// <summary>
    /// Applies AO2 IC chat text preprocessing and color markup rules.
    /// </summary>
    public static class AO2ChatTextFormatter
    {
        /// <summary>
        /// Resolves AO2 leading alignment markers and removes the consumed marker from <paramref name="text"/>.
        /// </summary>
        public static TextAlignment ResolveMessageAlignment(string rawText, out string text)
        {
            text = rawText ?? string.Empty;
            string trimmed = text.Trim();
            string? marker = trimmed.StartsWith("~~", StringComparison.Ordinal)
                ? "~~"
                : trimmed.StartsWith("~>", StringComparison.Ordinal)
                    ? "~>"
                    : trimmed.StartsWith("<>", StringComparison.Ordinal)
                        ? "<>"
                        : null;
            if (marker == null)
            {
                return TextAlignment.Left;
            }

            int markerIndex = text.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                text = text.Remove(markerIndex, marker.Length);
            }

            return marker switch
            {
                "~~" => TextAlignment.Center,
                "~>" => TextAlignment.Right,
                "<>" => TextAlignment.Justify,
                _ => TextAlignment.Left
            };
        }

        /// <summary>
        /// Enumerates AO2-formatted text segments after escape, speed-control, and chat color markup handling.
        /// </summary>
        public static IEnumerable<AO2FormattedTextSegment> EnumerateFormattedTextSegments(
            AO2ChatPreviewStyle style,
            string text,
            int defaultColorIndex,
            Color? defaultColorOverride = null)
        {
            int safeDefaultColorIndex = Math.Clamp(defaultColorIndex, 0, style.ChatColors.Length - 1);
            Stack<int> colorStack = new Stack<int>();
            colorStack.Push(safeDefaultColorIndex);
            bool parseEscape = false;
            for (int index = 0; index < text.Length;)
            {
                string textElement = GetTextElement(text, index);
                index += textElement.Length;

                if (parseEscape)
                {
                    parseEscape = false;
                    if (textElement == "n")
                    {
                        yield return new AO2FormattedTextSegment(
                            Environment.NewLine,
                            GetMessageColor(style, colorStack.Peek(), safeDefaultColorIndex, defaultColorOverride));
                    }
                    else if (textElement != "s" && textElement != "f" && textElement != "p")
                    {
                        yield return new AO2FormattedTextSegment(
                            textElement,
                            GetMessageColor(style, colorStack.Peek(), safeDefaultColorIndex, defaultColorOverride));
                    }

                    continue;
                }

                if (textElement == "\\")
                {
                    parseEscape = true;
                    continue;
                }

                if (textElement == "{" || textElement == "}")
                {
                    continue;
                }

                if (TryApplyAo2ColorMarkup(
                    style,
                    textElement,
                    safeDefaultColorIndex,
                    defaultColorOverride,
                    colorStack,
                    out bool skip,
                    out Color markerColor))
                {
                    if (!skip)
                    {
                        yield return new AO2FormattedTextSegment(textElement, markerColor);
                    }

                    continue;
                }

                yield return new AO2FormattedTextSegment(
                    textElement,
                    GetMessageColor(style, colorStack.Peek(), safeDefaultColorIndex, defaultColorOverride));
            }
        }

        /// <summary>
        /// Gets the AO2 chat color, optionally overriding the packet/default color slot.
        /// </summary>
        public static Color GetMessageColor(
            AO2ChatPreviewStyle style,
            int colorIndex,
            int defaultColorIndex,
            Color? defaultColorOverride = null)
        {
            if (colorIndex == defaultColorIndex && defaultColorOverride.HasValue)
            {
                return defaultColorOverride.Value;
            }

            return colorIndex >= 0 && colorIndex < style.ChatColors.Length
                ? style.ChatColors[colorIndex]
                : style.MessageColor;
        }

        private static bool TryApplyAo2ColorMarkup(
            AO2ChatPreviewStyle style,
            string textElement,
            int defaultColorIndex,
            Color? defaultColorOverride,
            Stack<int> colorStack,
            out bool skip,
            out Color markerColor)
        {
            skip = false;
            markerColor = GetMessageColor(
                style,
                colorStack.Count > 0 ? colorStack.Peek() : defaultColorIndex,
                defaultColorIndex,
                defaultColorOverride);
            for (int i = 0; i < style.ChatColors.Length; i++)
            {
                string start = style.ChatMarkupStart[i] ?? string.Empty;
                string end = style.ChatMarkupEnd[i] ?? string.Empty;
                if (string.IsNullOrEmpty(start))
                {
                    continue;
                }

                bool isToggle = string.IsNullOrEmpty(end) || string.Equals(end, start, StringComparison.Ordinal);
                if (isToggle && string.Equals(textElement, start, StringComparison.Ordinal))
                {
                    if (colorStack.Count > 0 && colorStack.Peek() == i && defaultColorIndex != i)
                    {
                        markerColor = GetMessageColor(style, colorStack.Peek(), defaultColorIndex, defaultColorOverride);
                        colorStack.Pop();
                    }
                    else
                    {
                        colorStack.Push(i);
                        markerColor = GetMessageColor(style, colorStack.Peek(), defaultColorIndex, defaultColorOverride);
                    }

                    skip = style.ChatMarkupRemove[i];
                    return true;
                }

                if (string.Equals(textElement, start, StringComparison.Ordinal))
                {
                    colorStack.Push(i);
                    markerColor = GetMessageColor(style, colorStack.Peek(), defaultColorIndex, defaultColorOverride);
                    skip = style.ChatMarkupRemove[i];
                    return true;
                }

                if (colorStack.Count > 0
                    && colorStack.Peek() == i
                    && string.Equals(textElement, end, StringComparison.Ordinal))
                {
                    markerColor = GetMessageColor(style, colorStack.Peek(), defaultColorIndex, defaultColorOverride);
                    colorStack.Pop();
                    skip = style.ChatMarkupRemove[i];
                    return true;
                }
            }

            return false;
        }

        private static string GetTextElement(string text, int index)
        {
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text, index);
            return enumerator.MoveNext()
                ? enumerator.GetTextElement()
                : text.Substring(index, 1);
        }
    }

    /// <summary>
    /// A contiguous piece of AO2-formatted chat text with one foreground color.
    /// </summary>
    public sealed class AO2FormattedTextSegment
    {
        /// <summary>
        /// Creates a formatted text segment.
        /// </summary>
        public AO2FormattedTextSegment(string text, Color color)
        {
            Text = text;
            Color = color;
        }

        /// <summary>
        /// Gets the visible text for this segment.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the foreground color for this segment.
        /// </summary>
        public Color Color { get; }
    }
}
