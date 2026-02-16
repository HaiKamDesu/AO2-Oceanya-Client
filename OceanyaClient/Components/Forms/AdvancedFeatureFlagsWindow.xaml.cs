using OceanyaClient.AdvancedFeatures;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace OceanyaClient
{
    public partial class AdvancedFeatureFlagsWindow : Window
    {
        private readonly Dictionary<string, CheckBox> featureCheckBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

        public AdvancedFeatureFlagsWindow()
        {
            InitializeComponent();
            BuildFeatureRows();
        }

        private void BuildFeatureRows()
        {
            FeatureRowsPanel.Children.Clear();
            featureCheckBoxes.Clear();

            foreach (FeatureDefinition definition in FeatureCatalog.Definitions)
            {
                Grid rowGrid = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 8)
                };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                CheckBox checkBox = new CheckBox
                {
                    Content = definition.DisplayName,
                    Foreground = System.Windows.Media.Brushes.White,
                    IsChecked = SaveFile.Data.AdvancedFeatures.IsEnabled(definition.FeatureId),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = definition.FeatureId
                };
                featureCheckBoxes[definition.FeatureId] = checkBox;
                Grid.SetColumn(checkBox, 0);
                rowGrid.Children.Add(checkBox);

                Button configButton = new Button
                {
                    Content = "Configure",
                    Width = 90,
                    Height = 26,
                    Margin = new Thickness(8, 0, 0, 0),
                    IsEnabled = definition.SupportsConfiguration
                };
                configButton.Click += (_, _) => OpenFeatureConfiguration(definition.FeatureId);
                Grid.SetColumn(configButton, 1);
                rowGrid.Children.Add(configButton);

                FeatureRowsPanel.Children.Add(rowGrid);
            }
        }

        private void OpenFeatureConfiguration(string featureId)
        {
            if (string.Equals(featureId, AdvancedFeatureIds.DreddBackgroundOverlayOverride, StringComparison.OrdinalIgnoreCase))
            {
                DreddOverlayDatabaseWindow window = new DreddOverlayDatabaseWindow
                {
                    Owner = this
                };
                window.ShowDialog();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var pair in featureCheckBoxes)
            {
                SaveFile.Data.AdvancedFeatures.SetEnabled(pair.Key, pair.Value.IsChecked == true);
            }

            SaveFile.Save();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
