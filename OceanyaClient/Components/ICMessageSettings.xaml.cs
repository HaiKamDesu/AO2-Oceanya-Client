using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using Common.WebAssets;
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
using OceanyaClient.Features.WebAssets;
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

        /// <summary>Debounce for rebuilding the emote grid after streamed button art lands.</summary>
        private const int EmoteArtRefreshDebounceMilliseconds = 400;

        private System.Windows.Threading.DispatcherTimer? emoteArtRefreshTimer;

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
            StartupTimingLogger.Log("ic_settings_ctor_begin");
            InitializeComponent();
            StartupTimingLogger.Log("ic_settings_initializecomponent_end");

            WebAssetService.AnyMaterialized += OnWebAssetMaterialized;
            CharacterDropdown.VisibleItemsChanged += (_, _) => WarmVisibleDropdownIcons();
            Unloaded += (_, _) =>
            {
                WebAssetService.AnyMaterialized -= OnWebAssetMaterialized;
                emoteArtRefreshTimer?.Stop();
                emoteArtRefreshTimer = null;
            };

            #region Emote Grid
            EmoteGrid.SetScrollMode(PageButtonGrid.ScrollMode.Horizontal);
            EmoteGrid.SetPageSize(2, 10);
            #endregion

            #region Char dropdown
            var alphabeticalCharacters = GetAlphabeticalCharacterFolders().ToList();
            StartupTimingLogger.Log("ic_settings_character_fulllist_touched",
                $"cacheHit={CharacterFolder.LastFullListWasCacheHit}, loadMs={CharacterFolder.LastFullListLoadMs}, "
                + $"fileBytes={CharacterFolder.LastFullListCacheFileBytes}, fileReadMs={CharacterFolder.LastFullListFileReadMs}, "
                + $"deserializeMs={CharacterFolder.LastFullListDeserializeMs}, "
                + $"compatibilityCheckMs={CharacterFolder.LastFullListCompatibilityCheckMs}, "
                + $"count={CharacterFolder.LastFullListLoadCount}");
            foreach (var ini in alphabeticalCharacters)
            {
                CharacterDropdown.Add(ini.Name, WebCharacterIconResolver.ResolveCharacterIcon(ini.Name, ini.CharIconPath));
            }
            CharacterDropdown.OnConfirm += CharacterDropdown_OnConfirm;
            CharacterDropdown.ContextMenu = BuildCharacterDropdownContextMenu();
            StartupTimingLogger.Log("ic_settings_character_dropdown_populated",
                $"count={alphabeticalCharacters.Count}");
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
            StartupTimingLogger.Log("ic_settings_ctor_end");
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

        /// <summary>
        /// Names the connected server offers that the user does not have installed, kept so the
        /// character dropdown can list them alongside local characters.
        /// </summary>
        /// <remarks>
        /// Session-scoped: cleared on disconnect and never persisted, so these entries only ever appear
        /// while connected to the server that actually provides them.
        /// </remarks>
        private readonly List<string> serverOnlyCharacterNames = new List<string>();

        /// <summary>
        /// Adds the connected server's characters to the character dropdown so a GM can iniswap to
        /// anything the server offers, not just what is installed locally.
        /// </summary>
        /// <param name="serverCharacterNames">The server's <c>SC#</c> roster.</param>
        public void SetServerCharacterRoster(IReadOnlyCollection<string>? serverCharacterNames)
        {
            serverOnlyCharacterNames.Clear();
            warmedDropdownIcons.Clear();

            if (serverCharacterNames != null && WebAssetService.IsActive)
            {
                HashSet<string> local = new HashSet<string>(
                    CharacterFolder.FullList.Select(folder => folder.Name),
                    StringComparer.OrdinalIgnoreCase);

                foreach (string name in serverCharacterNames)
                {
                    string trimmed = (name ?? string.Empty).Trim();
                    if (trimmed.Length > 0 && !local.Contains(trimmed))
                    {
                        serverOnlyCharacterNames.Add(trimmed);
                    }
                }
            }

            RepopulateCharacterDropdown();
        }

        /// <summary>
        /// Rebuilds the character dropdown from local characters plus any server-only ones.
        /// </summary>
        private void RepopulateCharacterDropdown()
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            List<ImageComboBox.DropdownItem> items = new List<ImageComboBox.DropdownItem>();

            foreach (CharacterFolder ini in GetAlphabeticalCharacterFolders())
            {
                items.Add(new ImageComboBox.DropdownItem
                {
                    Name = ini.Name,
                    ImagePath = WebCharacterIconResolver.ResolveCharacterIcon(ini.Name, ini.CharIconPath) ?? string.Empty,
                    Value = ini.Name
                });
            }

            foreach (string name in serverOnlyCharacterNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                // Icon path may be empty until the icon downloads; the dropdown tolerates that and the
                // materialize handler repopulates once art lands.
                items.Add(new ImageComboBox.DropdownItem
                {
                    Name = name,
                    ImagePath = WebCharacterIconResolver.ResolveCharacterIcon(name, bakedIconPath: null) ?? string.Empty,
                    Value = name
                });
            }

            CharacterDropdown.SetItems(items);

            CustomConsole.Info(
                $"[CHARDROPDOWN-TIMING] rebuild local={items.Count - serverOnlyCharacterNames.Count} "
                + $"serverOnly={serverOnlyCharacterNames.Count} total={items.Count} "
                + $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:0.0}",
                CustomConsole.LogCategory.Viewport);
        }

        /// <summary>Server-only characters whose icon has already been requested this session.</summary>
        private readonly HashSet<string> warmedDropdownIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Requests icons only for the dropdown rows currently on screen.
        /// </summary>
        /// <remarks>
        /// Scoped to the visible window for the same reason the character selector is: a roster can run
        /// to several thousand entries, and firing that many requests at once is what saturated the
        /// fetch pipeline in earlier testing. Scrolling warms what the user is actually looking at.
        /// </remarks>
        private void WarmVisibleDropdownIcons()
        {
            if (!WebAssetService.IsActive || serverOnlyCharacterNames.Count == 0)
            {
                return;
            }

            IReadOnlyList<ImageComboBox.DropdownItem> visible = CharacterDropdown.GetVisibleItems();
            if (visible.Count == 0)
            {
                return;
            }

            HashSet<string> serverOnly = new HashSet<string>(serverOnlyCharacterNames, StringComparer.OrdinalIgnoreCase);
            int requested = 0;
            foreach (ImageComboBox.DropdownItem item in visible)
            {
                string name = item.Name ?? string.Empty;
                if (name.Length == 0 || !serverOnly.Contains(name) || !warmedDropdownIcons.Add(name))
                {
                    continue;
                }

                string stem = "characters/" + WebAssetSource.NormalizeVPath(name) + "/char_icon";
                if (WebAssetService.FindInMirrorIfActive(stem, WebAssetKind.CharacterIcon) != null)
                {
                    continue;
                }

                WebAssetService.PrefetchIfActive(stem, WebAssetKind.CharacterIcon);
                requested++;
            }

            if (requested > 0)
            {
                CustomConsole.Debug(
                    $"[CHARDROPDOWN-TIMING] warmed {requested} visible server-only icons "
                    + $"(visible={visible.Count}, warmedTotal={warmedDropdownIcons.Count}/{serverOnlyCharacterNames.Count})",
                    CustomConsole.LogCategory.WebAssets);
            }
        }

        public void ReinitializeSettings()
        {
            // Reinitialize Character Dropdown. Goes through the shared rebuild so server-only entries
            // survive an asset refresh instead of silently disappearing mid-session.
            RepopulateCharacterDropdown();

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
                openExplorerItem.Header = "Open in file explorer";
                openExplorerItem.ToolTip = null;
                openExplorerItem.IsEnabled = Directory.Exists(backgroundDirectory);
                // Rebuilt every time the menu opens, so switching between a local and a streamed
                // background updates the entry instead of keeping the previous background's state.
                WebAssetMenuDecorator.ApplyProvenance(openExplorerItem, backgroundDirectory);
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
            if (ini == null && WebAssetService.IsActive)
            {
                // A server-only entry: fetch its char.ini, then finish the selection.
                _ = ConfirmServerOnlyCharacterAsync(iniName);
                return;
            }

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

        /// <summary>
        /// Mirrors a server-only character's config, then applies it like any local character.
        /// </summary>
        private async System.Threading.Tasks.Task ConfirmServerOnlyCharacterAsync(string iniName)
        {
            bool mirrored;
            try
            {
                mirrored = await WebCharacterMirror.EnsureCharacterAsync(iniName);
            }
            catch (Exception ex)
            {
                CustomConsole.Error(
                    $"Could not fetch character \"{iniName}\" from the server's assets.",
                    ex,
                    CustomConsole.LogCategory.WebAssets);
                return;
            }

            if (!mirrored)
            {
                CustomConsole.WriteLine($"Character {iniName} not found locally or on the server.");
                return;
            }

            CharacterFolder? ini = CharacterFolder.FullList.FirstOrDefault(x => x.Name == iniName);
            if (ini == null || curClient == null || curClient.currentINI == ini)
            {
                return;
            }

            txtICMessage.Focus();
            curClient.SetCharacter(ini);
            SetINI(ini);
            UpdatePosDropdown(curClient);
            OnClientStateChanged?.Invoke();
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
            // SetINI below re-selects the current emote, and SelectEmote resets the preanim checkbox to the
            // emote's default — which fires chkPreanim_Checked and overwrites client.PreanimEnabled. Capture the
            // client's real preanim state first so swapping away and back preserves the user's toggle instead of
            // snapping back to the emote default.
            bool preservedPreanim = client.PreanimEnabled;
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

            // Restore the preserved preanim state (SetINI's emote re-select may have clobbered the field).
            client.PreanimEnabled = preservedPreanim;
            chkPreanim.IsChecked = preservedPreanim;
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

            // Per-phase timing: SetINI (via SetClient) is the dominant cost of a client/character switch and freezes
            // the UI while it runs. Lap() returns ms since the last call; a breakdown is logged when SetINI exceeds
            // the threshold so the slow part (emote dropdown, emote grid, emote select, sound list) is identifiable.
            System.Diagnostics.Stopwatch setIniStopwatch = System.Diagnostics.Stopwatch.StartNew();
            double setIniLastMs = 0;
            double Lap()
            {
                double now = setIniStopwatch.Elapsed.TotalMilliseconds;
                double delta = now - setIniLastMs;
                setIniLastMs = now;
                return delta;
            }

            double msDropdownSet = 0;
            double msGridSet = 0;
            double msEmoteSelect = 0;
            double msPageNav = 0;
            double msSoundList = 0;

            try
            {
                txtICShowname_Placeholder.Text = ini.configINI.ShowName;

                EmoteGrid.ClearGrid();
                emotes.Clear();
                EmoteDropdown.Clear();
                emotes.AddRange(ini.configINI.Emotions.Values);

                // Streamed characters have no button art on disk yet; warm it so the grid fills in.
                WebCharacterIconResolver.PrefetchEmoteButtons(ini.Name, emotes);

                string? selectedDisplayId = curClient.currentEmote?.DisplayID;
                Emote? selectedEmote = string.IsNullOrWhiteSpace(selectedDisplayId)
                    ? null
                    : FindEmoteByDisplayId(selectedDisplayId);
                double msPrep = Lap();

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
                msDropdownSet = Lap();

                // The virtualized emote grid is built by SelectEmote below (it calls SetVirtualizedItems to reflect
                // the selection). Building it here too rebuilt the whole grid — including a synchronous image decode
                // per visible button — twice per switch. Build here only when there is no emote to select.
                if (selectedEmote != null)
                {
                    SelectEmote(selectedEmote, updateClient: !IsSelectedEmote(selectedEmote), focusMessageBox: false, notifyStateChanged: false);
                    msEmoteSelect = Lap();
                    EmoteGrid.SetPageToVirtualizedItem(item => ReferenceEquals(item, selectedEmote));
                    msPageNav = Lap();
                }
                else
                {
                    EmoteGrid.SetVirtualizedItems(emotes, CreateEmoteButton);
                    msEmoteSelect = Lap();
                }

                // Bulk-populate the sfx dropdown with a single SetItems (one ItemsSource refresh) instead of a
                // per-entry Add loop — each Add marshalled to the dispatcher and did a full item refresh, which for
                // a large base soundlist was the dominant remaining switch cost (the disk read itself is cached).
                IReadOnlyList<AO2SoundListEntry> soundListEntries = AO2SoundList.LoadEntries(
                    ini?.DirectoryPath ?? string.Empty,
                    Globals.BaseFolders);
                List<DropdownItem> sfxItems = new List<DropdownItem>(soundListEntries.Count + 2)
                {
                    new DropdownItem { Name = "Default", ImagePath = string.Empty, Value = "Default" },
                    new DropdownItem { Name = "Nothing", ImagePath = string.Empty, Value = "Nothing" }
                };
                foreach (AO2SoundListEntry soundListEntry in soundListEntries)
                {
                    sfxItems.Add(new DropdownItem
                    {
                        Name = soundListEntry.DisplayText,
                        ImagePath = string.Empty,
                        Value = soundListEntry.Value
                    });
                }
                sfxDropdown.SetItems(sfxItems);
                sfxDropdown.SelectedText = "Default";
                msSoundList = Lap();

                double msSetIniTotal = setIniStopwatch.Elapsed.TotalMilliseconds;
                if (msSetIniTotal >= 4.0)
                {
                    CustomConsole.Info(
                        $"[SETINI-TIMING] total={msSetIniTotal:0.0}ms | prep={msPrep:0.0} dropdownSet={msDropdownSet:0.0} gridSet={msGridSet:0.0} emoteSelect={msEmoteSelect:0.0} pageNav={msPageNav:0.0} soundList={msSoundList:0.0} | char=\"{ini?.Name ?? "(null)"}\" emotes={emotes.Count}",
                        CustomConsole.LogCategory.Viewport);
                }
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
        // BitmapFileLoader.LoadFrozen decodes from disk every call (IgnoreImageCache), so building the emote grid
        // re-decoded every visible button's on/off images on every client/character switch — the same images
        // reloaded each time you switch back to a character (measured as the dominant switch cost). Cache the frozen
        // (immutable, shareable) button bitmaps by path, validated by last-write-time so edited art still refreshes.
        private static readonly object EmoteButtonImageCacheLock = new object();
        private static readonly Dictionary<string, (DateTime WriteUtc, BitmapImage Image)> EmoteButtonImageCache =
            new Dictionary<string, (DateTime, BitmapImage)>(StringComparer.OrdinalIgnoreCase);
        private const int EmoteButtonImageCacheLimit = 1500;

        private static BitmapImage GetCachedButtonImage(string cacheKey, string statPath, Func<BitmapImage> factory)
        {
            DateTime writeUtc;
            try
            {
                writeUtc = System.IO.File.GetLastWriteTimeUtc(statPath);
            }
            catch
            {
                writeUtc = DateTime.MinValue;
            }

            lock (EmoteButtonImageCacheLock)
            {
                if (EmoteButtonImageCache.TryGetValue(cacheKey, out (DateTime WriteUtc, BitmapImage Image) cached)
                    && cached.WriteUtc == writeUtc)
                {
                    return cached.Image;
                }
            }

            BitmapImage image = factory();

            lock (EmoteButtonImageCacheLock)
            {
                EmoteButtonImageCache[cacheKey] = (writeUtc, image);
                if (EmoteButtonImageCache.Count > EmoteButtonImageCacheLimit)
                {
                    foreach (string staleKey in EmoteButtonImageCache.Keys.Take(EmoteButtonImageCache.Count - EmoteButtonImageCacheLimit).ToList())
                    {
                        EmoteButtonImageCache.Remove(staleKey);
                    }
                }
            }

            return image;
        }

        /// <summary>
        /// Rebuilds the emote grid once art for the current character finishes downloading.
        /// </summary>
        /// <remarks>
        /// Debounced because a character's buttons arrive as a burst of small files and a grid rebuild
        /// is the single most expensive part of a character switch.
        /// </remarks>
        private void OnWebAssetMaterialized(WebAssetMaterializedEventArgs args)
        {
            if (args.Kind != WebAssetKind.CharacterIcon)
            {
                return;
            }

            if (args.VPath.EndsWith("/char_icon" + System.IO.Path.GetExtension(args.VPath), StringComparison.Ordinal))
            {
                // A dropdown entry's icon arrived. Update that one row in place - rebuilding a roster
                // that can run to thousands of entries for every icon would be far more expensive than
                // the icon itself.
                string characterFolder = ExtractCharacterFolderFromVPath(args.VPath);
                string localPath = args.LocalPath;
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => UpdateDropdownIcon(characterFolder, localPath)));
                return;
            }

            string? characterName = curClient?.currentINI?.Name;
            if (string.IsNullOrWhiteSpace(characterName))
            {
                return;
            }

            string expected = "characters/" + WebAssetSource.NormalizeVPath(characterName) + "/";
            if (!args.VPath.StartsWith(expected, StringComparison.Ordinal))
            {
                return;
            }

            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(ScheduleEmoteArtRefresh));
        }

        /// <summary>
        /// Points one dropdown row at its freshly downloaded icon.
        /// </summary>
        private void UpdateDropdownIcon(string characterFolder, string localIconPath)
        {
            if (characterFolder.Length == 0)
            {
                return;
            }

            // The mirror stores lowercase folder names; the dropdown row keeps the server's casing.
            string? displayName = serverOnlyCharacterNames.FirstOrDefault(
                name => string.Equals(
                    WebAssetSource.NormalizeVPath(name),
                    characterFolder,
                    StringComparison.OrdinalIgnoreCase));

            if (displayName != null)
            {
                CharacterDropdown.TryUpdateItemImage(displayName, localIconPath);
            }
        }

        /// <summary>Extracts <c>&lt;folder&gt;</c> from <c>characters/&lt;folder&gt;/...</c>.</summary>
        private static string ExtractCharacterFolderFromVPath(string vpath)
        {
            const string prefix = "characters/";
            if (!vpath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            int separator = vpath.IndexOf('/', prefix.Length);
            return separator > prefix.Length ? vpath[prefix.Length..separator] : string.Empty;
        }

        private void ScheduleEmoteArtRefresh()
        {
            if (emoteArtRefreshTimer != null)
            {
                return;
            }

            emoteArtRefreshTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background,
                Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(EmoteArtRefreshDebounceMilliseconds)
            };
            emoteArtRefreshTimer.Tick += (_, _) =>
            {
                emoteArtRefreshTimer?.Stop();
                emoteArtRefreshTimer = null;

                CharacterFolder? character = curClient?.currentINI;
                if (character != null)
                {
                    SetINI(character);
                }
            };
            emoteArtRefreshTimer.Start();
        }

        private ToggleButton CreateEmoteButton(Emote emote)
        {
            // A streamed character's button art may not have existed when its CharacterFolder was
            // parsed, so the cached paths are empty; fall back to the web mirror.
            string characterName = curClient?.currentINI?.Name ?? string.Empty;
            string buttonOff = WebCharacterIconResolver.ResolveEmoteButton(
                characterName, emote, on: false, bakedPath: emote.PathToImage_off);
            string buttonOn = WebCharacterIconResolver.ResolveEmoteButton(
                characterName, emote, on: true, bakedPath: emote.PathToImage_on);
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
                    offImage = GetCachedButtonImage(buttonOff, buttonOff, () => BitmapFileLoader.LoadFrozen(buttonOff));
                    onImage = GetCachedButtonImage(buttonOn, buttonOn, () => BitmapFileLoader.LoadFrozen(buttonOn));
                }
                else if (offExists)
                {
                    // Only off image exists
                    offImage = GetCachedButtonImage(buttonOff, buttonOff, () => BitmapFileLoader.LoadFrozen(buttonOff));
                    // Create darkened version for on state
                    onImage = GetCachedButtonImage(buttonOff + "|dark", buttonOff, () => CreateDarkenedImage(buttonOff));
                }
                else // onExists
                {
                    // Only on image exists
                    onImage = GetCachedButtonImage(buttonOn, buttonOn, () => BitmapFileLoader.LoadFrozen(buttonOn));
                    // Create darkened version for off state
                    offImage = GetCachedButtonImage(buttonOn + "|dark", buttonOn, () => CreateDarkenedImage(buttonOn));
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
                ShowName = client.GetCurrentShowNameForPreview(),
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

        private async void btnPairingStudio_Click(object sender, RoutedEventArgs e)
        {
            if (curClient == null || curClient.currentINI == null)
            {
                txtICMessage.Focus();
                return;
            }

            CharacterPairingStudioWindow.PairingStudioResult? result =
                await CharacterPairingStudioWindow.ShowDialogAsync(
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
