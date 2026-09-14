using System.Collections.Generic;
using System.Windows.Documents;
using System.Windows.Media;

namespace OceanyaClient.Components
{
    /// <summary>
    /// Repaints the runs of an already-written log when its theme colours change.
    /// </summary>
    /// <remarks>
    /// Each run carries a <see cref="LogRunRole"/> in its Tag, set when the message was written, so this
    /// does not have to guess what a run was for. Runs with no role - the IC message colours, which are
    /// protocol rather than theme - are left alone.
    /// </remarks>
    public static class LogRunRecolorer
    {
        /// <summary>
        /// Repaints every tagged run in a set of documents.
        /// </summary>
        /// <param name="documents">Documents to walk.</param>
        /// <param name="colors">Colours to apply, or null to restore the fallbacks.</param>
        /// <param name="fallbacks">Colour to use per role when the theme does not set one.</param>
        public static void Apply(
            IEnumerable<FlowDocument> documents,
            LogThemeColors? colors,
            IReadOnlyDictionary<LogRunRole, Brush> fallbacks)
        {
            foreach (FlowDocument document in documents)
            {
                foreach (Block block in document.Blocks)
                {
                    if (block is Paragraph paragraph)
                    {
                        ApplyToInlines(paragraph.Inlines, colors, fallbacks);
                    }
                }
            }
        }

        private static void ApplyToInlines(
            InlineCollection inlines,
            LogThemeColors? colors,
            IReadOnlyDictionary<LogRunRole, Brush> fallbacks)
        {
            foreach (Inline inline in inlines)
            {
                if (inline is Span span)
                {
                    ApplyToInlines(span.Inlines, colors, fallbacks);
                    continue;
                }

                if (inline.Tag is not LogRunRole role || role == LogRunRole.None)
                {
                    continue;
                }

                Brush? themed = role switch
                {
                    LogRunRole.Body => colors?.Text,
                    LogRunRole.SenderName => colors?.SenderName,
                    LogRunRole.ServerName => colors?.ServerName,
                    LogRunRole.SelfName => colors?.SelfName,
                    LogRunRole.Timestamp => colors?.Timestamp,
                    _ => null
                };

                if (themed != null)
                {
                    inline.Foreground = themed;
                }
                else if (fallbacks.TryGetValue(role, out Brush? fallback))
                {
                    inline.Foreground = fallback;
                }
            }
        }
    }
}
