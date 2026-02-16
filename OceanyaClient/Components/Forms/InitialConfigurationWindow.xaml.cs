using AOBot_Testing.Structures;
using Microsoft.Win32;
using OceanyaClient.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;


namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for InitialConfigurationWindow.xaml
    /// </summary>
    public partial class InitialConfigurationWindow : Window
    {
        public InitialConfigurationWindow()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
            LoadSavefile();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Config files (*.ini)|*.ini",
                Title = "Select base config.ini"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ConfigINIPathTextBox.Text = openFileDialog.FileName;
            }
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
            string configIniPath = ConfigINIPathTextBox.Text;
            string connectionPath = ConnectionPathTextBox.Text;

            if (string.IsNullOrWhiteSpace(configIniPath) || string.IsNullOrWhiteSpace(connectionPath))
            {
                OceanyaMessageBox.Show("Please provide both the config.ini path and the connection path.",
                                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }


            if (!File.Exists(configIniPath))
            {
                OceanyaMessageBox.Show("File not found: " + configIniPath);
                return;
            }
            
            if (Path.GetFileName(configIniPath).ToLower() != "config.ini")
            {
                OceanyaMessageBox.Show("The filepath does not point to config.ini! " + configIniPath);
                return;
            }


            // Save to settings or use as needed
            try
            {
                Globals.UpdateConfigINI(configIniPath);
                Globals.ConnectionString = connectionPath;
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show("Error updating base folders: " + ex.Message,
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Save configuration to file
            SaveConfiguration(configIniPath, connectionPath, UseSingleClientCheckBox.IsChecked != false);

            // Refresh character and background info if checkbox is checked
            if (RefreshInfoCheckBox.IsChecked == true)
            {
                await WaitForm.ShowFormAsync("Refreshing character and background info...", this);
                CharacterFolder.RefreshCharacterList
                    ( 
                        onParsedCharacter:
                        (ini) =>
                        {
                            WaitForm.SetSubtitle("Parsed Character: " + ini.Name);
                        },
                        onChangedMountPath:
                        (path) =>
                        {
                            WaitForm.SetSubtitle("Changed mount path: " + path);
                        }
                    );
                WaitForm.CloseForm();
            }

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            
            this.Close();
        }

        private void LoadSavefile()
        {
            // Implement your configuration loading logic here
            // This would replace the LoadConfiguration method from the original code
            try
            {
                ConfigINIPathTextBox.Text = SaveFile.Data.ConfigIniPath;
                ConnectionPathTextBox.Text = SaveFile.Data.ConnectionPath;
                UseSingleClientCheckBox.IsChecked = SaveFile.Data.UseSingleInternalClient;
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show("Error loading configuration: " + ex.Message,
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveConfiguration(string configIniPath, string connectionPath, bool useSingleInternalClient)
        {
            // Implement your configuration saving logic here
            // This would replace the SaveConfiguration method from the original code
            try
            {
                SaveFile.Data.ConfigIniPath = configIniPath;
                SaveFile.Data.ConnectionPath = connectionPath;
                SaveFile.Data.UseSingleInternalClient = useSingleInternalClient;
                SaveFile.Save();
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show("Error saving configuration: " + ex.Message,
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // The FadeIn animation is triggered automatically by the EventTrigger in XAML
        }

        private bool _isClosing = false;
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // If we're already in the process of closing with animation, allow the close
            if (_isClosing)
            {
                return;
            }

            // Otherwise, cancel the default closing and animate first
            e.Cancel = true;
            _isClosing = true;

            // Play the fade out animation
            Storyboard fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Completed += (s, _) =>
            {
                // When animation completes, actually close the window
                this.Dispatcher.Invoke(() =>
                {
                    // Set a flag to bypass this handler and close just this window
                    _isClosing = true;
                    this.Close();
                });
            };
            fadeOut.Begin(this);
        }
    }
}
