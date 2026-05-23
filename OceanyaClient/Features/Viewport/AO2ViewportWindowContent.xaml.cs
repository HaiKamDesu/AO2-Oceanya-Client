using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.ChatPreview;
using OceanyaClient.Utilities;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Hosts the AO2 viewport surface inside the shared Oceanya window chrome.
    /// </summary>
    public partial class AO2ViewportWindowContent : OceanyaWindowContentControl
    {
        private readonly Dictionary<AOClient, AO2ViewportControl> profileControls = new Dictionary<AOClient, AO2ViewportControl>();
        private AOClient? activeClient;
        private bool _useAsWindowsPreview;
        private bool _chatboxOverlapsViewport;
        private bool _pictureInPictureViewport;
        private bool contextMenuEnabled = true;
        private bool renderAudioEnabled = true;
        private AO2ViewportWindowContent? mirrorSource;
        private Rectangle? mirrorSurface;

        /// <summary>Fired whenever <see cref="UseAsWindowsPreview"/> changes.</summary>
        public event EventHandler? UseAsWindowsPreviewChanged;

        public event EventHandler? PictureInPictureViewportChanged;

        public event EventHandler? ViewportSurfaceLayoutChanged;

        /// <summary>Requests that a character be opened in the character editor.</summary>
        public event Func<string, Task>? OpenCharacterInEditorRequested;

        /// <summary>Requests that a new character folder be created in the character editor.</summary>
        public event Func<Task>? NewCharacterFolderRequested;

        /// <summary>Requests that a character be duplicated and opened in the character editor.</summary>
        public event Func<string, Task>? DuplicateCharacterInEditorRequested;

        /// <summary>Requests that a character be selected in the character folder visualizer.</summary>
        public event Action<string>? OpenCharacterInFolderVisualizerRequested;

        /// <summary>Requests that a character folder be deleted.</summary>
        public event Func<string, string, Task>? DeleteCharacterFolderRequested;

        /// <summary>Requests that one character's asset cache be refreshed.</summary>
        public event Func<string, Task>? RefreshCharacterRequested;

        /// <summary>Requests that every local asset cache be refreshed.</summary>
        public event Func<Task>? RefreshAllAssetsRequested;

        /// <summary>Requests that all character asset caches be refreshed.</summary>
        public event Func<Task>? RefreshAllCharactersRequested;

        /// <summary>Requests that one background's asset cache be refreshed.</summary>
        public event Func<string, Task>? RefreshBackgroundRequested;

        /// <summary>Requests that the main settings window open directly to viewport settings.</summary>
        public event Action? ChangeViewportThemeRequested;

        /// <summary>
        /// When <see langword="true"/> and the viewport is visible, the viewport window takes
        /// the Windows taskbar slot instead of the main window.
        /// </summary>
        public bool UseAsWindowsPreview
        {
            get => _useAsWindowsPreview;
            set
            {
                if (_useAsWindowsPreview == value)
                {
                    return;
                }

                _useAsWindowsPreview = value;
                SaveFile.Data.GMViewportWindowPreviewPriority = value;
                SaveFile.Save();
                UseAsWindowsPreviewChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ChatboxOverlapsViewport
        {
            get => _chatboxOverlapsViewport;
            set
            {
                if (_chatboxOverlapsViewport == value)
                {
                    return;
                }

                _chatboxOverlapsViewport = value;
                SaveFile.Data.GMViewportChatboxOverlapsViewport = value;
                SaveFile.Save();
                foreach (AO2ViewportControl control in profileControls.Values)
                {
                    control.ChatboxOverlapsViewport = value;
                }

                RefreshHostSurfaceSize();
            }
        }

        public bool PictureInPictureViewport
        {
            get => _pictureInPictureViewport;
            set
            {
                if (_pictureInPictureViewport == value)
                {
                    return;
                }

                _pictureInPictureViewport = value;
                PictureInPictureViewportChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsViewportContextMenuEnabled
        {
            get => contextMenuEnabled;
            set => contextMenuEnabled = value;
        }

        public bool RenderAudioEnabled
        {
            get => renderAudioEnabled;
            set
            {
                if (renderAudioEnabled == value)
                {
                    return;
                }

                renderAudioEnabled = value;
                foreach (AO2ViewportControl control in profileControls.Values)
                {
                    control.RenderAudioEnabled = value;
                }
            }
        }

        public int CurrentSurfaceWidth => Math.Max(
            1,
            IsFinite(ViewportHost.Width) && ViewportHost.Width > 0
                ? (int)Math.Round(ViewportHost.Width)
                : AO2ViewportAssetResolver.ViewportToolWidth);

        public int CurrentSurfaceHeight => Math.Max(
            1,
            IsFinite(ViewportHost.Height) && ViewportHost.Height > 0
                ? (int)Math.Round(ViewportHost.Height)
                : AO2ViewportAssetResolver.ViewportToolHeight);

        /// <summary>
        /// Initializes a new instance of the <see cref="AO2ViewportWindowContent"/> class.
        /// </summary>
        public AO2ViewportWindowContent()
        {
            InitializeComponent();
            _useAsWindowsPreview = SaveFile.Data.GMViewportWindowPreviewPriority;
            _chatboxOverlapsViewport = SaveFile.Data.GMViewportChatboxOverlapsViewport;
            _pictureInPictureViewport = false;
            MarkAutomationReady();
            Loaded += (_, _) => MarkAutomationReady();
        }

        private void ViewportContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu)
            {
                return;
            }

            if (!contextMenuEnabled)
            {
                menu.Items.Clear();
                menu.IsOpen = false;
                return;
            }

            menu.Items.Clear();

            AO2ViewportControl? control = GetActiveControl();
            AddViewportMenuSection(menu);
            AddBackgroundMenuSection(menu, control);
            AddCharacterMenuSection(menu, control);
            AddChatboxMenuSection(menu, control);
        }

        private void AddViewportMenuSection(ContextMenu menu)
        {
            string themeName = AO2ThemeCatalog.GetConfiguredThemeName();
            ContextMenuSectionHelper.AddHeader(menu, "Viewport (" + themeName + ")", addLeadingSeparator: false);
            AddCopyNameItem(menu, themeName);

            MenuItem changeThemeItem = new MenuItem { Header = "Change theme..." };
            changeThemeItem.Click += (_, _) => ChangeViewportThemeRequested?.Invoke();
            menu.Items.Add(changeThemeItem);

            MenuItem useAsPreviewItem = new MenuItem
            {
                Header = "Use viewport as Windows preview",
                IsCheckable = true,
                IsChecked = _useAsWindowsPreview
            };
            useAsPreviewItem.Click += (_, _) => UseAsWindowsPreview = !UseAsWindowsPreview;
            menu.Items.Add(useAsPreviewItem);

            MenuItem pictureInPictureItem = new MenuItem
            {
                Header = "Picture in Picture Viewport",
                IsCheckable = true,
                IsChecked = _pictureInPictureViewport
            };
            pictureInPictureItem.Click += (_, _) => PictureInPictureViewport = !PictureInPictureViewport;
            menu.Items.Add(pictureInPictureItem);

            MenuItem overlapChatboxItem = new MenuItem
            {
                Header = "Make chatbox overlap viewport",
                IsCheckable = true,
                IsChecked = _chatboxOverlapsViewport
            };
            overlapChatboxItem.Click += (_, _) => ChatboxOverlapsViewport = !ChatboxOverlapsViewport;
            menu.Items.Add(overlapChatboxItem);
        }

        private void AddBackgroundMenuSection(ContextMenu menu, AO2ViewportControl? control)
        {
            string backgroundName = control?.CurrentBackgroundName?.Trim() ?? string.Empty;
            AOBot_Testing.Structures.Background? background = string.IsNullOrWhiteSpace(backgroundName)
                ? null
                : AOBot_Testing.Structures.Background.FromBGPath(backgroundName);
            string backgroundDirectory = background?.PathToFile?.Trim() ?? string.Empty;

            ContextMenuSectionHelper.AddHeader(
                menu,
                string.IsNullOrWhiteSpace(backgroundName) ? "Background" : "Background (" + backgroundName + ")",
                addLeadingSeparator: true);
            AddOpenFolderItem(menu, backgroundDirectory);
            AddOpenFileItem(menu, "Open design.ini", Path.Combine(backgroundDirectory, "design.ini"));
            AddCopyNameItem(menu, backgroundName);

            MenuItem refreshItem = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(backgroundName) ? "Refresh background" : "Refresh " + backgroundName,
                IsEnabled = !string.IsNullOrWhiteSpace(backgroundName)
            };
            refreshItem.Click += async (_, _) =>
            {
                if (RefreshBackgroundRequested != null)
                {
                    await RefreshBackgroundRequested(backgroundName);
                }
            };
            menu.Items.Add(refreshItem);
        }

        private void AddCharacterMenuSection(ContextMenu menu, AO2ViewportControl? control)
        {
            CharacterFolder? character = control?.CurrentCharacter;
            string characterName = character?.Name?.Trim() ?? string.Empty;
            string characterDirectory = character?.DirectoryPath?.Trim() ?? string.Empty;
            string readmePath = ResolveReadmePath(characterDirectory);
            string header = string.IsNullOrWhiteSpace(characterName) ? "Character" : "Character (" + characterName + ")";
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(CharacterContextMenuBuilder.BuildSubmenu(header, new CharacterContextMenuOptions
            {
                CharacterName = characterName,
                DirectoryPath = characterDirectory,
                CharIniPath = character?.PathToConfigIni?.Trim() ?? Path.Combine(characterDirectory, "char.ini"),
                ReadmePath = readmePath,
                Owner = Window.GetWindow(this),
                HasReadme = !string.IsNullOrWhiteSpace(readmePath),
                RefreshCharacterAsync = RefreshCharacterRequested == null
                    ? null
                    : () => RefreshCharacterRequested(characterName),
                RefreshAllAssetsAsync = RefreshAllAssetsRequested,
                RefreshAllCharactersAsync = RefreshAllCharactersRequested,
                NewCharacterFolderAsync = NewCharacterFolderRequested,
                EditCharacterFolderAsync = OpenCharacterInEditorRequested == null
                    ? null
                    : () => OpenCharacterInEditorRequested(characterDirectory),
                DuplicateCharacterFolderAsync = DuplicateCharacterInEditorRequested == null
                    ? null
                    : () => DuplicateCharacterInEditorRequested(characterDirectory),
                OpenCharacterEmoteVisualizer = character == null
                    ? null
                    : () =>
                    {
                        CharacterEmoteVisualizerWindow emoteVisualizerWindow = new CharacterEmoteVisualizerWindow(character)
                        {
                            Owner = Window.GetWindow(this)
                        };
                        emoteVisualizerWindow.ShowDialog();
                    },
                OpenCharacterFolderVisualizer = () => OpenCharacterInFolderVisualizerRequested?.Invoke(characterDirectory),
                DeleteCharacterFolderAsync = DeleteCharacterFolderRequested == null
                    ? null
                    : () => DeleteCharacterFolderRequested(characterName, characterDirectory)
            }));
        }

        private void AddChatboxMenuSection(ContextMenu menu, AO2ViewportControl? control)
        {
            string chatboxName = control?.CurrentChatboxName?.Trim() ?? string.Empty;
            bool isThemeDefault = string.IsNullOrWhiteSpace(chatboxName);
            string themeName = AO2ChatPreviewResolver.ResolveActiveThemeName(preferViewportTheme: true);
            string displayName = isThemeDefault ? "Theme default" : chatboxName;
            string copyName = isThemeDefault ? themeName : chatboxName;
            string chatboxDirectory = AO2ChatPreviewResolver.ResolveChatboxDirectoryPath(
                chatboxName,
                preferViewportTheme: true)?.Trim() ?? string.Empty;

            ContextMenuSectionHelper.AddHeader(
                menu,
                "Chatbox (" + displayName + ")",
                addLeadingSeparator: true);
            AddOpenFolderItem(menu, chatboxDirectory);
            AddOpenFileItem(menu, "Open config.ini", Globals.PathToConfigINI);
            AddCopyNameItem(menu, copyName);

            MenuItem chooseColorItem = new MenuItem { Header = "Set chat background color..." };
            chooseColorItem.IsEnabled = control != null;
            chooseColorItem.Click += (_, _) => control?.PickChatBackgroundColor();
            menu.Items.Add(chooseColorItem);

            MenuItem transparentItem = new MenuItem { Header = "Use transparent chat background" };
            transparentItem.IsEnabled = control != null;
            transparentItem.Click += (_, _) => control?.SetChatBackgroundColor(null);
            menu.Items.Add(transparentItem);
        }

        private static void AddOpenFolderItem(ContextMenu menu, string directory)
        {
            MenuItem item = new MenuItem
            {
                Header = "Open in file explorer",
                IsEnabled = Directory.Exists(directory)
            };
            item.Click += (_, _) => OpenDirectory(directory);
            menu.Items.Add(item);
        }

        private static void AddOpenFileItem(ContextMenu menu, string header, string filePath)
        {
            MenuItem item = new MenuItem
            {
                Header = header,
                IsEnabled = File.Exists(filePath)
            };
            item.Click += (_, _) => OpenFile(filePath);
            menu.Items.Add(item);
        }

        private static void AddCopyNameItem(ContextMenu menu, string name)
        {
            MenuItem item = new MenuItem
            {
                Header = "Copy name",
                IsEnabled = !string.IsNullOrWhiteSpace(name)
            };
            item.Click += (_, _) => ClipboardUtilities.TrySetText(name);
            menu.Items.Add(item);
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

        private static void OpenFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
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
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private AO2ViewportControl? GetActiveControl()
        {
            if (activeClient != null && profileControls.TryGetValue(activeClient, out AO2ViewportControl? control))
            {
                return control;
            }

            return null;
        }

        /// <inheritdoc/>
        public override string HeaderText => "Viewport";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        /// <inheritdoc/>
        public override bool IsUserMoveEnabled => true;

        /// <inheritdoc/>
        public override bool IsCloseButtonVisible => true;

        /// <summary>
        /// Attaches the hosted viewport to a client profile.
        /// </summary>
        public void AttachClient(AOClient? client)
        {
            AttachClient(client, client);
        }

        /// <summary>
        /// Attaches the hosted viewport to a selected profile and the client that receives server IC echoes.
        /// </summary>
        public void AttachClient(AOClient? client, AOClient? incomingMessageClient)
        {
            AttachClient(client, incomingMessageClient, null, null);
        }

        /// <summary>
        /// Attaches the visible viewport to a selected profile and optional hidden-message filters.
        /// </summary>
        public void AttachClient(
            AOClient? client,
            AOClient? incomingMessageClient,
            Func<ICMessage, bool>? messageFilter,
            Func<string, bool>? actionFilter)
        {
            if (client == null)
            {
                activeClient = null;
                foreach (AO2ViewportControl control in profileControls.Values)
                {
                    control.Visibility = Visibility.Collapsed;
                }

                return;
            }

            EnsureClient(client, incomingMessageClient, messageFilter, actionFilter);
            activeClient = client;
            foreach (KeyValuePair<AOClient, AO2ViewportControl> pair in profileControls)
            {
                pair.Value.Visibility = ReferenceEquals(pair.Key, client)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Ensures a hidden viewport exists and continues receiving scene updates for the given profile.
        /// </summary>
        public void EnsureClient(
            AOClient client,
            AOClient? incomingMessageClient,
            Func<ICMessage, bool>? messageFilter,
            Func<string, bool>? actionFilter)
        {
            if (!profileControls.TryGetValue(client, out AO2ViewportControl? control))
            {
                control = new AO2ViewportControl
                {
                    Visibility = Visibility.Collapsed,
                    ChatboxOverlapsViewport = _chatboxOverlapsViewport,
                    RenderAudioEnabled = renderAudioEnabled
                };
                control.SurfaceLayoutChanged += Control_SurfaceLayoutChanged;
                profileControls[client] = control;
                ViewportHost.Children.Add(control);
                RefreshHostSurfaceSize(control);
            }

            control.AttachClient(client, incomingMessageClient, messageFilter, actionFilter);
            if (ReferenceEquals(activeClient, client))
            {
                control.Visibility = Visibility.Visible;
                RefreshHostSurfaceSize(control);
            }
        }

        private void Control_SurfaceLayoutChanged(object? sender, EventArgs e)
        {
            if (sender is AO2ViewportControl control && ReferenceEquals(control, GetActiveControl()))
            {
                RefreshHostSurfaceSize(control);
            }
        }

        private void RefreshHostSurfaceSize(AO2ViewportControl? control = null)
        {
            control ??= GetActiveControl();
            int width = Math.Max(1, control?.SurfaceWidth ?? AO2ViewportAssetResolver.ViewportToolWidth);
            int height = Math.Max(1, control?.SurfaceHeight ?? AO2ViewportAssetResolver.ViewportToolHeight);
            ViewportHost.Width = width;
            ViewportHost.Height = height;
            ViewportSurfaceLayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Applies current saved volume settings to all active audio players in all hosted viewports.
        /// </summary>
        public void RefreshVolumes()
        {
            foreach (AO2ViewportControl control in profileControls.Values)
            {
                control.RefreshVolumes();
            }
        }

        public void ReloadThemeLayout()
        {
            _useAsWindowsPreview = SaveFile.Data.GMViewportWindowPreviewPriority;
            _chatboxOverlapsViewport = SaveFile.Data.GMViewportChatboxOverlapsViewport;
            foreach (AO2ViewportControl control in profileControls.Values)
            {
                control.ChatboxOverlapsViewport = _chatboxOverlapsViewport;
                control.ReloadThemeLayout();
            }

            RefreshHostSurfaceSize();
            UseAsWindowsPreviewChanged?.Invoke(this, EventArgs.Empty);
        }

        public void MirrorFrom(AO2ViewportWindowContent? source)
        {
            if (ReferenceEquals(mirrorSource, source))
            {
                RefreshMirrorSurfaceSize();
                return;
            }

            if (mirrorSource != null)
            {
                mirrorSource.ViewportSurfaceLayoutChanged -= MirrorSource_ViewportSurfaceLayoutChanged;
            }

            foreach (AO2ViewportControl control in profileControls.Values)
            {
                control.AttachClient(null, null);
                control.SurfaceLayoutChanged -= Control_SurfaceLayoutChanged;
            }

            profileControls.Clear();
            activeClient = null;
            ViewportHost.Children.Clear();
            mirrorSource = source;

            if (source == null)
            {
                mirrorSurface = null;
                RefreshHostSurfaceSize();
                return;
            }

            mirrorSurface = new Rectangle
            {
                IsHitTestVisible = false,
                Fill = new VisualBrush(source.ViewportHost)
                {
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                }
            };
            ViewportHost.Children.Add(mirrorSurface);
            source.ViewportSurfaceLayoutChanged += MirrorSource_ViewportSurfaceLayoutChanged;
            RefreshMirrorSurfaceSize();
        }

        private void MirrorSource_ViewportSurfaceLayoutChanged(object? sender, EventArgs e)
        {
            RefreshMirrorSurfaceSize();
        }

        private void RefreshMirrorSurfaceSize()
        {
            if (mirrorSource == null || mirrorSurface == null)
            {
                return;
            }

            int width = mirrorSource.CurrentSurfaceWidth;
            int height = mirrorSource.CurrentSurfaceHeight;
            ViewportHost.Width = width;
            ViewportHost.Height = height;
            mirrorSurface.Width = width;
            mirrorSurface.Height = height;
            ViewportSurfaceLayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public void ReleaseCharacterAssetsForDeletedFolder(string normalizedCharacterDirectory)
        {
            foreach (AO2ViewportControl control in profileControls.Values)
            {
                control.ReleaseCharacterAssetsForDeletedFolder(normalizedCharacterDirectory);
            }
        }

        /// <summary>
        /// Removes a profile viewport and detaches it from AO2 events.
        /// </summary>
        public void RemoveClient(AOClient client)
        {
            if (!profileControls.Remove(client, out AO2ViewportControl? control))
            {
                return;
            }

            control.AttachClient(null, null);
            control.SurfaceLayoutChanged -= Control_SurfaceLayoutChanged;
            ViewportHost.Children.Remove(control);
            if (ReferenceEquals(activeClient, client))
            {
                activeClient = null;
            }
        }
    }
}
