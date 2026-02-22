using System;
using System.IO;
using System.Threading;
using System.Windows;
using AOBot_Testing.Structures;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class CharacterEmoteVisualizerWindowTests
    {
        private string tempRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "emote_visualizer_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            if (Application.Current == null)
            {
                _ = new Application();
            }
        }

        [TearDown]
        public void TearDown()
        {
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
        public void BuildEmoteItems_ResolvesIconPreAnimationAndFinalAnimation()
        {
            CharacterFolder folder = BuildCharacterFolderWithEmotes();
            CharacterEmoteVisualizerWindow window = new CharacterEmoteVisualizerWindow(folder);

            System.Collections.Generic.List<EmoteVisualizerItem> items = window.BuildEmoteItems(folder);

            Assert.That(items.Count, Is.EqualTo(2));
            Assert.That(items[0].Name, Is.EqualTo("normal"));
            Assert.That(items[0].IconPath, Is.EqualTo(Path.Combine(folder.DirectoryPath, "Emotions", "button1_off.png")));
            Assert.That(items[0].PreAnimationPath, Is.EqualTo(Path.Combine(folder.DirectoryPath, "pre_normal.png")));
            Assert.That(items[0].AnimationPath, Is.EqualTo(Path.Combine(folder.DirectoryPath, "(a)normal.png")));

            Assert.That(items[1].NameWithId, Is.EqualTo("2: talk"));
            Assert.That(items[1].PreAnimationPath, Is.EqualTo(Path.Combine(folder.DirectoryPath, "pre_talk.png")));
            Assert.That(items[1].AnimationPath, Is.EqualTo(Path.Combine(folder.DirectoryPath, "(a)talk.png")));

            window.Close();
        }

        private CharacterFolder BuildCharacterFolderWithEmotes()
        {
            string characterDirectory = Path.Combine(tempRoot, "characters", "Apollo");
            string emotionsDirectory = Path.Combine(characterDirectory, "Emotions");
            Directory.CreateDirectory(characterDirectory);
            Directory.CreateDirectory(emotionsDirectory);

            File.WriteAllBytes(Path.Combine(characterDirectory, "char_icon.png"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(emotionsDirectory, "button1_off.png"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(characterDirectory, "pre_normal.png"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(characterDirectory, "(a)normal.png"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(characterDirectory, "pre_talk.png"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(characterDirectory, "(a)talk.png"), new byte[] { 1, 2, 3, 4 });

            string iniPath = Path.Combine(characterDirectory, "char.ini");
            string ini =
                "[Options]\n" +
                "showname=Apollo Justice\n" +
                "gender=unknown\n" +
                "side=def\n" +
                "[Emotions]\n" +
                "number=2\n" +
                "1=normal#pre_normal#normal#0#0\n" +
                "2=talk#pre_talk#talk#0#0\n";
            File.WriteAllText(iniPath, ini);

            return CharacterFolder.Create(iniPath);
        }
    }
}
