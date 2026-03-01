using OceanyaClient.AdvancedFeatures;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    public partial class AdvancedFeatureFlagsWindow : OceanyaWindowContentControl
    {
        private readonly Dictionary<string, CheckBox> featureCheckBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public override string HeaderText => "ADVANCED FEATURE FLAGGING";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => false;

        public AdvancedFeatureFlagsWindow()
        {
            InitializeComponent();
            Title = "Advanced Feature Flagging";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
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

                StackPanel textPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical
                };

                CheckBox checkBox = new CheckBox
                {
                    Content = definition.DisplayName,
                    Foreground = System.Windows.Media.Brushes.White,
                    IsChecked = SaveFile.Data.AdvancedFeatures.IsEnabled(definition.FeatureId),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                    ToolTip = definition.FeatureId
                };
                textPanel.Children.Add(checkBox);
                textPanel.Children.Add(new TextBlock
                {
                    Text = definition.Description,
                    Foreground = System.Windows.Media.Brushes.Gainsboro,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(22, 2, 0, 0)
                });

                featureCheckBoxes[definition.FeatureId] = checkBox;
                Grid.SetColumn(textPanel, 0);
                rowGrid.Children.Add(textPanel);

                Button configButton = new Button
                {
                    Content = "Configure",
                    Width = 90,
                    Height = 26,
                    Margin = new Thickness(8, 0, 0, 0),
                    IsEnabled = definition.SupportsConfiguration,
                    Style = (Style)FindResource("ModernButton")
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

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
