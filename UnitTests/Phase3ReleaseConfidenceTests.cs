using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AO2AIBot.Controller;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Components;

namespace UnitTests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public sealed class Phase3ReleaseConfidenceTests
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

            tempRoot = Path.Combine(Path.GetTempPath(), "phase3_release_confidence_" + Guid.NewGuid().ToString("N"));
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

        // ============================================================
        // Group 1: Character creator file-organization mutation tests
        // ============================================================

        [Test]
        public void AOCharacterFileCreator_FileOrganization_ExtraFileAtRootBecomesExternalEntry()
        {
            string characterDirectory = CreateMinimalFileOrgCharacterFolder();
            File.WriteAllText(Path.Combine(characterDirectory, "notes.txt"), "extra file");

            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            bool loaded = window.TryLoadCharacterFolderForEditing(characterDirectory, out string errorMessage);
            Assert.That(loaded, Is.True, errorMessage);

            IList<(string RelativePath, bool IsFolder)> externals = GetExternalEntries(window);

            Assert.Multiple(() =>
            {
                Assert.That(externals.Count, Is.EqualTo(1), "notes.txt is the only untracked file.");
                Assert.That(externals[0].RelativePath, Does.Contain("notes.txt").IgnoreCase,
                    "The external entry must reference notes.txt.");
                Assert.That(externals[0].IsFolder, Is.False,
                    "notes.txt is a file, not a folder.");
            });

            window.Close();
        }

        [Test]
        public void AOCharacterFileCreator_FileOrganization_AnimationAtRootRecordedAsOverride()
        {
            // customanim.png lives at the character root, not under Images/.
            // After loading the folder the override must point to customanim.png, not the
            // generated default path Images/customanim.png.
            string characterDirectory = CreateMinimalFileOrgCharacterFolder();

            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            bool loaded = window.TryLoadCharacterFolderForEditing(characterDirectory, out string errorMessage);
            Assert.That(loaded, Is.True, errorMessage);

            IDictionary overrides = (IDictionary)GetPrivateField(window, "generatedOrganizationOverrides");

            Assert.Multiple(() =>
            {
                Assert.That(overrides.Contains("emote:1:anim"), Is.True,
                    "generatedOrganizationOverrides must contain the emote:1:anim key after loading.");
                string overrideValue = (string)overrides["emote:1:anim"]!;
                Assert.That(overrideValue, Does.Not.Contain("Images/").And.Not.Contain("Images\\"),
                    "Override must not resolve to the Images/ subfolder — the file is at character root.");
                Assert.That(overrideValue, Does.Contain("customanim.png").IgnoreCase,
                    "Override must reference the actual customanim.png file.");
            });

            window.Close();
        }

        [Test]
        public void AOCharacterFileCreator_FileOrganization_ExtraSubdirectoryBecomesExternalEntry()
        {
            string characterDirectory = CreateMinimalFileOrgCharacterFolder();
            Directory.CreateDirectory(Path.Combine(characterDirectory, "Notes"));

            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            bool loaded = window.TryLoadCharacterFolderForEditing(characterDirectory, out string errorMessage);
            Assert.That(loaded, Is.True, errorMessage);

            IList<(string RelativePath, bool IsFolder)> externals = GetExternalEntries(window);

            Assert.That(
                externals.Any(e => e.IsFolder && e.RelativePath.Contains("Notes", StringComparison.OrdinalIgnoreCase)),
                Is.True,
                "The Notes/ subdirectory must appear as an external folder entry.");

            window.Close();
        }

        // ============================================================
        // Group 2: Character creator emote reorder / delete tests
        // ============================================================

        [Test]
        public void AOCharacterFileCreator_EmoteReorder_MoveUpShiftsEmoteToEarlierSlot()
        {
            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            AddThreeEmotes(window);

            IList emotes = (IList)GetPrivateField(window, "emotes");
            InvokePrivate(window, "SelectEmoteTile", emotes[1]!); // "Emote 2" → select it
            InvokePrivate(window, "MoveEmoteUpButton_Click", window, new RoutedEventArgs());

            emotes = (IList)GetPrivateField(window, "emotes");
            Assert.Multiple(() =>
            {
                Assert.That(emotes.Count, Is.EqualTo(3));
                Assert.That(GetEmoteName(emotes[0]!), Is.EqualTo("Emote 2"),
                    "Emote 2 must now occupy the first slot after MoveUp.");
                Assert.That(GetEmoteName(emotes[1]!), Is.EqualTo("Emote 1"),
                    "Emote 1 must have shifted down to the second slot.");
            });

            window.Close();
        }

        [Test]
        public void AOCharacterFileCreator_EmoteReorder_MoveDownShiftsEmoteToLaterSlot()
        {
            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            AddThreeEmotes(window);

            IList emotes = (IList)GetPrivateField(window, "emotes");
            InvokePrivate(window, "SelectEmoteTile", emotes[0]!); // "Emote 1" → select it
            InvokePrivate(window, "MoveEmoteDownButton_Click", window, new RoutedEventArgs());

            emotes = (IList)GetPrivateField(window, "emotes");
            Assert.Multiple(() =>
            {
                Assert.That(emotes.Count, Is.EqualTo(3));
                Assert.That(GetEmoteName(emotes[0]!), Is.EqualTo("Emote 2"),
                    "Emote 2 must now occupy the first slot after Emote 1 moved down.");
                Assert.That(GetEmoteName(emotes[1]!), Is.EqualTo("Emote 1"),
                    "Emote 1 must now be in the second slot.");
            });

            window.Close();
        }

        [Test]
        public void AOCharacterFileCreator_EmoteReorder_RemoveDeletesSelectedEmoteAndAdjustsSelection()
        {
            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            AddThreeEmotes(window);

            IList emotes = (IList)GetPrivateField(window, "emotes");
            InvokePrivate(window, "SelectEmoteTile", emotes[1]!); // "Emote 2" → select it
            InvokePrivate(window, "RemoveEmoteButton_Click", window, new RoutedEventArgs());

            emotes = (IList)GetPrivateField(window, "emotes");
            MethodInfo getSelectedMethod = typeof(AOCharacterFileCreatorWindow)
                .GetMethod("GetSelectedEmote", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? selectedEmote = getSelectedMethod.Invoke(window, Array.Empty<object>());

            Assert.Multiple(() =>
            {
                Assert.That(emotes.Count, Is.EqualTo(2),
                    "After removing Emote 2, only 2 emotes remain.");
                Assert.That(GetEmoteName(emotes[0]!), Is.EqualTo("Emote 1"),
                    "Emote 1 stays at index 0.");
                Assert.That(GetEmoteName(emotes[1]!), Is.EqualTo("Emote 3"),
                    "Emote 3 moves to index 1.");
                Assert.That(selectedEmote, Is.Not.Null,
                    "An adjacent emote must be auto-selected after removal.");
                Assert.That(GetEmoteName(selectedEmote!), Is.EqualTo("Emote 3"),
                    "Selection must land on Emote 3 (the successor at the same index).");
            });

            window.Close();
        }

        [Test]
        public void AOCharacterFileCreator_EmoteReorder_MoveUpAtFirstPositionIsNoOp()
        {
            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            AddThreeEmotes(window);

            IList emotes = (IList)GetPrivateField(window, "emotes");
            InvokePrivate(window, "SelectEmoteTile", emotes[0]!); // first emote
            InvokePrivate(window, "MoveEmoteUpButton_Click", window, new RoutedEventArgs());

            emotes = (IList)GetPrivateField(window, "emotes");
            Assert.Multiple(() =>
            {
                Assert.That(emotes.Count, Is.EqualTo(3));
                Assert.That(GetEmoteName(emotes[0]!), Is.EqualTo("Emote 1"),
                    "MoveUp on the first emote must leave order unchanged.");
                Assert.That(GetEmoteName(emotes[1]!), Is.EqualTo("Emote 2"));
                Assert.That(GetEmoteName(emotes[2]!), Is.EqualTo("Emote 3"));
            });

            window.Close();
        }

        [Test]
        public void AOCharacterFileCreator_EmoteReorder_MoveDownAtLastPositionIsNoOp()
        {
            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();
            AddThreeEmotes(window);

            IList emotes = (IList)GetPrivateField(window, "emotes");
            InvokePrivate(window, "SelectEmoteTile", emotes[2]!); // last emote
            InvokePrivate(window, "MoveEmoteDownButton_Click", window, new RoutedEventArgs());

            emotes = (IList)GetPrivateField(window, "emotes");
            Assert.Multiple(() =>
            {
                Assert.That(emotes.Count, Is.EqualTo(3));
                Assert.That(GetEmoteName(emotes[0]!), Is.EqualTo("Emote 1"));
                Assert.That(GetEmoteName(emotes[1]!), Is.EqualTo("Emote 2"));
                Assert.That(GetEmoteName(emotes[2]!), Is.EqualTo("Emote 3"),
                    "MoveDown on the last emote must leave order unchanged.");
            });

            window.Close();
        }

        // ============================================================
        // Group 3: AI MainWindow action executor tests
        // ============================================================

        [Test]
        public async Task MainWindow_AiExecutor_SetTextColorMutatesClientTextColor()
        {
            MainWindow window = new MainWindow();
            AOClient profileClient = new AOClient("ws://127.0.0.1:27016");
            AOClientControlSnapshot snapshot = AOClientControlSnapshotBuilder.Build(profileClient);

            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetTextColor, Value = "Cyan" }
                }
            };

            string summary = await InvokeExecuteAgentResponseAsync(window, profileClient, response, snapshot);

            Assert.Multiple(() =>
            {
                Assert.That(profileClient.textColor, Is.EqualTo(ICMessage.TextColors.Cyan),
                    "ExecuteAgentResponseAsync must apply SetTextColor to profileClient.textColor.");
                Assert.That(summary, Does.Contain("text color").IgnoreCase,
                    "Summary must describe the applied color change.");
            });

            window.Close();
        }

        [Test]
        public async Task MainWindow_AiExecutor_SetIcShownameMutatesClientShowname()
        {
            MainWindow window = new MainWindow();
            AOClient profileClient = new AOClient("ws://127.0.0.1:27016");
            AOClientControlSnapshot snapshot = AOClientControlSnapshotBuilder.Build(profileClient);

            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetIcShowname, Value = "Phoenix Wright" }
                }
            };

            string summary = await InvokeExecuteAgentResponseAsync(window, profileClient, response, snapshot);

            Assert.Multiple(() =>
            {
                Assert.That(profileClient.ICShowname, Is.EqualTo("Phoenix Wright"),
                    "ExecuteAgentResponseAsync must apply SetIcShowname to profileClient.ICShowname.");
                Assert.That(summary, Does.Contain("IC showname").IgnoreCase,
                    "Summary must describe the showname change.");
            });

            window.Close();
        }

        [Test]
        public async Task MainWindow_AiExecutor_SetPositionMutatesClientPosition()
        {
            MainWindow window = new MainWindow();
            AOClient profileClient = new AOClient("ws://127.0.0.1:27016");
            AOClientControlSnapshot snapshot = AOClientControlSnapshotBuilder.Build(profileClient);

            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetPosition, Value = "jud" }
                }
            };

            string summary = await InvokeExecuteAgentResponseAsync(window, profileClient, response, snapshot);

            Assert.Multiple(() =>
            {
                Assert.That(profileClient.curPos, Is.EqualTo("jud"),
                    "ExecuteAgentResponseAsync must apply SetPosition to profileClient.curPos.");
                Assert.That(summary, Does.Contain("position").IgnoreCase,
                    "Summary must describe the position change.");
            });

            window.Close();
        }

        [Test]
        public async Task MainWindow_AiExecutor_MultipleActionsAppliedInOrderAndSummaryDescribesBoth()
        {
            MainWindow window = new MainWindow();
            AOClient profileClient = new AOClient("ws://127.0.0.1:27016");
            AOClientControlSnapshot snapshot = AOClientControlSnapshotBuilder.Build(profileClient);

            AgentResponse response = new AgentResponse
            {
                ShouldRespond = true,
                Actions = new List<AgentAction>
                {
                    new AgentAction { Type = AgentActionType.SetTextColor, Value = "Green" },
                    new AgentAction { Type = AgentActionType.SetIcShowname, Value = "Miles Edgeworth" }
                }
            };

            string summary = await InvokeExecuteAgentResponseAsync(window, profileClient, response, snapshot);

            Assert.Multiple(() =>
            {
                Assert.That(profileClient.textColor, Is.EqualTo(ICMessage.TextColors.Green),
                    "SetTextColor action must be applied.");
                Assert.That(profileClient.ICShowname, Is.EqualTo("Miles Edgeworth"),
                    "SetIcShowname action must be applied after SetTextColor.");
                Assert.That(summary, Does.Contain("Green").IgnoreCase,
                    "Summary must mention the color change.");
                Assert.That(summary, Does.Contain("Miles Edgeworth").IgnoreCase,
                    "Summary must mention the showname change.");
            });

            window.Close();
        }

        // ============================================================
        // Group 4: AI raw-response tagging / log-link tests
        // ============================================================

        [Test]
        public void MainWindow_AiTagging_QueuePendingAiOriginResponse_AddsBothIcAndOocEntries()
        {
            MainWindow window = new MainWindow();
            AOClient profileClient = new AOClient("ws://127.0.0.1:27016");
            profileClient.SetICShowname("Phoenix Wright");
            profileClient.OOCShowname = "PhoenixOOC";

            InvokePrivate(window, "QueuePendingAiOriginResponse", profileClient, "raw AI response text");

            IDictionary pending = (IDictionary)GetPrivateField(window, "pendingAiOriginResponses");

            Assert.Multiple(() =>
            {
                Assert.That(pending.Contains(profileClient), Is.True,
                    "pendingAiOriginResponses must hold an entry for the queued client.");
                IList pendingList = (IList)pending[profileClient]!;
                Assert.That(pendingList.Count, Is.EqualTo(2),
                    "Both IC and OOC pending entries must be queued.");
            });

            window.Close();
        }

        [Test]
        public void MainWindow_AiTagging_HandleAiFinalMessageSuccess_QueuesPendingEntries()
        {
            MainWindow window = new MainWindow();
            AOClient profileClient = new AOClient("ws://127.0.0.1:27016");

            AOClientAgentStatusUpdate successUpdate = new AOClientAgentStatusUpdate(
                AOClientAgentStatusKind.FinalMessage,
                message: "AI responded successfully.",
                isError: false,
                rawResponse: "{ \"actions\": [] }");

            InvokePrivate(window, "HandleAiFinalMessage", profileClient, successUpdate, "[test]");

            IDictionary pending = (IDictionary)GetPrivateField(window, "pendingAiOriginResponses");

            Assert.That(pending.Contains(profileClient), Is.True,
                "A successful FinalMessage with a non-empty rawResponse must queue pending AI origin entries.");

            window.Close();
        }

        [Test]
        public void MainWindow_AiTagging_HandleAiFinalMessageError_DoesNotQueuePendingEntries()
        {
            MainWindow window = new MainWindow();
            AOClient profileClient = new AOClient("ws://127.0.0.1:27016");

            AOClientAgentStatusUpdate errorUpdate = new AOClientAgentStatusUpdate(
                AOClientAgentStatusKind.FinalMessage,
                message: "Parse error.",
                isError: true,
                rawResponse: "some raw response text");

            InvokePrivate(window, "HandleAiFinalMessage", profileClient, errorUpdate, "[test]");

            IDictionary pending = (IDictionary)GetPrivateField(window, "pendingAiOriginResponses");

            Assert.That(pending.Contains(profileClient), Is.False,
                "An error FinalMessage must not queue pending AI origin entries even when rawResponse is non-empty.");

            window.Close();
        }

        [Test]
        public void MainWindow_AiTagging_BuildRawResponseLinks_ReturnsLinkWithRequestedText()
        {
            MainWindow window = new MainWindow();
            AOClient profileClient = new AOClient("ws://127.0.0.1:27016");

            MethodInfo method = typeof(MainWindow).GetMethod(
                "BuildRawResponseLinks",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                ?? throw new InvalidOperationException("BuildRawResponseLinks not found on MainWindow.");

            object? result = method.Invoke(
                window,
                new object?[] { profileClient, "{ \"actions\": [] }", "Test", "(AI)" });

            IReadOnlyList<LogMessageActionLink>? links = result as IReadOnlyList<LogMessageActionLink>;

            Assert.Multiple(() =>
            {
                Assert.That(links, Is.Not.Null,
                    "BuildRawResponseLinks must return a non-null list when rawResponse is non-empty.");
                Assert.That(links!.Count, Is.GreaterThan(0),
                    "At least one LogMessageActionLink must be returned.");
                Assert.That(links[0].Text, Is.EqualTo("(AI)"),
                    "Link text must match the linkText argument passed to BuildRawResponseLinks.");
            });

            window.Close();
        }

        // ============================================================
        // Test helpers
        // ============================================================

        private string CreateMinimalFileOrgCharacterFolder()
        {
            // Minimal character folder for file-organization tests:
            //   char.ini  - one emote using animation "customanim"
            //   char_icon.png
            //   customanim.png  - at the character root (not under Images/)
            // After loading, generatedOrganizationOverrides["emote:1:anim"] must equal
            // "customanim.png" rather than the default "Images/customanim.png".
            string characterDirectory = Path.Combine(tempRoot, "characters", "TestChar_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(characterDirectory);
            File.WriteAllText(
                Path.Combine(characterDirectory, "char.ini"),
                "[Options]\n"
                + "showname=TestChar\n"
                + "side=def\n"
                + "gender=male\n"
                + "[Emotions]\n"
                + "number=1\n"
                + "1=customanim#-#customanim#0#1\n");
            CreateSolidPng(Path.Combine(characterDirectory, "char_icon.png"), Colors.SteelBlue);
            CreateSolidPng(Path.Combine(characterDirectory, "customanim.png"), Colors.ForestGreen);
            return characterDirectory;
        }

        private void AddThreeEmotes(AOCharacterFileCreatorWindow window)
        {
            InvokePrivate(window, "AddEmoteButton_Click", window, new RoutedEventArgs());
            InvokePrivate(window, "AddEmoteButton_Click", window, new RoutedEventArgs());
            InvokePrivate(window, "AddEmoteButton_Click", window, new RoutedEventArgs());
        }

        private static async Task<string> InvokeExecuteAgentResponseAsync(
            MainWindow window,
            AOClient profileClient,
            AgentResponse response,
            AOClientControlSnapshot snapshot)
        {
            MethodInfo method = typeof(MainWindow).GetMethod(
                "ExecuteAgentResponseAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                ?? throw new InvalidOperationException("ExecuteAgentResponseAsync not found on MainWindow.");
            Task<string> task = (Task<string>)method.Invoke(
                window,
                new object[] { profileClient, response, snapshot, CancellationToken.None })!;
            return await task;
        }

        private static IList<(string RelativePath, bool IsFolder)> GetExternalEntries(AOCharacterFileCreatorWindow window)
        {
            IList entries = (IList)GetPrivateField(window, "externalOrganizationEntries");
            List<(string, bool)> result = new List<(string, bool)>();
            foreach (object entry in entries)
            {
                string relPath = (string)entry.GetType().GetProperty("RelativePath")!.GetValue(entry)!;
                bool isFolder = (bool)entry.GetType().GetProperty("IsFolder")!.GetValue(entry)!;
                result.Add((relPath, isFolder));
            }

            return result;
        }

        private static string GetEmoteName(object emote)
        {
            return (string)emote.GetType().GetProperty("Name")!.GetValue(emote)!;
        }

        private static void ResetStaticTestHooks()
        {
            InvokeStaticNoArg(typeof(MainWindow), "ResetTestHooks");
            InvokeStaticNoArg(typeof(AOCharacterFileCreatorWindow), "ResetGenerateTestHooks");
        }

        private static void InvokeStaticNoArg(Type type, string methodName)
        {
            MethodInfo? method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            method?.Invoke(null, Array.Empty<object>());
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

            BitmapSource bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Pbgra32, null, pixels, 16 * 4);
            using FileStream stream = File.Create(path);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            return path;
        }
    }
}
