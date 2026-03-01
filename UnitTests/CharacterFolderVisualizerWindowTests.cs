using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class CharacterFolderVisualizerWindowTests
    {
        private List<string> originalBaseFolders = new List<string>();
        private string originalPathToConfigIni = string.Empty;
        private string tempRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            originalBaseFolders = new List<string>(Globals.BaseFolders);
            originalPathToConfigIni = Globals.PathToConfigINI;

            tempRoot = Path.Combine(Path.GetTempPath(), "visualizer_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            if (Application.Current == null)
            {
                _ = new Application();
            }
        }

        [TearDown]
        public void TearDown()
        {
            Globals.BaseFolders = originalBaseFolders;
            Globals.PathToConfigINI = originalPathToConfigIni;
            ResetCharacterCache();

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
        public void Constructor_DoesNotThrow_AndPopulatesItems()
        {
            BuildCharacterFolder(
                name: "Phoenix",
                createCharIcon: true,
                createIdleSprite: true,
                out string expectedIconPath,
                out string expectedPreviewPath);
            RefreshCharactersFromTempRoot();

            CharacterFolderVisualizerWindow? window = null;
            Assert.DoesNotThrow(() =>
            {
                window = new CharacterFolderVisualizerWindow(null);
                window.LoadCharacterItemsForTests();
            });

            Assert.That(window, Is.Not.Null);
            Assert.That(window!.FolderItems.Count, Is.EqualTo(1));
            Assert.That(window.FolderItems[0].Name, Is.EqualTo("Phoenix"));
            Assert.That(window.FolderItems[0].PreviewPath, Is.EqualTo(expectedPreviewPath));
            Assert.That(window.FolderItems[0].IconPath, Is.EqualTo(expectedIconPath));
            window.Close();
        }

        [Test]
        public void FolderItems_FallsBackToCharacterIcon_WhenNoIdleSpriteExists()
        {
            BuildCharacterFolder(
                name: "Maya",
                createCharIcon: true,
                createIdleSprite: false,
                out string expectedIconPath,
                out _);
            RefreshCharactersFromTempRoot();

            CharacterFolderVisualizerWindow window = new CharacterFolderVisualizerWindow(null);
            window.LoadCharacterItemsForTests();

            Assert.That(window.FolderItems.Count, Is.EqualTo(1));
            Assert.That(window.FolderItems[0].PreviewPath, Is.EqualTo(expectedIconPath));
            window.Close();
        }

        [Test]
        public void ResolveFirstCharacterIdleSpritePath_PrefersIdlePrefixOverBaseAnimation()
        {
            string characterDir = Path.Combine(tempRoot, "characters", "Edgeworth");
            Directory.CreateDirectory(characterDir);

            string idlePrefixedPath = Path.Combine(characterDir, "(a)normal.png");
            string baseAnimationPath = Path.Combine(characterDir, "normal.png");
            File.WriteAllBytes(idlePrefixedPath, new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(baseAnimationPath, new byte[] { 1, 2, 3, 4 });

            CharacterFolder character = new CharacterFolder
            {
                Name = "Edgeworth",
                DirectoryPath = characterDir,
                CharIconPath = Path.Combine(characterDir, "char_icon.png"),
                configINI = new CharacterConfigINI(string.Empty)
                {
                    EmotionsCount = 1,
                    Emotions = new Dictionary<int, Emote>
                    {
                        [1] = new Emote(1)
                        {
                            Name = "normal",
                            Animation = "normal"
                        }
                    }
                }
            };

            string resolvedPreviewPath = CharacterFolderVisualizerWindow.ResolveFirstCharacterIdleSpritePath(character);
            Assert.That(resolvedPreviewPath, Is.EqualTo(idlePrefixedPath));
        }

        [Test]
        public void ViewModeSwitch_UpdatesListViewLayoutWithoutErrors()
        {
            BuildCharacterFolder(
                name: "Franziska",
                createCharIcon: true,
                createIdleSprite: true,
                out _,
                out _);
            RefreshCharactersFromTempRoot();

            CharacterFolderVisualizerWindow window = new CharacterFolderVisualizerWindow(null);
            window.LoadCharacterItemsForTests();

            ComboBox? viewModeCombo = window.FindName("ViewModeCombo") as ComboBox;
            ListView? folderListView = window.FindName("FolderListView") as ListView;

            Assert.That(viewModeCombo, Is.Not.Null);
            Assert.That(folderListView, Is.Not.Null);
            ComboBox safeViewModeCombo = viewModeCombo!;
            ListView safeFolderListView = folderListView!;

            FolderVisualizerViewPreset? tablePreset = null;
            FolderVisualizerViewPreset? normalPreset = null;
            foreach (object entry in safeViewModeCombo.Items)
            {
                if (entry is not FolderVisualizerViewPreset preset)
                {
                    continue;
                }

                if (tablePreset == null && preset.Mode == FolderVisualizerLayoutMode.Table)
                {
                    tablePreset = preset;
                }

                if (normalPreset == null && preset.Mode == FolderVisualizerLayoutMode.Normal)
                {
                    normalPreset = preset;
                }
            }

            Assert.That(tablePreset, Is.Not.Null, "Expected at least one table preset.");
            Assert.That(normalPreset, Is.Not.Null, "Expected at least one normal preset.");

            Assert.DoesNotThrow(() => safeViewModeCombo.SelectedItem = tablePreset);
            Assert.That(safeFolderListView.View, Is.Not.Null, "Details mode should use GridView.");
            Assert.That(safeFolderListView.ItemTemplate, Is.Null, "Details mode should not use tile DataTemplate.");

            Assert.DoesNotThrow(() => safeViewModeCombo.SelectedItem = normalPreset);
            Assert.That(safeFolderListView.View, Is.Null, "Icon mode should not use GridView.");
            Assert.That(safeFolderListView.ItemTemplate, Is.Not.Null, "Icon mode should use tile DataTemplate.");

            window.Close();
        }

        [Test]
        public void ViewModeCombo_NullSelection_DoesNotResetSavedPreset()
        {
            BuildCharacterFolder(
                name: "Gumshoe",
                createCharIcon: true,
                createIdleSprite: true,
                out _,
                out _);
            RefreshCharactersFromTempRoot();

            CharacterFolderVisualizerWindow window = new CharacterFolderVisualizerWindow(null);
            window.LoadCharacterItemsForTests();

            ComboBox? viewModeCombo = window.FindName("ViewModeCombo") as ComboBox;
            Assert.That(viewModeCombo, Is.Not.Null);
            ComboBox safeViewModeCombo = viewModeCombo!;

            FolderVisualizerViewPreset? gridPreset = safeViewModeCombo.Items
                .OfType<FolderVisualizerViewPreset>()
                .FirstOrDefault(preset => preset.Mode == FolderVisualizerLayoutMode.Normal);
            Assert.That(gridPreset, Is.Not.Null);

            safeViewModeCombo.SelectedItem = gridPreset;
            Assert.That(SaveFile.Data.FolderVisualizer.SelectedPresetId, Is.EqualTo(gridPreset!.Id));

            safeViewModeCombo.SelectedItem = null;
            Assert.That(SaveFile.Data.FolderVisualizer.SelectedPresetId, Is.EqualTo(gridPreset.Id));
            Assert.That(SaveFile.Data.FolderVisualizer.SelectedPresetName, Is.EqualTo(gridPreset.Name));

            window.Close();
        }

        private void RefreshCharactersFromTempRoot()
        {
            Globals.BaseFolders = new List<string> { tempRoot };
            Globals.PathToConfigINI = Path.Combine(tempRoot, "config.ini");
            ResetCharacterCache();
            CharacterFolder.RefreshCharacterList();
        }

        private void BuildCharacterFolder(
            string name,
            bool createCharIcon,
            bool createIdleSprite,
            out string expectedIconPath,
            out string expectedPreviewPath)
        {
            string charDir = Path.Combine(tempRoot, "characters", name);
            Directory.CreateDirectory(charDir);

            expectedIconPath = Path.Combine(charDir, "char_icon.png");
            if (createCharIcon)
            {
                File.WriteAllBytes(expectedIconPath, new byte[] { 1, 2, 3, 4 });
            }

            expectedPreviewPath = Path.Combine(charDir, "(a)normal.png");
            if (createIdleSprite)
            {
                File.WriteAllBytes(expectedPreviewPath, new byte[] { 1, 2, 3, 4 });
            }

            string iniPath = Path.Combine(charDir, "char.ini");
            string ini =
                "[Options]\n" +
                $"showname={name}\n" +
                "gender=unknown\n" +
                "side=def\n" +
                "[Emotions]\n" +
                "number=2\n" +
                "1=normal#normal#normal#0#99\n" +
                "2=talk#talk#talk#0#99\n";

            File.WriteAllText(iniPath, ini);

            if (!createIdleSprite)
            {
                expectedPreviewPath = expectedIconPath;
            }
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
                    // Ignore cleanup errors in tests.
                }
            }
        }
    }
}
