using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Modal editor for one extra audio rule.
    /// </summary>
    public partial class ExtraAudioRuleEditorWindow : OceanyaWindowContentControl
    {
        private readonly List<CharacterSelectorOption> characters;
        private readonly List<string> shownames;
        private readonly List<string> blips;
        private readonly ExtraAudioRule? sourceRule;

        public ExtraAudioRuleEditorWindow(ExtraAudioRule? source)
        {
            InitializeComponent();
            Title = "Extra Audio Rule";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            sourceRule = source == null ? null : Clone(source);
            characters = BuildCharacterOptions();
            shownames = BuildShownameOptions(characters);
            blips = BlipCatalog.GetBlips().ToList();

            KindComboBox.ItemsSource = Enum.GetValues(typeof(ExtraAudioRuleKind));
            TargetComboBox.ItemsSource = new[]
            {
                ExtraAudioRuleTarget.Any,
                ExtraAudioRuleTarget.Character,
                ExtraAudioRuleTarget.Showname,
                ExtraAudioRuleTarget.Blip
            };
            KindComboBox.SelectedItem = sourceRule?.Kind ?? ExtraAudioRuleKind.Blip;
            TargetComboBox.SelectedItem = sourceRule?.Target ?? ExtraAudioRuleTarget.Character;
            VolumeSlider.Value = sourceRule?.VolumePercent ?? 100;
            RefreshMatchOptions(sourceRule?.Match);
            RefreshVolumeText();
        }

        public override string HeaderText => "EXTRA AUDIO RULE";

        public ExtraAudioRule? Rule { get; private set; }

        private void TargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshMatchOptions(null);
        }

        private void CharacterLookupButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterSelectorDialog dialog = new CharacterSelectorDialog(characters) { Owner = HostWindow };
            if (dialog.ShowDialog() == true && dialog.SelectedCharacter != null)
            {
                TargetComboBox.SelectedItem = ExtraAudioRuleTarget.Character;
                RefreshMatchOptions(dialog.SelectedCharacter.Name);
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RefreshVolumeText();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ExtraAudioRuleKind kind = KindComboBox.SelectedItem is ExtraAudioRuleKind selectedKind
                ? selectedKind
                : ExtraAudioRuleKind.Blip;
            ExtraAudioRuleTarget target = TargetComboBox.SelectedItem is ExtraAudioRuleTarget selectedTarget
                ? selectedTarget
                : ExtraAudioRuleTarget.Character;
            string match = ResolveSelectedMatch(target);
            if (target != ExtraAudioRuleTarget.Any && string.IsNullOrWhiteSpace(match))
            {
                return;
            }

            int volumePercent = Math.Clamp((int)Math.Round(VolumeSlider.Value), 0, 200);
            Rule = new ExtraAudioRule
            {
                Name = BuildRuleName(kind, target, match, volumePercent),
                Kind = kind,
                Target = target,
                Match = match,
                VolumePercent = volumePercent,
                IsEnabled = true
            };
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
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void RefreshMatchOptions(string? preferredValue)
        {
            ExtraAudioRuleTarget target = TargetComboBox.SelectedItem is ExtraAudioRuleTarget selectedTarget
                ? selectedTarget
                : ExtraAudioRuleTarget.Character;

            CharacterLookupButton.Visibility = target == ExtraAudioRuleTarget.Character ? Visibility.Visible : Visibility.Collapsed;
            MatchComboBox.IsEnabled = target != ExtraAudioRuleTarget.Any;
            MatchLabel.Text = target switch
            {
                ExtraAudioRuleTarget.Any => "Value",
                ExtraAudioRuleTarget.Character => "Character",
                ExtraAudioRuleTarget.Showname => "Showname",
                ExtraAudioRuleTarget.Blip => "Blip",
                _ => "Value"
            };

            if (target == ExtraAudioRuleTarget.Any)
            {
                MatchComboBox.ItemsSource = null;
                MatchComboBox.SelectedItem = null;
                return;
            }

            if (target == ExtraAudioRuleTarget.Character)
            {
                MatchComboBox.DisplayMemberPath = nameof(CharacterSelectorOption.DisplayText);
                MatchComboBox.ItemsSource = characters;
                MatchComboBox.SelectedItem = characters.FirstOrDefault(option =>
                    string.Equals(option.Name, preferredValue, StringComparison.OrdinalIgnoreCase));
                MatchComboBox.SelectedItem ??= characters.FirstOrDefault();
                return;
            }

            MatchComboBox.DisplayMemberPath = string.Empty;
            IReadOnlyList<string> options = target == ExtraAudioRuleTarget.Showname ? shownames : blips;
            MatchComboBox.ItemsSource = options;
            MatchComboBox.SelectedItem = options.FirstOrDefault(option =>
                string.Equals(option, preferredValue, StringComparison.OrdinalIgnoreCase));
            MatchComboBox.SelectedItem ??= options.FirstOrDefault();
        }

        private string ResolveSelectedMatch(ExtraAudioRuleTarget target)
        {
            if (target == ExtraAudioRuleTarget.Any)
            {
                return string.Empty;
            }

            if (target == ExtraAudioRuleTarget.Character)
            {
                return MatchComboBox.SelectedItem is CharacterSelectorOption option ? option.Name : string.Empty;
            }

            return MatchComboBox.SelectedItem?.ToString()?.Trim() ?? string.Empty;
        }

        private void RefreshVolumeText()
        {
            if (VolumeValueText != null)
            {
                VolumeValueText.Text = $"{Math.Round(VolumeSlider.Value).ToString(CultureInfo.InvariantCulture)}%";
            }
        }

        private static string BuildRuleName(
            ExtraAudioRuleKind kind,
            ExtraAudioRuleTarget target,
            string match,
            int volumePercent)
        {
            string targetText = target == ExtraAudioRuleTarget.Any ? "all" : $"{target} {match}";
            return $"{kind} rule: {targetText} {volumePercent}%";
        }

        private static List<CharacterSelectorOption> BuildCharacterOptions()
        {
            return CharacterFolder.FullList
                .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                .Select(character => new CharacterSelectorOption(character.Name, character.configINI.ShowName, character.DirectoryPath))
                .ToList();
        }

        private static List<string> BuildShownameOptions(IEnumerable<CharacterSelectorOption> characterOptions)
        {
            HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CharacterSelectorOption option in characterOptions)
            {
                if (!string.IsNullOrWhiteSpace(option.Showname))
                {
                    values.Add(option.Showname);
                }
            }

            foreach (CharacterFolder character in CharacterFolder.FullList)
            {
                foreach (string showname in character.configINI.ShowNameOverridesByIndex.Values)
                {
                    if (!string.IsNullOrWhiteSpace(showname))
                    {
                        values.Add(showname);
                    }
                }
            }

            return values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static ExtraAudioRule Clone(ExtraAudioRule rule)
        {
            return new ExtraAudioRule
            {
                Name = rule.Name,
                Kind = rule.Kind,
                Target = rule.Target,
                Match = rule.Match,
                VolumePercent = rule.VolumePercent,
                IsEnabled = rule.IsEnabled
            };
        }
    }
}
