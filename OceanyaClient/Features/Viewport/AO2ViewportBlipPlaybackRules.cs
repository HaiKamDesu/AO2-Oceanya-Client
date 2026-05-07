using System;
using System.Globalization;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Shared AO2 text-crawl blip timing rules used by the viewport and settings previews.
    /// </summary>
    internal static class AO2ViewportBlipPlaybackRules
    {
        internal const string PreviewSentence = "This is a test message to scroll through blips, hello!";

        private static readonly double[] ChatDisplayMultipliers = { 0, 0.25, 0.65, 1, 1.25, 1.75, 2.25 };
        private const int DefaultChatDisplaySpeed = 3;
        private const int ChatPauseMilliseconds = 100;

        internal static BlipCrawlState CreateState(
            string text,
            int textCrawlMilliseconds,
            int blipRate,
            bool blankBlipEnabled,
            string[]? markupStart = null,
            string[]? markupEnd = null,
            bool[]? markupRemove = null)
        {
            return new BlipCrawlState(
                text ?? string.Empty,
                Math.Max(0, textCrawlMilliseconds),
                blipRate,
                blankBlipEnabled,
                markupStart ?? Array.Empty<string>(),
                markupEnd ?? Array.Empty<string>(),
                markupRemove ?? Array.Empty<bool>());
        }

        internal static int GetNextDisplayedTextElement(
            BlipCrawlState state,
            out string textElement,
            out bool shouldPlayBlip,
            out bool triggerScreenShake,
            out bool triggerFlash,
            out int blipGateDelay)
        {
            shouldPlayBlip = false;
            triggerScreenShake = false;
            triggerFlash = false;
            textElement = GetTextElement(state.Text, state.Position);
            state.Position += textElement.Length;
            blipGateDelay = 0;

            if (textElement == "{")
            {
                state.DisplaySpeed = Math.Min(ChatDisplayMultipliers.Length - 1, state.DisplaySpeed + 1);
                textElement = string.Empty;
                return 0;
            }

            if (textElement == "}")
            {
                state.DisplaySpeed = Math.Max(0, state.DisplaySpeed - 1);
                textElement = string.Empty;
                return 0;
            }

            if (IsRemovedAo2ColorMarkup(state, textElement))
            {
                textElement = string.Empty;
                return 0;
            }

            if (textElement == "\\" && state.Position < state.Text.Length)
            {
                string escapedElement = GetTextElement(state.Text, state.Position);
                state.Position += escapedElement.Length;

                switch (escapedElement)
                {
                    case "n":
                        textElement = Environment.NewLine;
                        return 0;
                    case "p":
                        textElement = string.Empty;
                        return ChatPauseMilliseconds;
                    case "s":
                        textElement = string.Empty;
                        triggerScreenShake = true;
                        return 0;
                    case "f":
                        textElement = string.Empty;
                        triggerFlash = true;
                        return 0;
                    default:
                        textElement = escapedElement;
                        shouldPlayBlip = true;
                        blipGateDelay = GetChatTextDelay(state, textElement, includePunctuationDelay: false);
                        return GetChatTextDelay(state, textElement, includePunctuationDelay: true);
                }
            }

            shouldPlayBlip = true;
            blipGateDelay = GetChatTextDelay(state, textElement, includePunctuationDelay: false);
            return GetChatTextDelay(state, textElement, includePunctuationDelay: true);
        }

        internal static bool ShouldPlayBlipForTextElement(BlipCrawlState state, string textElement, int delay)
        {
            string firstElement = GetTextElement(textElement, 0);
            bool isWhitespace = string.IsNullOrWhiteSpace(firstElement) || char.IsWhiteSpace(firstElement[0]);
            int effectiveBlipRate = state.BlipRate;
            if (delay > 0 && delay <= 25)
            {
                effectiveBlipRate = Math.Max(
                    effectiveBlipRate,
                    (int)Math.Round(state.TextCrawlMilliseconds / (double)delay, MidpointRounding.AwayFromZero));
            }

            bool shouldPlay = (state.BlipRate <= 0 && state.BlipTicker < 1)
                || (effectiveBlipRate > 0 && state.BlipTicker % effectiveBlipRate == 0);
            if (shouldPlay)
            {
                if (!isWhitespace || state.BlankBlipEnabled)
                {
                    state.BlipTicker++;
                    return true;
                }
            }
            else
            {
                state.BlipTicker++;
            }

            return false;
        }

        internal static string GetTextElement(string text, int index)
        {
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text, index);
            return enumerator.MoveNext()
                ? enumerator.GetTextElement()
                : text.Substring(index, 1);
        }

        private static bool IsRemovedAo2ColorMarkup(BlipCrawlState state, string textElement)
        {
            for (int i = 0; i < state.MarkupStart.Length && i < state.MarkupRemove.Length; i++)
            {
                if (!state.MarkupRemove[i])
                {
                    continue;
                }

                string start = state.MarkupStart[i] ?? string.Empty;
                string end = i < state.MarkupEnd.Length ? state.MarkupEnd[i] ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(start))
                {
                    continue;
                }

                if (string.Equals(textElement, start, StringComparison.Ordinal)
                    || (!string.IsNullOrEmpty(end) && string.Equals(textElement, end, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetChatTextDelay(BlipCrawlState state, string textElement, bool includePunctuationDelay)
        {
            int delay = (int)(state.TextCrawlMilliseconds * ChatDisplayMultipliers[state.DisplaySpeed]);
            if (!includePunctuationDelay || string.IsNullOrEmpty(textElement))
            {
                return delay;
            }

            if (state.DisplaySpeed > 1 && ".,?!:;".Contains(textElement, StringComparison.Ordinal))
            {
                int maxDelay = (int)(state.TextCrawlMilliseconds * ChatDisplayMultipliers[6] * 1.5);
                delay = Math.Min(maxDelay, delay * 3);
            }

            return delay;
        }

        internal sealed class BlipCrawlState
        {
            internal BlipCrawlState(
                string text,
                int textCrawlMilliseconds,
                int blipRate,
                bool blankBlipEnabled,
                string[] markupStart,
                string[] markupEnd,
                bool[] markupRemove)
            {
                Text = text;
                TextCrawlMilliseconds = textCrawlMilliseconds;
                BlipRate = blipRate;
                BlankBlipEnabled = blankBlipEnabled;
                MarkupStart = markupStart;
                MarkupEnd = markupEnd;
                MarkupRemove = markupRemove;
            }

            internal string Text { get; }

            internal int TextCrawlMilliseconds { get; }

            internal int BlipRate { get; }

            internal bool BlankBlipEnabled { get; }

            internal string[] MarkupStart { get; }

            internal string[] MarkupEnd { get; }

            internal bool[] MarkupRemove { get; }

            internal int Position { get; set; }

            internal int DisplaySpeed { get; set; } = DefaultChatDisplaySpeed;

            internal int BlipTicker { get; set; }
        }
    }
}
