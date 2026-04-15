using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    public sealed class Phase1ReleaseConfidenceTests
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

            tempRoot = Path.Combine(Path.GetTempPath(), "phase1_release_confidence_" + Guid.NewGuid().ToString("N"));
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

            MainWindow.ResetTestHooks();
            AOCharacterFileCreatorWindow.ResetGenerateTestHooks();
        }

        [TearDown]
        public void TearDown()
        {
            MainWindow.ResetTestHooks();
            AOCharacterFileCreatorWindow.ResetGenerateTestHooks();
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
        public void MainWindow_SelectClient_SwitchesDisplayedStateWithoutMixingLogs()
        {
            CreateCharacterFolder("Phoenix", "Phoenix Wright", "def");
            CreateCharacterFolder("Edgeworth", "Miles Edgeworth", "pro");
            RefreshCharactersFromTempRoot();

            MainWindow window = new MainWindow();
            AOClient firstClient = CreateClient("FirstClient", "Phoenix", "First IC", "First OOC");
            AOClient secondClient = CreateClient("SecondClient", "Edgeworth", "Second IC", "Second OOC");

            InvokePrivate(window, "AddLoggedIcMessage", firstClient, "First IC", "first-only-message", true, ICMessage.TextColors.White);
            InvokePrivate(window, "AddLoggedIcMessage", secondClient, "Second IC", "second-only-message", true, ICMessage.TextColors.White);

            InvokePrivate(window, "SelectClient", firstClient);

            OOCLog oocLog = (OOCLog)window.FindName("OOCLogControl");
            ICLog icLog = (ICLog)window.FindName("ICLogControl");
            ICMessageSettings settings = (ICMessageSettings)window.FindName("ICMessageSettingsControl");

            TextBox oocShowname = (TextBox)oocLog.FindName("txtOOCShowname");
            TextBox icShowname = (TextBox)settings.FindName("txtICShowname");

            Assert.Multiple(() =>
            {
                Assert.That(icLog.GetCurrentClient(), Is.SameAs(firstClient));
                Assert.That(oocShowname.Text, Is.EqualTo("First OOC"));
                Assert.That(icShowname.Text, Is.EqualTo("First IC"));
                Assert.That(ReadDocumentText(icLog.FindName("LogBox")), Does.Contain("first-only-message"));
                Assert.That(ReadDocumentText(icLog.FindName("LogBox")), Does.Not.Contain("second-only-message"));
            });

            InvokePrivate(window, "SelectClient", secondClient);

            Assert.Multiple(() =>
            {
                Assert.That(icLog.GetCurrentClient(), Is.SameAs(secondClient));
                Assert.That(oocShowname.Text, Is.EqualTo("Second OOC"));
                Assert.That(icShowname.Text, Is.EqualTo("Second IC"));
                Assert.That(ReadDocumentText(icLog.FindName("LogBox")), Does.Contain("second-only-message"));
                Assert.That(ReadDocumentText(icLog.FindName("LogBox")), Does.Not.Contain("first-only-message"));
            });

            window.Close();
        }

        [Test]
        public async Task MainWindow_AddClient_WhenConnectFails_RestoresUiAndDoesNotAddClient()
        {
            CreateCharacterFolder("Phoenix", "Phoenix Wright", "def");
            RefreshCharactersFromTempRoot();

            string? shownTitle = null;
            string? shownMessage = null;

            SetNonPublicStaticField(
                typeof(MainWindow),
                "testConnectClientAsyncOverride",
                new Func<AOClient, Task>(_ => throw new InvalidOperationException("Simulated connection failure")));
            SetNonPublicStaticField(
                typeof(MainWindow),
                "testMessageBoxOverride",
                new Action<Window?, string, string, MessageBoxButton, MessageBoxImage>((_, message, title, _, _) =>
                {
                    shownTitle = title;
                    shownMessage = message;
                }));

            MainWindow window = new MainWindow();

            await InvokePrivateAsync(window, "AddClientAsync", "BrokenClient");

            IDictionary clients = (IDictionary)GetPrivateField(window, "clients");
            object? currentClient = GetPrivateField(window, "currentClient");

            Assert.Multiple(() =>
            {
                Assert.That(window.IsEnabled, Is.True);
                Assert.That(clients.Count, Is.EqualTo(0));
                Assert.That(currentClient, Is.Null);
                Assert.That(shownTitle, Is.EqualTo("Connection Failed"));
                Assert.That(shownMessage, Does.Contain("Simulated connection failure"));
            });

            window.Close();
        }

        [Test]
        public void MainWindow_DirectClientReceiveHandlers_RenderIncomingIcAndActionMessagesIntoClientLog()
        {
            CreateCharacterFolder("Phoenix", "Phoenix Wright", "def");
            RefreshCharactersFromTempRoot();

            MainWindow window = new MainWindow();
            AOClient client = CreateClient("PacketClient", "Phoenix", "Phoenix Wright", "Phoenix Wright");
            client.iniPuppetID = 7;

            AddClientToWindow(window, client);
            InvokePrivate(window, "AttachDirectClientMessageHandlers", client);
            InvokePrivate(window, "SelectClient", client);

            client.OnICMessageReceived?.Invoke(new ICMessage
            {
                CharId = 7,
                ShowName = "Phoenix Wright",
                Message = "Hold it from receive path",
                TextColor = ICMessage.TextColors.Red
            });
            client.OnIcActionReceived?.Invoke("Phoenix Wright", "shouts OBJECTION!", true, ICMessage.TextColors.White);

            ICLog icLog = (ICLog)window.FindName("ICLogControl");
            string logText = ReadDocumentText(icLog.FindName("LogBox"));

            Assert.That(logText, Does.Contain("Hold it from receive path"));
            Assert.That(logText, Does.Contain("OBJECTION!"));

            window.Close();
        }

        [Test]
        public void MainWindow_DirectClientReceiveHandlers_RenderIncomingOocMessagesIntoClientStream()
        {
            CreateCharacterFolder("Phoenix", "Phoenix Wright", "def");
            RefreshCharactersFromTempRoot();

            MainWindow window = new MainWindow();
            AOClient client = CreateClient("PacketClient", "Phoenix", "Phoenix Wright", "Phoenix OOC");

            AddClientToWindow(window, client);
            InvokePrivate(window, "AttachDirectClientMessageHandlers", client);
            InvokePrivate(window, "SelectClient", client);

            client.OnOOCMessageReceived?.Invoke("Judge", "OOC hello from receive path", true);

            OOCLog oocLog = (OOCLog)window.FindName("OOCLogControl");
            string logText = ReadDocumentText(oocLog.FindName("LogBox"));

            Assert.That(logText, Does.Contain("OOC hello from receive path"));

            window.Close();
        }

        [Test]
        public async Task AOCharacterFileCreator_GenerateButton_CopiesAssetsAndRespectsOutputOverrides()
        {
            string mountPath = Path.Combine(tempRoot, "mount");
            Directory.CreateDirectory(Path.Combine(mountPath, "characters"));

            string preanimPath = CreateSolidPng(Path.Combine(tempRoot, "pre.png"), Colors.CornflowerBlue);
            string animationPath = CreateSolidPng(Path.Combine(tempRoot, "anim.png"), Colors.OrangeRed);
            string sfxPath = Path.Combine(tempRoot, "voice.wav");
            File.WriteAllBytes(sfxPath, new byte[] { 1, 2, 3, 4 });
            string iconPath = CreateSolidPng(Path.Combine(tempRoot, "char_icon.png"), Colors.Goldenrod);
            string extraPath = Path.Combine(tempRoot, "notes.txt");
            File.WriteAllText(extraPath, "phase1 export extra");

            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            TaskCompletionSource<string> completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
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

            ((AutoCompleteDropdownField)window.FindName("MountPathComboBox")).Text = mountPath;
            ((TextBox)window.FindName("CharacterFolderNameTextBox")).Text = "Phase1Apollo";
            ((TextBox)window.FindName("ShowNameTextBox")).Text = "Apollo Justice";
            ((AutoCompleteDropdownField)window.FindName("SideComboBox")).Text = "def";
            ((AutoCompleteDropdownField)window.FindName("GenderBlipsDropdown")).Text = "male";
            ((AutoCompleteDropdownField)window.FindName("ChatDropdown")).Text = "default";
            ((AutoCompleteDropdownField)window.FindName("EffectsDropdown")).Text = "default";
            ((AutoCompleteDropdownField)window.FindName("ScalingDropdown")).Text = "default";
            ((AutoCompleteDropdownField)window.FindName("StretchDropdown")).Text = "default";
            ((AutoCompleteDropdownField)window.FindName("NeedsShownameDropdown")).Text = "false";

            ObservableCollection<CharacterCreationEmoteViewModel> emotes =
                (ObservableCollection<CharacterCreationEmoteViewModel>)GetPrivateField(window, "emotes");
            emotes.Add(new CharacterCreationEmoteViewModel
            {
                Index = 1,
                Name = "normal",
                PreAnimation = "pre_normal",
                Animation = "normal",
                PreAnimationAssetSourcePath = preanimPath,
                AnimationAssetSourcePath = animationPath,
                SfxAssetSourcePath = sfxPath
            });

            SetPrivateField(window, "selectedCharacterIconSourcePath", iconPath);

            ObservableCollection<ExternalOrganizationEntry> externalEntries =
                (ObservableCollection<ExternalOrganizationEntry>)GetPrivateField(window, "externalOrganizationEntries");
            externalEntries.Add(new ExternalOrganizationEntry
            {
                RelativePath = "Docs/notes.txt",
                SourcePath = extraPath,
                IsFolder = false
            });

            Dictionary<string, string> overrides =
                (Dictionary<string, string>)GetPrivateField(window, "generatedOrganizationOverrides");
            overrides["emote:1:anim"] = "Custom/anim_override.png";

            InvokePrivate(window, "GenerateButton_Click", window, new RoutedEventArgs());

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.That(finished, Is.SameAs(completion.Task), "Generate flow should finish and report success.");

            string characterDirectory = Path.Combine(mountPath, "characters", "Phase1Apollo");
            string charIniPath = Path.Combine(characterDirectory, "char.ini");
            string charIni = File.ReadAllText(charIniPath);

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(characterDirectory), Is.True);
                Assert.That(File.Exists(charIniPath), Is.True);
                Assert.That(File.Exists(Path.Combine(characterDirectory, "Images", "pre.png")), Is.True);
                Assert.That(File.Exists(Path.Combine(characterDirectory, "Custom", "anim_override.png")), Is.True);
                Assert.That(File.Exists(Path.Combine(characterDirectory, "Sounds", "voice.wav")), Is.True);
                Assert.That(File.Exists(Path.Combine(characterDirectory, "char_icon.png")), Is.True);
                Assert.That(File.Exists(Path.Combine(characterDirectory, "Docs", "notes.txt")), Is.True);
                Assert.That(charIni, Does.Contain("Images/pre"));
                Assert.That(charIni, Does.Contain("Custom/anim_override"));
                Assert.That(charIni, Does.Contain("../../characters/Phase1Apollo/Sounds/voice"));
            });

            window.Close();
        }

        [Test]
        public void CharacterFolderVisualizer_SearchText_FiltersProjectedItemsByName()
        {
            CreateCharacterFolder("Apollo", "Apollo Justice", "def");
            CreateCharacterFolder("Athena", "Athena Cykes", "def");
            RefreshCharactersFromTempRoot();

            CharacterFolderVisualizerWindow window = new CharacterFolderVisualizerWindow(onAssetsRefreshed: null, suppressInitialLoadWaitForm: true);
            window.LoadCharacterItemsForTests();

            SetPrivateField(window, "searchText", "athena");
            ICollectionView view = (ICollectionView)InvokePrivateWithResult(window, "GetOrCreateItemsView");
            view.Refresh();
            WaitForDispatcher();
            List<FolderVisualizerItem> filtered = view.Cast<FolderVisualizerItem>().ToList();

            Assert.That(filtered.Select(item => item.Name).ToArray(), Is.EqualTo(new[] { "Athena" }));

            window.Close();
        }

        [Test]
        public async Task CharacterFolderVisualizer_ActiveIncludeTagFilter_RestrictsProjectedItems()
        {
            CreateCharacterFolder("Apollo", "Apollo Justice", "def");
            CreateCharacterFolder("Athena", "Athena Cykes", "def");
            RefreshCharactersFromTempRoot();

            CharacterFolderVisualizerWindow window = new CharacterFolderVisualizerWindow(onAssetsRefreshed: null, suppressInitialLoadWaitForm: true);
            window.LoadCharacterItemsForTests();

            IReadOnlyList<FolderVisualizerItem> items = window.FolderItems;
            Dictionary<string, HashSet<string>> folderTagsByDirectory =
                (Dictionary<string, HashSet<string>>)GetPrivateField(window, "folderTagsByDirectory");
            HashSet<string> activeIncludeFilters =
                (HashSet<string>)GetPrivateField(window, "activeIncludeTagFilters");

            folderTagsByDirectory["Athena"] =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hero" };
            activeIncludeFilters.Add("hero");

            ICollectionView view = (ICollectionView)InvokePrivateWithResult(window, "GetOrCreateItemsView");
            view.Refresh();
            WaitForDispatcher();
            List<FolderVisualizerItem> filtered = view.Cast<FolderVisualizerItem>().ToList();

            Assert.That(filtered.Select(item => item.Name).ToArray(), Is.EqualTo(new[] { "Athena" }));

            window.Close();
        }

        [Test]
        public void OceanyanFileHivemindWindow_RunAtStartupToggle_PersistsAndRequestsLauncherUpdate()
        {
            int registrationCalls = 0;
            int launchCalls = 0;
            FakeAutoStartRegistrar registrar = new FakeAutoStartRegistrar(() => registrationCalls++);
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                registrar,
                _ =>
                {
                    launchCalls++;
                    return true;
                },
                () => false);

            FileHivemindConnectionProfile connection = CreateEligibleConnection("phase1-runatstartup", autoSyncEnabled: true);
            SaveFile.Data.FileHivemind.Connections.Add(connection);
            SaveFile.Data.FileHivemind.RunAgentAtStartup = false;

            OceanyanFileHivemindWindow window = CreateHivemindWindow(launcher);
            InvokePrivate(window, "RefreshConnections", connection.Id);

            CheckBox runAtStartup = (CheckBox)window.FindName("RunAtStartupCheckBox");
            runAtStartup.IsChecked = true;
            InvokePrivate(window, "RunAtStartupCheckBox_Changed", runAtStartup, new RoutedEventArgs());

            Assert.Multiple(() =>
            {
                Assert.That(SaveFile.Data.FileHivemind.RunAgentAtStartup, Is.True);
                Assert.That(SaveFile.Data.FileHivemind.BackgroundStartupPreferenceConfigured, Is.True);
                Assert.That(registrationCalls, Is.GreaterThan(0));
                Assert.That(launchCalls, Is.GreaterThanOrEqualTo(0));
            });

            window.Close();
        }

        [Test]
        public void OceanyanFileHivemindWindow_SelectedConnectionAutoSyncToggle_PersistsAndRequestsLauncherUpdate()
        {
            int registrationCalls = 0;
            int launchCalls = 0;
            FakeAutoStartRegistrar registrar = new FakeAutoStartRegistrar(() => registrationCalls++);
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                registrar,
                _ =>
                {
                    launchCalls++;
                    return true;
                },
                () => false);

            FileHivemindConnectionProfile connection = CreateEligibleConnection("phase1-autosync", autoSyncEnabled: false);
            SaveFile.Data.FileHivemind.RunAgentAtStartup = true;
            SaveFile.Data.FileHivemind.Connections.Add(connection);

            OceanyanFileHivemindWindow window = CreateHivemindWindow(launcher);
            InvokePrivate(window, "RefreshConnections", connection.Id);

            CheckBox autoSync = (CheckBox)window.FindName("SelectedConnectionAutoSyncCheckBox");
            autoSync.IsChecked = true;
            InvokePrivate(window, "SelectedConnectionAutoSyncCheckBox_Changed", autoSync, new RoutedEventArgs());

            Assert.Multiple(() =>
            {
                Assert.That(connection.AutoSyncEnabled, Is.True);
                Assert.That(registrationCalls, Is.GreaterThan(0));
                Assert.That(launchCalls, Is.GreaterThanOrEqualTo(0));
            });

            window.Close();
        }

        [Test]
        public void OceanyanFileHivemindWindow_GlobalSettings_TogglesPersistToastAndPollInterval()
        {
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                new FakeAutoStartRegistrar(),
                _ => true,
                () => false);

            SaveFile.Data.FileHivemind.RemotePollIntervalSeconds = 30;
            OceanyanFileHivemindWindow window = CreateHivemindWindow(launcher);

            CheckBox toasts = (CheckBox)window.FindName("DesktopToastsCheckBox");
            TextBox pollTextBox = (TextBox)window.FindName("RemotePollIntervalTextBox");

            toasts.IsChecked = true;
            InvokePrivate(window, "DesktopToastsCheckBox_Changed", toasts, new RoutedEventArgs());

            pollTextBox.Text = "45";
            InvokePrivate(window, "RemotePollIntervalTextBox_LostFocus", pollTextBox, new RoutedEventArgs());

            Assert.Multiple(() =>
            {
                Assert.That(SaveFile.Data.FileHivemind.ShowDesktopToasts, Is.True);
                Assert.That(SaveFile.Data.FileHivemind.DesktopToastPreferenceConfigured, Is.True);
                Assert.That(SaveFile.Data.FileHivemind.RemotePollIntervalSeconds, Is.EqualTo(45));
            });

            window.Close();
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
            string characterDirectory = Path.Combine(tempRoot, "characters", folderName);
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

        private OceanyanFileHivemindWindow CreateHivemindWindow(FileHivemindBackgroundAgentLauncher launcher)
        {
            return new OceanyanFileHivemindWindow(
                manageStartupWaitForm: false,
                runtimeStateStore: new GoogleDriveConnectionRuntimeStateStore(Path.Combine(tempRoot, "runtime")),
                credentialStore: new GoogleDriveSecureClientCredentialStore(Path.Combine(tempRoot, "creds"), new ObfuscatingProtector()),
                backgroundAgentLauncher: launcher,
                backgroundLogStore: new FileHivemindBackgroundLogStore(Path.Combine(tempRoot, "background.log")));
        }

        private static FileHivemindConnectionProfile CreateEligibleConnection(string id, bool autoSyncEnabled)
        {
            return new FileHivemindConnectionProfile
            {
                Id = id,
                AutoSyncEnabled = autoSyncEnabled,
                ProviderId = FileHivemindProviderIds.GoogleDrive,
                DisplayName = "Phase 1 Connection",
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

        private static object? InvokePrivateWithResult(object target, string methodName, params object?[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                ?? throw new InvalidOperationException("Method not found: " + methodName);
            object? result = method.Invoke(target, args);
            WaitForDispatcher();
            return result;
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
            private readonly Action? onSetRegistered;
            private bool registered;

            public FakeAutoStartRegistrar(Action? onSetRegistered = null)
            {
                this.onSetRegistered = onSetRegistered;
            }

            public bool IsRegistered()
            {
                return registered;
            }

            public void SetRegistered(bool enabled)
            {
                registered = enabled;
                onSetRegistered?.Invoke();
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
