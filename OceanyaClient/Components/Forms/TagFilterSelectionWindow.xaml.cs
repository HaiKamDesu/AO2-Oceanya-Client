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
    public partial class TagFilterSelectionWindow : Window
    {
        private readonly ObservableCollection<TagFilterOption> allOptions = new ObservableCollection<TagFilterOption>();
        private readonly ICollectionView optionsView;
        private string searchText = string.Empty;

        public IReadOnlyList<string> IncludedTags => allOptions
            .Where(option => option.IncludeSelected)
            .Select(option => option.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        public IReadOnlyList<string> ExcludedTags => allOptions
            .Where(option => option.ExcludeSelected)
            .Select(option => option.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        public TagFilterSelectionWindow(
            IEnumerable<string> allTags,
            IEnumerable<string> includedTags,
            IEnumerable<string> excludedTags,
            VisualizerWindowState? savedWindowState = null)
        {
            InitializeComponent();
            ApplySavedWindowState(savedWindowState);

            HashSet<string> includeSet = new HashSet<string>(
                includedTags ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> excludeSet = new HashSet<string>(
                excludedTags ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (string tag in (allTags ?? Enumerable.Empty<string>()).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase))
            {
                allOptions.Add(new TagFilterOption
                {
                    Name = tag,
                    CanExclude = !string.Equals(tag, "(none)", StringComparison.OrdinalIgnoreCase),
                    IncludeSelected = includeSet.Contains(tag),
                    ExcludeSelected = excludeSet.Contains(tag)
                });
            }

            foreach (TagFilterOption option in allOptions)
            {
                option.PropertyChanged += Option_PropertyChanged;
            }

            optionsView = CollectionViewSource.GetDefaultView(allOptions);
            optionsView.Filter = FilterOption;
            TagListBox.ItemsSource = optionsView;
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

        private void ApplySavedWindowState(VisualizerWindowState? state)
        {
            VisualizerWindowState safeState = state ?? new VisualizerWindowState
            {
                Width = 500,
                Height = 560,
                IsMaximized = false
            };

            Width = Math.Max(MinWidth, safeState.Width);
            Height = Math.Max(MinHeight, safeState.Height);
            if (safeState.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        private bool FilterOption(object obj)
        {
            if (obj is not TagFilterOption option)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            return option.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            searchText = SearchTextBox.Text?.Trim() ?? string.Empty;
            optionsView.Refresh();
            UpdateSelectionSummary();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTableColumnWidths();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTableColumnWidths();
        }

        private void TagListBox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTableColumnWidths();
        }

        private void UpdateTableColumnWidths()
        {
            if (TagGridView == null)
            {
                return;
            }

            double fixedIncludeWidth = 84;
            double fixedExcludeWidth = 84;
            IncludeColumn.Width = fixedIncludeWidth;
            ExcludeColumn.Width = fixedExcludeWidth;

            double available = TagListBox.ActualWidth - fixedIncludeWidth - fixedExcludeWidth - 26;
            TagNameColumn.Width = Math.Max(160, available);
        }

        private void SelectAllVisibleButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (TagFilterOption option in allOptions)
            {
                if (FilterOption(option))
                {
                    option.IncludeSelected = true;
                }
            }
            UpdateSelectionSummary();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (TagFilterOption option in allOptions)
            {
                option.IncludeSelected = false;
                option.ExcludeSelected = false;
            }
            UpdateSelectionSummary();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Option_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(TagFilterOption.IncludeSelected), StringComparison.Ordinal)
                && !string.Equals(e.PropertyName, nameof(TagFilterOption.ExcludeSelected), StringComparison.Ordinal))
            {
                return;
            }

            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            int includedCount = allOptions.Count(option => option.IncludeSelected);
            int excludedCount = allOptions.Count(option => option.ExcludeSelected);
            int visibleCount = allOptions.Count(option => FilterOption(option));
            SelectionSummaryText.Text =
                $"Include: {includedCount} | Exclude: {excludedCount} | Visible: {visibleCount} | Total: {allOptions.Count}";
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
}
