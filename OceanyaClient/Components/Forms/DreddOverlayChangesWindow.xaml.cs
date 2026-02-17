using OceanyaClient.AdvancedFeatures;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OceanyaClient
{
    public partial class DreddOverlayChangesWindow : Window
    {
        private sealed class ChangeDiffItem
        {
            public string DesignIniPath { get; set; } = string.Empty;
            public string PositionKey { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string DiffBefore { get; set; } = string.Empty;
            public string DiffAfter { get; set; } = string.Empty;
        }

        private readonly ObservableCollection<ChangeDiffItem> changes
            = new ObservableCollection<ChangeDiffItem>();

        public DreddOverlayChangesWindow()
        {
            InitializeComponent();
            DiffItemsControl.ItemsSource = changes;
            ReloadChanges();
        }

        private void ReloadChanges()
        {
            changes.Clear();
            foreach (DreddBackgroundOverlayOverrideService.DreddOverlayChangePreview change
                in DreddBackgroundOverlayOverrideService.GetCachedChangesPreview())
            {
                string keyPrefix = string.IsNullOrWhiteSpace(change.PositionKey) ? string.Empty : $"{change.PositionKey}=";
                changes.Add(new ChangeDiffItem
                {
                    DesignIniPath = change.DesignIniPath,
                    PositionKey = change.PositionKey,
                    Status = change.Status,
                    DiffBefore = $"- {keyPrefix}{change.OriginalValue}",
                    DiffAfter = $"+ {keyPrefix}{change.CurrentValue}"
                });
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            ReloadChanges();
        }

        private void DiscardAllButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult confirmation = OceanyaMessageBox.Show(
                "Discard all pending Dredd overlay changes?",
                "Discard All Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            bool success = DreddBackgroundOverlayOverrideService.TryDiscardAllChanges(out string message);
            if (!success)
            {
                OceanyaMessageBox.Show(
                    $"Some changes could not be discarded:{System.Environment.NewLine}{message}",
                    "Discard Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ReloadChanges();
        }

        private void ApplyAllButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult confirmation = OceanyaMessageBox.Show(
                "Apply all pending Dredd overlay changes as the new baseline?",
                "Apply All Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            bool success = DreddBackgroundOverlayOverrideService.TryKeepAllChanges(out string message);
            if (!success)
            {
                OceanyaMessageBox.Show(
                    $"Some changes could not be applied:{System.Environment.NewLine}{message}",
                    "Apply Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ReloadChanges();
        }

        private void DiscardSingleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ChangeDiffItem? item = ResolveItemFromMenuEvent(sender);
            if (item == null)
            {
                return;
            }

            bool success = DreddBackgroundOverlayOverrideService.TryDiscardSingleChange(
                item.DesignIniPath,
                item.PositionKey,
                out string message);

            if (!success)
            {
                OceanyaMessageBox.Show(message, "Discard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ReloadChanges();
        }

        private void KeepSingleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ChangeDiffItem? item = ResolveItemFromMenuEvent(sender);
            if (item == null)
            {
                return;
            }

            bool success = DreddBackgroundOverlayOverrideService.TryKeepSingleChange(
                item.DesignIniPath,
                item.PositionKey,
                out string message);
            if (!success)
            {
                OceanyaMessageBox.Show(message, "Keep Change Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ReloadChanges();
        }

        private static ChangeDiffItem? ResolveItemFromMenuEvent(object sender)
        {
            if (sender is not MenuItem menuItem)
            {
                return null;
            }

            ContextMenu? contextMenu = menuItem.Parent as ContextMenu ?? menuItem.TemplatedParent as ContextMenu;
            if (contextMenu?.PlacementTarget is FrameworkElement target && target.DataContext is ChangeDiffItem item)
            {
                return item;
            }

            return menuItem.DataContext as ChangeDiffItem;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void TitleCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
