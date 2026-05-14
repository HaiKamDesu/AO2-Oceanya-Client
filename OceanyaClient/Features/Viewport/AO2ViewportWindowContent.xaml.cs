using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.ChatPreview;
using OceanyaClient.Utilities;

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

        /// <summary>Fired whenever <see cref="UseAsWindowsPreview"/> changes.</summary>
        public event EventHandler? UseAsWindowsPreviewChanged;

        /// <summary>Requests that a character be opened in the character editor.</summary>
        public event Func<string, Task>? OpenCharacterInEditorRequested;

        /// <summary>Requests that a character be duplicated and opened in the character editor.</summary>
        public event Func<string, Task>? DuplicateCharacterInEditorRequested;

        /// <summary>Requests that a character be selected in the character folder visualizer.</summary>
        public event Action<string>? OpenCharacterInFolderVisualizerRequested;

        /// <summary>Requests that one character's asset cache be refreshed.</summary>
        public event Func<string, Task>? RefreshCharacterRequested;

        /// <summary>Requests that one background's asset cache be refreshed.</summary>
        public event Func<string, Task>? RefreshBackgroundRequested;

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

        /// <summary>
        /// Initializes a new instance of the <see cref="AO2ViewportWindowContent"/> class.
        /// </summary>
        public AO2ViewportWindowContent()
        {
            InitializeComponent();
            _useAsWindowsPreview = SaveFile.Data.GMViewportWindowPreviewPriority;
            MarkAutomationReady();
            Loaded += (_, _) => MarkAutomationReady();
        }

        private void ViewportContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu)
            {
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
            ContextMenuSectionHelper.AddHeader(menu, "Viewport", addLeadingSeparator: false);
            MenuItem useAsPreviewItem = new MenuItem
            {
                Header = "Use viewport as Windows preview",
                IsCheckable = true,
                IsChecked = _useAsWindowsPreview
            };
            useAsPreviewItem.Click += (_, _) => UseAsWindowsPreview = !UseAsWindowsPreview;
            menu.Items.Add(useAsPreviewItem);
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

            ContextMenuSectionHelper.AddHeader(
                menu,
                string.IsNullOrWhiteSpace(characterName) ? "Character" : "Character (" + characterName + ")",
                addLeadingSeparator: true);
            AddOpenFolderItem(menu, characterDirectory);
            AddCopyNameItem(menu, characterName);

            MenuItem refreshItem = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(characterName) ? "Refresh character" : "Refresh " + characterName,
                IsEnabled = !string.IsNullOrWhiteSpace(characterName)
            };
            refreshItem.Click += async (_, _) =>
            {
                if (RefreshCharacterRequested != null)
                {
                    await RefreshCharacterRequested(characterName);
                }
            };
            menu.Items.Add(refreshItem);

            MenuItem openEditorItem = new MenuItem
            {
                Header = "Open in Character Editor",
                IsEnabled = Directory.Exists(characterDirectory)
            };
            openEditorItem.Click += async (_, _) =>
            {
                if (OpenCharacterInEditorRequested != null)
                {
                    await OpenCharacterInEditorRequested(characterDirectory);
                }
            };
            menu.Items.Add(openEditorItem);

            MenuItem duplicateEditorItem = new MenuItem
            {
                Header = "Duplicate and open in Character Editor",
                IsEnabled = Directory.Exists(characterDirectory)
            };
            duplicateEditorItem.Click += async (_, _) =>
            {
                if (DuplicateCharacterInEditorRequested != null)
                {
                    await DuplicateCharacterInEditorRequested(characterDirectory);
                }
            };
            menu.Items.Add(duplicateEditorItem);

            MenuItem emoteVisualizerItem = new MenuItem
            {
                Header = "Open in Character Emote Visualizer",
                IsEnabled = character != null
            };
            emoteVisualizerItem.Click += (_, _) =>
            {
                if (character != null)
                {
                    CharacterEmoteVisualizerWindow emoteVisualizerWindow = new CharacterEmoteVisualizerWindow(character)
                    {
                        Owner = Window.GetWindow(this)
                    };
                    emoteVisualizerWindow.ShowDialog();
                }
            };
            menu.Items.Add(emoteVisualizerItem);

            MenuItem folderVisualizerItem = new MenuItem
            {
                Header = "Open in Character Folder Visualizer",
                IsEnabled = !string.IsNullOrWhiteSpace(characterDirectory)
            };
            folderVisualizerItem.Click += (_, _) => OpenCharacterInFolderVisualizerRequested?.Invoke(characterDirectory);
            menu.Items.Add(folderVisualizerItem);
        }

        private void AddChatboxMenuSection(ContextMenu menu, AO2ViewportControl? control)
        {
            string chatboxName = control?.CurrentChatboxName?.Trim() ?? string.Empty;
            string chatboxDirectory = AO2ChatPreviewResolver.ResolveChatboxDirectoryPath(
                chatboxName,
                preferViewportTheme: true)?.Trim() ?? string.Empty;

            ContextMenuSectionHelper.AddHeader(
                menu,
                string.IsNullOrWhiteSpace(chatboxName) ? "Chatbox" : "Chatbox (" + chatboxName + ")",
                addLeadingSeparator: true);
            AddOpenFolderItem(menu, chatboxDirectory);
            AddCopyNameItem(menu, chatboxName);
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
                    Visibility = Visibility.Collapsed
                };
                profileControls[client] = control;
                ViewportHost.Children.Add(control);
            }

            control.AttachClient(client, incomingMessageClient, messageFilter, actionFilter);
            if (ReferenceEquals(activeClient, client))
            {
                control.Visibility = Visibility.Visible;
            }
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
            ViewportHost.Children.Remove(control);
            if (ReferenceEquals(activeClient, client))
            {
                activeClient = null;
            }
        }
    }
}
