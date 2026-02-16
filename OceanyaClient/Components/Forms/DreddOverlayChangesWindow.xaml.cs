using OceanyaClient.AdvancedFeatures;
using System.Collections.ObjectModel;
using System.Windows;

namespace OceanyaClient
{
    public partial class DreddOverlayChangesWindow : Window
    {
        private readonly ObservableCollection<DreddBackgroundOverlayOverrideService.DreddOverlayChangePreview> changes
            = new ObservableCollection<DreddBackgroundOverlayOverrideService.DreddOverlayChangePreview>();

        public DreddOverlayChangesWindow()
        {
            InitializeComponent();
            ChangesGrid.ItemsSource = changes;
            ReloadChanges();
        }

        private void ReloadChanges()
        {
            changes.Clear();
            foreach (var change in DreddBackgroundOverlayOverrideService.GetCachedChangesPreview())
            {
                changes.Add(change);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            ReloadChanges();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
