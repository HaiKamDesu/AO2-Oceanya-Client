using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests
{
    [TestFixture]
    public class AO2SoundListTests
    {
        private string tempRoot = string.Empty;
        private List<string> originalBaseFolders = new List<string>();
        private string originalConfigPath = string.Empty;

        [SetUp]
        public void SetUp()
        {
            originalBaseFolders = new List<string>(Globals.BaseFolders);
            originalConfigPath = Globals.PathToConfigINI;
            tempRoot = Path.Combine(Path.GetTempPath(), "ao2_soundlist_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            Globals.BaseFolders = originalBaseFolders;
            Globals.PathToConfigINI = originalConfigPath;

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        [Test]
        public void ParseLine_SplitsAo2DisplayLabelFromActualValue()
        {
            AO2SoundListEntry entry = AO2SoundList.ParseLine("Jason/Hit = Hit");

            Assert.That(entry.Value, Is.EqualTo("Jason/Hit"));
            Assert.That(entry.DisplayText, Is.EqualTo("Hit"));
        }

        [Test]
        public void LoadEntries_FallsBackToSoundsIni_AndAppendsBaseSoundList()
        {
            string characterDirectory = Path.Combine(tempRoot, "characters", "Jason");
            Directory.CreateDirectory(characterDirectory);
            File.WriteAllText(Path.Combine(characterDirectory, "sounds.ini"), "Jason/Hit = Hit\nJason/Slash = Slash");
            File.WriteAllText(Path.Combine(tempRoot, "soundlist.ini"), "global/boom = Boom");

            IReadOnlyList<AO2SoundListEntry> entries = AO2SoundList.LoadEntries(
                characterDirectory,
                new List<string> { tempRoot });

            Assert.That(entries.Select(static entry => entry.Value).ToArray(), Is.EqualTo(new[]
            {
                "Jason/Hit",
                "Jason/Slash",
                "global/boom"
            }));
            Assert.That(entries.Select(static entry => entry.DisplayText).ToArray(), Is.EqualTo(new[]
            {
                "Hit",
                "Slash",
                "Boom"
            }));
        }

        [Test]
        public void LoadEntries_UsesHighestPriorityBaseSoundList_FromLastConfiguredMountLikeAo2()
        {
            string baseDirectory = Path.Combine(tempRoot, "base");
            string lowPriorityMount = Path.Combine(tempRoot, "content_low");
            string highPriorityMount = Path.Combine(tempRoot, "content_high");
            string configPath = Path.Combine(baseDirectory, "config.ini");
            string characterDirectory = Path.Combine(baseDirectory, "characters", "Jason");

            Directory.CreateDirectory(baseDirectory);
            Directory.CreateDirectory(lowPriorityMount);
            Directory.CreateDirectory(highPriorityMount);
            Directory.CreateDirectory(characterDirectory);

            File.WriteAllText(
                configPath,
                $"mount_paths={lowPriorityMount},{highPriorityMount}{Environment.NewLine}log_maximum=20");
            File.WriteAllText(Path.Combine(lowPriorityMount, "soundlist.ini"), "low/boom = Low Boom");
            File.WriteAllText(Path.Combine(highPriorityMount, "soundlist.ini"), "high/boom = High Boom");
            File.WriteAllText(Path.Combine(baseDirectory, "soundlist.ini"), "base/boom = Base Boom");

            Globals.UpdateConfigINI(configPath);

            IReadOnlyList<AO2SoundListEntry> entries = AO2SoundList.LoadEntries(characterDirectory);

            Assert.That(entries.Select(static entry => entry.Value).ToArray(), Is.EqualTo(new[]
            {
                "high/boom"
            }));
            Assert.That(entries.Select(static entry => entry.DisplayText).ToArray(), Is.EqualTo(new[]
            {
                "High Boom"
            }));
        }
    }

    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class AOCharacterFileCreatorSoundListSupportTests
    {
        private List<string> originalBaseFolders = new List<string>();
        private string originalPathToConfigIni = string.Empty;
        private string tempRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            originalBaseFolders = new List<string>(Globals.BaseFolders);
            originalPathToConfigIni = Globals.PathToConfigINI;
            tempRoot = Path.Combine(Path.GetTempPath(), "ao_char_creator_soundlist_tests_" + Guid.NewGuid().ToString("N"));
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

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        [Test]
        public void EditingExistingFolder_TreatsSoundListIniAsUsedSupportFile()
        {
            string characterDirectory = Path.Combine(tempRoot, "characters", "Jason");
            string nestedDirectory = Path.Combine(characterDirectory, "Alt");
            Directory.CreateDirectory(nestedDirectory);

            File.WriteAllText(
                Path.Combine(characterDirectory, "char.ini"),
                "[Options]\nshowname=Jason\ngender=male\nside=wit\n[Emotions]\nnumber=1\n1=normal#-#normal#0#99\n");
            File.WriteAllText(Path.Combine(characterDirectory, "soundlist.ini"), "Jason/Hit = Hit");
            File.WriteAllText(Path.Combine(nestedDirectory, "soundlist.ini"), "Jason/AltHit = Alt Hit");
            File.WriteAllText(Path.Combine(characterDirectory, "notes.txt"), "unused");

            Globals.BaseFolders = new List<string> { tempRoot };
            Globals.PathToConfigINI = Path.Combine(tempRoot, "config.ini");
            File.WriteAllText(Globals.PathToConfigINI, "mount_paths=@Invalid()\n");

            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow(characterDirectory);
            try
            {
                FieldInfo? field = typeof(AOCharacterFileCreatorWindow).GetField(
                    "allFileOrganizationEntries",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);

                List<FileOrganizationEntryViewModel>? entries = field!.GetValue(window) as List<FileOrganizationEntryViewModel>;
                Assert.That(entries, Is.Not.Null);
                List<FileOrganizationEntryViewModel> safeEntries = entries!;

                FileOrganizationEntryViewModel? rootSoundList = safeEntries.FirstOrDefault(static entry =>
                    string.Equals(entry.RelativePath, "soundlist.ini", StringComparison.OrdinalIgnoreCase));
                FileOrganizationEntryViewModel? nestedSoundList = safeEntries.FirstOrDefault(static entry =>
                    string.Equals(entry.RelativePath, "Alt/soundlist.ini", StringComparison.OrdinalIgnoreCase));
                FileOrganizationEntryViewModel? notes = safeEntries.FirstOrDefault(static entry =>
                    string.Equals(entry.RelativePath, "notes.txt", StringComparison.OrdinalIgnoreCase));

                Assert.That(rootSoundList, Is.Not.Null);
                Assert.That(rootSoundList!.IsUnused, Is.False);
                Assert.That(rootSoundList.StatusText, Is.EqualTo("Character support file"));

                Assert.That(nestedSoundList, Is.Not.Null);
                Assert.That(nestedSoundList!.IsUnused, Is.False);

                Assert.That(notes, Is.Not.Null);
                Assert.That(notes!.IsUnused, Is.True);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
