using System;
using System.Collections.Generic;
using System.Linq;
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
                RunSearch();
            };

            TxtSearch.TextChanged += (_, _) => ScheduleSearchRefresh();
            Closed += (_, _) =>
            {
                searchRefreshTimer.Stop();
                ClearAllHighlights();
            };
            Loaded += (_, _) => TxtSearch.Focus();
        }

        private void ScheduleSearchRefresh()
        {
            currentMatches = Array.Empty<SearchMatch>();
            currentMatchIndex = -1;
            ClearAllHighlights();
            UpdateUI();

            searchRefreshTimer.Stop();
            searchRefreshTimer.Start();
        }

        private void RunSearch()
        {
            string searchText = TxtSearch.Text ?? string.Empty;
            bool matchCase = ChkMatchCase.IsChecked == true;
            bool wholeWord = ChkWholeWord.IsChecked == true;
            bool useRegex = ChkRegex.IsChecked == true;

            ClearAllHighlights();

            currentMatches = Array.Empty<SearchMatch>();
            currentMatchIndex = -1;

            if (!string.IsNullOrEmpty(searchText))
            {
                List<SearchMatch> matches = new List<SearchMatch>();
                foreach (ILogFindTarget target in targets)
                {
                    IReadOnlyList<LogTextMatch> targetMatches;
                    try
                    {
                        targetMatches = target.FindInCurrentDocument(searchText, matchCase, wholeWord, useRegex);
                    }
                    catch
                    {
                        targetMatches = Array.Empty<LogTextMatch>();
                    }

                    matches.AddRange(targetMatches.Select(match => new SearchMatch(target, match)));
                }

                currentMatches = matches;
                if (currentMatches.Count > 0)
                {
                    currentMatchIndex = 0;
                }
            }

            ApplyHighlights();
            UpdateUI();
        }

        private void NavigateTo(int index)
        {
            searchRefreshTimer.Stop();
            if (!string.IsNullOrEmpty(TxtSearch.Text) && currentMatches.Count == 0)
            {
                RunSearch();
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

                target.HighlightMatches(targetMatches, activeTargetIndex);
            }
        }

        private void UpdateUI()
        {
            bool hasResults = currentMatches.Count > 0;
            BtnPrev.IsEnabled = hasResults;
            BtnNext.IsEnabled = hasResults;
            TxtResultCount.Text = hasResults
                ? $"{currentMatchIndex + 1} of {currentMatches.Count} ({currentMatches[currentMatchIndex].Target.FindScopeName})"
                : string.IsNullOrEmpty(TxtSearch.Text) ? string.Empty : "No results";
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
