using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        [Test]
        public void BuildEmoteItems_LeadingSlashAnimationToken_ResolvesAoIdleAsset()
        {
            string characterDirectory = Path.Combine(tempRoot, "characters", "Akechi");
            Directory.CreateDirectory(characterDirectory);
            File.WriteAllBytes(Path.Combine(characterDirectory, "(a)Normal.webp"), new byte[] { 1, 2, 3, 4 });

            string iniPath = Path.Combine(characterDirectory, "char.ini");
            string ini =
                "[Options]\n" +
                "showname=Akechi\n" +
                "gender=unknown\n" +
                "side=def\n" +
                "[Emotions]\n" +
                "number=1\n" +
                "1=Normal#-#/Normal#0#1\n";
            File.WriteAllText(iniPath, ini);

            CharacterFolder folder = CharacterFolder.Create(iniPath);
            CharacterEmoteVisualizerWindow window = new CharacterEmoteVisualizerWindow(folder);
            System.Collections.Generic.List<EmoteVisualizerItem> items = window.BuildEmoteItems(folder);

            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(items[0].AnimationPath, Is.EqualTo(Path.Combine(characterDirectory, "(a)Normal.webp")));
            window.Close();
        }

        [Test]
        public void BuildViewportAutoplayItemIds_PrioritizesSelectedAndCapsCount()
        {
            List<EmoteVisualizerItem> visibleItems = new List<EmoteVisualizerItem>
            {
                new EmoteVisualizerItem { Id = 10 },
                new EmoteVisualizerItem { Id = 11 },
                new EmoteVisualizerItem { Id = 12 },
                new EmoteVisualizerItem { Id = 13 }
            };

            HashSet<int> autoplay = CharacterEmoteVisualizerWindow.BuildViewportAutoplayItemIds(
                visibleItems,
                selectedId: 12,
                maxItems: 2);

            Assert.That(autoplay.Count, Is.EqualTo(2));
            Assert.That(autoplay.Contains(12), Is.True);
            Assert.That(autoplay.Contains(10), Is.True);
        }

        [Test]
        public void ReleaseTransientResourcesForTests_StopsPlayersAndClearsCacheAndItems()
        {
            CharacterFolder folder = BuildCharacterFolderWithEmotes();
            CharacterEmoteVisualizerWindow window = new CharacterEmoteVisualizerWindow(folder);

            CreateSolidPng(Path.Combine(folder.DirectoryPath, "temp_preview.png"));
            ImageSource cached = Ao2AnimationPreview.LoadStaticPreviewImage(
                Path.Combine(folder.DirectoryPath, "temp_preview.png"),
                decodePixelWidth: 64);
            Assert.That(cached, Is.Not.Null);
            Assert.That(Ao2AnimationPreview.GetStaticPreviewCacheEntryCountForTests(), Is.GreaterThan(0));

            TestAnimationPlayer prePlayer = new TestAnimationPlayer();
            TestAnimationPlayer animPlayer = new TestAnimationPlayer();
            EmoteVisualizerItem item = new EmoteVisualizerItem
            {
                Id = 1,
                HasPreAnimation = true,
                IconImage = cached,
                PreAnimationImage = cached,
                AnimationImage = cached,
                PreAnimationPlayer = prePlayer,
                AnimationPlayer = animPlayer
            };

            window.SetEmoteItemsForTests(new[] { item });
            window.ReleaseTransientResourcesForTests(clearItems: false);

            Assert.That(prePlayer.StopCallCount, Is.EqualTo(1));
            Assert.That(animPlayer.StopCallCount, Is.EqualTo(1));
            Assert.That(item.PreAnimationPlayer, Is.Null);
            Assert.That(item.AnimationPlayer, Is.Null);
            Assert.That(Ao2AnimationPreview.GetStaticPreviewCacheEntryCountForTests(), Is.EqualTo(0));

            window.ReleaseTransientResourcesForTests(clearItems: true);
            Assert.That(window.EmoteItems.Count, Is.EqualTo(0));
            window.Close();
        }

        [Test]
        public void ReleaseTransientResourcesForTests_AllowsLargePreviewBitmapsToBeCollected()
        {
            CharacterFolder folder = BuildCharacterFolderWithEmotes();
            CharacterEmoteVisualizerWindow window = new CharacterEmoteVisualizerWindow(folder);
            List<EmoteVisualizerItem> items = new List<EmoteVisualizerItem>();
            List<WeakReference> imageRefs = new List<WeakReference>();
            for (int i = 0; i < 10; i++)
            {
                ImageSource icon = CreateLargeBitmap(1600, 900);
                ImageSource pre = CreateLargeBitmap(1600, 900);
                ImageSource anim = CreateLargeBitmap(1600, 900);
                imageRefs.Add(new WeakReference(icon));
                imageRefs.Add(new WeakReference(pre));
                imageRefs.Add(new WeakReference(anim));
                items.Add(new EmoteVisualizerItem
                {
                    Id = i + 1,
                    HasPreAnimation = true,
                    IconImage = icon,
                    PreAnimationImage = pre,
                    AnimationImage = anim
                });
            }

            window.SetEmoteItemsForTests(items);
            window.ReleaseTransientResourcesForTests(clearItems: true);
            items = new List<EmoteVisualizerItem>();
            ForceFullCollection();

            Assert.That(window.EmoteItems.Count, Is.EqualTo(0));
            int aliveCount = imageRefs.Count(reference => reference.IsAlive);
            Assert.That(aliveCount, Is.LessThan(imageRefs.Count / 3));
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

        private static void CreateSolidPng(string path)
        {
            WriteableBitmap bitmap = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[4 * 4 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 16;
                pixels[i + 1] = 32;
                pixels[i + 2] = 48;
                pixels[i + 3] = 255;
            }

            bitmap.WritePixels(new Int32Rect(0, 0, 4, 4), pixels, 4 * 4, 0);
            using FileStream stream = File.Create(path);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

        private static ImageSource CreateLargeBitmap(int width, int height)
        {
            WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 5;
                pixels[i + 1] = 20;
                pixels[i + 2] = 80;
                pixels[i + 3] = 255;
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            bitmap.Freeze();
            return bitmap;
        }

        private static void ForceFullCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private sealed class TestAnimationPlayer : IAnimationPlayer
        {
            public event Action<ImageSource>? FrameChanged;

            public int StopCallCount { get; private set; }

            public ImageSource CurrentFrame => new DrawingImage();

            public void SetLoop(bool shouldLoop)
            {
                _ = shouldLoop;
            }

            public void Restart()
            {
            }

            public void Stop()
            {
                StopCallCount++;
                FrameChanged = null;
            }
        }
    }
}
