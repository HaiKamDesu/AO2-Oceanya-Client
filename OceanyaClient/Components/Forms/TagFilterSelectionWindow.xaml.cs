using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace OceanyaClient
{
    public partial class TagFilterSelectionWindow : Window, INotifyPropertyChanged
    {
        private readonly Dictionary<string, int> allTagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool suppressTagInputHandlers;
        private bool includeNavigationSelectionActive;
        private bool excludeNavigationSelectionActive;
        private string includeInputText = string.Empty;
        private string excludeInputText = string.Empty;
        private bool isIncludeSuggestionsOpen;
        private bool isExcludeSuggestionsOpen;
        private FolderVisualizerTableColumnKey? sortColumn;

        public ObservableCollection<FilterColumnChoice> ColumnChoices { get; } = new ObservableCollection<FilterColumnChoice>();
        public ObservableCollection<FilterColumnChoice> SortColumnChoices { get; } = new ObservableCollection<FilterColumnChoice>();
        public ObservableCollection<FolderFilterRule> FilterTreeRoots { get; } = new ObservableCollection<FolderFilterRule>();
        public ObservableCollection<TagTokenItem> IncludedTagTokens { get; } = new ObservableCollection<TagTokenItem>();
        public ObservableCollection<TagTokenItem> ExcludedTagTokens { get; } = new ObservableCollection<TagTokenItem>();
        public ObservableCollection<TagTokenItem> IncludeTagSuggestions { get; } = new ObservableCollection<TagTokenItem>();
        public ObservableCollection<TagTokenItem> ExcludeTagSuggestions { get; } = new ObservableCollection<TagTokenItem>();

        public IReadOnlyList<FolderFilterOperator> OperatorChoices { get; } = Enum
            .GetValues(typeof(FolderFilterOperator))
            .Cast<FolderFilterOperator>()
            .ToList();

        public IReadOnlyList<FolderFilterConnector> ConnectorChoices { get; } = Enum
            .GetValues(typeof(FolderFilterConnector))
            .Cast<FolderFilterConnector>()
            .ToList();

        public FolderVisualizerTableColumnKey? SortColumn
        {
            get => sortColumn;
            set
            {
                if (sortColumn == value)
                {
                    return;
                }

                sortColumn = value;
                OnPropertyChanged();
            }
        }

        public bool IsIncludeSuggestionsOpen
        {
            get => isIncludeSuggestionsOpen;
            set
            {
                if (isIncludeSuggestionsOpen == value)
                {
                    return;
                }

                isIncludeSuggestionsOpen = value;
                OnPropertyChanged();
            }
        }

        public bool IsExcludeSuggestionsOpen
        {
            get => isExcludeSuggestionsOpen;
            set
            {
                if (isExcludeSuggestionsOpen == value)
                {
                    return;
                }

                isExcludeSuggestionsOpen = value;
                OnPropertyChanged();
            }
        }

        public ListSortDirection SortDirection => SortDescRadio.IsChecked == true
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        public IReadOnlyList<string> IncludedTags => IncludedTagTokens
            .Select(token => token.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        public IReadOnlyList<string> ExcludedTags => ExcludedTagTokens
            .Select(token => token.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        public FolderFilterRule FilterRoot { get; private set; } = FolderFilterRule.CreateGroup(FolderFilterConnector.And);

        public event PropertyChangedEventHandler? PropertyChanged;

        public TagFilterSelectionWindow(
            IEnumerable<string> allTags,
            IEnumerable<string> includedTags,
            IEnumerable<string> excludedTags,
            FolderFilterRule filterRoot,
            FolderVisualizerTableColumnKey? currentSortColumn,
            ListSortDirection currentSortDirection,
            IReadOnlyDictionary<string, int>? tagCounts = null,
            VisualizerWindowState? savedWindowState = null)
        {
            InitializeComponent();
            DataContext = this;
            ApplySavedWindowState(savedWindowState);

            foreach (string tag in (allTags ?? Enumerable.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase))
            {
                int count = 0;
                if (tagCounts != null && tagCounts.TryGetValue(tag, out int resolvedCount))
                {
                    count = resolvedCount;
                }
                allTagCounts[tag] = Math.Max(0, count);
            }

            foreach (string tag in includedTags ?? Enumerable.Empty<string>())
            {
                string normalized = NormalizeTag(tag);
                if (!string.IsNullOrWhiteSpace(normalized) && !ContainsTag(IncludedTagTokens, normalized))
                {
                    IncludedTagTokens.Add(CreateTagToken(normalized));
                }
            }

            foreach (string tag in excludedTags ?? Enumerable.Empty<string>())
            {
                string normalized = NormalizeTag(tag);
                if (!string.IsNullOrWhiteSpace(normalized) && !ContainsTag(ExcludedTagTokens, normalized))
                {
                    ExcludedTagTokens.Add(CreateTagToken(normalized));
                }
            }

            foreach (FolderVisualizerTableColumnKey key in Enum.GetValues(typeof(FolderVisualizerTableColumnKey)).Cast<FolderVisualizerTableColumnKey>())
            {
                if (key == FolderVisualizerTableColumnKey.Icon)
                {
                    continue;
                }

                FilterColumnChoice choice = new FilterColumnChoice(key, GetColumnName(key));
                ColumnChoices.Add(choice);
                SortColumnChoices.Add(choice);
            }

            FolderFilterRule clonedRoot = filterRoot?.Clone() ?? FolderFilterRule.CreateGroup(FolderFilterConnector.And);
            if (!clonedRoot.IsGroup)
            {
                FolderFilterRule wrapped = FolderFilterRule.CreateGroup(FolderFilterConnector.And);
                clonedRoot.Parent = wrapped;
                wrapped.Children.Add(clonedRoot);
                clonedRoot = wrapped;
            }

            EnsureParents(clonedRoot, null);
            if (clonedRoot.Children.Count == 0)
            {
                FolderFilterRule seed = FolderFilterRule.CreateCondition();
                seed.Parent = clonedRoot;
                clonedRoot.Children.Add(seed);
            }

            FilterRoot = clonedRoot;
            FilterTreeRoots.Clear();
            FilterTreeRoots.Add(FilterRoot);

            SortColumn = currentSortColumn ?? FolderVisualizerTableColumnKey.RowNumber;
            SortAscRadio.IsChecked = currentSortColumn == null || currentSortDirection == ListSortDirection.Ascending;
            SortDescRadio.IsChecked = currentSortColumn != null && currentSortDirection == ListSortDirection.Descending;

            RefreshIncludeSuggestions(IncludeTagInputTextBox?.Text ?? string.Empty);
            RefreshExcludeSuggestions(ExcludeTagInputTextBox?.Text ?? string.Empty);
            UpdateSelectionSummary();
        }

        public VisualizerWindowState CaptureWindowState()
        {
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            double capturedWidth = bounds.Width > 0 ? bounds.Width : Width;
            double capturedHeight = bounds.Height > 0 ? bounds.Height : Height;

            return new VisualizerWindowState
            {
                Width = Math.Max(MinWidth, capturedWidth),
                Height = Math.Max(MinHeight, capturedHeight),
                IsMaximized = WindowState == WindowState.Maximized
            };
        }

        private static void EnsureParents(FolderFilterRule node, FolderFilterRule? parent)
        {
            node.Parent = parent;
            foreach (FolderFilterRule child in node.Children)
            {
                EnsureParents(child, node);
            }
        }

        private static string GetColumnName(FolderVisualizerTableColumnKey key)
        {
            return key switch
            {
                FolderVisualizerTableColumnKey.RowNumber => "ID",
                FolderVisualizerTableColumnKey.IconType => "Icon Type",
                FolderVisualizerTableColumnKey.Name => "Name",
                FolderVisualizerTableColumnKey.Tags => "Tags",
                FolderVisualizerTableColumnKey.DirectoryPath => "Folder Path",
                FolderVisualizerTableColumnKey.PreviewPath => "Preview Path",
                FolderVisualizerTableColumnKey.LastModified => "Last Modified",
                FolderVisualizerTableColumnKey.EmoteCount => "Emotes",
                FolderVisualizerTableColumnKey.Size => "Size",
                FolderVisualizerTableColumnKey.IntegrityFailures => "Integrity Failures",
                FolderVisualizerTableColumnKey.OpenCharIni => "Has char.ini",
                FolderVisualizerTableColumnKey.Readme => "Has readme",
                _ => key.ToString()
            };
        }

        private void ApplySavedWindowState(VisualizerWindowState? state)
        {
            VisualizerWindowState safeState = state ?? new VisualizerWindowState
            {
                Width = 980,
                Height = 680,
                IsMaximized = false
            };

            Width = Math.Max(MinWidth, safeState.Width);
            Height = Math.Max(MinHeight, safeState.Height);
            if (safeState.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        private static string NormalizeTag(string? input)
        {
            return (input ?? string.Empty).Trim();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSelectionSummary();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSelectionSummary();
        }

        private void SelectAllVisibleButton_Click(object sender, RoutedEventArgs e)
        {
            IncludedTagTokens.Clear();
            ExcludedTagTokens.Clear();
            FilterRoot = FolderFilterRule.CreateGroup(FolderFilterConnector.And);
            FilterRoot.Children.Add(FolderFilterRule.CreateCondition());
            EnsureParents(FilterRoot, null);
            FilterTreeRoots.Clear();
            FilterTreeRoots.Add(FilterRoot);
            SortColumn = FolderVisualizerTableColumnKey.RowNumber;
            SortAscRadio.IsChecked = true;
            SortDescRadio.IsChecked = false;
            ClearInputAndRefresh(includeInput: true);
            ClearInputAndRefresh(includeInput: false);
            RefreshIncludeSuggestions(IncludeTagInputTextBox.Text ?? string.Empty);
            RefreshExcludeSuggestions(ExcludeTagInputTextBox.Text ?? string.Empty);
            UpdateSelectionSummary();
        }

        private static bool ContainsTag(IEnumerable<TagTokenItem> source, string tag)
        {
            return source.Any(item => string.Equals(item.Name, tag, StringComparison.OrdinalIgnoreCase));
        }

        private void IncludeTagInputTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (suppressTagInputHandlers)
            {
                return;
            }

            includeNavigationSelectionActive = false;
            includeInputText = IncludeTagInputTextBox.Text ?? string.Empty;
            RefreshIncludeSuggestions(includeInputText);
        }

        private void ExcludeTagInputTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (suppressTagInputHandlers)
            {
                return;
            }

            excludeNavigationSelectionActive = false;
            excludeInputText = ExcludeTagInputTextBox.Text ?? string.Empty;
            RefreshExcludeSuggestions(excludeInputText);
        }

        private void IncludeTagInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleInputPreviewKeyDown(
                isIncludeInput: true,
                inputTextBox: IncludeTagInputTextBox,
                suggestionsListBox: IncludeSuggestionsListBox,
                suggestions: IncludeTagSuggestions,
                e);
        }

        private void ExcludeTagInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleInputPreviewKeyDown(
                isIncludeInput: false,
                inputTextBox: ExcludeTagInputTextBox,
                suggestionsListBox: ExcludeSuggestionsListBox,
                suggestions: ExcludeTagSuggestions,
                e);
        }

        private void HandleInputPreviewKeyDown(
            bool isIncludeInput,
            TextBox inputTextBox,
            ListBox suggestionsListBox,
            ObservableCollection<TagTokenItem> suggestions,
            KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                suggestionsListBox.SelectedIndex = -1;
                if (isIncludeInput)
                {
                    includeNavigationSelectionActive = false;
                    IsIncludeSuggestionsOpen = false;
                }
                else
                {
                    excludeNavigationSelectionActive = false;
                    IsExcludeSuggestionsOpen = false;
                }

                return;
            }

            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                e.Handled = true;
                NavigateSuggestions(
                    suggestionsListBox,
                    suggestions,
                    moveDown: e.Key == Key.Down,
                    isIncludeInput: isIncludeInput);
                return;
            }

            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CommitFromInput(
                isIncludeInput: isIncludeInput,
                inputTextBox: inputTextBox,
                suggestionsListBox: suggestionsListBox,
                suggestions: suggestions);
        }

        private void NavigateSuggestions(
            ListBox suggestionsListBox,
            ObservableCollection<TagTokenItem> suggestions,
            bool moveDown,
            bool isIncludeInput)
        {
            if (suggestions.Count == 0)
            {
                if (isIncludeInput)
                {
                    IsIncludeSuggestionsOpen = false;
                    includeNavigationSelectionActive = false;
                }
                else
                {
                    IsExcludeSuggestionsOpen = false;
                    excludeNavigationSelectionActive = false;
                }

                return;
            }

            if (isIncludeInput)
            {
                IsIncludeSuggestionsOpen = true;
            }
            else
            {
                IsExcludeSuggestionsOpen = true;
            }

            int currentIndex = suggestionsListBox.SelectedIndex;
            int nextIndex;
            if (moveDown)
            {
                nextIndex = currentIndex < 0 ? 0 : Math.Min(currentIndex + 1, suggestions.Count - 1);
            }
            else
            {
                nextIndex = currentIndex < 0 ? suggestions.Count - 1 : Math.Max(currentIndex - 1, 0);
            }

            suggestionsListBox.SelectedIndex = nextIndex;
            suggestionsListBox.ScrollIntoView(suggestionsListBox.SelectedItem);
            if (isIncludeInput)
            {
                includeNavigationSelectionActive = nextIndex >= 0;
            }
            else
            {
                excludeNavigationSelectionActive = nextIndex >= 0;
            }
        }

        private void CommitFromInput(
            bool isIncludeInput,
            TextBox inputTextBox,
            ListBox suggestionsListBox,
            IEnumerable<TagTokenItem> suggestions)
        {
            TagTokenItem? selectedSuggestion = suggestionsListBox.SelectedItem as TagTokenItem;
            bool useSelectedSuggestion = isIncludeInput
                ? includeNavigationSelectionActive
                : excludeNavigationSelectionActive;
            string candidate = ResolveInputCandidate(
                inputTextBox.Text,
                suggestions,
                useSelectedSuggestion ? selectedSuggestion : null);

            if (isIncludeInput)
            {
                includeNavigationSelectionActive = false;
                IsIncludeSuggestionsOpen = false;
            }
            else
            {
                excludeNavigationSelectionActive = false;
                IsExcludeSuggestionsOpen = false;
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            if (isIncludeInput)
            {
                AddIncludeTagToken(candidate);
                return;
            }

            AddExcludeTagToken(candidate);
        }

        private string ResolveInputCandidate(
            string rawInput,
            IEnumerable<TagTokenItem> suggestions,
            TagTokenItem? selectedSuggestion)
        {
            if (selectedSuggestion != null && !string.IsNullOrWhiteSpace(selectedSuggestion.Name))
            {
                return selectedSuggestion.Name;
            }

            string normalized = NormalizeTag(rawInput);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string? exactFromSuggestions = suggestions
                .Select(item => item.Name)
                .FirstOrDefault(tag => string.Equals(tag, normalized, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exactFromSuggestions))
            {
                return exactFromSuggestions;
            }

            string? exactFromAll = allTagCounts.Keys
                .FirstOrDefault(tag => string.Equals(tag, normalized, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exactFromAll))
            {
                return exactFromAll;
            }

            string? startsWith = suggestions
                .Select(item => item.Name)
                .FirstOrDefault(tag => tag.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(startsWith))
            {
                return startsWith;
            }

            return string.Empty;
        }

        private void IncludeSuggestionItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not TagTokenItem token)
            {
                return;
            }

            AddIncludeTagToken(token.Name);
            e.Handled = true;
        }

        private void ExcludeSuggestionItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not TagTokenItem token)
            {
                return;
            }

            AddExcludeTagToken(token.Name);
            e.Handled = true;
        }

        private void AddIncludeTagToken(string tag)
        {
            string normalized = NormalizeTag(tag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!ContainsTag(IncludedTagTokens, normalized))
            {
                IncludedTagTokens.Add(CreateTagToken(normalized));
            }

            RemoveTagFromCollection(ExcludedTagTokens, normalized);
            ClearInputAndRefresh(includeInput: true);
            ClearInputAndRefresh(includeInput: false);
            UpdateSelectionSummary();
        }

        private void AddExcludeTagToken(string tag)
        {
            string normalized = NormalizeTag(tag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!ContainsTag(ExcludedTagTokens, normalized))
            {
                ExcludedTagTokens.Add(CreateTagToken(normalized));
            }

            RemoveTagFromCollection(IncludedTagTokens, normalized);
            ClearInputAndRefresh(includeInput: false);
            ClearInputAndRefresh(includeInput: true);
            UpdateSelectionSummary();
        }

        private void RemoveIncludedTagTokenButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not TagTokenItem token)
            {
                return;
            }

            RemoveTagFromCollection(IncludedTagTokens, token.Name);
            RefreshIncludeSuggestions(IncludeTagInputTextBox.Text ?? string.Empty);
            UpdateSelectionSummary();
        }

        private void RemoveExcludedTagTokenButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not TagTokenItem token)
            {
                return;
            }

            RemoveTagFromCollection(ExcludedTagTokens, token.Name);
            RefreshExcludeSuggestions(ExcludeTagInputTextBox.Text ?? string.Empty);
            UpdateSelectionSummary();
        }

        private static void RemoveTagFromCollection(ObservableCollection<TagTokenItem> collection, string tag)
        {
            TagTokenItem? existing = collection.FirstOrDefault(entry =>
                string.Equals(entry.Name, tag, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                collection.Remove(existing);
            }
        }

        private void ClearInputAndRefresh(bool includeInput)
        {
            suppressTagInputHandlers = true;
            if (includeInput)
            {
                IncludeTagInputTextBox.Text = string.Empty;
                IncludeSuggestionsListBox.SelectedIndex = -1;
                IsIncludeSuggestionsOpen = false;
            }
            else
            {
                ExcludeTagInputTextBox.Text = string.Empty;
                ExcludeSuggestionsListBox.SelectedIndex = -1;
                IsExcludeSuggestionsOpen = false;
            }

            suppressTagInputHandlers = false;
            includeNavigationSelectionActive = false;
            excludeNavigationSelectionActive = false;
            if (includeInput)
            {
                includeInputText = string.Empty;
            }
            else
            {
                excludeInputText = string.Empty;
            }

            if (includeInput)
            {
                RefreshIncludeSuggestions(string.Empty);
            }
            else
            {
                RefreshExcludeSuggestions(string.Empty);
            }
        }

        private void RefreshIncludeSuggestions(string input)
        {
            RefreshSuggestions(IncludeTagSuggestions, input, IncludedTagTokens);
            IsIncludeSuggestionsOpen = IncludeTagSuggestions.Count > 0 && !string.IsNullOrWhiteSpace(input);
            if (IsIncludeSuggestionsOpen && IncludeSuggestionsListBox.SelectedIndex >= IncludeTagSuggestions.Count)
            {
                IncludeSuggestionsListBox.SelectedIndex = -1;
            }
        }

        private void RefreshExcludeSuggestions(string input)
        {
            RefreshSuggestions(ExcludeTagSuggestions, input, ExcludedTagTokens);
            IsExcludeSuggestionsOpen = ExcludeTagSuggestions.Count > 0 && !string.IsNullOrWhiteSpace(input);
            if (IsExcludeSuggestionsOpen && ExcludeSuggestionsListBox.SelectedIndex >= ExcludeTagSuggestions.Count)
            {
                ExcludeSuggestionsListBox.SelectedIndex = -1;
            }
        }

        private void RefreshSuggestions(
            ObservableCollection<TagTokenItem> target,
            string input,
            ObservableCollection<TagTokenItem> existingTokens)
        {
            string query = NormalizeTag(input);
            IEnumerable<string> matches = allTagCounts.Keys
                .Where(tag => !ContainsTag(existingTokens, tag))
                .Where(tag => string.IsNullOrWhiteSpace(query)
                    || tag.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(40);

            target.Clear();
            foreach (string match in matches)
            {
                target.Add(CreateTagToken(match));
            }
        }

        private TagTokenItem CreateTagToken(string tag)
        {
            int count = allTagCounts.TryGetValue(tag, out int resolved) ? resolved : 0;
            return new TagTokenItem(tag, count);
        }

        private void AddConditionToRootButton_Click(object sender, RoutedEventArgs e)
        {
            AddConditionToGroup(FilterRoot);
        }

        private void AddGroupToRootButton_Click(object sender, RoutedEventArgs e)
        {
            AddGroupToGroup(FilterRoot);
        }

        private void AddConditionToNodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not FolderFilterRule node)
            {
                return;
            }

            FolderFilterRule targetGroup = node.IsGroup ? node : node.Parent ?? FilterRoot;
            AddConditionToGroup(targetGroup);
        }

        private void AddGroupToNodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not FolderFilterRule node)
            {
                return;
            }

            FolderFilterRule targetGroup = node.IsGroup ? node : node.Parent ?? FilterRoot;
            AddGroupToGroup(targetGroup);
        }

        private void RemoveNodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not FolderFilterRule node)
            {
                return;
            }

            if (node.Parent == null)
            {
                return;
            }

            node.Parent.Children.Remove(node);
            if (FilterRoot.Children.Count == 0)
            {
                AddConditionToGroup(FilterRoot);
            }

            UpdateSelectionSummary();
        }

        private void ListValueTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not FolderFilterListValue listValue)
            {
                return;
            }

            FolderFilterRule? owner = listValue.Owner;
            owner?.OnListValueChanged();
            UpdateSelectionSummary();
        }

        private void EnumSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not FolderFilterRule rule)
            {
                return;
            }

            ContextMenu menu = new ContextMenu();
            foreach (FolderFilterEnumOption option in rule.EnumOptions)
            {
                MenuItem item = new MenuItem
                {
                    Header = option.Value,
                    IsCheckable = true,
                    IsChecked = option.IsSelected,
                    StaysOpenOnClick = true
                };
                item.Click += (_, _) =>
                {
                    option.IsSelected = item.IsChecked;
                    UpdateSelectionSummary();
                };
                menu.Items.Add(item);
            }

            menu.Closed += (_, _) =>
            {
                rule.OnEnumSelectionChanged();
                UpdateSelectionSummary();
            };
            menu.PlacementTarget = element;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void AddConditionToGroup(FolderFilterRule group)
        {
            if (!group.IsGroup)
            {
                return;
            }

            FolderFilterRule condition = FolderFilterRule.CreateCondition();
            condition.Parent = group;
            group.Children.Add(condition);
            UpdateSelectionSummary();
        }

        private void AddGroupToGroup(FolderFilterRule group)
        {
            if (!group.IsGroup)
            {
                return;
            }

            FolderFilterRule nestedGroup = FolderFilterRule.CreateGroup(FolderFilterConnector.And);
            nestedGroup.Parent = group;
            FolderFilterRule seedCondition = FolderFilterRule.CreateCondition();
            seedCondition.Parent = nestedGroup;
            nestedGroup.Children.Add(seedCondition);
            group.Children.Add(nestedGroup);
            UpdateSelectionSummary();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            FilterRoot = FilterRoot.Clone();
            EnsureParents(FilterRoot, null);
            DialogResult = true;
            Close();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                for (DependencyObject? current = source; current != null;)
                {
                    if (current.GetType().Name.Contains("Button", StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (current is FrameworkElement element)
                    {
                        current = element.Parent ?? element.TemplatedParent as DependencyObject;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void UpdateSelectionSummary()
        {
            int includedCount = IncludedTagTokens.Count;
            int excludedCount = ExcludedTagTokens.Count;
            int activeRulesCount = FilterRoot.CountActiveConditions();
            SelectionSummaryText.Text =
                $"Include: {includedCount} | Exclude: {excludedCount} | Active Rules: {activeRulesCount}";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class TagTokenItem
    {
        public TagTokenItem(string name, int count)
        {
            Name = name;
            Count = count;
        }

        public string Name { get; }
        public int Count { get; }
        public string CountText => $"({Count})";

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class TagFilterOption : INotifyPropertyChanged
    {
        private bool includeSelected;
        private bool excludeSelected;

        public string Name { get; set; } = string.Empty;
        public bool CanExclude { get; set; } = true;

        public bool IncludeSelected
        {
            get => includeSelected;
            set
            {
                if (includeSelected == value)
                {
                    return;
                }

                includeSelected = value;
                if (includeSelected && excludeSelected)
                {
                    excludeSelected = false;
                    OnPropertyChanged(nameof(ExcludeSelected));
                }
                OnPropertyChanged();
            }
        }

        public bool ExcludeSelected
        {
            get => excludeSelected;
            set
            {
                bool safeValue = CanExclude && value;
                if (excludeSelected == safeValue)
                {
                    return;
                }

                excludeSelected = safeValue;
                if (excludeSelected && includeSelected)
                {
                    includeSelected = false;
                    OnPropertyChanged(nameof(IncludeSelected));
                }
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class FilterColumnChoice
    {
        public FilterColumnChoice(FolderVisualizerTableColumnKey key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }

        public FolderVisualizerTableColumnKey Key { get; }
        public string DisplayName { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
