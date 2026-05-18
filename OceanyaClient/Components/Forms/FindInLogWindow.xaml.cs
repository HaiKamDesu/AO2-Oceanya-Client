using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OceanyaClient.Components;

namespace OceanyaClient.Components.Forms
{
    /// <summary>
    /// Modeless find-in-log dialog. Searches the visible IC and OOC log documents
    /// and highlights all matches, with a distinct active match.
    /// </summary>
    public partial class FindInLogWindow : OceanyaWindowContentControl
    {
        private sealed class SearchMatch
        {
            public SearchMatch(ILogFindTarget target, LogTextMatch match)
            {
                Target = target;
                Match = match;
            }

            public ILogFindTarget Target { get; }
            public LogTextMatch Match { get; }
        }

        private readonly IReadOnlyList<ILogFindTarget> targets;
        private IReadOnlyList<SearchMatch> currentMatches = Array.Empty<SearchMatch>();
        private int currentMatchIndex = -1;
        private readonly DispatcherTimer searchRefreshTimer;
        private CancellationTokenSource? searchCancellation;
        private int searchGeneration;

        public override string HeaderText => "Find in Log";

        public FindInLogWindow(IReadOnlyList<ILogFindTarget> targets, ILogFindTarget? initialTarget = null)
        {
            IReadOnlyList<ILogFindTarget> cleanTargets = (targets == null || targets.Count == 0)
                ? throw new ArgumentException("At least one log target is required.", nameof(targets))
                : targets;
            this.targets = initialTarget == null
                ? cleanTargets
                : cleanTargets
                    .OrderByDescending(target => ReferenceEquals(target, initialTarget))
                    .ToList();
            InitializeComponent();
            searchRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(90)
            };
            searchRefreshTimer.Tick += (_, _) =>
            {
                searchRefreshTimer.Stop();
                _ = RunSearchAsync();
            };

            ChkSearchIC.IsChecked = true;
            ChkSearchOOC.IsChecked = true;

            TxtSearch.TextChanged += (_, _) => ScheduleSearchRefresh();
            Closed += (_, _) =>
            {
                searchRefreshTimer.Stop();
                CancelActiveSearch();
                ClearAllHighlights();
            };
            Loaded += (_, _) => TxtSearch.Focus();
        }

        private void ScheduleSearchRefresh()
        {
            CancelActiveSearch();
            currentMatches = Array.Empty<SearchMatch>();
            currentMatchIndex = -1;
            ClearAllHighlights();
            UpdateUI();

            searchRefreshTimer.Stop();
            searchRefreshTimer.Start();
        }

        private async Task RunSearchAsync()
        {
            CancelActiveSearch();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            searchCancellation = cancellation;
            int generation = ++searchGeneration;
            CancellationToken cancellationToken = cancellation.Token;

            string searchText = TxtSearch.Text ?? string.Empty;
            bool matchCase = ChkMatchCase.IsChecked == true;
            bool wholeWord = ChkWholeWord.IsChecked == true;
            bool useRegex = ChkRegex.IsChecked == true;

            ClearAllHighlights();

            currentMatches = Array.Empty<SearchMatch>();
            currentMatchIndex = -1;

            try
            {
                if (!string.IsNullOrEmpty(searchText))
                {
                    IReadOnlyList<ILogFindTarget> activeTargets = GetActiveTargets();
                    List<(ILogFindTarget Target, LogDocumentSearch.DocumentTextIndex Index)> indexes = new();
                    foreach (ILogFindTarget target in activeTargets)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        indexes.Add((target, target.CreateFindIndex()));
                    }

                    var targetMatches = await Task.Run(() =>
                    {
                        List<(ILogFindTarget Target, LogDocumentSearch.DocumentTextIndex Index, IReadOnlyList<LogTextOffsetMatch> Matches)> results = new();
                        foreach ((ILogFindTarget target, LogDocumentSearch.DocumentTextIndex index) in indexes)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            IReadOnlyList<LogTextOffsetMatch> matches;
                            try
                            {
                                matches = LogDocumentSearch.FindOffsets(
                                    index,
                                    searchText,
                                    matchCase,
                                    wholeWord,
                                    useRegex,
                                    cancellationToken);
                            }
                            catch
                            {
                                matches = Array.Empty<LogTextOffsetMatch>();
                            }

                            results.Add((target, index, matches));
                        }

                        return results;
                    }, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();
                    if (generation != searchGeneration)
                    {
                        return;
                    }

                    List<SearchMatch> matchesForUi = new List<SearchMatch>();
                    foreach ((ILogFindTarget target, LogDocumentSearch.DocumentTextIndex index, IReadOnlyList<LogTextOffsetMatch> offsetMatches) in targetMatches)
                    {
                        IReadOnlyList<LogTextMatch> resolvedMatches;
                        try
                        {
                            resolvedMatches = target.ResolveFindMatches(index, offsetMatches);
                        }
                        catch
                        {
                            resolvedMatches = Array.Empty<LogTextMatch>();
                        }

                        matchesForUi.AddRange(resolvedMatches.Select(match => new SearchMatch(target, match)));
                    }

                    currentMatches = matchesForUi;
                    if (currentMatches.Count > 0)
                    {
                        currentMatchIndex = 0;
                    }
                }

                await ApplyHighlightsAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (generation == searchGeneration)
                {
                    UpdateUI();
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(searchCancellation, cancellation))
                {
                    searchCancellation = null;
                }

                cancellation.Dispose();
            }
        }

        private void CancelActiveSearch()
        {
            searchCancellation?.Cancel();
        }

        private void NavigateTo(int index)
        {
            searchRefreshTimer.Stop();
            if (!string.IsNullOrEmpty(TxtSearch.Text) && currentMatches.Count == 0)
            {
                _ = RunSearchAsync();
                return;
            }

            if (currentMatches.Count == 0)
            {
                return;
            }

            currentMatchIndex = ((index % currentMatches.Count) + currentMatches.Count) % currentMatches.Count;
            ApplyHighlights();
            UpdateUI();
        }

        private void ClearAllHighlights()
        {
            foreach (ILogFindTarget target in targets)
            {
                target.ClearHighlight();
            }
        }

        private void ApplyHighlights()
        {
            IReadOnlyList<ILogFindTarget> activeTargets = GetActiveTargets();
            foreach (ILogFindTarget target in targets)
            {
                List<LogTextMatch> targetMatches = currentMatches
                    .Where(match => ReferenceEquals(match.Target, target))
                    .Select(match => match.Match)
                    .ToList();
                int activeTargetIndex = -1;
                if (currentMatchIndex >= 0 && currentMatchIndex < currentMatches.Count)
                {
                    SearchMatch activeMatch = currentMatches[currentMatchIndex];
                    if (ReferenceEquals(activeMatch.Target, target))
                    {
                        activeTargetIndex = targetMatches.FindIndex(match =>
                            match.Start.CompareTo(activeMatch.Match.Start) == 0
                            && match.End.CompareTo(activeMatch.Match.End) == 0);
                    }
                }

                if (activeTargets.Contains(target))
                {
                    target.HighlightMatches(targetMatches, activeTargetIndex);
                }
                else
                {
                    target.ClearHighlight();
                }
            }
        }

        private async Task ApplyHighlightsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ILogFindTarget> activeTargets = GetActiveTargets();
            foreach (ILogFindTarget target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<LogTextMatch> targetMatches = currentMatches
                    .Where(match => ReferenceEquals(match.Target, target))
                    .Select(match => match.Match)
                    .ToList();
                int activeTargetIndex = -1;
                if (currentMatchIndex >= 0 && currentMatchIndex < currentMatches.Count)
                {
                    SearchMatch activeMatch = currentMatches[currentMatchIndex];
                    if (ReferenceEquals(activeMatch.Target, target))
                    {
                        activeTargetIndex = targetMatches.FindIndex(match =>
                            match.Start.CompareTo(activeMatch.Match.Start) == 0
                            && match.End.CompareTo(activeMatch.Match.End) == 0);
                    }
                }

                if (activeTargets.Contains(target))
                {
                    await target.HighlightMatchesAsync(targetMatches, activeTargetIndex, cancellationToken);
                }
                else
                {
                    target.ClearHighlight();
                }
            }
        }

        private IReadOnlyList<ILogFindTarget> GetActiveTargets()
        {
            bool includeIc = ChkSearchIC.IsChecked == true;
            bool includeOoc = ChkSearchOOC.IsChecked == true;
            return targets
                .Where(target =>
                    (includeIc && string.Equals(target.FindScopeName, "IC", StringComparison.OrdinalIgnoreCase))
                    || (includeOoc && string.Equals(target.FindScopeName, "OOC", StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private void UpdateUI()
        {
            bool hasResults = currentMatches.Count > 0;
            BtnPrev.IsEnabled = hasResults;
            BtnNext.IsEnabled = hasResults;
            TxtResultCount.Text = hasResults
                ? $"{currentMatchIndex + 1} of {currentMatches.Count} ({currentMatches[currentMatchIndex].Target.FindScopeName})"
                : string.IsNullOrEmpty(TxtSearch.Text)
                    ? string.Empty
                    : GetActiveTargets().Count == 0 ? "No log selected" : "No results";
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(currentMatchIndex - 1);
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(currentMatchIndex + 1);
        }

        private void SearchOptions_Changed(object sender, RoutedEventArgs e)
        {
            ScheduleSearchRefresh();
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    NavigateTo(currentMatchIndex - 1);
                else
                    NavigateTo(currentMatchIndex + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HostWindow?.Close();
            }
        }

        private void FindWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F3)
            {
                NavigateTo(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? currentMatchIndex - 1
                    : currentMatchIndex + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.G && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                NavigateTo(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? currentMatchIndex - 1
                    : currentMatchIndex + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HostWindow?.Close();
                e.Handled = true;
            }
        }
    }
}
