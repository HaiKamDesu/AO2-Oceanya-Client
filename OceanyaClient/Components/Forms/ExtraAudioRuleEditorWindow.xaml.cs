using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.Viewport;

namespace OceanyaClient
{
    /// <summary>
    /// Modal editor for one extra audio rule.
    /// </summary>
    public partial class ExtraAudioRuleEditorWindow : OceanyaWindowContentControl
    {
        private readonly List<CharacterSelectorOption> characters;
        private readonly List<string> blips;
        private readonly List<string> sfxTokens;
        private readonly List<string> musicTokens;
        private readonly IReadOnlyDictionary<ExtraAudioRuleKind, string> kindDisplayNames =
            new Dictionary<ExtraAudioRuleKind, string>
            {
                [ExtraAudioRuleKind.Blip] = "Blip",
                [ExtraAudioRuleKind.Sfx] = "SFX",
                [ExtraAudioRuleKind.Music] = "Music"
            };
        private readonly IReadOnlyDictionary<ExtraAudioRuleTarget, string> targetDisplayNames =
            new Dictionary<ExtraAudioRuleTarget, string>
            {
                [ExtraAudioRuleTarget.Character] = "Character",
                [ExtraAudioRuleTarget.Showname] = "Showname",
                [ExtraAudioRuleTarget.Blip] = "Blip",
                [ExtraAudioRuleTarget.Sfx] = "SFX"
            };
        private readonly Dictionary<string, string> blipTokenByDisplayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ExtraAudioRule? sourceRule;
        private readonly AO2BlipPreviewPlayer blipPreviewPlayer = new AO2BlipPreviewPlayer();
        private DispatcherTimer? blipPreviewTimer;
        private AO2ViewportBlipPlaybackRules.BlipCrawlState? blipPreviewState;
        private bool suppressVolumeTextEvents;
        private int currentVolumePercent = 100;

        public ExtraAudioRuleEditorWindow(ExtraAudioRule? source)
        {
            InitializeComponent();
            Title = "Extra Audio Rule";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            Closed += (_, _) =>
            {
                StopBlipPreview();
                blipPreviewPlayer.Dispose();
            };
            sourceRule = source == null ? null : Clone(source);
            characters = BuildCharacterOptions();
            blips = BuildBlipDisplayNames(BlipCatalog.GetBlips(), blipTokenByDisplayName);
            sfxTokens = BuildSfxTokenCatalog();
            musicTokens = BuildAudioTokenCatalog("music");
            PopulateCharacterComboBox();
            BlipDropdown.ItemsSource = blips;
            SfxDropdown.ItemsSource = sfxTokens;
            MusicDropdown.ItemsSource = musicTokens;

            KindDropdown.ItemsSource = kindDisplayNames
                .OrderBy(pair => (int)pair.Key)
                .Select(pair => pair.Value)
                .ToList();
            KindDropdown.Text = kindDisplayNames[sourceRule?.Kind ?? ExtraAudioRuleKind.Blip];
            RefreshTargetOptions(sourceRule?.Target);
            SetVolumePercent(sourceRule?.VolumePercent ?? 100, updateSlider: true, updateText: true);
            ShownameCaseSensitiveCheckBox.IsChecked = sourceRule?.IsCaseSensitive == true;
            RefreshMatchOptions(sourceRule?.Match);
        }

        public override string HeaderText => "EXTRA AUDIO RULE";

        public ExtraAudioRule? Rule { get; private set; }

        private void TargetDropdown_TextValueChanged(object? sender, EventArgs e)
        {
            RefreshMatchOptions(null);
        }

        private void KindDropdown_TextValueChanged(object? sender, EventArgs e)
        {
            StopBlipPreview();
            RefreshTargetOptions(null);
            RefreshMatchOptions(null);
        }

        private void CharacterLookupButton_Click(object sender, RoutedEventArgs e)
        {
            StopBlipPreview();
            CharacterSelectorOption? selected = null;
            CharacterFolderVisualizerWindow selector = new CharacterFolderVisualizerWindow(
                null,
                suppressInitialLoadWaitForm: false,
                characterSelectionMode: true,
                selectCharacterForDialog: item =>
                {
                    selected = characters.FirstOrDefault(option =>
                        string.Equals(option.DirectoryPath, item.DirectoryPath, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(option.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                })
            {
                Owner = HostWindow,
                WindowState = WindowState.Maximized
            };
            if (selector.ShowDialog() == true && selected != null)
            {
                TargetDropdown.Text = targetDisplayNames[ExtraAudioRuleTarget.Character];
                CharacterComboBox.SelectedText = selected.Name;
                UpdateBlipPreviewVisibility();
            }
        }

        private void CharacterComboBox_OnConfirm(object? sender, string value)
        {
            StopBlipPreview();
            CharacterComboBox.SelectedText = value?.Trim() ?? string.Empty;
            UpdateBlipPreviewVisibility();
        }

        private void BlipDropdown_TextValueChanged(object? sender, EventArgs e)
        {
            StopBlipPreview();
            UpdateBlipPreviewVisibility();
        }

        private void AudioTokenDropdown_TextValueChanged(object? sender, EventArgs e)
        {
            StopBlipPreview();
            UpdateBlipPreviewVisibility();
        }

        private void BlipPreviewButton_PlayRequested(object? sender, EventArgs e)
        {
            ExtraAudioRuleKind kind = GetSelectedKind();
            string? path = ResolvePreviewAudioPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                BlipPreviewButton.IsPlaying = false;
                return;
            }

            blipPreviewPlayer.Stop();
            if (!blipPreviewPlayer.TrySetBlip(path))
            {
                BlipPreviewButton.IsPlaying = false;
                return;
            }

            blipPreviewPlayer.Volume = (float)ResolvePreviewVolume();
            if (kind != ExtraAudioRuleKind.Blip)
            {
                BlipPreviewButton.DurationMs = Math.Max(160, blipPreviewPlayer.GetLoadedDurationMs());
                BlipPreviewButton.IsPlaying = true;
                _ = blipPreviewPlayer.PlayBlip();
                return;
            }

            blipPreviewState = AO2ViewportBlipPlaybackRules.CreateState(
                AO2ViewportBlipPlaybackRules.PreviewSentence,
                AO2ViewportAssetResolver.GetTextCrawlMilliseconds(),
                AO2ViewportAssetResolver.GetBlipRate(),
                AO2ViewportAssetResolver.GetBlankBlipEnabled());
            BlipPreviewButton.DurationMs = EstimatePreviewDurationMs(blipPreviewState);
            BlipPreviewButton.IsPlaying = true;
            StartBlipPreviewTimer();
        }

        private void BlipPreviewButton_StopRequested(object? sender, EventArgs e)
        {
            StopBlipPreview();
        }

        private void BlipPreviewButton_PlaybackCompleted(object? sender, EventArgs e)
        {
            StopBlipPreview();
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VolumeSlider == null)
            {
                return;
            }

            SetVolumePercent((int)Math.Round(VolumeSlider.Value), updateSlider: false, updateText: true);
            blipPreviewPlayer.Volume = (float)ResolvePreviewVolume();
        }

        private void VolumeValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressVolumeTextEvents || VolumeValueTextBox == null)
            {
                return;
            }

            if (TryParsePositivePercent(VolumeValueTextBox.Text, out int value))
            {
                SetVolumePercent(value, updateSlider: true, updateText: false);
                blipPreviewPlayer.Volume = (float)ResolvePreviewVolume();
            }
        }

        private void VolumeValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            RefreshVolumeText();
            blipPreviewPlayer.Volume = (float)ResolvePreviewVolume();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ExtraAudioRuleKind kind = GetSelectedKind();
            ExtraAudioRuleTarget target = GetSelectedTarget(kind);
            if (kind == ExtraAudioRuleKind.Music)
            {
                target = ExtraAudioRuleTarget.Any;
            }

            string match = ResolveSelectedMatch(target);
            if (string.IsNullOrWhiteSpace(match))
            {
                return;
            }

            int volumePercent = Math.Max(0, currentVolumePercent);
            Rule = new ExtraAudioRule
            {
                Name = BuildRuleName(kind, target, match, volumePercent),
                Kind = kind,
                Target = target,
                Match = match,
                VolumePercent = volumePercent,
                IsEnabled = true,
                IsCaseSensitive = target == ExtraAudioRuleTarget.Showname && ShownameCaseSensitiveCheckBox.IsChecked == true
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
            ExtraAudioRuleKind kind = GetSelectedKind();
            ExtraAudioRuleTarget target = GetSelectedTarget(kind);

            bool usesTarget = kind != ExtraAudioRuleKind.Music;
            TargetLabelRow.Visibility = usesTarget ? Visibility.Visible : Visibility.Collapsed;
            TargetDropdown.Visibility = usesTarget ? Visibility.Visible : Visibility.Collapsed;
            CharacterLookupButton.Visibility = usesTarget && target == ExtraAudioRuleTarget.Character ? Visibility.Visible : Visibility.Collapsed;
            BlipPreviewButton.Visibility = Visibility.Collapsed;
            CharacterComboBox.Visibility =
                (kind == ExtraAudioRuleKind.Blip || kind == ExtraAudioRuleKind.Sfx)
                && target == ExtraAudioRuleTarget.Character
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ShownameTextBox.Visibility =
                (kind == ExtraAudioRuleKind.Blip || kind == ExtraAudioRuleKind.Sfx)
                && target == ExtraAudioRuleTarget.Showname
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ShownameCaseSensitiveCheckBox.Visibility = ShownameTextBox.Visibility;
            BlipDropdown.Visibility = kind == ExtraAudioRuleKind.Blip && target == ExtraAudioRuleTarget.Blip ? Visibility.Visible : Visibility.Collapsed;
            SfxDropdown.Visibility = kind == ExtraAudioRuleKind.Sfx && target == ExtraAudioRuleTarget.Sfx ? Visibility.Visible : Visibility.Collapsed;
            MusicDropdown.Visibility = kind == ExtraAudioRuleKind.Music ? Visibility.Visible : Visibility.Collapsed;
            MatchLabel.Text = kind == ExtraAudioRuleKind.Music ? "Music" : target switch
            {
                ExtraAudioRuleTarget.Character => "Character",
                ExtraAudioRuleTarget.Showname => "Showname",
                ExtraAudioRuleTarget.Blip => "Blip",
                ExtraAudioRuleTarget.Sfx => "SFX",
                _ => "Value"
            };
            UpdateBlipPreviewVisibility();

            if ((kind == ExtraAudioRuleKind.Blip || kind == ExtraAudioRuleKind.Sfx) && target == ExtraAudioRuleTarget.Character)
            {
                CharacterSelectorOption? selected = characters.FirstOrDefault(option =>
                    CharacterOptionMatches(option, preferredValue));
                CharacterComboBox.SelectedText = selected?.Name ?? characters.FirstOrDefault()?.Name ?? string.Empty;
                UpdateBlipPreviewVisibility();
                return;
            }

            if ((kind == ExtraAudioRuleKind.Blip || kind == ExtraAudioRuleKind.Sfx) && target == ExtraAudioRuleTarget.Showname)
            {
                ShownameTextBox.Text = preferredValue ?? string.Empty;
                return;
            }

            if (kind == ExtraAudioRuleKind.Sfx && target == ExtraAudioRuleTarget.Sfx)
            {
                SfxDropdown.Text = ResolveKnownOrFallback(preferredValue, sfxTokens);
                UpdateBlipPreviewVisibility();
                return;
            }

            if (kind == ExtraAudioRuleKind.Music)
            {
                MusicDropdown.Text = ResolveKnownOrFallback(preferredValue, musicTokens);
                UpdateBlipPreviewVisibility();
                return;
            }

            string displayName = ResolveBlipDisplayName(preferredValue);
            BlipDropdown.Text = string.IsNullOrWhiteSpace(displayName) ? blips.FirstOrDefault() ?? string.Empty : displayName;
            UpdateBlipPreviewVisibility();
        }

        private string ResolveSelectedMatch(ExtraAudioRuleTarget target)
        {
            ExtraAudioRuleKind kind = GetSelectedKind();
            if (kind == ExtraAudioRuleKind.Music)
            {
                return MusicDropdown.Text?.Trim() ?? string.Empty;
            }

            if (target == ExtraAudioRuleTarget.Character)
            {
                return CharacterComboBox.SelectedText?.Trim() ?? string.Empty;
            }

            if (target == ExtraAudioRuleTarget.Showname)
            {
                return ShownameTextBox.Text?.Trim() ?? string.Empty;
            }

            if (kind == ExtraAudioRuleKind.Sfx)
            {
                return SfxDropdown.Text?.Trim() ?? string.Empty;
            }

            return BlipDropdown.Text?.Trim() ?? string.Empty;
        }

        private void RefreshTargetOptions(ExtraAudioRuleTarget? preferredTarget)
        {
            ExtraAudioRuleKind kind = GetSelectedKind();
            ExtraAudioRuleTarget[] targets = kind switch
            {
                ExtraAudioRuleKind.Sfx => new[]
                {
                    ExtraAudioRuleTarget.Character,
                    ExtraAudioRuleTarget.Showname,
                    ExtraAudioRuleTarget.Sfx
                },
                ExtraAudioRuleKind.Music => Array.Empty<ExtraAudioRuleTarget>(),
                _ => new[]
                {
                    ExtraAudioRuleTarget.Character,
                    ExtraAudioRuleTarget.Showname,
                    ExtraAudioRuleTarget.Blip
                }
            };

            TargetDropdown.ItemsSource = targets.Select(target => targetDisplayNames[target]).ToList();
            if (targets.Length == 0)
            {
                TargetDropdown.Text = string.Empty;
                return;
            }

            ExtraAudioRuleTarget requested = preferredTarget.HasValue && targets.Contains(preferredTarget.Value)
                ? preferredTarget.Value
                : DefaultTargetForKind(kind);
            TargetDropdown.Text = targetDisplayNames[requested];
        }

        private ExtraAudioRuleKind GetSelectedKind()
        {
            string displayName = KindDropdown.Text?.Trim() ?? string.Empty;
            return kindDisplayNames
                .FirstOrDefault(pair => string.Equals(pair.Value, displayName, StringComparison.OrdinalIgnoreCase))
                .Key;
        }

        private ExtraAudioRuleTarget GetSelectedTarget(ExtraAudioRuleKind kind)
        {
            string displayName = TargetDropdown.Text?.Trim() ?? string.Empty;
            KeyValuePair<ExtraAudioRuleTarget, string> match = targetDisplayNames
                .FirstOrDefault(pair => string.Equals(pair.Value, displayName, StringComparison.OrdinalIgnoreCase));
            return targetDisplayNames.ContainsKey(match.Key) ? match.Key : DefaultTargetForKind(kind);
        }

        private static ExtraAudioRuleTarget DefaultTargetForKind(ExtraAudioRuleKind kind)
        {
            return kind == ExtraAudioRuleKind.Sfx ? ExtraAudioRuleTarget.Sfx : ExtraAudioRuleTarget.Character;
        }

        private void RefreshVolumeText()
        {
            if (VolumeValueTextBox != null)
            {
                suppressVolumeTextEvents = true;
                VolumeValueTextBox.Text = currentVolumePercent.ToString(CultureInfo.InvariantCulture) + "%";
                suppressVolumeTextEvents = false;
            }
        }

        private void SetVolumePercent(int value, bool updateSlider, bool updateText)
        {
            currentVolumePercent = Math.Max(0, value);
            if (updateSlider && VolumeSlider != null)
            {
                VolumeSlider.Value = Math.Clamp(currentVolumePercent, 0, 200);
            }

            if (updateText)
            {
                RefreshVolumeText();
            }
        }

        private static bool TryParsePositivePercent(string? text, out int value)
        {
            string normalized = (text ?? string.Empty).Trim().TrimEnd('%').Trim();
            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && parsed >= 0)
            {
                value = parsed;
                return true;
            }

            value = 0;
            return false;
        }

        private static string BuildRuleName(
            ExtraAudioRuleKind kind,
            ExtraAudioRuleTarget target,
            string match,
            int volumePercent)
        {
            return $"{kind} rule: {target} {match} {volumePercent}%";
        }

        private void PopulateCharacterComboBox()
        {
            CharacterComboBox.Clear();
            foreach (CharacterSelectorOption option in characters)
            {
                CharacterComboBox.Add(option.Name, option.IconPath, option.Name);
            }
        }

        private static List<CharacterSelectorOption> BuildCharacterOptions()
        {
            return CharacterFolder.FullList
                .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                .Select(character => new CharacterSelectorOption(
                    character.Name,
                    character.configINI.ShowName,
                    character.DirectoryPath,
                    character.CharIconPath))
                .ToList();
        }

        private static List<string> BuildBlipDisplayNames(
            IEnumerable<string> tokens,
            Dictionary<string, string> tokenByDisplayName)
        {
            List<string> result = new List<string>();
            foreach (string token in tokens)
            {
                string normalizedToken = token?.Trim() ?? string.Empty;
                string display = Path.GetFileName(normalizedToken.Replace('\\', '/'));
                if (string.IsNullOrWhiteSpace(display))
                {
                    continue;
                }

                if (!tokenByDisplayName.ContainsKey(display))
                {
                    tokenByDisplayName[display] = normalizedToken;
                    result.Add(display);
                }
            }

            return result.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> BuildAudioTokenCatalog(string subFolder)
        {
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
                {
                    continue;
                }

                AddAudioTokensFromRoot(tokens, Path.Combine(baseFolder, "sounds", subFolder), string.Empty);

                if (string.Equals(subFolder, "music", StringComparison.OrdinalIgnoreCase))
                {
                    string charactersRoot = Path.Combine(baseFolder, "characters");
                    if (!Directory.Exists(charactersRoot))
                    {
                        continue;
                    }

                    foreach (string characterDirectory in Directory.EnumerateDirectories(charactersRoot))
                    {
                        string characterName = Path.GetFileName(characterDirectory);
                        AddAudioTokensFromRoot(
                            tokens,
                            Path.Combine(characterDirectory, "music"),
                            "../../characters/" + characterName + "/music");
                    }
                }
            }

            return tokens.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> BuildSfxTokenCatalog()
        {
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
                {
                    continue;
                }

                AddAudioTokensFromRoot(tokens, Path.Combine(baseFolder, "sounds", "general"), string.Empty);
                AddAudioTokensFromRoot(tokens, Path.Combine(baseFolder, "sounds"), string.Empty, excludeTopLevelDirectories: new[] { "music", "blips" });
                AddAudioTokensFromRoot(tokens, Path.Combine(baseFolder, "misc"), "../../misc");
                AddAudioTokensFromRoot(tokens, Path.Combine(baseFolder, "themes", "default", "misc"), "../../themes/default/misc");

                string charactersRoot = Path.Combine(baseFolder, "characters");
                if (!Directory.Exists(charactersRoot))
                {
                    continue;
                }

                foreach (string characterDirectory in Directory.EnumerateDirectories(charactersRoot))
                {
                    string characterName = Path.GetFileName(characterDirectory);
                    AddAudioTokensFromRoot(
                        tokens,
                        Path.Combine(characterDirectory, "sounds"),
                        "../../characters/" + characterName + "/sounds");
                    AddAudioTokensFromRoot(
                        tokens,
                        Path.Combine(characterDirectory, "general"),
                        "../../characters/" + characterName + "/general");
                }
            }

            return tokens.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddAudioTokensFromRoot(
            ISet<string> tokens,
            string root,
            string tokenPrefix,
            IReadOnlyCollection<string>? excludeTopLevelDirectories = null)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (string filePath in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedAudioFile))
            {
                string relative = Path.GetRelativePath(root, filePath).Replace('\\', '/');
                string topLevelDirectory = relative.Split('/').FirstOrDefault() ?? string.Empty;
                if (excludeTopLevelDirectories?.Contains(topLevelDirectory, StringComparer.OrdinalIgnoreCase) == true)
                {
                    continue;
                }

                string withoutExtension = Path.ChangeExtension(relative, null) ?? relative;
                string token = string.IsNullOrWhiteSpace(tokenPrefix)
                    ? withoutExtension
                    : tokenPrefix.TrimEnd('/') + "/" + withoutExtension;
                tokens.Add(token);
            }
        }

        private static bool IsSupportedAudioFile(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return extension.Equals(".opus", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveKnownOrFallback(string? preferredValue, IReadOnlyList<string> knownValues)
        {
            string preferred = preferredValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(preferred))
            {
                return knownValues.FirstOrDefault() ?? string.Empty;
            }

            return knownValues.FirstOrDefault(value => string.Equals(value, preferred, StringComparison.OrdinalIgnoreCase))
                ?? preferred;
        }

        private string ResolveBlipDisplayName(string? savedMatch)
        {
            string match = savedMatch?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(match))
            {
                return string.Empty;
            }

            if (blipTokenByDisplayName.ContainsKey(match))
            {
                return match;
            }

            string display = Path.GetFileName(match.Replace('\\', '/'));
            return blipTokenByDisplayName.ContainsKey(display) ? display : match;
        }

        private string? ResolveSelectedBlipPath()
        {
            string? token = ResolveSelectedBlipToken();
            return string.IsNullOrWhiteSpace(token) ? null : AO2ViewportAudioResolver.ResolveBlipPath(token);
        }

        private string? ResolveSelectedBlipToken()
        {
            string text = BlipDropdown.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return blipTokenByDisplayName.TryGetValue(text, out string? knownToken) ? knownToken : text;
        }

        private string? ResolveSelectedCharacterBlipToken()
        {
            string name = CharacterComboBox.SelectedText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            CharacterFolder? character = CharacterFolder.FullList.FirstOrDefault(folder =>
                string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(folder.DirectoryPath, name, StringComparison.OrdinalIgnoreCase));
            return AO2ViewportAssetResolver.ResolveCharacterBlipToken(character, null);
        }

        private string? ResolveSelectedCharacterName()
        {
            string name = CharacterComboBox.SelectedText?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        private string? ResolvePreviewBlipToken()
        {
            if (GetSelectedKind() != ExtraAudioRuleKind.Blip)
            {
                return null;
            }

            ExtraAudioRuleTarget target = GetSelectedTarget(ExtraAudioRuleKind.Blip);
            return target == ExtraAudioRuleTarget.Character
                ? ResolveSelectedCharacterBlipToken()
                : target == ExtraAudioRuleTarget.Blip
                    ? ResolveSelectedBlipToken()
                    : null;
        }

        private string? ResolvePreviewBlipPath()
        {
            string? token = ResolvePreviewBlipToken();
            return string.IsNullOrWhiteSpace(token) ? null : AO2ViewportAudioResolver.ResolveBlipPath(token);
        }

        private string? ResolvePreviewAudioPath()
        {
            ExtraAudioRuleKind kind = GetSelectedKind();
            if (kind == ExtraAudioRuleKind.Sfx)
            {
                ExtraAudioRuleTarget target = GetSelectedTarget(kind);
                return target == ExtraAudioRuleTarget.Sfx
                    ? AO2ViewportAudioResolver.ResolveSfxPath(SfxDropdown.Text?.Trim())
                    : null;
            }

            if (kind == ExtraAudioRuleKind.Music)
            {
                return AO2ViewportAudioResolver.ResolveMusicPath(MusicDropdown.Text?.Trim());
            }

            return ResolvePreviewBlipPath();
        }

        private void UpdateBlipPreviewVisibility()
        {
            if (BlipPreviewButton == null)
            {
                return;
            }

            string? path = ResolvePreviewAudioPath();
            BlipPreviewButton.Visibility = !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? Visibility.Visible
                : Visibility.Collapsed;
            BlipPreviewButton.ToolTipText = GetSelectedKind() == ExtraAudioRuleKind.Music
                ? "Play/stop resolved music."
                : GetSelectedKind() == ExtraAudioRuleKind.Sfx
                    ? "Preview resolved SFX."
                    : "Preview resolved blip.";
        }

        private void StopBlipPreview()
        {
            blipPreviewTimer?.Stop();
            blipPreviewTimer = null;
            blipPreviewState = null;
            blipPreviewPlayer.Stop();
            if (BlipPreviewButton != null)
            {
                BlipPreviewButton.IsPlaying = false;
            }
        }

        private void StartBlipPreviewTimer()
        {
            blipPreviewTimer?.Stop();
            blipPreviewTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
            blipPreviewTimer.Interval = TimeSpan.Zero;
            blipPreviewTimer.Tick += OnBlipPreviewTimerTick;
            blipPreviewTimer.Start();
        }

        private void OnBlipPreviewTimerTick(object? sender, EventArgs e)
        {
            if (blipPreviewState == null)
            {
                StopBlipPreview();
                return;
            }

            if (blipPreviewState.Position >= blipPreviewState.Text.Length)
            {
                StopBlipPreview();
                return;
            }

            int delay = AO2ViewportBlipPlaybackRules.GetNextDisplayedTextElement(
                blipPreviewState,
                out string textElement,
                out bool shouldPlayBlip,
                out _,
                out _,
                out int blipGateDelay);
            if (shouldPlayBlip
                && AO2ViewportBlipPlaybackRules.ShouldPlayBlipForTextElement(blipPreviewState, textElement, blipGateDelay))
            {
                _ = blipPreviewPlayer.PlayBlip();
            }

            if (blipPreviewState.Position >= blipPreviewState.Text.Length)
            {
                StopBlipPreview();
                return;
            }

            if (blipPreviewTimer == null)
            {
                return;
            }

            blipPreviewTimer.Stop();
            blipPreviewTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, delay));
            blipPreviewTimer.Start();
        }

        private double ResolvePreviewVolume()
        {
            ExtraAudioRuleKind kind = GetSelectedKind();
            ExtraAudioRuleTarget target = GetSelectedTarget(kind);
            string match = ResolveSelectedMatch(target);
            ExtraAudioRule previewRule = new ExtraAudioRule
            {
                Kind = kind,
                Target = target,
                Match = match,
                VolumePercent = Math.Max(0, currentVolumePercent),
                IsEnabled = true,
                IsCaseSensitive = target == ExtraAudioRuleTarget.Showname && ShownameCaseSensitiveCheckBox.IsChecked == true
            };
            if (previewRule.Kind == ExtraAudioRuleKind.Sfx)
            {
                string? sfxToken = target == ExtraAudioRuleTarget.Sfx ? SfxDropdown.Text?.Trim() : null;
                string? sfxCharacterName = target == ExtraAudioRuleTarget.Character ? ResolveSelectedCharacterName() : null;
                string? showname = target == ExtraAudioRuleTarget.Showname ? ShownameTextBox.Text?.Trim() : null;
                return AudioSettings.ResolveSfxVolume(sfxCharacterName, showname, null, sfxToken, new[] { previewRule });
            }

            if (previewRule.Kind == ExtraAudioRuleKind.Music)
            {
                return Math.Max(0, currentVolumePercent) / 100.0;
            }

            string? token = ResolvePreviewBlipToken();
            string? characterName = target == ExtraAudioRuleTarget.Character ? match : null;
            return AudioSettings.ResolveBlipVolume(characterName, null, null, token, new[] { previewRule });
        }

        private static double EstimatePreviewDurationMs(AO2ViewportBlipPlaybackRules.BlipCrawlState sourceState)
        {
            AO2ViewportBlipPlaybackRules.BlipCrawlState state = AO2ViewportBlipPlaybackRules.CreateState(
                sourceState.Text,
                sourceState.TextCrawlMilliseconds,
                sourceState.BlipRate,
                sourceState.BlankBlipEnabled,
                sourceState.MarkupStart,
                sourceState.MarkupEnd,
                sourceState.MarkupRemove);
            double total = 0;
            while (state.Position < state.Text.Length)
            {
                total += AO2ViewportBlipPlaybackRules.GetNextDisplayedTextElement(
                    state,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _);
            }

            return Math.Max(120, total);
        }

        private static bool CharacterOptionMatches(CharacterSelectorOption option, string? value)
        {
            string match = value?.Trim() ?? string.Empty;
            return string.Equals(option.Name, match, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.DirectoryPath, match, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.DisplayText, match, StringComparison.OrdinalIgnoreCase);
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
                IsEnabled = rule.IsEnabled,
                IsCaseSensitive = rule.IsCaseSensitive
            };
        }
    }
}
