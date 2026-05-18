using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media;

namespace OceanyaClient.Components
{
    public readonly record struct LogTextMatch(TextPointer Start, TextPointer End);

    public interface ILogFindTarget
    {
        string FindScopeName { get; }

        IReadOnlyList<LogTextMatch> FindInCurrentDocument(
            string searchText,
            bool matchCase,
            bool wholeWord,
            bool useRegex);

        LogDocumentSearch.DocumentTextIndex CreateFindIndex();

        IReadOnlyList<LogTextMatch> ResolveFindMatches(
            LogDocumentSearch.DocumentTextIndex index,
            IReadOnlyList<LogTextOffsetMatch> matches);

        void HighlightMatches(IReadOnlyList<LogTextMatch> matches, int activeMatchIndex);

        Task HighlightMatchesAsync(
            IReadOnlyList<LogTextMatch> matches,
            int activeMatchIndex,
            CancellationToken cancellationToken);

        void ClearHighlight();
    }

    internal static class LogFindHighlightBrushes
    {
        public static readonly Brush Match = CreateFrozenBrush(Color.FromRgb(84, 74, 36));
        public static readonly Brush ActiveMatch = CreateFrozenBrush(Color.FromRgb(255, 216, 64));

        private static Brush CreateFrozenBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
