using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Diagnostics;
using Path = System.IO.Path;
using System.ComponentModel;
using static OceanyaClient.Components.ImageComboBox;
using System.Xml.Linq;
using OceanyaClient.Utilities;
using System.Windows.Automation;
using OceanyaClient.Features.Viewport;

namespace OceanyaClient.Components
{
    /// <summary>
    /// Interaction logic for ICMessageSettings.xaml
    /// </summary>
    public partial class ICMessageSettings : UserControl
    {
        public static int ICShownameMaxLength = 22;
        public static int ICMessageMaxLength = 256;
        readonly List<Emote> emotes = new();
        bool suppressEmoteToggleEvents;
        AOClient? curClient;
        readonly Dictionary<AOClient, int> clientEmotePages = new();
        public bool stickyEffects;

        public Action<string>? OnSendICMessage;
        public Action<string>? OnRefreshCharacterRequested;
        public Action<string>? OnRefreshBackgroundRequested;
        public Action? OnRefreshAllAssetsRequested;
        public Action? OnRefreshAllCharactersRequested;
        public Action? OnNewCharacterFolderRequested;
        public Action<string>? OnOpenInCharacterEditorRequested;
        public Action<string>? OnDuplicateInCharacterEditorRequested;
        public Action<string>? OnOpenInCharacterEmoteVisualizerRequested;
        public Action<string>? OnOpenInCharacterFolderVisualizerRequested;
        public Func<string, string, Task>? OnDeleteCharacterFolderRequested;
        public Action<AOClient, string>? OnPositionConfirmed;
        public Action? OnClientStateChanged;
        public Func<IReadOnlyList<AOClient>>? PairingClientProvider;
        public Func<AOClient, AOClient?>? PairingNetworkClientProvider;
        private const string DefaultPositionDisplayPrefix = "default";

        public ICMessageSettings()
        {
            InitializeComponent();

            #region Emote Grid
            EmoteGrid.SetScrollMode(PageButtonGrid.ScrollMode.Horizontal);
            EmoteGrid.SetPageSize(2, 10);
            #endregion

            #region Char dropdown
            foreach (var ini in GetAlphabeticalCharacterFolders())
            {
                CharacterDropdown.Add(ini.Name, ini.CharIconPath);
            }
            CharacterDropdown.OnConfirm += CharacterDropdown_OnConfirm;
            CharacterDropdown.ContextMenu = BuildCharacterDropdownContextMenu();
            #endregion

            EmoteDropdown.OnConfirm += EmoteDropdown_OnConfirm;
            EmoteDropdown.SetComboBoxReadOnly(true);

            PositionDropdown.OnConfirm += PositionDropdown_OnConfirm;
            PositionDropdown.ContextMenu = BuildPositionDropdownContextMenu();

            foreach (var color in Enum.GetValues(typeof(ICMessage.TextColors)).Cast<ICMessage.TextColors>())
            {
                var colorsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Resources", "Colors");
                if (!Directory.Exists(colorsDirectory))
                {
                    Directory.CreateDirectory(colorsDirectory);
                }

                var filePath = Path.Combine(colorsDirectory, $"{color}.png");
                TextColorDropdown.Add(color.ToString(), filePath);
            }
            TextColorDropdown.OnConfirm += TextColorDropdown_OnConfirm;

            TextColorDropdown.SetComboBoxReadOnly(true);

            EffectDropdown.SetComboBoxReadOnly(true);
            foreach (var effect in Enum.GetValues(typeof(ICMessage.Effects)).Cast<ICMessage.Effects>())
            {
                var path = $"pack://application:,,,/Resources/Buttons/MessageEffects/{effect.ToString().ToLower()}.png";
                EffectDropdown.Add(effect.ToString(), effect == ICMessage.Effects.None ? "" : path);
            }
            EffectDropdown.OnConfirm += EffectDropdown_OnConfirm;

            sfxDropdown.SetImageFieldVisible(false);
            sfxDropdown.OnConfirm += SfxDropdown_OnConfirm;

            txtICShowname.MaxLength = ICShownameMaxLength;
            txtICMessage.MaxLength = ICMessageMaxLength;
        }

        private void SfxDropdown_OnConfirm(object? sender, string sfx)
        {
            txtICMessage.Focus();
            if (curClient == null) return;
            curClient.curSFX = sfx;
            OnClientStateChanged?.Invoke();
        }

        private static IEnumerable<CharacterFolder> GetAlphabeticalCharacterFolders()
        {
            return CharacterFolder.FullList
                .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(character => character.DirectoryPath, StringComparer.OrdinalIgnoreCase);
        }

        public void ReinitializeSettings()
        {
            // Reinitialize Character Dropdown
            CharacterDropdown.Clear();
            foreach (var ini in GetAlphabeticalCharacterFolders())
            {
                CharacterDropdown.Add(ini.Name, ini.CharIconPath);
            }

            // Reinitialize Emote Dropdown
            EmoteDropdown.Clear();

            // Reinitialize Text Color Dropdown
            TextColorDropdown.Clear();
            foreach (var color in Enum.GetValues(typeof(ICMessage.TextColors)).Cast<ICMessage.TextColors>())
            {
                var colorsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Resources", "Colors");
                if (!Directory.Exists(colorsDirectory))
                {
                    Directory.CreateDirectory(colorsDirectory);
                }

                var filePath = Path.Combine(colorsDirectory, $"{color}.png");
                TextColorDropdown.Add(color.ToString(), filePath);
            }

            // Reinitialize Effect Dropdown
            EffectDropdown.Clear();
            foreach (var effect in Enum.GetValues(typeof(ICMessage.Effects)).Cast<ICMessage.Effects>())
            {
                var path = $"pack://application:,,,/Resources/Buttons/MessageEffects/{effect.ToString().ToLower()}.png";
                EffectDropdown.Add(effect.ToString(), effect == ICMessage.Effects.None ? "" : path);
            }
        }

        private ContextMenu BuildCharacterDropdownContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu();
            contextMenu.Opened += (_, _) =>
            {
                contextMenu.Items.Clear();
                CharacterContextMenuBuilder.Populate(contextMenu, BuildCurrentCharacterContextMenuOptions());
            };

            return contextMenu;
        }

        private CharacterContextMenuOptions BuildCurrentCharacterContextMenuOptions()
        {
            string characterName = ResolveCurrentCharacterName();
            string characterDirectory = ResolveCurrentCharacterDirectory();
            string charIniPath = curClient?.currentINI?.PathToConfigIni?.Trim() ?? Path.Combine(characterDirectory, "char.ini");
            string readmePath = ResolveReadmePath(characterDirectory);
            return new CharacterContextMenuOptions
            {
                CharacterName = characterName,
                DirectoryPath = characterDirectory,
                CharIniPath = charIniPath,
                ReadmePath = readmePath,
                Owner = Window.GetWindow(this),
                HasReadme = !string.IsNullOrWhiteSpace(readmePath),
                RefreshCharacterAsync = () =>
                {
                    if (!string.IsNullOrWhiteSpace(characterName))
                    {
                        OnRefreshCharacterRequested?.Invoke(characterName);
                    }

                    return Task.CompletedTask;
                },
                RefreshAllAssetsAsync = () =>
                {
                    OnRefreshAllAssetsRequested?.Invoke();
                    return Task.CompletedTask;
                },
                RefreshAllCharactersAsync = () =>
                {
                    OnRefreshAllCharactersRequested?.Invoke();
                    return Task.CompletedTask;
                },
                NewCharacterFolderAsync = () =>
                {
                    OnNewCharacterFolderRequested?.Invoke();
                    return Task.CompletedTask;
                },
                EditCharacterFolderAsync = () =>
                {
                    if (!string.IsNullOrWhiteSpace(characterDirectory))
                    {
                        OnOpenInCharacterEditorRequested?.Invoke(characterDirectory);
                    }

                    return Task.CompletedTask;
                },
                DuplicateCharacterFolderAsync = () =>
                {
                    if (!string.IsNullOrWhiteSpace(characterDirectory))
                    {
                        OnDuplicateInCharacterEditorRequested?.Invoke(characterDirectory);
                    }

                    return Task.CompletedTask;
                },
                OpenCharacterEmoteVisualizer = () => OnOpenInCharacterEmoteVisualizerRequested?.Invoke(characterDirectory),
                OpenCharacterFolderVisualizer = () => OnOpenInCharacterFolderVisualizerRequested?.Invoke(characterDirectory),
                DeleteCharacterFolderAsync = OnDeleteCharacterFolderRequested == null
                    ? null
                    : () => OnDeleteCharacterFolderRequested(characterName, characterDirectory)
            };
        }

        private ContextMenu BuildPositionDropdownContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu();
            ContextMenuSectionHelper.AddHeader(contextMenu, "Background", addLeadingSeparator: false);

            MenuItem openExplorerItem = new MenuItem { Header = "Open in file explorer" };
            openExplorerItem.Click += (_, _) => OpenDirectory(ResolveCurrentBackgroundDirectory());
            contextMenu.Items.Add(openExplorerItem);

            MenuItem copyNameItem = new MenuItem { Header = "Copy name" };
            copyNameItem.Click += (_, _) =>
            {
                string backgroundName = ResolveCurrentBackgroundName();
                if (!string.IsNullOrWhiteSpace(backgroundName))
                {
                    ClipboardUtilities.TrySetText(backgroundName);
                }
            };
            contextMenu.Items.Add(copyNameItem);

            MenuItem refreshBackgroundItem = new MenuItem();
            refreshBackgroundItem.Click += (_, _) =>
            {
                string backgroundName = ResolveCurrentBackgroundName();
                if (!string.IsNullOrWhiteSpace(backgroundName))
                {
                    OnRefreshBackgroundRequested?.Invoke(backgroundName);
                }
            };
            contextMenu.Items.Add(refreshBackgroundItem);

            contextMenu.Opened += (_, _) =>
            {
                string backgroundName = ResolveCurrentBackgroundName();
                string backgroundDirectory = ResolveCurrentBackgroundDirectory();
                openExplorerItem.IsEnabled = Directory.Exists(backgroundDirectory);
                copyNameItem.IsEnabled = !string.IsNullOrWhiteSpace(backgroundName);
                refreshBackgroundItem.Header = string.IsNullOrWhiteSpace(backgroundName)
                    ? "Refresh background"
                    : "Refresh " + backgroundName;
                refreshBackgroundItem.IsEnabled = !string.IsNullOrWhiteSpace(backgroundName);
            };

            return contextMenu;
        }

        private string ResolveCurrentCharacterName()
        {
            string selectedText = CharacterDropdown.SelectedText?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                return selectedText;
            }

            if (!string.IsNullOrWhiteSpace(curClient?.currentINI?.Name))
            {
                return curClient.currentINI.Name;
            }

            return string.Empty;
        }

        private string ResolveCurrentCharacterDirectory()
        {
            return curClient?.currentINI?.DirectoryPath?.Trim() ?? string.Empty;
        }

        private static string ResolveReadmePath(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return string.Empty;
            }

            string[] candidates =
            {
                Path.Combine(directory, "readme.txt"),
                Path.Combine(directory, "README.txt"),
                Path.Combine(directory, "readme.md"),
                Path.Combine(directory, "README.md")
            };
            return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private string ResolveCurrentBackgroundName()
        {
            return curClient?.curBG?.Trim() ?? string.Empty;
        }

        private string ResolveCurrentBackgroundDirectory()
        {
            string backgroundName = ResolveCurrentBackgroundName();
            if (string.IsNullOrWhiteSpace(backgroundName))
            {
                return string.Empty;
            }

            return AOBot_Testing.Structures.Background.FromBGPath(backgroundName)?.PathToFile?.Trim() ?? string.Empty;
        }

        private static void OpenDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }

        private void EffectDropdown_OnConfirm(object? sender, string newEffect)
        {
            if (curClient == null) return;

            if (Enum.TryParse(newEffect, out ICMessage.Effects parsedEffect))
            {
                curClient.effect = parsedEffect;
                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
            else
            {
                // Handle the error if the color cannot be parsed
                CustomConsole.WriteLine($"Invalid color: {newEffect}");
            }
        }

        private void TextColorDropdown_OnConfirm(object? sender, string newColor)
        {
            if (curClient == null) return;

            if (Enum.TryParse(newColor, out ICMessage.TextColors parsedColor))
            {
                curClient.textColor = parsedColor;
                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
            else
            {
                // Handle the error if the color cannot be parsed
                CustomConsole.WriteLine($"Invalid color: {newColor}");
            }
        }

        private void PositionDropdown_OnConfirm(object? sender, string newPos)
        {
            if (curClient == null) return;

            txtICMessage.Focus();
            curClient.SetPos(newPos, true);
            OnPositionConfirmed?.Invoke(curClient, newPos);
            OnClientStateChanged?.Invoke();
        }

        private void EmoteDropdown_OnConfirm(object? sender, string emoteDisplayID)
        {
            Emote? emote = FindEmoteByDisplayId(emoteDisplayID);
            if (emote == null) return;
            EmoteGrid.SetPageToVirtualizedItem(item => item is Emote candidate
                && string.Equals(candidate.DisplayID, emote.DisplayID, StringComparison.OrdinalIgnoreCase));
            SelectEmote(emote, updateClient: true, focusMessageBox: true, notifyStateChanged: true);
            txtICMessage.Focus();
        }

        private void CharacterDropdown_OnConfirm(object? sender, string iniName)
        {
            if (curClient == null) return;

            var ini = CharacterFolder.FullList.FirstOrDefault(x => x.Name == iniName);
            if(ini != null)
            {
                txtICMessage.Focus();

                if (curClient.currentINI == ini) return;

                curClient.SetCharacter(ini);
                SetINI(ini);
                UpdatePosDropdown(curClient);
                OnClientStateChanged?.Invoke();
            }
            else
            {
                //handle error in customconsole.writeline method
                CustomConsole.WriteLine($"Character {iniName} not found.");

            }
        }

        public void SetClient(AOClient client)
        {
            if (client == null)
            {
                ClearSettings();
                return;
            }

            AOClient? previousClient = this.curClient;
            if (previousClient != null)
            {
                previousClient.OnSideChange -= UpdatePos;
                previousClient.OnBGChange -= EventHandler_ClientOnBgChange;
                clientEmotePages[previousClient] = EmoteGrid.GetCurrentPage();
            }

            this.curClient = client;
            CharacterFolder? iniToUse = client.currentINI;
            if (iniToUse == null && CharacterFolder.FullList.Any())
            {
                client.SetCharacter(CharacterFolder.FullList.First());
                iniToUse = client.currentINI;
            }

            if (iniToUse == null)
            {
                ClearSettings();
                txtICShowname_Placeholder.Text = "No character data loaded";
                return;
            }

            SetINI(iniToUse);
            if (clientEmotePages.TryGetValue(client, out int savedEmotePage))
                EmoteGrid.SetCurrentPage(savedEmotePage);

            txtICShowname.Text = client.ICShowname;

            chkPreanim.IsChecked = client.PreanimEnabled;
            chkFlip.IsChecked = client.flip;
            chkAdditive.IsChecked = client.Additive;
            chkImmediate.IsChecked = client.Immediate;

            CharacterDropdown.SelectedText = iniToUse.Name;
            TextColorDropdown.SelectedText = client.textColor.ToString();
            EffectDropdown.SelectedText = client.effect.ToString();

            //pos
            UpdatePosDropdown(client);
            curClient.OnBGChange += EventHandler_ClientOnBgChange;
            curClient.OnSideChange += UpdatePos;
        }

        private void EventHandler_ClientOnBgChange(string newBG)
        {
            if (this.curClient == null)
            {
                return;
            }

            UpdatePosDropdown(this.curClient);
        }

        private void UpdatePosDropdown(AOClient client)
        {
            PositionDropdown.Dispatcher.Invoke(() =>
            {
                PositionDropdown.Clear();
                var bg = AOBot_Testing.Structures.Background.FromBGPath(client.curBG);
                string defaultPos = client.currentINI?.configINI.Side?.Trim() ?? string.Empty;
                string defaultDisplay = BuildDefaultPositionDisplay(defaultPos);

                if(bg != null)
                {
                    IReadOnlyList<Background.PositionOption> allPos = bg.GetAo2PositionOptions();
                    string defaultImage = allPos.FirstOrDefault(pos =>
                        string.Equals(pos.Name, defaultPos, StringComparison.OrdinalIgnoreCase))?.ImagePath ?? string.Empty;
                    PositionDropdown.Add(defaultDisplay, defaultImage, string.Empty);

                    foreach (var pos in allPos)
                    {
                        PositionDropdown.Add(pos.Name, pos.ImagePath);
                    }

                    if (string.IsNullOrWhiteSpace(client.curPos))
                    {
                        PositionDropdown.SelectedText = defaultDisplay;
                    }
                    else if (allPos.Any(pos => string.Equals(pos.Name, client.curPos, StringComparison.OrdinalIgnoreCase)))
                    {
                        PositionDropdown.SelectedText = client.curPos;
                    }
                    else
                    {
                        PositionDropdown.SelectedText = client.curPos;
                    }
                }
                else
                {
                    PositionDropdown.Add(defaultDisplay, string.Empty, string.Empty);
                    PositionDropdown.SelectedText = string.IsNullOrWhiteSpace(client.curPos)
                        ? defaultDisplay
                        : client.curPos;
                }
            });
        }
        private void UpdatePos(string newPos)
        {
            if (curClient == null)
            {
                return;
            }

            PositionDropdown.Dispatcher.Invoke(() =>
            {
                curClient.OnSideChange -= UpdatePos;
                PositionDropdown.SelectedText = string.IsNullOrWhiteSpace(newPos)
                    ? BuildDefaultPositionDisplay(curClient.currentINI?.configINI.Side?.Trim() ?? string.Empty)
                    : newPos;
                curClient.OnSideChange += UpdatePos;
            });
        }

        private static string BuildDefaultPositionDisplay(string defaultPos)
        {
            string cleanDefaultPos = defaultPos?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(cleanDefaultPos)
                ? DefaultPositionDisplayPrefix
                : $"{DefaultPositionDisplayPrefix} ({cleanDefaultPos})";
        }
        private void SetINI(CharacterFolder ini)
        {
            if (ini == null || curClient == null)
            {
                return;
            }

            try
            {
                txtICShowname_Placeholder.Text = ini.configINI.ShowName;

                EmoteGrid.ClearGrid();
                emotes.Clear();
                EmoteDropdown.Clear();
                emotes.AddRange(ini.configINI.Emotions.Values);

                string? selectedDisplayId = curClient.currentEmote?.DisplayID;
                Emote? selectedEmote = string.IsNullOrWhiteSpace(selectedDisplayId)
                    ? null
                    : FindEmoteByDisplayId(selectedDisplayId);

                if (emotes.Count == 0)
                {
                    CustomConsole.Warning(
                        $"No emotes were loaded for INI '{ini?.Name ?? "unknown"}'. " +
                        "Using fallback synthetic emote to keep UI functional.");

                    var syntheticFallbackEmote = new Emote(1)
                    {
                        Name = "normal",
                        PreAnimation = "-",
                        Animation = "normal",
                        Modifier = ICMessage.EmoteModifiers.NoPreanimation,
                        DeskMod = ICMessage.DeskMods.Chat
                    };

                    emotes.Add(syntheticFallbackEmote);
                    selectedEmote = syntheticFallbackEmote;
                }

                selectedEmote ??= emotes.FirstOrDefault();
                EmoteDropdown.SetItems(emotes.Select(emote => new DropdownItem
                {
                    Name = emote.DisplayID,
                    ImagePath = emote.PathToImage_off,
                    Value = emote.DisplayID
                }));
                EmoteGrid.SetVirtualizedItems(emotes, CreateEmoteButton);
                if (selectedEmote != null)
                {
                    SelectEmote(selectedEmote, updateClient: !IsSelectedEmote(selectedEmote), focusMessageBox: false, notifyStateChanged: false);
                    EmoteGrid.SetPageToVirtualizedItem(item => ReferenceEquals(item, selectedEmote));
                }

                sfxDropdown.Clear();
                sfxDropdown.Add("Default", "");
                sfxDropdown.Add("Nothing", "");

                IReadOnlyList<AO2SoundListEntry> soundListEntries = AO2SoundList.LoadEntries(
                    ini?.DirectoryPath ?? string.Empty,
                    Globals.BaseFolders);
                foreach (AO2SoundListEntry soundListEntry in soundListEntries)
                {
                    sfxDropdown.Add(soundListEntry.DisplayText, "", soundListEntry.Value);
                }
                sfxDropdown.SelectedText = "Default";
            }
            catch (Exception ex)
            {
                var context = new Dictionary<string, string>
                {
                    { "Method", "ICMessageSettings.SetINI" },
                    { "IniName", ini?.Name ?? "null" },
                    { "IniPath", ini?.PathToConfigIni ?? "null" },
                    { "SoundListPath", ini?.SoundListPath ?? "null" },
                    { "CurrentClientNull", (curClient == null).ToString() },
                    { "CurrentClientName", curClient?.clientName ?? "null" },
                    { "CurrentINI", curClient?.currentINI?.Name ?? "null" },
                    { "CurrentEmote", curClient?.currentEmote?.DisplayID ?? "null" },
                    { "EmotionsCount", ini?.configINI?.Emotions?.Count.ToString() ?? "null" }
                };

                CrashLogger.LogUnhandledException(ex, "SetINI", isTerminating: false, additionalContext: context);
                throw;
            }
        }
        private ToggleButton CreateEmoteButton(Emote emote)
        {
            string buttonOff = emote.PathToImage_off;
            string buttonOn = emote.PathToImage_on;
            ToggleButton toggleBtn = new ToggleButton
            {
                Width = 40,
                Height = 40,
                ToolTip = emote.DisplayID,
                Focusable = false,
                IsTabStop = false,
                Tag = emote,
                IsChecked = IsSelectedEmote(emote)
            };
            AutomationProperties.SetAutomationId(toggleBtn, "Main.Ic.EmoteGrid." + SanitizeAutomationSegment(emote.DisplayID));
            AutomationProperties.SetName(toggleBtn, emote.DisplayID);
            toggleBtn.Checked += EmoteToggleBtn_Checked;
            toggleBtn.Unchecked += EmoteToggleBtn_Unchecked;

            bool offExists = System.IO.File.Exists(buttonOff);
            bool onExists = System.IO.File.Exists(buttonOn);

            if (offExists || onExists)
            {
                // Create the ControlTemplate dynamically
                ControlTemplate template = new ControlTemplate(typeof(ToggleButton));
                FrameworkElementFactory gridFactory = new FrameworkElementFactory(typeof(Grid));
                FrameworkElementFactory imageFactory = new FrameworkElementFactory(typeof(Image));
                imageFactory.Name = "ButtonImage";
                imageFactory.SetValue(Image.WidthProperty, 40.0);
                imageFactory.SetValue(Image.HeightProperty, 40.0);

                BitmapImage offImage;
                BitmapImage onImage;

                if (offExists && onExists)
                {
                    // Both images exist, use them as is
                    offImage = BitmapFileLoader.LoadFrozen(buttonOff);
                    onImage = BitmapFileLoader.LoadFrozen(buttonOn);
                }
                else if (offExists)
                {
                    // Only off image exists
                    BitmapImage existingImage = BitmapFileLoader.LoadFrozen(buttonOff);
                    // Create darkened version for on state
                    onImage = CreateDarkenedImage(buttonOff);
                   
                    // Use existing image as the off state
                    offImage = existingImage; 
                }
                else // onExists
                {
                    // Only on image exists
                    BitmapImage existingImage = BitmapFileLoader.LoadFrozen(buttonOn);
                    // Use existing image as the on state
                    onImage = existingImage;
                    // Create darkened version for off state
                    offImage = CreateDarkenedImage(buttonOn);
                }

                // Set default (off) state image
                imageFactory.SetValue(Image.SourceProperty, offImage);

                gridFactory.AppendChild(imageFactory);
                template.VisualTree = gridFactory;

                // Add the trigger for toggled state
                Trigger trigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                trigger.Setters.Add(new Setter
                {
                    Property = Image.SourceProperty,
                    TargetName = "ButtonImage",
                    Value = onImage
                });
                template.Triggers.Add(trigger);
                toggleBtn.Template = template;
            }
            else
            {
                // No image exists, use a default button with text
                toggleBtn.Content = emote.DisplayID;
            }

            toggleBtn.ContextMenu = BuildEmoteButtonContextMenu(emote);
            return toggleBtn;
        }

        private ContextMenu BuildEmoteButtonContextMenu(Emote emote)
        {
            ContextMenu menu = new ContextMenu();
            ContextMenuSectionHelper.AddHeader(menu, "Sprite preview", addLeadingSeparator: false);
            bool hasPreAnim = !string.IsNullOrWhiteSpace(emote.PreAnimation)
                && emote.PreAnimation.Trim() != "-";

            MenuItem preAnimItem = new MenuItem { Header = "Preview Pre-animation" };
            preAnimItem.IsEnabled = hasPreAnim;
            preAnimItem.Click += (_, _) => OpenEmoteSpritePreview(
                $"Pre-animation: {emote.PreAnimation}",
                ResolveEmoteSpritePath(EmoteSpriteKind.PreAnimation, emote));
            menu.Items.Add(preAnimItem);

            MenuItem idleItem = new MenuItem { Header = "Preview Idle" };
            idleItem.Click += (_, _) => OpenEmoteSpritePreview(
                $"Idle: {emote.Animation}",
                ResolveEmoteSpritePath(EmoteSpriteKind.Idle, emote));
            menu.Items.Add(idleItem);

            MenuItem talkItem = new MenuItem { Header = "Preview Talk" };
            talkItem.Click += (_, _) => OpenEmoteSpritePreview(
                $"Talk: {emote.Animation}",
                ResolveEmoteSpritePath(EmoteSpriteKind.Talk, emote));
            menu.Items.Add(talkItem);

            ContextMenuSectionHelper.AddHeader(menu, "Viewport", addLeadingSeparator: true);
            MenuItem viewportItem = new MenuItem { Header = "Preview in Viewport" };
            viewportItem.Click += (_, _) => OpenEmoteViewportPreview(emote);
            menu.Items.Add(viewportItem);

            return menu;
        }

        private enum EmoteSpriteKind { PreAnimation, Idle, Talk }

        private string? ResolveEmoteSpritePath(EmoteSpriteKind kind, Emote emote)
        {
            CharacterFolder? character = curClient?.currentINI;
            if (character == null) return null;
            return kind switch
            {
                EmoteSpriteKind.PreAnimation => AO2ViewportAssetResolver.ResolveCharacterPreAnimation(character, emote.PreAnimation),
                EmoteSpriteKind.Idle => AO2ViewportAssetResolver.ResolveCharacterDialogAnimation(character, emote.DisplayID, talking: false),
                EmoteSpriteKind.Talk => AO2ViewportAssetResolver.ResolveCharacterDialogAnimation(character, emote.DisplayID, talking: true),
                _ => null
            };
        }

        private void OpenEmoteSpritePreview(string title, string? assetPath)
        {
            Window? ownerWindow = Window.GetWindow(this);
            var entry = new AssetImageViewerDialog.AssetEntry(
                AbsolutePath: assetPath,
                Label: System.IO.Path.GetFileName(assetPath ?? title),
                MetaText: assetPath ?? "(not resolved)");
            AssetImageViewerDialog.Show(ownerWindow, new[] { entry });
        }

        private void OpenEmoteViewportPreview(Emote emote)
        {
            if (curClient?.currentINI == null) return;
            AOClient client = curClient;

            ICMessage previewMsg = BuildPreviewICMessage(emote, client);

            Window? ownerWindow = Window.GetWindow(this);

            AO2ViewportControl viewport = new AO2ViewportControl();

            AOClient syntheticClient = new AOClient("ws://localhost:1");
            syntheticClient.curBG = client.curBG;
            syntheticClient.curPos = client.curPos;
            viewport.AttachClient(syntheticClient, null, null, null);

            Button replayButton = new Button
            {
                Content = "▶ Replay",
                Width = 90,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 6, 0, 6)
            };

            TextBlock infoText = new TextBlock
            {
                Text = $"{client.currentINI.Name}  ·  {emote.DisplayID}",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 213, 226)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Border previewBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(160, 70, 20)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 2, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = "PREVIEW",
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                }
            };

            DockPanel toolbar = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(replayButton, Dock.Left);
            DockPanel.SetDock(previewBadge, Dock.Right);
            toolbar.Children.Add(replayButton);
            toolbar.Children.Add(previewBadge);
            toolbar.Children.Add(infoText);

            Viewbox viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = viewport
            };

            Grid content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(viewbox, 0);
            Grid.SetRow(toolbar, 1);
            content.Children.Add(viewbox);
            content.Children.Add(toolbar);

            string emoteName = string.IsNullOrWhiteSpace(emote.Name) ? emote.DisplayID : emote.Name;
            GenericOceanyaWindow dialog = new GenericOceanyaWindow
            {
                Owner = ownerWindow,
                Title = $"Emote Preview — {emoteName}",
                HeaderText = $"Emote Preview — {emoteName}",
                Width = 540,
                Height = 660,
                MinWidth = 320,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                IsUserResizeEnabled = true,
                IsUserMoveEnabled = true,
                IsCloseButtonVisible = true,
                BodyMargin = new Thickness(0),
                BodyContent = content
            };

            replayButton.Click += (_, _) => viewport.PreviewMessage(previewMsg);
            dialog.Loaded += (_, _) => viewport.PreviewMessage(previewMsg);
            dialog.Show();
        }

        internal static ICMessage BuildPreviewICMessage(Emote emote, AOClient client)
        {
            string side = !string.IsNullOrWhiteSpace(client.curPos)
                ? client.curPos
                : client.currentINI?.configINI?.Side ?? "def";

            string sfxName = client.PreanimEnabled && !string.IsNullOrWhiteSpace(emote.sfxName)
                ? emote.sfxName
                : "1";

            return new ICMessage
            {
                DeskMod = emote.DeskMod,
                PreAnim = emote.PreAnimation,
                Character = client.currentINI!.Name,
                Emote = emote.Animation,
                Message = "This is a preview",
                Side = side,
                SfxName = sfxName,
                EmoteModifier = ResolvePreviewEmoteModifier(emote.Modifier, client.PreanimEnabled, client.Immediate),
                SfxDelay = emote.sfxDelay,
                ShoutModifier = ICMessage.ShoutModifiers.Nothing,
                Flip = client.flip,
                Realization = false,
                TextColor = ICMessage.TextColors.White,
                ShowName = "",
                CharId = 0,
                EvidenceID = "0",
                OtherCharId = -1,
                SelfOffset = client.SelfOffset,
                NonInterruptingPreAnim = client.PreanimEnabled && client.Immediate,
                SfxLooping = false,
                ScreenShake = false,
                FramesShake = $"{emote.PreAnimation}^(b){emote.Animation}^(a){emote.Animation}^",
                FramesRealization = $"{emote.PreAnimation}^(b){emote.Animation}^(a){emote.Animation}^",
                FramesSfx = $"{emote.PreAnimation}^(b){emote.Animation}^(a){emote.Animation}^",
                Additive = false,
                Effect = ICMessage.Effects.None,
                Blips = "",
                Slide = false
            };
        }

        internal static ICMessage.EmoteModifiers ResolvePreviewEmoteModifier(
            ICMessage.EmoteModifiers baseModifier, bool preanimEnabled, bool immediate)
        {
            ICMessage.EmoteModifiers resolved = baseModifier;

            if (resolved == ICMessage.EmoteModifiers.PlayPreanimationAndObjection)
                resolved = ICMessage.EmoteModifiers.PlayPreanimation;
            else if (resolved == ICMessage.EmoteModifiers.Unused3)
                resolved = ICMessage.EmoteModifiers.NoPreanimation;
            else if (resolved == ICMessage.EmoteModifiers.Unused4)
                resolved = ICMessage.EmoteModifiers.NoPreanimationAndZoom;

            if (preanimEnabled && !immediate)
            {
                if (resolved == ICMessage.EmoteModifiers.NoPreanimation)
                    resolved = ICMessage.EmoteModifiers.PlayPreanimation;
                else if (resolved == ICMessage.EmoteModifiers.NoPreanimationAndZoom)
                    resolved = ICMessage.EmoteModifiers.ObjectionAndZoomNoPreanim;
            }
            else
            {
                if (resolved == ICMessage.EmoteModifiers.PlayPreanimation)
                    resolved = ICMessage.EmoteModifiers.NoPreanimation;
                else if (resolved == ICMessage.EmoteModifiers.ObjectionAndZoomNoPreanim)
                    resolved = ICMessage.EmoteModifiers.NoPreanimationAndZoom;
            }

            return resolved;
        }

        private static string SanitizeAutomationSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Empty";
            }

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Creates a darkened version of the image at the specified path
        /// </summary>
        private BitmapImage CreateDarkenedImage(string imagePath)
        {
            // Load the original image
            BitmapImage originalImage = BitmapFileLoader.LoadFrozen(imagePath);

            // Create a writable bitmap to manipulate the pixels
            WriteableBitmap writableBmp = new WriteableBitmap(originalImage);

            // Create an array to hold the pixel data
            int width = writableBmp.PixelWidth;
            int height = writableBmp.PixelHeight;
            int stride = width * 4; // 4 bytes per pixel (BGRA)
            byte[] pixels = new byte[height * stride];

            // Copy the pixel data
            writableBmp.CopyPixels(pixels, stride, 0);

            // Darken each pixel (reduce brightness by 30%)
            float darkenFactor = 0.7f;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                // BGRA format
                byte blue = pixels[i];
                byte green = pixels[i + 1];
                byte red = pixels[i + 2];
                // Alpha channel at i+3 remains unchanged

                // Darken each color component
                pixels[i] = (byte)(blue * darkenFactor);
                pixels[i + 1] = (byte)(green * darkenFactor);
                pixels[i + 2] = (byte)(red * darkenFactor);
            }

            // Create a new WriteableBitmap for the darkened image
            WriteableBitmap darkenedBmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            darkenedBmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

            // Convert back to BitmapImage for use in the UI
            BitmapImage result = new BitmapImage();
            using (MemoryStream stream = new MemoryStream())
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(darkenedBmp));
                encoder.Save(stream);
                stream.Position = 0;

                result.BeginInit();
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze(); // Make it thread-safe
            }

            return result;
        }

        public void ClearSettings()
        {
            ReinitializeSettings();
            CharacterDropdown.SelectedText = string.Empty;
            EffectDropdown.SelectedText = string.Empty;
            EmoteDropdown.SelectedText = string.Empty;
            PositionDropdown.SelectedText = string.Empty;
            TextColorDropdown.SelectedText = string.Empty;
            sfxDropdown.SelectedText = string.Empty;
            txtICShowname.Clear();
            txtICMessage.Clear();
            EmoteGrid.ClearGrid();
            emotes.Clear();
        }

        private void EmoteToggleBtn_Checked(object sender, RoutedEventArgs e)
        {
            if (suppressEmoteToggleEvents)
            {
                return;
            }

            ToggleButton? clickedButton = sender as ToggleButton;
            if (clickedButton?.Tag is not Emote emote || curClient == null)
            {
                return;
            }

            SelectEmote(emote, updateClient: true, focusMessageBox: true, notifyStateChanged: true);
        }
        private void EmoteToggleBtn_Unchecked(object sender, RoutedEventArgs e)
        {
            if (suppressEmoteToggleEvents)
            {
                return;
            }

            ToggleButton? clickedButton = sender as ToggleButton;
            if (clickedButton?.Tag is not Emote emote)
            {
                return;
            }

            if (clickedButton.IsChecked == false && IsSelectedEmote(emote))
            {
                chkPreanim.IsChecked = !chkPreanim.IsChecked;

                clickedButton.Checked -= EmoteToggleBtn_Checked;
                clickedButton.IsChecked = true;
                clickedButton.Checked += EmoteToggleBtn_Checked;
            }
        }

        private Emote? FindEmoteByDisplayId(string? displayId)
        {
            return emotes.FirstOrDefault(emote =>
                string.Equals(emote.DisplayID, displayId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSelectedEmote(Emote emote)
        {
            return string.Equals(
                curClient?.currentEmote?.DisplayID,
                emote.DisplayID,
                StringComparison.OrdinalIgnoreCase);
        }

        private void SelectEmote(Emote emote, bool updateClient, bool focusMessageBox, bool notifyStateChanged)
        {
            if (curClient == null)
            {
                return;
            }

            suppressEmoteToggleEvents = true;
            try
            {
                if (updateClient)
                {
                    curClient.SetEmote(emote.DisplayID);
                }

                EmoteDropdown.SelectedText = emote.DisplayID;
                chkPreanim.IsChecked = emote.Modifier == ICMessage.EmoteModifiers.PlayPreanimation
                    || emote.Modifier == ICMessage.EmoteModifiers.PlayPreanimationAndObjection;
                EmoteGrid.SetVirtualizedItems(emotes, CreateEmoteButton);
            }
            finally
            {
                suppressEmoteToggleEvents = false;
            }

            if (focusMessageBox)
            {
                txtICMessage.Focus();
            }

            if (notifyStateChanged)
            {
                OnClientStateChanged?.Invoke();
            }
        }
        private void txtICMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true; // Prevents the beep sound from default Enter behavior
                string message = txtICMessage.Text;
                OnSendICMessage?.Invoke(message);
            }
        }

        public Action? OnResetMessageEffects;
        public void ResetMessageEffects()
        {
            btnRealization.IsChecked = false;
            btnScreenshake.IsChecked = false;
            EffectDropdown.SelectedText = ICMessage.Effects.None.ToString();
            if (curClient != null) curClient.effect = ICMessage.Effects.None;
            chkPreanim.IsChecked = false;
            sfxDropdown.SelectedText = "Default";
            if (curClient != null) curClient.curSFX = string.Empty;

            OnResetMessageEffects?.Invoke();
        }

        private void txtICShowname_TextChanged(object sender, TextChangedEventArgs e)
        {
            txtICShowname_Placeholder.Visibility = string.IsNullOrWhiteSpace(txtICShowname.Text) ? Visibility.Visible : Visibility.Collapsed;

            curClient?.SetICShowname(txtICShowname.Text.Trim());
            OnClientStateChanged?.Invoke();
        }

        private void txtICMessage_TextChanged(object sender, TextChangedEventArgs e)
        {
            txtICMessage_Placeholder.Visibility = string.IsNullOrWhiteSpace(txtICMessage.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void chkPreanim_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                // Assuming 'currentClient' is an instance of AOBot
                if (curClient == null) return;
                curClient.PreanimEnabled = checkBox.IsChecked == true;
                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
        }

        private void chkFlip_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                if (curClient == null) return;
                curClient.flip = checkBox.IsChecked == true;
                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
        }

        private void chkAdditive_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                if (curClient == null) return;
                curClient.Additive = checkBox.IsChecked == true;
                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
        }

        private void chkImmediate_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                if (curClient == null) return;
                curClient.Immediate = checkBox.IsChecked == true;
                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
        }

        private void btnRealization_Checked(object sender, RoutedEventArgs e)
        {
            // Handle the checked state
            if (sender is ToggleButton toggleButton)
            {
                EffectDropdown.SelectedText = ICMessage.Effects.Realization.ToString();
                if (curClient != null) curClient.effect = ICMessage.Effects.Realization;
                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
        }

        private void btnRealization_Unchecked(object sender, RoutedEventArgs e)
        {
            // Handle the unchecked state
            if (sender is ToggleButton toggleButton)
            {
                if(EffectDropdown.SelectedText == ICMessage.Effects.Realization.ToString())
                    EffectDropdown.SelectedText = ICMessage.Effects.None.ToString();

                if (curClient != null && Enum.TryParse(EffectDropdown.SelectedText, out ICMessage.Effects parsedEffect))
                    curClient.effect = parsedEffect;

                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
        }

        private void btnScreenshake_Checked(object sender, RoutedEventArgs e)
        {
            // Handle the checked state
            if (sender is ToggleButton toggleButton)
            {
                if (curClient == null) return;
                curClient.screenshake = true;
                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
        }

        private void btnScreenshake_Unchecked(object sender, RoutedEventArgs e)
        {
            // Handle the unchecked state
            if (sender is ToggleButton toggleButton)
            {
                if (curClient == null) return;
                curClient.screenshake = false;
                txtICMessage.Focus();
                OnClientStateChanged?.Invoke();
            }
        }

        private void btnOffset_Click(object sender, RoutedEventArgs e)
        {
            if (curClient == null || curClient.currentINI == null)
            {
                txtICMessage.Focus();
                return;
            }

            (int Horizontal, int Vertical)? result = CharacterOffsetEditorWindow.ShowDialog(Window.GetWindow(this), curClient);
            if (result.HasValue)
            {
                curClient.SelfOffset = result.Value;
                OnClientStateChanged?.Invoke();
            }

            txtICMessage.Focus();
        }

        private void btnPairingStudio_Click(object sender, RoutedEventArgs e)
        {
            if (curClient == null || curClient.currentINI == null)
            {
                txtICMessage.Focus();
                return;
            }

            CharacterPairingStudioWindow.PairingStudioResult? result =
                CharacterPairingStudioWindow.ShowDialog(
                    Window.GetWindow(this),
                    curClient,
                    PairingNetworkClientProvider?.Invoke(curClient) ?? curClient,
                    PairingClientProvider?.Invoke());
            if (result != null)
            {
                curClient.PairTargetCharId = result.TargetCharId;
                curClient.PairTargetCharacterName = result.TargetCharacterName;
                curClient.PairLayerOrder = Math.Clamp(result.LayerOrder, 0, 1);
                curClient.SelfOffset = result.SelfOffset;
                OnClientStateChanged?.Invoke();
            }

            txtICMessage.Focus();
        }
    }
}
