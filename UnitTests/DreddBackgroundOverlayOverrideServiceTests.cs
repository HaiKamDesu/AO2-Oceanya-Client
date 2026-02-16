using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.AdvancedFeatures;
using AOBot_Testing.Agents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UnitTests
{
    [TestFixture]
    public class DreddBackgroundOverlayOverrideServiceTests
    {
        private string tempRoot = string.Empty;
        private SaveData originalSaveData = new SaveData();
        private List<string> originalBaseFolders = new List<string>();

        [SetUp]
        public void SetUp()
        {
            originalSaveData = CloneSaveData(SaveFile.Data);
            originalBaseFolders = new List<string>(Globals.BaseFolders);

            tempRoot = Path.Combine(Path.GetTempPath(), $"dredd_overlay_tests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            Globals.BaseFolders = new List<string> { tempRoot };
            SaveFile.Data = new SaveData();
            SaveFile.Save();
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Data = CloneSaveData(originalSaveData);
            SaveFile.Save();
            Globals.BaseFolders = originalBaseFolders;

            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, true);
                }
                catch
                {
                    // ignore test cleanup failures
                }
            }
        }

        [Test]
        public void ResolveOverlayValueForDesignIni_ResolvesDotSegmentsAndExtensionlessFile()
        {
            string bgDir = Path.Combine(tempRoot, "background", "testbg");
            Directory.CreateDirectory(bgDir);

            string designIniPath = Path.Combine(bgDir, "design.ini");
            File.WriteAllText(designIniPath, "");

            string overlayFilePath = Path.Combine(bgDir, "overlayTestBG.png");
            File.WriteAllText(overlayFilePath, "");

            string result = DreddBackgroundOverlayOverrideService.ResolveOverlayValueForDesignIni(
                "custom/../overlayTestBG",
                designIniPath,
                bgDir);

            Assert.That(result, Is.EqualTo("overlayTestBG.png"));
        }

        [Test]
        public void ResolveOverlayValueForDesignIni_AcceptsFolderStylePath()
        {
            string bgDir = Path.Combine(tempRoot, "background", "testbg");
            Directory.CreateDirectory(bgDir);

            string designIniPath = Path.Combine(bgDir, "design.ini");
            File.WriteAllText(designIniPath, "");

            Directory.CreateDirectory(Path.Combine(bgDir, "overlayTestBG"));

            string result = DreddBackgroundOverlayOverrideService.ResolveOverlayValueForDesignIni(
                "custom/../overlayTestBG",
                designIniPath,
                bgDir);

            Assert.That(result, Is.EqualTo("overlayTestBG"));
        }

        [Test]
        public void TryApplyOverlay_ResolvesBackgroundPathWithDotSegments()
        {
            string bgDir = Path.Combine(tempRoot, "background", "overlayTestBG");
            Directory.CreateDirectory(bgDir);
            string designIniPath = Path.Combine(bgDir, "design.ini");
            File.WriteAllText(designIniPath, "");

            string overlayPath = Path.Combine(bgDir, "ovr.png");
            File.WriteAllText(overlayPath, "");

            AOClient client = new AOClient("ws://localhost:12345")
            {
                curBG = "custom/../overlayTestBG",
                curPos = "def"
            };

            DreddOverlayEntry overlay = new DreddOverlayEntry
            {
                Name = "test",
                FilePath = overlayPath
            };

            bool applied = DreddBackgroundOverlayOverrideService.TryApplyOverlay(client, overlay, out string error);

            Assert.That(applied, Is.True, error);
            string[] lines = File.ReadAllLines(designIniPath);
            Assert.That(lines.Any(line => line.StartsWith("def=", StringComparison.OrdinalIgnoreCase)), Is.True);
        }

        [Test]
        public void GetCachedChangesPreview_ReturnsCurrentAndOriginalValues()
        {
            string bgDir = Path.Combine(tempRoot, "background", "testbg");
            Directory.CreateDirectory(bgDir);

            string designIniPath = Path.Combine(bgDir, "design.ini");
            File.WriteAllLines(designIniPath, new[]
            {
                "[Overlays]",
                "def=overlay_new.png"
            });

            SaveFile.Data.DreddBackgroundOverlayOverride.MutationCache.Add(new DreddOverlayMutationRecord
            {
                DesignIniPath = designIniPath,
                PositionKey = "def",
                FileExisted = true,
                OverlaysSectionExisted = true,
                EntryExisted = true,
                OriginalValue = "overlay_old.png"
            });
            SaveFile.Save();

            var previews = DreddBackgroundOverlayOverrideService.GetCachedChangesPreview();

            Assert.That(previews.Count, Is.EqualTo(1));
            Assert.That(previews[0].PositionKey, Is.EqualTo("def"));
            Assert.That(previews[0].OriginalValue, Is.EqualTo("overlay_old.png"));
            Assert.That(previews[0].CurrentValue, Is.EqualTo("overlay_new.png"));
            Assert.That(previews[0].Status, Is.EqualTo("Modified"));
        }

        [Test]
        public void TryDiscardAllChanges_RestoresOriginalOverlayEntry()
        {
            string bgDir = Path.Combine(tempRoot, "background", "testbg");
            Directory.CreateDirectory(bgDir);

            string designIniPath = Path.Combine(bgDir, "design.ini");
            File.WriteAllLines(designIniPath, new[]
            {
                "[Overlays]",
                "def=overlay_new.png"
            });

            SaveFile.Data.DreddBackgroundOverlayOverride.MutationCache.Add(new DreddOverlayMutationRecord
            {
                DesignIniPath = designIniPath,
                PositionKey = "def",
                FileExisted = true,
                OverlaysSectionExisted = true,
                EntryExisted = true,
                OriginalValue = "overlay_old.png"
            });
            SaveFile.Save();

            bool success = DreddBackgroundOverlayOverrideService.TryDiscardAllChanges(out string message);

            Assert.That(success, Is.True, message);
            string[] lines = File.ReadAllLines(designIniPath);
            Assert.That(lines.Any(line => line.Trim().Equals("def=overlay_old.png", StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(SaveFile.Data.DreddBackgroundOverlayOverride.MutationCache, Is.Empty);
        }

        private static SaveData CloneSaveData(SaveData source)
        {
            string json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<SaveData>(json) ?? new SaveData();
        }
    }
}
