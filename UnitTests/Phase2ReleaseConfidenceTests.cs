using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Components;
using OceanyaClient.Features.FileHivemind;
using OceanyaClient.Features.GoogleDriveSync;

namespace UnitTests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public sealed class Phase2ReleaseConfidenceTests
    {
        private static Application? sharedApplication;
        private static Window? sharedRootWindow;
        private string tempRoot = string.Empty;
        private string originalSaveFilePath = string.Empty;
        private string originalPathToConfigIni = string.Empty;
        private List<string> originalBaseFolders = new List<string>();
        private string originalSelectedServerEndpoint = string.Empty;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EnsureSharedApplication();
        }

        [SetUp]
        public void SetUp()
        {
            EnsureSharedApplication();
            CloseNonRootWindows();

            tempRoot = Path.Combine(Path.GetTempPath(), "phase2_release_confidence_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            originalSaveFilePath = SaveFile.CurrentStoragePath;
            originalPathToConfigIni = Globals.PathToConfigINI;
            originalBaseFolders = new List<string>(Globals.BaseFolders);
            originalSelectedServerEndpoint = Globals.SelectedServerEndpoint;

            SaveFile.ConfigureStoragePathForTests(Path.Combine(tempRoot, "savefile.json"));
            SaveFile.ResetForTests(new SaveData(), persist: false);
            SaveFile.Data.UseSingleInternalClient = false;
            Globals.PathToConfigINI = Path.Combine(tempRoot, "config.ini");
            Globals.BaseFolders = new List<string>();
            Globals.SetSelectedServerEndpoint("ws://127.0.0.1:27016");
            OceanyaTestMode.SetCurrent(new OceanyaTestModeOptions
            {
                IsEnabled = true,
                DisableWaitForms = true,
                DisableFakeLoading = true,
                DisableLoadingScreen = true
            });

            ResetStaticTestHooks();
        }

        [TearDown]
        public void TearDown()
        {
            ResetStaticTestHooks();
            OceanyaTestMode.Reset();
            Globals.PathToConfigINI = originalPathToConfigIni;
            Globals.BaseFolders = originalBaseFolders;
            Globals.SelectedServerEndpoint = originalSelectedServerEndpoint;
            SaveFile.ConfigureStoragePathForTests(originalSaveFilePath);
            SaveFile.ResetForTests(new SaveData(), persist: false);
            CloseNonRootWindows();

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        [Test]
        public void MainWindow_SingleClientReconnect_RefreshesProfileStateAndLogsReconnectLifecycle()
        {
            CreateCharacterFolder("Phoenix", "Phoenix Wright", "def");
            RefreshCharactersFromTempRoot();

            MainWindow window = new MainWindow();
            AOClient profileClient = CreateClient("ProfileClient", "Phoenix", "Phoenix Wright", "Phoenix OOC");
            AOClient networkClient = new AOClient("ws://127.0.0.1:27016");
            networkClient.playerID = 81;
            networkClient.iniPuppetID = 13;
            networkClient.curBG = "gk2_courthouse_lobby";
            networkClient.SetPos("pro");

            AddClientToWindow(window, profileClient);
            SetPrivateField(window, "useSingleInternalClient", true);
            SetPrivateField(window, "singleInternalClient", networkClient);
            SetPrivateField(window, "boundSingleClientProfile", profileClient);
            SetPrivateField(window, "currentClient", profileClient);

            InvokePrivate(window, "InitializeCommonClientEvents", profileClient, networkClient);
            InvokePrivate(window, "SelectClient", profileClient);

            networkClient.OnReconnectionAttempt?.Invoke(1);
            networkClient.OnReconnectionAttemptFailed?.Invoke(1);
            networkClient.OnReconnect?.Invoke();

            ICLog icLog = (ICLog)window.FindName("ICLogControl");
            OOCLog oocLog = (OOCLog)window.FindName("OOCLogControl");
            string icText = ReadDocumentText(icLog.FindName("LogBox"));
            string oocText = ReadDocumentText(oocLog.FindName("LogBox"));

            Assert.Multiple(() =>
            {
                Assert.That(profileClient.playerID, Is.EqualTo(81));
                Assert.That(profileClient.iniPuppetID, Is.EqualTo(13));
                Assert.That(profileClient.curBG, Is.EqualTo("gk2_courthouse_lobby"));
                Assert.That(icText, Does.Contain("Reconnecting..."));
                Assert.That(icText, Does.Contain("Attempt 1 failed."));
                Assert.That(icText, Does.Contain("Reconnected to server."));
                Assert.That(oocText, Does.Contain("Reconnecting..."));
                Assert.That(oocText, Does.Contain("Attempt 1 failed."));
                Assert.That(oocText, Does.Contain("Reconnected to server."));
            });

            window.Close();
        }

        [Test]
        public async Task AOCharacterFileCreator_EditExistingFolder_RebuildsLoadedFolderInPlace()
        {
            string mountPath = Path.Combine(tempRoot, "mount");
            string characterDirectory = CreateCharacterFolderInMount(mountPath, "Apollo", "Apollo Justice", "def");

            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            TaskCompletionSource<string> completion = CreateGenerateCompletionSource();

            bool loaded = window.TryLoadCharacterFolderForEditing(characterDirectory, out string errorMessage);

            Assert.That(loaded, Is.True, errorMessage);
            ((TextBox)window.FindName("ShowNameTextBox")).Text = "Apollo Justice Reloaded";

            InvokePrivate(window, "GenerateButton_Click", window, new RoutedEventArgs());

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.That(finished, Is.SameAs(completion.Task), "Edit flow should finish and report success.");

            string charIniText = File.ReadAllText(Path.Combine(characterDirectory, "char.ini"));
            Button generateButton = (Button)window.FindName("GenerateButton");
            TextBlock availability = (TextBlock)window.FindName("CharacterFolderAvailabilityTextBlock");

            Assert.Multiple(() =>
            {
                Assert.That(generateButton.Content?.ToString(), Is.EqualTo("Edit Character Folder"));
                Assert.That(availability.Text, Does.Contain("rebuilt in-place"));
                Assert.That(File.Exists(Path.Combine(characterDirectory, "char.ini")), Is.True);
                Assert.That(charIniText, Does.Contain("showname=Apollo Justice Reloaded"));
                Assert.That(Directory.Exists(Path.Combine(mountPath, "characters", "Apollo - Copy")), Is.False);
            });

            window.Close();
        }

        [Test]
        public async Task AOCharacterFileCreator_EditExistingFolder_BlocksOverwriteOfDifferentExistingFolder()
        {
            string mountPath = Path.Combine(tempRoot, "mount");
            string sourceDirectory = CreateCharacterFolderInMount(mountPath, "Apollo", "Apollo Justice", "def");
            CreateCharacterFolderInMount(mountPath, "Athena", "Athena Cykes", "def");

            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            TaskCompletionSource<string> completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetNonPublicStaticField(
                typeof(AOCharacterFileCreatorWindow),
                "testGenerateMessageOverride",
                new Action<Window?, string, string, MessageBoxButton, MessageBoxImage>((_, message, _, _, _) =>
                {
                    completion.TrySetResult(message);
                }));

            bool loaded = window.TryLoadCharacterFolderForEditing(sourceDirectory, out string errorMessage);
            Assert.That(loaded, Is.True, errorMessage);

            ((TextBox)window.FindName("CharacterFolderNameTextBox")).Text = "Athena";
            InvokePrivate(window, "UpdateFolderAvailabilityStatus");

            TextBlock availability = (TextBlock)window.FindName("CharacterFolderAvailabilityTextBlock");
            Assert.That(availability.Text, Does.Contain("already exists"));

            InvokePrivate(window, "GenerateButton_Click", window, new RoutedEventArgs());

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.That(finished, Is.SameAs(completion.Task), "Blocked edit flow should report a warning.");
            Assert.That(await completion.Task, Does.Contain("Target folder already exists"));
            Assert.That(File.ReadAllText(Path.Combine(sourceDirectory, "char.ini")), Does.Contain("showname=Apollo Justice"));

            window.Close();
        }

        [Test]
        public void AOCharacterFileCreator_DuplicateFolder_LoadsCreateModeWithUniqueCopyName()
        {
            string mountPath = Path.Combine(tempRoot, "mount");
            string sourceDirectory = CreateCharacterFolderInMount(mountPath, "Apollo", "Apollo Justice", "def");
            Directory.CreateDirectory(Path.Combine(mountPath, "characters", "Apollo - Copy"));

            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();

            bool loaded = window.TryLoadCharacterFolderForDuplication(sourceDirectory, out string errorMessage);

            TextBox folderName = (TextBox)window.FindName("CharacterFolderNameTextBox");
            Button generateButton = (Button)window.FindName("GenerateButton");
            TextBlock status = (TextBlock)window.FindName("StatusTextBlock");
            TextBlock availability = (TextBlock)window.FindName("CharacterFolderAvailabilityTextBlock");

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.True, errorMessage);
                Assert.That(folderName.Text, Is.EqualTo("Apollo - Copy 2"));
                Assert.That(generateButton.Content?.ToString(), Is.EqualTo("Generate Character Folder"));
                Assert.That(status.Text, Is.EqualTo("Loaded character folder for duplication."));
                Assert.That(availability.Text, Does.Contain("available folder name"));
            });

            window.Close();
        }

        [Test]
        public async Task AOCharacterFileCreator_DuplicateFolder_GenerateCreatesSeparateFolderWithoutChangingOriginal()
        {
            string mountPath = Path.Combine(tempRoot, "mount");
            string sourceDirectory = CreateCharacterFolderInMount(mountPath, "Apollo", "Apollo Justice", "def");

            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            TaskCompletionSource<string> completion = CreateGenerateCompletionSource();

            bool loaded = window.TryLoadCharacterFolderForDuplication(sourceDirectory, out string errorMessage);
            Assert.That(loaded, Is.True, errorMessage);

            ((TextBox)window.FindName("ShowNameTextBox")).Text = "Apollo Justice Clone";
            InvokePrivate(window, "GenerateButton_Click", window, new RoutedEventArgs());

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.That(finished, Is.SameAs(completion.Task), "Duplicate flow should finish and report success.");

            string duplicateDirectory = Path.Combine(mountPath, "characters", "Apollo - Copy");

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(sourceDirectory, "char.ini")), Does.Contain("showname=Apollo Justice"));
                Assert.That(Directory.Exists(duplicateDirectory), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(duplicateDirectory, "char.ini")), Does.Contain("showname=Apollo Justice Clone"));
            });

            window.Close();
        }

        [Test]
        public void CharacterIntegrityVerifierResultsWindow_LoadsPersistedFailuresIntoSummaryAndRows()
        {
            string characterDirectory = Path.Combine(tempRoot, "Apollo");
            Directory.CreateDirectory(characterDirectory);
            string charIniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                charIniPath,
                "[Options]\n"
                + "showname=Apollo\n"
                + "emotes=\"\"\n"
                + "[Emotions]\n"
                + "number=2\n"
                + "1=normal#-#normal#0#0\n"
                + "2=talk#-#talk#0#0\n");

            CharacterIntegrityReport report = CharacterIntegrityVerifier.RunAndPersist(characterDirectory, charIniPath, "Apollo");

            CharacterIntegrityVerifierResultsWindow window = new CharacterIntegrityVerifierResultsWindow(report, characterDirectory, "Apollo");

            DataGrid resultsGrid = (DataGrid)window.FindName("ResultsDataGrid");
            CharacterIntegrityIssueViewModel blankEmotesRow = resultsGrid.Items
                .OfType<CharacterIntegrityIssueViewModel>()
                .First(row => string.Equals(row.TestName, "Blank emotes Definition", StringComparison.OrdinalIgnoreCase));
            TextBlock summary = (TextBlock)window.FindName("SummaryTextBlock");

            Assert.Multiple(() =>
            {
                Assert.That(blankEmotesRow.Passed, Is.False);
                Assert.That(blankEmotesRow.CanAutoFix, Is.True);
                Assert.That(summary.Text, Does.Contain("Failed checks:"));
                Assert.That(resultsGrid.Items.Count, Is.EqualTo(report.Results.Count));
            });

            window.Close();
        }

        [Test]
        public void CharacterIntegrityVerifier_RerunSingleTest_AfterBlankEmotesFixMarksBlankEmotesRowPassed()
        {
            string characterDirectory = Path.Combine(tempRoot, "ApolloRerun");
            Directory.CreateDirectory(characterDirectory);
            string charIniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                charIniPath,
                "[Options]\n"
                + "showname=Apollo\n"
                + "emotes=\"\"\n"
                + "[Emotions]\n"
                + "number=2\n"
                + "1=normal#-#normal#0#0\n"
                + "2=talk#-#talk#0#0\n");

            CharacterIntegrityReport report = CharacterIntegrityVerifier.RunAndPersist(characterDirectory, charIniPath, "Apollo");
            CharacterIntegrityIssue issue = report.Results.First(result =>
                string.Equals(result.TestName, "Blank emotes Definition", StringComparison.OrdinalIgnoreCase) && !result.Passed);

            bool applied = CharacterIntegrityVerifier.TryApplyFix(report, issue, out string fixMessage);
            CharacterIntegrityReport rerunReport = CharacterIntegrityVerifier.RerunSingleTest(report, issue);
            CharacterIntegrityIssue rerunIssue = rerunReport.Results.First(result =>
                string.Equals(result.TestName, "Blank emotes Definition", StringComparison.OrdinalIgnoreCase));

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True, fixMessage);
                Assert.That(File.ReadAllText(charIniPath), Does.Contain("emotes=2"));
                Assert.That(rerunIssue.Passed, Is.True);
                Assert.That(rerunReport.Results.Count(result => string.Equals(result.TestName, "Blank emotes Definition", StringComparison.OrdinalIgnoreCase)),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public void OceanyanFileHivemindWindow_ExportConnection_WritesSelectedConnectionFile()
        {
            FileHivemindConnectionProfile connection = CreateEligibleConnection("export-connection", autoSyncEnabled: true);
            SaveFile.Data.FileHivemind.Connections.Add(connection);
            SaveFile.Data.FileHivemind.SelectedConnectionId = connection.Id;

            OceanyanFileHivemindWindow window = CreateHivemindWindow();
            InvokePrivate(window, "RefreshConnections", connection.Id);

            string exportPath = Path.Combine(tempRoot, "phase2-export.oceanyahive.json");
            SetNonPublicStaticField(
                typeof(OceanyanFileHivemindWindow),
                "testSaveFileDialogOverride",
                new Func<Microsoft.Win32.SaveFileDialog, bool?>(dialog =>
                {
                    dialog.FileName = exportPath;
                    return true;
                }));

            InvokePrivate(window, "ExportConnectionButton_Click", window, new RoutedEventArgs());

            string exportedText = File.ReadAllText(exportPath);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(exportPath), Is.True);
                Assert.That(exportedText, Does.Contain(connection.GoogleDrive.RemoteFolderId));
                Assert.That(exportedText, Does.Contain(connection.DisplayName));
            });

            window.Close();
        }

        [Test]
        public async Task OceanyanFileHivemindWindow_ImportConnection_PersistsImportedConnectionAndSelectsIt()
        {
            GoogleDriveSecureClientCredentialStore credentialStore = new GoogleDriveSecureClientCredentialStore(
                Path.Combine(tempRoot, "creds"),
                new ObfuscatingProtector());
            FileHivemindConnectionProfile exportedConnection = CreateEligibleConnection("import-source", autoSyncEnabled: true);
            exportedConnection.DisplayName = "Imported Phase 2 Connection";
            exportedConnection.GoogleDrive.RemoteFolderId = "phase2-remote-folder";
            exportedConnection.GoogleDrive.TokenStoreKey = "phase2-import-token";
            exportedConnection.GoogleDrive.LastSignedInEmail = "imported@example.com";

            string importPath = Path.Combine(tempRoot, "phase2-import.oceanyahive.json");
            File.WriteAllText(
                importPath,
                FileHivemindConnectionExchangeSerializer.Serialize(exportedConnection, credentialStore),
                new UTF8Encoding(false));

            TaskCompletionSource<string> completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetNonPublicStaticField(
                typeof(OceanyanFileHivemindWindow),
                "testOpenFileDialogOverride",
                new Func<Microsoft.Win32.OpenFileDialog, bool?>(dialog =>
                {
                    dialog.FileName = importPath;
                    return true;
                }));
            SetNonPublicStaticField(
                typeof(OceanyanFileHivemindWindow),
                "testPromptForImportedConnectionAccountOverride",
                new Func<Window, FileHivemindConnectionProfile, GoogleDriveSignedInAccount?>((_, connection) =>
                {
                    connection.GoogleDrive.LastSignedInEmail = "imported@example.com";
                    return new GoogleDriveSignedInAccount();
                }));
            SetNonPublicStaticField(
                typeof(OceanyanFileHivemindWindow),
                "testImportSyncOverride",
                new Func<string, FileHivemindConnectionProfile, Task<bool>>((_, _) => Task.FromResult(true)));
            SetNonPublicStaticField(
                typeof(OceanyanFileHivemindWindow),
                "testMessageBoxOverride",
                new Func<Window?, string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult>((_, message, title, _, _) =>
                {
                    if (string.Equals(title, "Import Complete", StringComparison.Ordinal))
                    {
                        completion.TrySetResult(message);
                    }

                    return MessageBoxResult.OK;
                }));

            OceanyanFileHivemindWindow window = new OceanyanFileHivemindWindow(
                manageStartupWaitForm: false,
                runtimeStateStore: new GoogleDriveConnectionRuntimeStateStore(Path.Combine(tempRoot, "runtime")),
                credentialStore: credentialStore,
                backgroundAgentLauncher: new FileHivemindBackgroundAgentLauncher(
                    new FakeAutoStartRegistrar(),
                    _ => true,
                    () => false),
                backgroundLogStore: new FileHivemindBackgroundLogStore(Path.Combine(tempRoot, "background.log")));

            InvokePrivate(window, "ImportConnectionButton_Click", window, new RoutedEventArgs());

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.That(finished, Is.SameAs(completion.Task), "Import flow should finish and report success.");

            FileHivemindConnectionProfile imported = SaveFile.Data.FileHivemind.Connections.Single();
            Assert.Multiple(() =>
            {
                Assert.That(SaveFile.Data.FileHivemind.SelectedConnectionId, Is.EqualTo(imported.Id));
                Assert.That(imported.DisplayName, Is.EqualTo("Imported Phase 2 Connection"));
                Assert.That(imported.GoogleDrive.RemoteFolderId, Is.EqualTo("phase2-remote-folder"));
                Assert.That(imported.GoogleDrive.LastSignedInEmail, Is.EqualTo("imported@example.com"));
            });

            window.Close();
        }

        [Test]
        public void OceanyanFileHivemindWindow_DeleteConnection_RemovesSelectedConnectionAndRuntimeState()
        {
            FileHivemindConnectionProfile connection = CreateEligibleConnection("delete-connection", autoSyncEnabled: true);
            SaveFile.Data.FileHivemind.Connections.Add(connection);
            SaveFile.Data.FileHivemind.SelectedConnectionId = connection.Id;

            string runtimeRoot = Path.Combine(tempRoot, "runtime");
            GoogleDriveConnectionRuntimeStateStore runtimeStateStore = new GoogleDriveConnectionRuntimeStateStore(runtimeRoot);
            runtimeStateStore.Save(new GoogleDriveConnectionRuntimeState
            {
                ConnectionId = connection.Id,
                RootFolderId = connection.GoogleDrive.RemoteFolderId,
                LastStatusMessage = "ready"
            });

            SetNonPublicStaticField(
                typeof(OceanyanFileHivemindWindow),
                "testMessageBoxOverride",
                new Func<Window?, string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult>((_, _, title, buttons, _) =>
                {
                    return string.Equals(title, "Delete Connection", StringComparison.Ordinal) && buttons == MessageBoxButton.YesNo
                        ? MessageBoxResult.Yes
                        : MessageBoxResult.OK;
                }));

            OceanyanFileHivemindWindow window = new OceanyanFileHivemindWindow(
                manageStartupWaitForm: false,
                runtimeStateStore: runtimeStateStore,
                credentialStore: new GoogleDriveSecureClientCredentialStore(Path.Combine(tempRoot, "creds"), new ObfuscatingProtector()),
                backgroundAgentLauncher: new FileHivemindBackgroundAgentLauncher(
                    new FakeAutoStartRegistrar(),
                    _ => true,
                    () => false),
                backgroundLogStore: new FileHivemindBackgroundLogStore(Path.Combine(tempRoot, "background.log")));
            InvokePrivate(window, "RefreshConnections", connection.Id);

            InvokePrivate(window, "DeleteConnectionButton_Click", window, new RoutedEventArgs());

            Assert.Multiple(() =>
            {
                Assert.That(SaveFile.Data.FileHivemind.Connections, Is.Empty);
                Assert.That(SaveFile.Data.FileHivemind.SelectedConnectionId, Is.Empty);
                Assert.That(File.Exists(runtimeStateStore.GetFilePath(connection.Id)), Is.False);
            });

            window.Close();
        }

        private TaskCompletionSource<string> CreateGenerateCompletionSource()
        {
            TaskCompletionSource<string> completion =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetNonPublicStaticField(
                typeof(AOCharacterFileCreatorWindow),
                "testGenerateMessageOverride",
                new Action<Window?, string, string, MessageBoxButton, MessageBoxImage>((_, message, _, _, image) =>
                {
                    if (image == MessageBoxImage.Information)
                    {
                        completion.TrySetResult(message);
                    }
                    else
                    {
                        completion.TrySetException(new InvalidOperationException(message));
                    }
                }));
            return completion;
        }

        private void RefreshCharactersFromTempRoot()
        {
            Globals.BaseFolders = new List<string> { tempRoot };
            Globals.PathToConfigINI = Path.Combine(tempRoot, "config.ini");
            File.WriteAllText(Globals.PathToConfigINI, "mount_paths=\n");
            ResetCharacterCache();
            CharacterFolder.RefreshCharacterList();
        }

        private void CreateCharacterFolder(string folderName, string showName, string side)
        {
            CreateCharacterFolderInMount(tempRoot, folderName, showName, side);
        }

        private string CreateCharacterFolderInMount(string mountPath, string folderName, string showName, string side)
        {
            string characterDirectory = Path.Combine(mountPath, "characters", folderName);
            Directory.CreateDirectory(characterDirectory);
            Directory.CreateDirectory(Path.Combine(characterDirectory, "Emotions"));
            File.WriteAllText(
                Path.Combine(characterDirectory, "char.ini"),
                "[Options]\n"
                + $"showname={showName}\n"
                + $"side={side}\n"
                + "gender=male\n"
                + "[Emotions]\n"
                + "number=1\n"
                + "1=normal#-#normal#0#1\n");
            CreateSolidPng(Path.Combine(characterDirectory, "char_icon.png"), Colors.SteelBlue);
            CreateSolidPng(Path.Combine(characterDirectory, "(a)normal.png"), Colors.OrangeRed);
            CreateSolidPng(Path.Combine(characterDirectory, "normal.png"), Colors.ForestGreen);
            CreateSolidPng(Path.Combine(characterDirectory, "pre_normal.png"), Colors.Goldenrod);
            CreateSolidPng(Path.Combine(characterDirectory, "Emotions", "button1_off.png"), Colors.MediumPurple);
            return characterDirectory;
        }

        private AOClient CreateClient(string clientName, string characterName, string icShowname, string oocShowname)
        {
            AOClient client = new AOClient("ws://127.0.0.1:27016");
            client.clientName = clientName;
            client.SetCharacter(characterName);
            client.SetICShowname(icShowname);
            client.OOCShowname = oocShowname;
            return client;
        }

        private static void AddClientToWindow(MainWindow window, AOClient client)
        {
            IDictionary clients = (IDictionary)GetPrivateField(window, "clients");
            System.Windows.Controls.Primitives.ToggleButton toggleButton =
                new System.Windows.Controls.Primitives.ToggleButton();
            clients.Add(toggleButton, client);

            object emoteGrid = GetPrivateField(window, "EmoteGrid");
            MethodInfo addElement = emoteGrid.GetType().GetMethod("AddElement", BindingFlags.Instance | BindingFlags.Public)!
                ?? throw new InvalidOperationException("EmoteGrid.AddElement not found.");
            addElement.Invoke(emoteGrid, new object[] { toggleButton });
        }

        private OceanyanFileHivemindWindow CreateHivemindWindow()
        {
            return new OceanyanFileHivemindWindow(
                manageStartupWaitForm: false,
                runtimeStateStore: new GoogleDriveConnectionRuntimeStateStore(Path.Combine(tempRoot, "runtime")),
                credentialStore: new GoogleDriveSecureClientCredentialStore(Path.Combine(tempRoot, "creds"), new ObfuscatingProtector()),
                backgroundAgentLauncher: new FileHivemindBackgroundAgentLauncher(
                    new FakeAutoStartRegistrar(),
                    _ => true,
                    () => false),
                backgroundLogStore: new FileHivemindBackgroundLogStore(Path.Combine(tempRoot, "background.log")));
        }

        private static FileHivemindConnectionProfile CreateEligibleConnection(string id, bool autoSyncEnabled)
        {
            return new FileHivemindConnectionProfile
            {
                Id = id,
                AutoSyncEnabled = autoSyncEnabled,
                ProviderId = FileHivemindProviderIds.GoogleDrive,
                DisplayName = "Phase 2 Connection",
                GoogleDrive = new GoogleDriveSyncSettings
                {
                    OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                    OAuthClientSecret = "desktop-client-secret",
                    RemoteFolderId = "folder-id",
                    LocalFolderPath = @"C:\sync",
                    TokenStoreKey = "token-key",
                    LastSignedInEmail = "tester@example.com"
                }
            };
        }

        private static string ReadDocumentText(object? element)
        {
            RichTextBox richTextBox = (RichTextBox)element!;
            return new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd).Text;
        }

        private static string CreateSolidPng(string path, Color color)
        {
            byte[] pixels = new byte[16 * 16 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = color.A;
            }

            BitmapSource bitmap = BitmapSource.Create(
                16,
                16,
                96,
                96,
                PixelFormats.Pbgra32,
                null,
                pixels,
                16 * 4);

            using FileStream stream = File.Create(path);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            return path;
        }

        private static void ResetStaticTestHooks()
        {
            InvokeStaticNoArg(typeof(MainWindow), "ResetTestHooks");
            InvokeStaticNoArg(typeof(AOCharacterFileCreatorWindow), "ResetGenerateTestHooks");
            InvokeStaticNoArg(typeof(CharacterIntegrityVerifierResultsWindow), "ResetTestHooks");
            InvokeStaticNoArg(typeof(OceanyanFileHivemindWindow), "ResetTestHooks");
        }

        private static void InvokeStaticNoArg(Type type, string methodName)
        {
            MethodInfo? method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            method?.Invoke(null, Array.Empty<object>());
        }

        private static async Task InvokePrivateAsync(object target, string methodName, params object?[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                ?? throw new InvalidOperationException("Method not found: " + methodName);
            object? result = method.Invoke(target, args);
            if (result is Task task)
            {
                await task;
            }
        }

        private static void InvokePrivate(object target, string methodName, params object?[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                ?? throw new InvalidOperationException("Method not found: " + methodName);
            method.Invoke(target, args);
            WaitForDispatcher();
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                ?? throw new InvalidOperationException("Field not found: " + fieldName);
            return field.GetValue(target)!;
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                ?? throw new InvalidOperationException("Field not found: " + fieldName);
            field.SetValue(target, value);
        }

        private static void SetNonPublicStaticField(Type type, string fieldName, object? value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)!
                ?? throw new InvalidOperationException("Field not found: " + fieldName);
            field.SetValue(null, value);
        }

        private static void WaitForDispatcher()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Application.Current!.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static void EnsureSharedApplication()
        {
            sharedApplication = WpfTestApplicationContext.EnsureCreated();

            if (sharedRootWindow == null)
            {
                sharedRootWindow = new Window
                {
                    Width = 1,
                    Height = 1,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                    Visibility = Visibility.Hidden
                };
                sharedRootWindow.Show();
                sharedRootWindow.Hide();
            }

            sharedApplication.MainWindow = sharedRootWindow;
        }

        private static void CloseNonRootWindows()
        {
            if (Application.Current == null)
            {
                return;
            }

            List<Window> windowsToClose = Application.Current.Windows
                .OfType<Window>()
                .Where(window => !ReferenceEquals(window, sharedRootWindow))
                .ToList();

            foreach (Window window in windowsToClose)
            {
                try
                {
                    window.Close();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            WaitForDispatcher();
            Application.Current.MainWindow = sharedRootWindow;
        }

        private static void ResetCharacterCache()
        {
            Type type = typeof(CharacterFolder);
            type.GetField("characterConfigs", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
            type.GetField("cacheFile", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
            type.GetField("cachePathInitialized", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, false);
        }

        private sealed class FakeAutoStartRegistrar : IFileHivemindAutoStartRegistrar
        {
            private bool registered;

            public bool IsRegistered()
            {
                return registered;
            }

            public void SetRegistered(bool enabled)
            {
                registered = enabled;
            }
        }

        private sealed class ObfuscatingProtector : ISecretProtector
        {
            public byte[] Protect(byte[] value)
            {
                byte[] bytes = value ?? Array.Empty<byte>();
                return Encoding.UTF8.GetBytes(Convert.ToBase64String(bytes));
            }

            public byte[] Unprotect(byte[] value)
            {
                string protectedValue = Encoding.UTF8.GetString(value ?? Array.Empty<byte>());
                return Convert.FromBase64String(protectedValue);
            }
        }
    }
}
