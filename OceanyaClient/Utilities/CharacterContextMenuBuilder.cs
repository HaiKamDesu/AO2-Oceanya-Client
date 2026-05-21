using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient.Utilities
{
    /// <summary>
    /// Builds the standard character context menu used by the client, viewport, and character database.
    /// </summary>
    public static class CharacterContextMenuBuilder
    {
        public static ContextMenu Build(CharacterContextMenuOptions options)
        {
            ContextMenu menu = new ContextMenu();
            Populate(menu, options);
            return menu;
        }

        public static MenuItem BuildSubmenu(string header, CharacterContextMenuOptions options)
        {
            MenuItem submenu = new MenuItem
            {
                Header = ContextMenuSectionHelper.CreateHeaderLabel(header),
                IsEnabled = !string.IsNullOrWhiteSpace(options.CharacterName)
                    || !string.IsNullOrWhiteSpace(options.DirectoryPath)
            };
            Populate(submenu, options);
            return submenu;
        }

        public static void Populate(ItemsControl menu, CharacterContextMenuOptions options, bool addLeadingSeparator = false)
        {
            ContextMenuSectionHelper.AddHeader(menu, "Oceanya Client", addLeadingSeparator);

            if (options.SelectCharacterAsync != null)
            {
                AddAsyncItem(menu, "Select this character", true, options.SelectCharacterAsync);
                return;
            }

            if (options.SetCharacterInClient != null)
            {
                AddItem(menu, "Set character in client", options.CanSetCharacterInClient, options.SetCharacterInClient);
            }

            AddAsyncItem(
                menu,
                string.IsNullOrWhiteSpace(options.CharacterName) ? "Refresh Current Character" : "Refresh " + options.CharacterName,
                !string.IsNullOrWhiteSpace(options.CharacterName) && options.RefreshCharacterAsync != null,
                options.RefreshCharacterAsync);
            AddAsyncItem(menu, "Refresh All Assets", options.RefreshAllAssetsAsync != null, options.RefreshAllAssetsAsync);
            AddAsyncItem(menu, "Refresh All Characters", options.RefreshAllCharactersAsync != null, options.RefreshAllCharactersAsync);

            ContextMenuSectionHelper.AddHeader(menu, "Oceanya Editor", addLeadingSeparator: true);
            Func<Task> newCharacter = options.NewCharacterFolderAsync ?? (() => OpenNewCharacterFolderAsync(options));
            bool canOpenInEditor = Directory.Exists(options.DirectoryPath) && File.Exists(options.CharIniPath);
            Func<Task>? editCharacter = options.EditCharacterFolderAsync ?? (() => OpenCharacterFolderInCreatorAsync(options, duplicate: false));
            Func<Task>? duplicateCharacter = options.DuplicateCharacterFolderAsync ?? (() => OpenCharacterFolderInCreatorAsync(options, duplicate: true));
            AddAsyncItem(menu, "New Character Folder", true, newCharacter);
            AddAsyncItem(menu, "Edit Character Folder", canOpenInEditor, editCharacter);
            AddAsyncItem(menu, "Duplicate Character Folder", canOpenInEditor, duplicateCharacter);

            ContextMenuSectionHelper.AddHeader(menu, "Character View", addLeadingSeparator: true);
            AddItem(menu, "Open Char.ini", File.Exists(options.CharIniPath), () => TryOpenPath(options.CharIniPath));
            AddItem(menu, "Open Readme", options.HasReadme && File.Exists(options.ReadmePath), () => TryOpenPath(options.ReadmePath));
            AddItem(menu, "Show in explorer", Directory.Exists(options.DirectoryPath), () => ShowInExplorer(options.DirectoryPath));
            AddItem(menu, "Copy name", !string.IsNullOrWhiteSpace(options.CharacterName), () => ClipboardUtilities.TrySetText(options.CharacterName));
            AddItem(menu, "Open in Character Emote Visualizer", Directory.Exists(options.DirectoryPath) && options.OpenCharacterEmoteVisualizer != null, options.OpenCharacterEmoteVisualizer);
            AddItem(menu, "Open in Character Folder Visualizer", !string.IsNullOrWhiteSpace(options.DirectoryPath) && options.OpenCharacterFolderVisualizer != null, options.OpenCharacterFolderVisualizer);

            ContextMenuSectionHelper.AddHeader(menu, "Integrity verifier", addLeadingSeparator: true);
            Func<Task>? runVerifier = options.RunIntegrityVerifierAsync ?? (() => RunVerifierAsync(options, openResultsAfterRun: false));
            Func<Task>? viewResults = options.ViewIntegrityVerifierResultsAsync ?? (() => ViewVerifierResultsAsync(options));
            AddAsyncItem(menu, "Run Verifier", Directory.Exists(options.DirectoryPath), runVerifier);
            AddAsyncItem(menu, "View Results", Directory.Exists(options.DirectoryPath), viewResults);

            ContextMenuSectionHelper.AddHeader(menu, "Attorney Online", addLeadingSeparator: true);
            Func<Task>? deleteCharacter = options.DeleteCharacterFolderAsync ?? (() => DeleteCharacterFolderAsync(options));
            AddAsyncItem(menu, "Delete character folder", Directory.Exists(options.DirectoryPath), deleteCharacter);
        }

        private static void AddItem(ItemsControl menu, string header, bool isEnabled, Action? action)
        {
            MenuItem item = new MenuItem
            {
                Header = header,
                IsEnabled = isEnabled && action != null
            };
            if (action != null)
            {
                item.Click += (_, _) => action();
            }

            menu.Items.Add(item);
        }

        private static void AddAsyncItem(ItemsControl menu, string header, bool isEnabled, Func<Task>? action)
        {
            MenuItem item = new MenuItem
            {
                Header = header,
                IsEnabled = isEnabled && action != null
            };
            if (action != null)
            {
                item.Click += async (_, _) =>
                {
                    try
                    {
                        await action();
                    }
                    catch (Exception ex)
                    {
                        CustomConsole.Error("Character context menu action failed.", ex);
                        OceanyaMessageBox.Show(
                            ResolveOwnerFromItem(item),
                            "The character menu action could not be completed:\n" + ex.Message,
                            "Character Menu",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                };
            }

            menu.Items.Add(item);
        }

        private static bool TryOpenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return true;
        }

        private static void ShowInExplorer(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = directoryPath,
                UseShellExecute = true
            });
        }

        private static async Task RunVerifierAsync(CharacterContextMenuOptions options, bool openResultsAfterRun)
        {
            string directory = options.DirectoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            CharacterFolder? character = ResolveCharacter(options);
            if (character == null)
            {
                return;
            }

            Window? owner = ResolveOwner(options);
            if (owner != null)
            {
                await WaitForm.ShowFormAsync("Running integrity verifier...", owner);
            }

            CharacterIntegrityReport report;
            try
            {
                WaitForm.SetSubtitle("Verifying folder: " + character.Name);
                report = await Task.Run(() => CharacterIntegrityVerifier.RunAndPersist(character));
            }
            finally
            {
                if (owner != null)
                {
                    WaitForm.CloseForm();
                }
            }

            if (openResultsAfterRun)
            {
                OpenVerifierResultsWindow(options, report);
            }
        }

        private static async Task ViewVerifierResultsAsync(CharacterContextMenuOptions options)
        {
            string directory = options.DirectoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            if (!CharacterIntegrityVerifier.TryLoadPersistedReport(directory, out CharacterIntegrityReport? report) || report == null)
            {
                await RunVerifierAsync(options, openResultsAfterRun: true);
                return;
            }

            OpenVerifierResultsWindow(options, report);
        }

        private static void OpenVerifierResultsWindow(CharacterContextMenuOptions options, CharacterIntegrityReport report)
        {
            CharacterIntegrityVerifierResultsWindow resultsWindow = new CharacterIntegrityVerifierResultsWindow(
                report,
                options.DirectoryPath ?? string.Empty,
                string.IsNullOrWhiteSpace(options.CharacterName) ? report.CharacterName : options.CharacterName)
            {
                Owner = ResolveOwner(options)
            };
            resultsWindow.Show();
        }

        private static async Task DeleteCharacterFolderAsync(CharacterContextMenuOptions options)
        {
            string targetPath = options.DirectoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            {
                return;
            }

            Window? owner = ResolveOwner(options);
            string characterName = string.IsNullOrWhiteSpace(options.CharacterName)
                ? Path.GetFileName(targetPath)
                : options.CharacterName.Trim();
            MessageBoxResult confirmationResult = OceanyaMessageBox.Show(
                owner,
                "You are about to permanently delete this character folder:\n\n"
                + characterName + "\n" + targetPath
                + "\n\nThis cannot be undone. Do you want to continue?",
                "Delete Character Folder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmationResult != MessageBoxResult.Yes)
            {
                return;
            }

            if (owner != null)
            {
                await WaitForm.ShowFormAsync("Deleting character folder...", owner);
            }

            try
            {
                WaitForm.SetSubtitle("Deleting folder: " + characterName);
                await Task.Run(() => Directory.Delete(targetPath, true));
                WaitForm.SetSubtitle("Refreshing character index...");
                if (!CharacterFolder.TryRemoveCharacterFolderFromCache(targetPath, characterName, out _, out string removeError))
                {
                    throw new InvalidOperationException(removeError);
                }

                await (options.AfterCharacterDeletedAsync?.Invoke(characterName, targetPath) ?? Task.CompletedTask);
            }
            finally
            {
                if (owner != null)
                {
                    WaitForm.CloseForm();
                }
            }
        }

        private static async Task OpenNewCharacterFolderAsync(CharacterContextMenuOptions options)
        {
            Window? owner = ResolveOwner(options);
            AOCharacterFileCreatorWindow creator = new AOCharacterFileCreatorWindow();
            Window creatorWindow = OceanyaWindowManager.CreateWindow(creator);
            if (owner != null)
            {
                creatorWindow.Owner = owner;
            }

            _ = creatorWindow.ShowDialog();
            if (creator.CharacterGenerationCompleted)
            {
                await (options.AfterCharacterChangedAsync?.Invoke() ?? Task.CompletedTask);
            }
        }

        private static async Task OpenCharacterFolderInCreatorAsync(CharacterContextMenuOptions options, bool duplicate)
        {
            string directoryPath = options.DirectoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            Window? owner = ResolveOwner(options);
            if (owner != null)
            {
                await WaitForm.ShowFormAsync("Opening character editor...", owner);
            }

            AOCharacterFileCreatorWindow creator = new AOCharacterFileCreatorWindow();
            bool loadedSuccessfully;
            string errorMessage;
            try
            {
                WaitForm.SetSubtitle(duplicate ? "Loading duplicate template..." : "Loading character folder...");
                loadedSuccessfully = duplicate
                    ? creator.TryLoadCharacterFolderForDuplication(directoryPath, out errorMessage)
                    : creator.TryLoadCharacterFolderForEditing(directoryPath, out errorMessage);
            }
            finally
            {
                if (owner != null)
                {
                    WaitForm.CloseForm();
                }
            }

            if (!loadedSuccessfully)
            {
                OceanyaMessageBox.Show(
                    owner,
                    duplicate
                        ? "Could not load the selected character for duplication.\n" + (string.IsNullOrWhiteSpace(errorMessage) ? "Unknown error." : errorMessage)
                        : "Could not open the selected character in the AO Character File Creator.\n" + (string.IsNullOrWhiteSpace(errorMessage) ? "Unknown error." : errorMessage),
                    duplicate ? "Duplicate Character Folder" : "Edit Character Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Window editorWindow = OceanyaWindowManager.CreateWindow(creator);
            if (owner != null)
            {
                editorWindow.Owner = owner;
            }

            _ = editorWindow.ShowDialog();
            if (creator.CharacterGenerationCompleted)
            {
                await (options.AfterCharacterChangedAsync?.Invoke() ?? Task.CompletedTask);
            }
        }

        private static CharacterFolder? ResolveCharacter(CharacterContextMenuOptions options)
        {
            string directory = options.DirectoryPath?.Trim() ?? string.Empty;
            CharacterFolder? existing = CharacterFolder.FullList.FirstOrDefault(character =>
                string.Equals(character.DirectoryPath, directory, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            string charIniPath = options.CharIniPath?.Trim() ?? string.Empty;
            if (!File.Exists(charIniPath))
            {
                return null;
            }

            try
            {
                return CharacterFolder.Create(charIniPath);
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Unable to load character for shared context menu.", ex);
                return null;
            }
        }

        private static Window? ResolveOwner(CharacterContextMenuOptions options)
        {
            return options.Owner ?? Application.Current?.MainWindow;
        }

        private static Window? ResolveOwnerFromItem(DependencyObject item)
        {
            return Window.GetWindow(item) ?? Application.Current?.MainWindow;
        }
    }

    public sealed class CharacterContextMenuOptions
    {
        public string CharacterName { get; init; } = string.Empty;
        public string DirectoryPath { get; init; } = string.Empty;
        public string CharIniPath { get; init; } = string.Empty;
        public string ReadmePath { get; init; } = string.Empty;
        public Window? Owner { get; init; }
        public bool HasReadme { get; init; }
        public bool CanSetCharacterInClient { get; init; }
        public Action? SetCharacterInClient { get; init; }
        public Func<Task>? SelectCharacterAsync { get; init; }
        public Func<Task>? RefreshCharacterAsync { get; init; }
        public Func<Task>? RefreshAllAssetsAsync { get; init; }
        public Func<Task>? RefreshAllCharactersAsync { get; init; }
        public Func<Task>? NewCharacterFolderAsync { get; init; }
        public Func<Task>? EditCharacterFolderAsync { get; init; }
        public Func<Task>? DuplicateCharacterFolderAsync { get; init; }
        public Action? OpenCharacterEmoteVisualizer { get; init; }
        public Action? OpenCharacterFolderVisualizer { get; init; }
        public Func<Task>? RunIntegrityVerifierAsync { get; init; }
        public Func<Task>? ViewIntegrityVerifierResultsAsync { get; init; }
        public Func<Task>? DeleteCharacterFolderAsync { get; init; }
        public Func<string, string, Task>? AfterCharacterDeletedAsync { get; init; }
        public Func<Task>? AfterCharacterChangedAsync { get; init; }
    }
}
