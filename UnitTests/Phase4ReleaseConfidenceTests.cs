using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Common;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.FileHivemind;

namespace UnitTests
{
    /// <summary>
    /// Phase 4 regression tests.
    /// Group 1: Database viewer search-filter logic (STA, no FlaUI).
    ///   FlaUI "open" workflow is already locked in by FirstWaveSmokeTests.
    ///   The core filter method is testable in-process at the STA layer.
    /// Group 2: Hivemind agent process / stop-signal integration.
    ///   Locks in the real named-Mutex IsAgentRunning check and the real
    ///   named-EventWaitHandle stop-signal mechanism, complementing the
    ///   injected-delegate tests already in FileHivemindBackgroundSyncTests.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public sealed class Phase4ReleaseConfidenceTests
    {
        private static Application? sharedApplication;
        private static Window? sharedRootWindow;
        private List<string> originalBaseFolders = new List<string>();
        private string originalPathToConfigIni = string.Empty;
        private string tempRoot = string.Empty;

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

            tempRoot = Path.Combine(Path.GetTempPath(), "phase4_release_confidence_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            originalBaseFolders = new List<string>(Globals.BaseFolders);
            originalPathToConfigIni = Globals.PathToConfigINI;
        }

        [TearDown]
        public void TearDown()
        {
            Globals.BaseFolders = originalBaseFolders;
            Globals.PathToConfigINI = originalPathToConfigIni;
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

        // ============================================================
        // Group 1: Database viewer search filter (STA layer)
        // ============================================================

        [Test]
        public void FolderVisualizerWindow_SearchFilter_EmptyQueryPassesAllItems()
        {
            CharacterFolderVisualizerWindow window = CreateVisualizerWindow();

            FolderVisualizerItem itemA = new FolderVisualizerItem { Name = "Phoenix", DirectoryPath = @"C:\chars\Phoenix" };
            FolderVisualizerItem itemB = new FolderVisualizerItem { Name = "Edgeworth", DirectoryPath = @"C:\chars\Edgeworth" };

            SetSearchText(window, string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(InvokeFilterItemBySearch(window, itemA), Is.True,
                    "Empty search must pass all items (Phoenix).");
                Assert.That(InvokeFilterItemBySearch(window, itemB), Is.True,
                    "Empty search must pass all items (Edgeworth).");
            });

            window.Close();
        }

        [Test]
        public void FolderVisualizerWindow_SearchFilter_MatchingNamePassesItem()
        {
            CharacterFolderVisualizerWindow window = CreateVisualizerWindow();

            FolderVisualizerItem matching = new FolderVisualizerItem { Name = "Phoenix Wright", DirectoryPath = @"C:\chars\Phoenix" };
            FolderVisualizerItem nonMatching = new FolderVisualizerItem { Name = "Edgeworth", DirectoryPath = @"C:\chars\Edgeworth" };

            SetSearchText(window, "Phoenix");

            Assert.Multiple(() =>
            {
                Assert.That(InvokeFilterItemBySearch(window, matching), Is.True,
                    "Item whose Name contains the search query must pass.");
                Assert.That(InvokeFilterItemBySearch(window, nonMatching), Is.False,
                    "Item whose Name does not contain the query must be filtered out.");
            });

            window.Close();
        }

        [Test]
        public void FolderVisualizerWindow_SearchFilter_MatchIsCaseInsensitive()
        {
            CharacterFolderVisualizerWindow window = CreateVisualizerWindow();

            FolderVisualizerItem item = new FolderVisualizerItem { Name = "Phoenix Wright", DirectoryPath = @"C:\chars\Phoenix" };

            SetSearchText(window, "phoenix");

            Assert.That(InvokeFilterItemBySearch(window, item), Is.True,
                "Search must be case-insensitive: 'phoenix' must match 'Phoenix Wright'.");

            window.Close();
        }

        [Test]
        public void FolderVisualizerWindow_SearchFilter_WhitespaceOnlyQueryPassesAllItems()
        {
            CharacterFolderVisualizerWindow window = CreateVisualizerWindow();

            FolderVisualizerItem item = new FolderVisualizerItem { Name = "Franziska von Karma", DirectoryPath = @"C:\chars\Franziska" };

            SetSearchText(window, "   ");

            Assert.That(InvokeFilterItemBySearch(window, item), Is.True,
                "Whitespace-only search text must be treated as empty and pass all items.");

            window.Close();
        }

        [Test]
        public void FolderVisualizerWindow_SearchFilter_DirectoryPathMatchPassesItem()
        {
            CharacterFolderVisualizerWindow window = CreateVisualizerWindow();

            FolderVisualizerItem item = new FolderVisualizerItem
            {
                Name = "Unknown",
                DirectoryPath = @"C:\mycharacters\special_folder\SomeChar"
            };

            SetSearchText(window, "special_folder");

            Assert.That(InvokeFilterItemBySearch(window, item), Is.True,
                "DirectoryPath is included in the searchable values and must match.");

            window.Close();
        }

        // ============================================================
        // Group 2: Hivemind agent process / stop-signal integration
        // ============================================================

        [Test]
        public void FileHivemindLauncher_IsAgentRunning_ReturnsTrueWhenAgentMutexExists()
        {
            // Simulate the agent holding the named mutex.
            using Mutex agentMutex = new Mutex(initiallyOwned: true,
                FileHivemindBackgroundAgentCommandLine.AgentMutexName);

            try
            {
                FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                    new FakeAutoStartRegistrar());

                bool running = launcher.IsAgentRunning();

                Assert.That(running, Is.True,
                    "IsAgentRunning must return true when the agent's named mutex is held.");
            }
            finally
            {
                try { agentMutex.ReleaseMutex(); } catch { }
            }
        }

        [Test]
        public void FileHivemindLauncher_IsAgentRunning_ReturnsFalseWhenAgentMutexDoesNotExist()
        {
            // Verify that no leftover mutex from another test or process is present before asserting.
            if (Mutex.TryOpenExisting(FileHivemindBackgroundAgentCommandLine.AgentMutexName, out Mutex? existing))
            {
                existing.Dispose();
                Assert.Ignore("Named agent mutex already exists (held by another process); cannot run this test.");
                return;
            }

            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                new FakeAutoStartRegistrar());

            bool running = launcher.IsAgentRunning();

            Assert.That(running, Is.False,
                "IsAgentRunning must return false when the agent's named mutex does not exist.");
        }

        [Test]
        public void FileHivemindLauncher_RequestStopForCurrentSession_SetsNamedStopSignalEvent()
        {
            // The real stop-signal path: RequestStopForCurrentSession → SignalAgentStopRequest
            // creates/opens the named EventWaitHandle and calls Set().
            // We open the same named event here first so we can verify it was set.
            using EventWaitHandle stopEvent = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                FileHivemindBackgroundAgentCommandLine.AgentStopSignalEventName);
            stopEvent.Reset();

            // Use the real requestAgentStop path (null → default SignalAgentStopRequest).
            // Inject isAgentRunning = true so RequestStopForCurrentSession proceeds.
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                autoStartRegistrar: new FakeAutoStartRegistrar(),
                startAgentProcess: null,
                isAgentRunning: () => true,
                requestAgentStop: null);

            bool result = launcher.RequestStopForCurrentSession();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True,
                    "RequestStopForCurrentSession must return true when the agent reports as running.");
                Assert.That(stopEvent.WaitOne(0), Is.True,
                    "The named stop-signal EventWaitHandle must be set after RequestStopForCurrentSession.");
            });

            // Clean up: reset the event so it does not interfere with other tests.
            stopEvent.Reset();
        }

        [Test]
        public void FileHivemindLauncher_EnsureRunning_ResetsStopSignalBeforeLaunch()
        {
            // Pre-set the stop signal (as if a previous stop was requested but the
            // event was never cleared). Starting the agent should reset it first.
            using EventWaitHandle stopEvent = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                FileHivemindBackgroundAgentCommandLine.AgentStopSignalEventName);
            stopEvent.Set(); // Pre-set intentionally.

            bool processLaunchCalled = false;
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                autoStartRegistrar: new FakeAutoStartRegistrar(),
                startAgentProcess: _ =>
                {
                    processLaunchCalled = true;
                    return true;
                },
                isAgentRunning: () => false,
                requestAgentStop: null);

            FileHivemindSettings settings = BuildEligibleSettings(runAtStartup: true);
            launcher.EnsureRunningForCurrentSession(settings);

            Assert.Multiple(() =>
            {
                Assert.That(processLaunchCalled, Is.True,
                    "EnsureRunningForCurrentSession must attempt to launch the process when eligible and not already running.");
                Assert.That(stopEvent.WaitOne(0), Is.False,
                    "EnsureRunningForCurrentSession must reset the stop-signal event before launching the agent.");
            });
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static CharacterFolderVisualizerWindow CreateVisualizerWindow()
        {
            return new CharacterFolderVisualizerWindow(null, suppressInitialLoadWaitForm: true);
        }

        private static void SetSearchText(CharacterFolderVisualizerWindow window, string text)
        {
            FieldInfo field = typeof(CharacterFolderVisualizerWindow)
                .GetField("searchText", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Field 'searchText' not found.");
            field.SetValue(window, text);
        }

        private static bool InvokeFilterItemBySearch(CharacterFolderVisualizerWindow window, FolderVisualizerItem item)
        {
            MethodInfo method = typeof(CharacterFolderVisualizerWindow)
                .GetMethod("FilterItemBySearch", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Method 'FilterItemBySearch' not found.");
            return (bool)method.Invoke(window, new object[] { item })!;
        }

        private static FileHivemindSettings BuildEligibleSettings(bool runAtStartup)
        {
            return new FileHivemindSettings
            {
                RunAgentAtStartup = runAtStartup,
                Connections = new List<FileHivemindConnectionProfile>
                {
                    new FileHivemindConnectionProfile
                    {
                        AutoSyncEnabled = true,
                        ProviderId = FileHivemindProviderIds.GoogleDrive,
                        GoogleDrive = new GoogleDriveSyncSettings
                        {
                            OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                            OAuthClientSecret = "desktop-client-secret",
                            RemoteFolderId = "folder-id",
                            LocalFolderPath = @"C:\sync",
                            TokenStoreKey = "token-key",
                            LastSignedInEmail = "tester@example.com"
                        }
                    }
                }
            };
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
                .Where(w => !ReferenceEquals(w, sharedRootWindow))
                .ToList();

            foreach (Window w in windowsToClose)
            {
                try { w.Close(); } catch { }
            }

            WaitForDispatcher();
            Application.Current.MainWindow = sharedRootWindow;
        }

        private static void WaitForDispatcher()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Application.Current!.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private sealed class FakeAutoStartRegistrar : IFileHivemindAutoStartRegistrar
        {
            public bool LastEnabledValue { get; private set; }

            public bool IsRegistered() => LastEnabledValue;

            public void SetRegistered(bool enabled)
            {
                LastEnabledValue = enabled;
            }
        }
    }
}
