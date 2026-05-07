using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Focused selector for local AO2 character folders.
    /// </summary>
    public partial class CharacterSelectorDialog : OceanyaWindowContentControl
    {
        private readonly List<CharacterSelectorOption> allCharacters;
        private readonly ObservableCollection<CharacterSelectorOption> visibleCharacters =
            new ObservableCollection<CharacterSelectorOption>();

        public CharacterSelectorDialog(IEnumerable<CharacterSelectorOption> characters)
        {
            InitializeComponent();
            Title = "Select Character";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            allCharacters = characters
                .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            CharactersListBox.ItemsSource = visibleCharacters;
            RefreshVisibleCharacters();
        }

        public override string HeaderText => "SELECT CHARACTER";

        public override bool IsUserResizeEnabled => true;

        public CharacterSelectorOption? SelectedCharacter { get; private set; }

        private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshVisibleCharacters();
        }

        private void CharactersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AcceptSelection();
        }

        private void UseThisButton_Click(object sender, RoutedEventArgs e)
        {
            AcceptSelection();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RefreshVisibleCharacters()
        {
            string filter = SearchTextBox.Text?.Trim() ?? string.Empty;
            visibleCharacters.Clear();
            foreach (CharacterSelectorOption option in allCharacters)
            {
                if (!string.IsNullOrWhiteSpace(filter)
                    && !option.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !option.Showname.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                visibleCharacters.Add(option);
            }

            if (CharactersListBox.SelectedItem == null)
            {
                CharactersListBox.SelectedItem = visibleCharacters.FirstOrDefault();
            }
        }

        private void AcceptSelection()
        {
            if (CharactersListBox.SelectedItem is not CharacterSelectorOption selected)
            {
                return;
            }

            SelectedCharacter = selected;
            DialogResult = true;
            Close();
        }
    }
}
