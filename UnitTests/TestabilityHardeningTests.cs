using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Viewport;
using OceanyaClient.Components;
using OceanyaClient.Features.Startup;

namespace UnitTests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class TestabilityHardeningTests
    {
        private string tempRoot = string.Empty;
        private string originalSaveFilePath = string.Empty;
        private string originalPathToConfigIni = string.Empty;
        private List<string> originalBaseFolders = new List<string>();
        private string originalSelectedServerEndpoint = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _ = WpfTestApplicationContext.EnsureCreated();

            tempRoot = Path.Combine(Path.GetTempPath(), "oceanya_testability_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            originalSaveFilePath = SaveFile.CurrentStoragePath;
            originalPathToConfigIni = Globals.PathToConfigINI;
            originalBaseFolders = new List<string>(Globals.BaseFolders);
            originalSelectedServerEndpoint = Globals.SelectedServerEndpoint;

            string isolatedSaveFilePath = Path.Combine(tempRoot, "savefile.json");
            SaveFile.ConfigureStoragePathForTests(isolatedSaveFilePath);
            SaveFile.ResetForTests(new SaveData(), persist: false);
            Globals.ReloadServerIpsForTests(null);
            OceanyaTestMode.Reset();
            ResetInitialConfigurationWindowTestHooks();
        }

        [TearDown]
        public void TearDown()
        {
            OceanyaTestMode.Reset();
            Globals.PathToConfigINI = originalPathToConfigIni;
            Globals.BaseFolders = originalBaseFolders;
            Globals.SelectedServerEndpoint = originalSelectedServerEndpoint;
            Globals.ReloadServerIpsForTests(null);
            SaveFile.ConfigureStoragePathForTests(originalSaveFilePath);
            ResetInitialConfigurationWindowTestHooks();

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        [Test]
        public void OceanyaTestMode_ParseArgs_AppliesExpectedOptions()
        {
            OceanyaTestModeOptions options = OceanyaTestMode.ParseArgs(
                new[]
                {
                    "--test-mode",
                    "--test-disable-fake-loading",
                    "--test-disable-loading-screen",
                    "--test-disable-waitforms",
                    "--test-auto-launch-startup",
                    "--test-skip-server-validation",
                    "--test-skip-asset-refresh-prompts",
                    "--test-startup-functionality=" + StartupFunctionalityIds.CharacterDatabaseViewer,
                    "--test-config-ini=C:\\temp\\config.ini",
                    "--test-server-endpoint=ws://127.0.0.1:27016",
                    "--test-savefile=C:\\temp\\savefile.json",
                    "--test-server-json=C:\\temp\\server.json"
                });

            Assert.Multiple(() =>
            {
                Assert.That(options.IsEnabled, Is.True);
                Assert.That(options.DisableFakeLoading, Is.True);
                Assert.That(options.DisableLoadingScreen, Is.True);
                Assert.That(options.DisableWaitForms, Is.True);
                Assert.That(options.AutoLaunchStartupFunctionality, Is.True);
                Assert.That(options.SkipServerValidation, Is.True);
                Assert.That(options.SkipAssetRefreshPrompts, Is.True);
                Assert.That(options.StartupFunctionalityId, Is.EqualTo(StartupFunctionalityIds.CharacterDatabaseViewer));
                Assert.That(options.ConfigIniPath, Is.EqualTo("C:\\temp\\config.ini"));
                Assert.That(options.ServerEndpoint, Is.EqualTo("ws://127.0.0.1:27016"));
                Assert.That(options.SaveFilePath, Is.EqualTo("C:\\temp\\savefile.json"));
                Assert.That(options.ServerJsonPath, Is.EqualTo("C:\\temp\\server.json"));
            });
        }

        [Test]
        public void SaveFile_ConfigureStoragePathForTests_AndResetHooks_UseIsolatedFile()
        {
            string isolatedSaveFilePath = Path.Combine(tempRoot, "isolated-savefile.json");

            SaveFile.ConfigureStoragePathForTests(isolatedSaveFilePath);
            SaveFile.ResetForTests(
                new SaveData
                {
                    ConfigIniPath = "C:\\tests\\config.ini",
                    SelectedServerEndpoint = "ws://127.0.0.1:27016",
                    SelectedServerName = "Test Server"
                },
                persist: true);

            Assert.Multiple(() =>
            {
                Assert.That(SaveFile.CurrentStoragePath, Is.EqualTo(isolatedSaveFilePath));
                Assert.That(File.Exists(isolatedSaveFilePath), Is.True);
            });

            SaveFile.ResetForTests(new SaveData { ConfigIniPath = "different.ini" }, persist: true);
            SaveFile.ReloadFromDiskForTests();

            Assert.That(SaveFile.Data.ConfigIniPath, Is.EqualTo("different.ini"));
        }

        [Test]
        public void SaveFile_NormalizesAudioVolumes()
        {
            SaveFile.ResetForTests(
                new SaveData
                {
                    AudioMusicVolume = 2.0,
                    AudioSfxVolume = -1.0,
                    AudioBlipVolume = 0.35
                },
                persist: false);

            Assert.Multiple(() =>
            {
                Assert.That(SaveFile.Data.AudioMusicVolume, Is.EqualTo(1.0));
                Assert.That(SaveFile.Data.AudioSfxVolume, Is.EqualTo(0.0));
                Assert.That(SaveFile.Data.AudioBlipVolume, Is.EqualTo(0.35));
            });
        }

        [Test]
        public void AudioSettings_ConversionHelpersClampExpectedRanges()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AudioSettings.PercentToScalar(-10), Is.EqualTo(0.0));
                Assert.That(AudioSettings.PercentToScalar(25), Is.EqualTo(0.25));
                Assert.That(AudioSettings.PercentToScalar(120), Is.EqualTo(1.0));
                Assert.That(AudioSettings.ScalarToPercent(-0.1), Is.EqualTo(0.0));
                Assert.That(AudioSettings.ScalarToPercent(0.5), Is.EqualTo(50.0));
                Assert.That(AudioSettings.ScalarToPercent(2.0), Is.EqualTo(100.0));
            });
        }

        [Test]
        public void AudioSettings_ScaleEmbeddedSfxVolume_UsesPersistedSfxSlider()
        {
            SaveFile.ResetForTests(
                new SaveData
                {
                    AudioSfxVolume = 0.4
                },
                persist: false);

            Assert.That(AudioSettings.ScaleEmbeddedSfxVolume(0.5f), Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void AudioSettings_ResolveSfxVolume_AppliesCharacterShownameAndTokenRules()
        {
            SaveFile.ResetForTests(
                new SaveData
                {
                    AudioSfxVolume = 0.4,
                    ExtraAudioRules = new List<ExtraAudioRule>
                    {
                        new ExtraAudioRule
                        {
                            Kind = ExtraAudioRuleKind.Sfx,
                            Target = ExtraAudioRuleTarget.Character,
                            Match = "Phoenix",
                            VolumePercent = 25,
                            IsEnabled = true
                        },
                        new ExtraAudioRule
                        {
                            Kind = ExtraAudioRuleKind.Sfx,
                            Target = ExtraAudioRuleTarget.Showname,
                            Match = "Judge",
                            VolumePercent = 60,
                            IsEnabled = true
                        },
                        new ExtraAudioRule
                        {
                            Kind = ExtraAudioRuleKind.Sfx,
                            Target = ExtraAudioRuleTarget.Sfx,
                            Match = "sfx-objection",
                            VolumePercent = 80,
                            IsEnabled = true
                        }
                    }
                },
                persist: false);

            Assert.Multiple(() =>
            {
                Assert.That(AudioSettings.ResolveSfxVolume("Phoenix", null, null, "sfx-damage"), Is.EqualTo(0.25));
                Assert.That(AudioSettings.ResolveSfxVolume(null, "The Judge", null, "sfx-damage"), Is.EqualTo(0.6));
                Assert.That(AudioSettings.ResolveSfxVolume(null, null, null, "sfx-objection"), Is.EqualTo(0.8));
                Assert.That(AudioSettings.ResolveSfxVolume(null, null, null, "sfx-other"), Is.EqualTo(0.4));
            });
        }

        [Test]
        public void CallwordAudioNotifier_WholeWord_MatchesOnlyDelimitedText()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CallwordAudioNotifier.ContainsWholeWord("Kam, hello", "Kam"), Is.True);
                Assert.That(CallwordAudioNotifier.ContainsWholeWord("hello Kam!", "Kam"), Is.True);
                Assert.That(CallwordAudioNotifier.ContainsWholeWord("hello Kamui", "Kam"), Is.False);
                Assert.That(CallwordAudioNotifier.ContainsWholeWord("hello akam", "Kam"), Is.False);
                Assert.That(CallwordAudioNotifier.ContainsWholeWord("hello Kam_Test", "Kam"), Is.False);
            });
        }

        [Test]
        public void ExtraAudioRuleEditorWindow_CatalogsIncludeCharacterGeneralAndMusicFolders()
        {
            string characterRoot = Path.Combine(tempRoot, "characters", "Naoto");
            string characterGeneral = Path.Combine(characterRoot, "general");
            string characterMusic = Path.Combine(characterRoot, "music");
            Directory.CreateDirectory(characterGeneral);
            Directory.CreateDirectory(characterMusic);
            File.WriteAllBytes(Path.Combine(characterGeneral, "cutin.wav"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(characterMusic, "theme.ogg"), new byte[] { 1 });
            Globals.BaseFolders = new List<string> { tempRoot };

            MethodInfo buildSfxCatalog = typeof(ExtraAudioRuleEditorWindow).GetMethod(
                "BuildSfxTokenCatalog",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            MethodInfo buildAudioCatalog = typeof(ExtraAudioRuleEditorWindow).GetMethod(
                "BuildAudioTokenCatalog",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            List<string> sfxTokens = (List<string>)buildSfxCatalog.Invoke(null, null)!;
            List<string> musicTokens = (List<string>)buildAudioCatalog.Invoke(null, new object[] { "music" })!;

            Assert.Multiple(() =>
            {
                Assert.That(sfxTokens, Does.Contain("../../characters/Naoto/general/cutin"));
                Assert.That(musicTokens, Does.Contain("../../characters/Naoto/music/theme"));
            });
        }

        [Test]
        public void AO2ViewportControl_MessageRequestsScreenShake_RequiresExplicitPacketRequest()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AO2ViewportControl.MessageRequestsScreenShake(null), Is.False);
                Assert.That(AO2ViewportControl.MessageRequestsScreenShake(new ICMessage()), Is.False);
                Assert.That(
                    AO2ViewportControl.MessageRequestsScreenShake(new ICMessage { ScreenShake = true }),
                    Is.True);
                Assert.That(
                    AO2ViewportControl.MessageRequestsScreenShake(new ICMessage { FramesShake = "pre^talk^" }),
                    Is.False);
            });
        }

        [Test]
        public void Globals_ReloadServerIpsForTests_UsesProvidedServerJson()
        {
            string serverJsonPath = Path.Combine(tempRoot, "server.json");
            File.WriteAllText(
                serverJsonPath,
                """
                {
                  "ChillAndDices": "ws://127.0.0.1:27016",
                  "Vanilla": "ws://127.0.0.1:27017",
                  "CaseCafe": "ws://127.0.0.1:27018"
                }
                """);

            Globals.ReloadServerIpsForTests(serverJsonPath);

            Assert.Multiple(() =>
            {
                Assert.That(Globals.IPs[Globals.Servers.ChillAndDices], Is.EqualTo("ws://127.0.0.1:27016"));
                Assert.That(Globals.IPs[Globals.Servers.Vanilla], Is.EqualTo("ws://127.0.0.1:27017"));
                Assert.That(Globals.IPs[Globals.Servers.CaseCafe], Is.EqualTo("ws://127.0.0.1:27018"));
            });
        }

        [Test]
        public async Task WaitForm_ShowFormAsync_NoOps_WhenDisabledByTestMode()
        {
            OceanyaTestMode.SetCurrent(new OceanyaTestModeOptions
            {
                IsEnabled = true,
                DisableWaitForms = true
            });

            FieldInfo? uiThreadField = typeof(WaitForm).GetField("_uiThread", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(uiThreadField, Is.Not.Null);

            await WaitForm.ShowFormAsync("Testing wait form", new Window());
            await WaitForm.CloseFormAsync();
            WaitForm.SetSubtitle("Still disabled");

            Assert.Multiple(() =>
            {
                Assert.That(WaitForm.Showing, Is.False);
                Assert.That(uiThreadField!.GetValue(null), Is.Null);
            });
        }

        [Test]
        public async Task LoadingScreenManager_ShowFormAsync_NoOps_WhenDisabledByTestMode()
        {
            OceanyaTestMode.SetCurrent(new OceanyaTestModeOptions
            {
                IsEnabled = true,
                DisableLoadingScreen = true
            });

            FieldInfo? uiThreadField = typeof(LoadingScreenManager).GetField("_uiThread", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(uiThreadField, Is.Not.Null);

            await LoadingScreenManager.ShowFormAsync("Testing loading screen");
            await LoadingScreenManager.CloseFormAsync();
            LoadingScreenManager.SetProgress(0.5);
            LoadingScreenManager.SetSubtitle("Still disabled");

            Assert.That(uiThreadField!.GetValue(null), Is.Null);
        }

        [Test]
        public void InitialConfigurationWindow_AppliesTestStartupOverrides()
        {
            string configIniPath = CreateConfigIni();
            SaveFile.ResetForTests(
                new SaveData
                {
                    ConfigIniPath = "stale.ini",
                    StartupFunctionalityId = StartupFunctionalityIds.GmMultiClient,
                    UseSingleInternalClient = true,
                    SelectedServerEndpoint = "ws://stale-server:27016",
                    SelectedServerName = "Stale Server"
                },
                persist: false);

            OceanyaTestMode.SetCurrent(new OceanyaTestModeOptions
            {
                IsEnabled = true,
                ConfigIniPath = configIniPath,
                StartupFunctionalityId = StartupFunctionalityIds.CharacterDatabaseViewer,
                ServerEndpoint = "ws://127.0.0.1:27016"
            });

            InitialConfigurationWindow window = new InitialConfigurationWindow();

            Assert.Multiple(() =>
            {
                Assert.That(((TextBox)window.FindName("ConfigINIPathTextBox")).Text, Is.EqualTo(configIniPath));
                Assert.That(((ComboBox)window.FindName("StartupFunctionalityComboBox")).SelectedValue?.ToString(),
                    Is.EqualTo(StartupFunctionalityIds.CharacterDatabaseViewer));
                Assert.That(((TextBox)window.FindName("SelectedServerTextBox")).Text, Is.EqualTo("ws://127.0.0.1:27016"));
            });
        }

        [Test]
        public async Task StartupRefreshPrompt_WhenUserDeclines_DoesNotRefreshAssets()
        {
            string configIniPath = CreateConfigIni();
            SaveFile.ResetForTests(
                new SaveData
                {
                    ConfigIniPath = "stale.ini",
                    StartupFunctionalityId = StartupFunctionalityIds.GmMultiClient,
                    UseSingleInternalClient = true,
                    SelectedServerEndpoint = "ws://stale-server:27016",
                    SelectedServerName = "Stale Server"
                },
                persist: false);

            OceanyaTestMode.SetCurrent(new OceanyaTestModeOptions
            {
                IsEnabled = true,
                DisableWaitForms = true,
                DisableFakeLoading = true,
                SkipServerValidation = true
            });

            Globals.UpdateConfigINI(configIniPath);
            ResetCharacterCache();
            CharacterFolder.RefreshCharacterList();
            Assert.That(CharacterFolder.FullList, Is.Empty);

            InitialConfigurationWindow window = new InitialConfigurationWindow();
            ((TextBox)window.FindName("ConfigINIPathTextBox")).Text = configIniPath;
            ((ComboBox)window.FindName("StartupFunctionalityComboBox")).SelectedValue =
                StartupFunctionalityIds.CharacterDatabaseViewer;
            ((CheckBox)window.FindName("RefreshInfoCheckBox")).IsChecked = false;

            int promptCalls = 0;
            int fullRefreshCalls = 0;
            int targetedRefreshCalls = 0;

            SetNonPublicStaticField(
                typeof(InitialConfigurationWindow),
                "testMessageBoxOverride",
                new Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult>(
                    (message, caption, buttons, image) =>
                    {
                        promptCalls++;

                        Assert.Multiple(() =>
                        {
                            Assert.That(caption, Is.EqualTo("Refresh Required"));
                            Assert.That(buttons, Is.EqualTo(MessageBoxButton.YesNo));
                            Assert.That(image, Is.EqualTo(MessageBoxImage.Question));
                            Assert.That(message, Does.Contain("full asset refresh is required").IgnoreCase);
                        });

                        return MessageBoxResult.No;
                    }));

            SetNonPublicStaticField(
                typeof(InitialConfigurationWindow),
                "testRefreshCharactersAndBackgroundsAsyncOverride",
                new Func<Window, Task>(_ =>
                {
                    fullRefreshCalls++;
                    return Task.CompletedTask;
                }));

            SetNonPublicStaticField(
                typeof(InitialConfigurationWindow),
                "testRefreshTargetedAssetsAsyncOverride",
                new Func<Window, TargetedAssetRefreshPlan, Task>((_, _) =>
                {
                    targetedRefreshCalls++;
                    return Task.CompletedTask;
                }));

            await InvokeNonPublicInstanceMethodAsync(window, "ExecuteOkButtonClickAsync");

            Assert.Multiple(() =>
            {
                Assert.That(promptCalls, Is.EqualTo(1));
                Assert.That(fullRefreshCalls, Is.EqualTo(0));
                Assert.That(targetedRefreshCalls, Is.EqualTo(0));
                Assert.That(SaveFile.Data.ConfigIniPath, Is.EqualTo("stale.ini"));
                Assert.That(SaveFile.Data.StartupFunctionalityId, Is.EqualTo(StartupFunctionalityIds.GmMultiClient));
                Assert.That(SaveFile.Data.SelectedServerEndpoint, Is.EqualTo("ws://stale-server:27016"));
            });
        }

        [Test]
        public void InitialConfigurationWindow_HasCoreAutomationIds()
        {
            InitialConfigurationWindow window = new InitialConfigurationWindow();

            Assert.Multiple(() =>
            {
                Assert.That(GetAutomationId(window, "ConfigINIPathTextBox"), Is.EqualTo("InitialConfig.ConfigIniPath"));
                Assert.That(GetAutomationId(window, "BrowseButton"), Is.EqualTo("InitialConfig.BrowseConfigIni"));
                Assert.That(GetAutomationId(window, "StartupFunctionalityComboBox"), Is.EqualTo("InitialConfig.StartupFunctionality"));
                Assert.That(GetAutomationId(window, "SelectedServerTextBox"), Is.EqualTo("InitialConfig.SelectedServerText"));
                Assert.That(GetAutomationId(window, "SelectServerButton"), Is.EqualTo("InitialConfig.SelectServer"));
                Assert.That(GetAutomationId(window, "RefreshInfoCheckBox"), Is.EqualTo("InitialConfig.RefreshAssets"));
                Assert.That(GetAutomationId(window, "UseSingleClientCheckBox"), Is.EqualTo("InitialConfig.UseSingleInternalClient"));
                Assert.That(GetAutomationId(window, "OkButton"), Is.EqualTo("InitialConfig.Launch"));
            });
        }

        [Test]
        public void ServerSelectionDialog_AndFavoriteServerEditor_HaveCoreAutomationIds()
        {
            string configIniPath = CreateConfigIni();
            ServerSelectionDialog serverDialog = new ServerSelectionDialog(configIniPath, "ws://127.0.0.1:27016");
            FavoriteServerEditorDialog favoriteDialog = new FavoriteServerEditorDialog("Favorite", "SAVE", "Name", "ws://127.0.0.1:27016", "Desc");

            Assert.Multiple(() =>
            {
                Assert.That(GetAutomationId(serverDialog, "SearchTextBox"), Is.EqualTo("ServerSelection.Search"));
                Assert.That(GetAutomationId(serverDialog, "RefreshPollButton"), Is.EqualTo("ServerSelection.RefreshPoll"));
                Assert.That(GetAutomationId(serverDialog, "ServerTabs"), Is.EqualTo("ServerSelection.Tabs"));
                Assert.That(GetAutomationId(serverDialog, "FavoritesListView"), Is.EqualTo("ServerSelection.FavoritesList"));
                Assert.That(GetAutomationId(serverDialog, "AddFavoriteButton"), Is.EqualTo("ServerSelection.AddFavorite"));
                Assert.That(GetAutomationId(serverDialog, "CancelButton"), Is.EqualTo("ServerSelection.Cancel"));
                Assert.That(GetAutomationId(serverDialog, "SelectButton"), Is.EqualTo("ServerSelection.Select"));
                Assert.That(GetAutomationId(serverDialog, "StatusTextBlock"), Is.EqualTo("ServerSelection.Status"));

                Assert.That(GetAutomationId(favoriteDialog, "ServerNameTextBox"), Is.EqualTo("FavoriteServer.Name"));
                Assert.That(GetAutomationId(favoriteDialog, "ServerEndpointTextBox"), Is.EqualTo("FavoriteServer.Endpoint"));
                Assert.That(GetAutomationId(favoriteDialog, "ServerDescriptionTextBox"), Is.EqualTo("FavoriteServer.Description"));
                Assert.That(GetAutomationId(favoriteDialog, "ActionButton"), Is.EqualTo("FavoriteServer.Save"));
            });
        }

        [Test]
        public void MainWindow_AndChildControls_HaveCoreAutomationIds()
        {
            string mainWindowXamlPath = Path.Combine(GetRepositoryRoot(), "OceanyaClient", "MainWindow.xaml");
            string mainWindowXaml = File.ReadAllText(mainWindowXamlPath);
            OOCLog oocLog = new OOCLog();
            ICMessageSettings icSettings = new ICMessageSettings();

            Assert.Multiple(() =>
            {
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.AddClient\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.RemoveClient\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.Shout.HoldIt\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.Shout.Objection\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.Shout.TakeThat\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.Shout.Custom\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.OpenCharacterFolderVisualizer\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.AreaNavigator.Open\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.AreaNavigator.Popup\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.AreaNavigator.List\""));
                Assert.That(mainWindowXaml, Does.Contain("AutomationProperties.AutomationId=\"Main.AreaNavigator.Go\""));

                Assert.That(GetAutomationId(oocLog, "txtOOCMessage"), Is.EqualTo("Main.Ooc.Message"));
                Assert.That(GetAutomationId(oocLog, "txtOOCShowname"), Is.EqualTo("Main.Ooc.Showname"));
                Assert.That(GetAutomationId(oocLog, "btnServerConsole"), Is.EqualTo("Main.Ooc.ServerConsole"));
                Assert.That(GetAutomationId(oocLog, "LogBox"), Is.EqualTo("Main.Ooc.Log"));
                Assert.That(GetAutomationId(oocLog, "lblStream"), Is.EqualTo("Main.Ooc.StreamLabel"));

                Assert.That(GetAutomationId(icSettings, "txtICShowname"), Is.EqualTo("Main.Ic.Showname"));
                Assert.That(GetAutomationId(icSettings, "txtICMessage"), Is.EqualTo("Main.Ic.Message"));
                Assert.That(GetAutomationId(icSettings, "chkPreanim"), Is.EqualTo("Main.Ic.Preanim"));
                Assert.That(GetAutomationId(icSettings, "chkFlip"), Is.EqualTo("Main.Ic.Flip"));
                Assert.That(GetAutomationId(icSettings, "chkAdditive"), Is.EqualTo("Main.Ic.Additive"));
                Assert.That(GetAutomationId(icSettings, "chkImmediate"), Is.EqualTo("Main.Ic.Immediate"));
                Assert.That(GetAutomationId(icSettings, "btnRealization"), Is.EqualTo("Main.Ic.Realization"));
                Assert.That(GetAutomationId(icSettings, "btnScreenshake"), Is.EqualTo("Main.Ic.Screenshake"));
                Assert.That(GetAutomationId(icSettings, "EmoteGrid"), Is.EqualTo("Main.Ic.EmoteGrid"));
            });
        }

        [Test]
        public void MainWindow_Constructor_DoesNotThrow_WhenScienceBlurAssetResolves()
        {
            MainWindow? window = null;

            Assert.DoesNotThrow(() => window = new MainWindow());
            Assert.That(window, Is.Not.Null);
        }

        [Test]
        public void VisualizerWindows_HaveCoreAutomationIds()
        {
            CharacterFolderVisualizerWindow folderWindow = new CharacterFolderVisualizerWindow(onAssetsRefreshed: null, suppressInitialLoadWaitForm: true);
            CharacterFolder character = new CharacterFolder
            {
                Name = "Phoenix",
                DirectoryPath = tempRoot,
                PathToConfigIni = Path.Combine(tempRoot, "char.ini"),
                configINI = new CharacterConfigINI(string.Empty)
            };
            CharacterEmoteVisualizerWindow emoteWindow = new CharacterEmoteVisualizerWindow(character);

            Assert.Multiple(() =>
            {
                Assert.That(GetAutomationId(folderWindow, "SearchTextBox"), Is.EqualTo("FolderVisualizer.Search"));
                Assert.That(GetAutomationId(folderWindow, "ViewModeCombo"), Is.EqualTo("FolderVisualizer.ViewMode"));
                Assert.That(GetAutomationId(folderWindow, "RefreshAssetsButton"), Is.EqualTo("FolderVisualizer.RefreshAssets"));
                Assert.That(GetAutomationId(folderWindow, "SelectTagsButton"), Is.EqualTo("FolderVisualizer.FiltersSorting"));
                Assert.That(GetAutomationId(folderWindow, "FolderListView"), Is.EqualTo("FolderVisualizer.List"));
                Assert.That(GetAutomationId(folderWindow, "SummaryText"), Is.EqualTo("FolderVisualizer.Summary"));
                Assert.That(GetAutomationId(folderWindow, "ConfigureViewsButton"), Is.EqualTo("FolderVisualizer.ConfigureViews"));
                Assert.That(GetAutomationId(folderWindow, "CloseBottomButton"), Is.EqualTo("FolderVisualizer.Close"));

                Assert.That(GetAutomationId(emoteWindow, "SearchTextBox"), Is.EqualTo("EmoteVisualizer.Search"));
                Assert.That(GetAutomationId(emoteWindow, "ViewModeCombo"), Is.EqualTo("EmoteVisualizer.ViewMode"));
                Assert.That(GetAutomationId(emoteWindow, "RefreshCharacterButton"), Is.EqualTo("EmoteVisualizer.RefreshCharacter"));
                Assert.That(GetAutomationId(emoteWindow, "OpenCharacterIniButton"), Is.EqualTo("EmoteVisualizer.OpenCharIni"));
                Assert.That(GetAutomationId(emoteWindow, "OpenReadmeButton"), Is.EqualTo("EmoteVisualizer.OpenReadme"));
                Assert.That(GetAutomationId(emoteWindow, "LoopAnimationsCheckBox"), Is.EqualTo("EmoteVisualizer.LoopAnimations"));
                Assert.That(GetAutomationId(emoteWindow, "EmoteListView"), Is.EqualTo("EmoteVisualizer.List"));
                Assert.That(GetAutomationId(emoteWindow, "SummaryText"), Is.EqualTo("EmoteVisualizer.Summary"));
                Assert.That(GetAutomationId(emoteWindow, "ConfigureViewsButton"), Is.EqualTo("EmoteVisualizer.ConfigureViews"));
            });
        }

        [Test]
        public void DialogControls_HaveCoreAutomationIds()
        {
            InputDialog inputDialog = new InputDialog("Prompt", defaultText: "Value");
            ConstructorInfo? messageBoxCtor = typeof(OceanyaMessageBox).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

            Assert.That(messageBoxCtor, Is.Not.Null);
            OceanyaMessageBox messageBox = (OceanyaMessageBox)messageBoxCtor!.Invoke(null);

            Assert.Multiple(() =>
            {
                Assert.That(GetAutomationId(inputDialog, "PromptTextBlock"), Is.EqualTo("InputDialog.Prompt"));
                Assert.That(GetAutomationId(inputDialog, "InputTextBox"), Is.EqualTo("InputDialog.Input"));
                Assert.That(GetAutomationId(inputDialog, "OkButton"), Is.EqualTo("InputDialog.Ok"));
                Assert.That(GetAutomationId(inputDialog, "CancelButton"), Is.EqualTo("InputDialog.Cancel"));

                Assert.That(GetAutomationId(messageBox, "MessageTextBlock"), Is.EqualTo("MessageBox.Message"));
                Assert.That(GetAutomationId(messageBox, "YesButton"), Is.EqualTo("MessageBox.Yes"));
                Assert.That(GetAutomationId(messageBox, "NoButton"), Is.EqualTo("MessageBox.No"));
                Assert.That(GetAutomationId(messageBox, "OKButton"), Is.EqualTo("MessageBox.Ok"));
                Assert.That(GetAutomationId(messageBox, "CancelButton"), Is.EqualTo("MessageBox.Cancel"));
            });
        }

        [Test]
        public void CustomDropdownControls_PropagateAutomationIds()
        {
            ImageComboBox imageComboBox = new ImageComboBox
            {
                AutomationId = "Main.Ic.Character"
            };
            AutoCompleteDropdownField autoComplete = new AutoCompleteDropdownField
            {
                AutomationId = "FolderVisualizer.TagInput"
            };

            Assert.Multiple(() =>
            {
                Assert.That(AutomationProperties.GetAutomationId(imageComboBox), Is.EqualTo("Main.Ic.Character"));
                Assert.That(AutomationProperties.GetAutomationId((DependencyObject)imageComboBox.FindName("cboINISelect")),
                    Is.EqualTo("Main.Ic.Character.ComboBox"));

                Assert.That(AutomationProperties.GetAutomationId(autoComplete), Is.EqualTo("FolderVisualizer.TagInput"));
                Assert.That(AutomationProperties.GetAutomationId((DependencyObject)autoComplete.FindName("InputTextBox")),
                    Is.EqualTo("FolderVisualizer.TagInput.Input"));
                Assert.That(AutomationProperties.GetAutomationId((DependencyObject)autoComplete.FindName("ToggleButton")),
                    Is.EqualTo("FolderVisualizer.TagInput.Toggle"));
                Assert.That(AutomationProperties.GetAutomationId((DependencyObject)autoComplete.FindName("SuggestionsListBox")),
                    Is.EqualTo("FolderVisualizer.TagInput.Suggestions"));
            });
        }

        [Test]
        public void HostedWindow_ReadyMarker_TracksAutomationReadyState()
        {
            TestReadyContent content = new TestReadyContent();
            GenericOceanyaWindow window = (GenericOceanyaWindow)OceanyaWindowManager.CreateWindow(content);

            window.Show();
            window.ApplyTemplate();
            window.UpdateLayout();

            TextBlock? marker = window.FindName("AutomationReadyMarker") as TextBlock;
            Assert.That(marker, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(content.AutomationReadyState, Is.EqualTo(OceanyaWindowContentControl.AutomationReadyStateLoading));
                Assert.That(marker!.Text, Is.EqualTo(OceanyaWindowContentControl.AutomationReadyStateLoading));
                Assert.That(AutomationProperties.GetItemStatus(window), Is.EqualTo(OceanyaWindowContentControl.AutomationReadyStateLoading));
            });

            content.MarkReadyForTests();
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(content.AutomationReadyState, Is.EqualTo(OceanyaWindowContentControl.AutomationReadyStateReady));
                Assert.That(marker!.Text, Is.EqualTo(OceanyaWindowContentControl.AutomationReadyStateReady));
                Assert.That(AutomationProperties.GetName(marker), Is.EqualTo(OceanyaWindowContentControl.AutomationReadyStateReady));
                Assert.That(AutomationProperties.GetItemStatus(window), Is.EqualTo(OceanyaWindowContentControl.AutomationReadyStateReady));
            });

            window.Close();
        }

        [Test]
        public void InitialConfigurationWindow_And_MainWindow_LoadHandlers_MarkAutomationReady()
        {
            string configIniPath = CreateConfigIni();
            SaveFile.ResetForTests(
                new SaveData
                {
                    ConfigIniPath = configIniPath,
                    StartupFunctionalityId = StartupFunctionalityIds.GmMultiClient,
                    SelectedServerEndpoint = "ws://127.0.0.1:27016",
                    SelectedServerName = "Smoke Favorite"
                },
                persist: false);

            InitialConfigurationWindow initialWindow = new InitialConfigurationWindow();
            InvokeNonPublicInstanceMethod(initialWindow, "Window_Loaded", initialWindow, new RoutedEventArgs());
            Assert.That(initialWindow.AutomationReadyState, Is.EqualTo(OceanyaWindowContentControl.AutomationReadyStateReady));

            MainWindow mainWindow = new MainWindow();
            InvokeNonPublicInstanceMethod(mainWindow, "MainWindow_Loaded", mainWindow, new RoutedEventArgs());
            Assert.That(mainWindow.AutomationReadyState, Is.EqualTo(OceanyaWindowContentControl.AutomationReadyStateReady));
        }

        [Test]
        public void FlaUiSmokeFixtures_LoadSeededFavoritesAndCharacter()
        {
            string fixtureRoot = Path.Combine(GetRepositoryRoot(), "UnitTests", "TestAssets", "FlaUISmoke");
            string configIniPath = Path.Combine(fixtureRoot, "config.ini");
            string serverJsonPath = Path.Combine(fixtureRoot, "server.json");

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(configIniPath), Is.True, "Missing smoke config.ini fixture.");
                Assert.That(File.Exists(serverJsonPath), Is.True, "Missing smoke server.json fixture.");
                Assert.That(File.Exists(Path.Combine(fixtureRoot, "favorite_servers.ini")), Is.True, "Missing smoke favorites fixture.");
                Assert.That(File.Exists(Path.Combine(fixtureRoot, "characters", "SmokePhoenix", "char.ini")), Is.True, "Missing smoke character fixture.");
            });

            Globals.ReloadServerIpsForTests(serverJsonPath);
            Globals.UpdateConfigINI(configIniPath);
            ResetCharacterCache();
            CharacterFolder.RefreshCharacterList();

            List<ServerEndpointDefinition> favorites = ServerEndpointCatalog.LoadFavorites(configIniPath);
            CharacterFolder? smokeCharacter = CharacterFolder.FullList
                .Find(character => string.Equals(character.Name, "SmokePhoenix", StringComparison.OrdinalIgnoreCase));

            Assert.Multiple(() =>
            {
                Assert.That(favorites.Count, Is.GreaterThanOrEqualTo(1));
                Assert.That(favorites[0].Endpoint, Is.EqualTo("ws://127.0.0.1:27016"));
                Assert.That(smokeCharacter, Is.Not.Null);
                Assert.That(smokeCharacter!.configINI.EmotionsCount, Is.EqualTo(2));
            });
        }

        private string CreateConfigIni()
        {
            string mountsRoot = Path.Combine(tempRoot, "base");
            Directory.CreateDirectory(mountsRoot);

            string configIniPath = Path.Combine(tempRoot, "config.ini");
            File.WriteAllText(
                configIniPath,
                "mount_paths=" + mountsRoot.Replace('\\', '/') + Environment.NewLine +
                "log_maximum=250" + Environment.NewLine);
            return configIniPath;
        }

        private static string GetAutomationId(FrameworkElement root, string controlName)
        {
            object? element = root.FindName(controlName);
            Assert.That(element, Is.AssignableTo<DependencyObject>(), $"Expected '{controlName}' to resolve to a dependency object.");
            return AutomationProperties.GetAutomationId((DependencyObject)element!);
        }

        private static void InvokeNonPublicInstanceMethod(object target, string methodName, params object[] parameters)
        {
            MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, $"Expected method '{methodName}' on {target.GetType().Name}.");
            method!.Invoke(target, parameters);
        }

        private static async Task InvokeNonPublicInstanceMethodAsync(object target, string methodName, params object[] parameters)
        {
            MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, $"Expected method '{methodName}' on {target.GetType().Name}.");

            object? result = method!.Invoke(target, parameters);
            Assert.That(result, Is.AssignableTo<Task>(), $"Expected '{methodName}' to return a Task.");
            await (Task)result!;
        }

        private static void SetNonPublicStaticField(Type declaringType, string fieldName, object? value)
        {
            FieldInfo? field = declaringType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, $"Expected static field '{fieldName}' on {declaringType.Name}.");
            field!.SetValue(null, value);
        }

        private static void ResetInitialConfigurationWindowTestHooks()
        {
            SetNonPublicStaticField(typeof(InitialConfigurationWindow), "testMessageBoxOverride", null);
            SetNonPublicStaticField(typeof(InitialConfigurationWindow), "testRefreshCharactersAndBackgroundsAsyncOverride", null);
            SetNonPublicStaticField(typeof(InitialConfigurationWindow), "testRefreshTargetedAssetsAsyncOverride", null);
        }

        private static void ResetCharacterCache()
        {
            Type type = typeof(CharacterFolder);
            FieldInfo? configsField = type.GetField("characterConfigs", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo? cacheFileField = type.GetField("cacheFile", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo? cachePathInitializedField = type.GetField("cachePathInitialized", BindingFlags.NonPublic | BindingFlags.Static);

            configsField?.SetValue(null, new List<CharacterFolder>());
            cachePathInitializedField?.SetValue(null, false);

            string? cacheFile = cacheFileField?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(cacheFile) && File.Exists(cacheFile))
            {
                try
                {
                    File.Delete(cacheFile);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }

        private static string GetRepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        }

        private sealed class TestReadyContent : OceanyaWindowContentControl
        {
            public override string HeaderText => "READY TEST";

            public void MarkReadyForTests()
            {
                MarkAutomationReady();
            }
        }
    }
}
