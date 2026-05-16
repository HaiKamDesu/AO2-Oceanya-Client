using System.Collections.Generic;
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

        void HighlightMatches(IReadOnlyList<LogTextMatch> matches, int activeMatchIndex);

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
