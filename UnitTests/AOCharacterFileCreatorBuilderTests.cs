using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.CharacterCreator;
using OceanyaClient.Features.Startup;

namespace UnitTests
{
    [TestFixture]
    public class AOCharacterFileCreatorBuilderTests
    {
        private string tempRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "ao_char_creator_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
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
        public void BuildCharIni_WritesCoreSections_OptionsN_AndFrameSections()
        {
            CharacterCreationProject project = new CharacterCreationProject
            {
                CharacterFolderName = "Apollo",
                ShowName = "Apollo",
                Side = "def",
                Gender = "male",
                Blips = "male",
                Emotes =
                {
                    new CharacterCreationEmote
                    {
                        Name = "Pointing",
                        PreAnimation = "pointing",
                        Animation = "apollopointing",
                        EmoteModifier = 1,
                        DeskModifier = 1,
                        SfxName = "sfx-objection",
                        SfxDelayMs = 8,
                        SfxLooping = true,
                        PreAnimationDurationMs = 300,
                        StayTimeMs = 120,
                        BlipsOverride = "ddmale",
                        FrameEvents =
                        {
                            new CharacterCreationFrameEvent
                            {
                                Target = CharacterFrameTarget.PreAnimation,
                                EventType = CharacterFrameEventType.Sfx,
                                Frame = 1,
                                Value = "sfx-objection"
                            },
                            new CharacterCreationFrameEvent
                            {
                                Target = CharacterFrameTarget.AnimationA,
                                EventType = CharacterFrameEventType.Realization,
                                Frame = 4,
                                Value = "1"
                            }
                        }
                    }
                }
            };

            string ini = AOCharacterFileCreatorBuilder.BuildCharIni(project);

            Assert.That(ini, Does.Contain("[Options]"));
            Assert.That(ini, Does.Contain("[Emotions]"));
            Assert.That(ini, Does.Contain("[SoundN]"));
            Assert.That(ini, Does.Contain("[SoundT]"));
            Assert.That(ini, Does.Contain("[SoundL]"));
            Assert.That(ini, Does.Contain("[Time]"));
            Assert.That(ini, Does.Contain("[stay_time]"));
            Assert.That(ini, Does.Contain("[OptionsN]"));
            Assert.That(ini, Does.Contain("[Options1]"));
            Assert.That(ini, Does.Contain("[pointing_FrameSFX]"));
            Assert.That(ini, Does.Contain("[(a)/apollopointing_FrameRealization]"));
        }

        [Test]
        public void CreateCharacterFolder_CreatesIniReadmeAndAssetFolders()
        {
            CharacterCreationProject project = new CharacterCreationProject
            {
                MountPath = tempRoot,
                CharacterFolderName = "NewApollo",
                ShowName = "Apollo",
                Side = "def",
                Gender = "male",
                AssetFolders = { "anim", "sfx/custom", "portraits/hd" },
                Emotes =
                {
                    new CharacterCreationEmote
                    {
                        Name = "Normal",
                        PreAnimation = "-",
                        Animation = "normal",
                        EmoteModifier = 0,
                        DeskModifier = 1,
                        SfxName = "1",
                        SfxDelayMs = 1
                    }
                }
            };

            string folder = AOCharacterFileCreatorBuilder.CreateCharacterFolder(project);

            Assert.That(Directory.Exists(folder), Is.True);
            Assert.That(File.Exists(Path.Combine(folder, "char.ini")), Is.True);
            Assert.That(File.Exists(Path.Combine(folder, "readme.txt")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(folder, "Emotions")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(folder, "anim")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(folder, "sfx", "custom")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(folder, "portraits", "hd")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(folder, "readme.txt")), Does.Contain("AO Character File Creator"));
        }

        [Test]
        public void GeneratedAssetPathCollisionResolver_RenamesDifferentSourcesThatShareTheSameFileName()
        {
            List<GeneratedAssetPathCollisionCandidate> candidates = new List<GeneratedAssetPathCollisionCandidate>
            {
                new GeneratedAssetPathCollisionCandidate
                {
                    AssetKey = "emote:1:anim",
                    DefaultRelativePath = "Images/attack.png",
                    PreferredRelativePath = "Images/attack.png",
                    SourceIdentity = @"D:\chars\a\attack.png"
                },
                new GeneratedAssetPathCollisionCandidate
                {
                    AssetKey = "emote:2:anim",
                    DefaultRelativePath = "Images/attack.png",
                    PreferredRelativePath = "Images/attack.png",
                    SourceIdentity = @"D:\chars\b\attack.png"
                }
            };

            Dictionary<string, string> resolved = GeneratedAssetPathCollisionResolver.Resolve(candidates);

            Assert.Multiple(() =>
            {
                Assert.That(resolved["emote:1:anim"], Is.EqualTo("Images/attack.png"));
                Assert.That(resolved["emote:2:anim"], Is.EqualTo("Images/attack-1.png"));
            });
        }

        [Test]
        public void GeneratedAssetPathCollisionResolver_KeepsSamePathWhenDuplicateReferenceUsesTheSameSource()
        {
            List<GeneratedAssetPathCollisionCandidate> candidates = new List<GeneratedAssetPathCollisionCandidate>
            {
                new GeneratedAssetPathCollisionCandidate
                {
                    AssetKey = "emote:1:anim",
                    DefaultRelativePath = "Images/attack.png",
                    PreferredRelativePath = "Images/attack.png",
                    SourceIdentity = @"D:\chars\a\attack.png"
                },
                new GeneratedAssetPathCollisionCandidate
                {
                    AssetKey = "emote:1:splitbase",
                    DefaultRelativePath = "Images/attack.png",
                    PreferredRelativePath = "Images/attack.png",
                    SourceIdentity = @"D:\chars\a\attack.png"
                }
            };

            Dictionary<string, string> resolved = GeneratedAssetPathCollisionResolver.Resolve(candidates);

            Assert.Multiple(() =>
            {
                Assert.That(resolved["emote:1:anim"], Is.EqualTo("Images/attack.png"));
                Assert.That(resolved["emote:1:splitbase"], Is.EqualTo("Images/attack.png"));
            });
        }

        /// <summary>
        /// R-012 — Two emotes whose source files share the same filename but come from
        /// different directories must be assigned distinct output paths. Without
        /// disambiguation, one file silently overwrites the other in the generated folder.
        /// </summary>
        [Test]
        public void GenerateFiles_DuplicateAssetFilenames_AreSuffixDisambiguated()
        {
            List<GeneratedAssetPathCollisionCandidate> candidates = new List<GeneratedAssetPathCollisionCandidate>
            {
                new GeneratedAssetPathCollisionCandidate
                {
                    AssetKey = "emote:1:anim",
                    DefaultRelativePath = "images/attack.png",
                    PreferredRelativePath = "images/attack.png",
                    SourceIdentity = @"D:\project\charA\attack.png"
                },
                new GeneratedAssetPathCollisionCandidate
                {
                    AssetKey = "emote:2:anim",
                    DefaultRelativePath = "images/attack.png",
                    PreferredRelativePath = "images/attack.png",
                    SourceIdentity = @"D:\project\charB\attack.png"
                }
            };

            Dictionary<string, string> resolved = GeneratedAssetPathCollisionResolver.Resolve(candidates);

            string path1 = resolved["emote:1:anim"];
            string path2 = resolved["emote:2:anim"];

            Assert.That(path1, Is.Not.EqualTo(path2),
                "Two emotes from different source files with the same filename must resolve to distinct output paths");
            Assert.That(
                resolved.Values.All(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)),
                Is.True,
                "All resolved paths must retain their file extension");
        }
    }

    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class AOCharacterFileCreatorStartupTests
    {
        [SetUp]
        public void SetUp()
        {
            if (Application.Current == null)
            {
                _ = new Application();
            }
        }

        [Test]
        public void StartupCatalog_AndLauncher_ExposeCharacterFileCreator()
        {
            StartupFunctionalityOption option =
                StartupFunctionalityCatalog.GetByIdOrDefault(StartupFunctionalityIds.CharacterFileCreator);

            Assert.That(option.Id, Is.EqualTo(StartupFunctionalityIds.CharacterFileCreator));
            Assert.That(option.RequiresServerEndpoint, Is.False);

            Window window = StartupWindowLauncher.CreateStartupWindow(StartupFunctionalityIds.CharacterFileCreator);
            Assert.That(window, Is.TypeOf<GenericOceanyaWindow>());
            Assert.That(((GenericOceanyaWindow)window).BodyContent, Is.TypeOf<AOCharacterFileCreatorWindow>());
            window.Close();

            Assert.That(
                StartupFunctionalityCatalog.Options.Any(o =>
                    string.Equals(o.Id, StartupFunctionalityIds.CharacterFileCreator, StringComparison.OrdinalIgnoreCase)),
                Is.True);
        }

        [Test]
        public void StartupCatalog_AndLauncher_ExposeOceanyanFileHivemind()
        {
            StartupFunctionalityOption option =
                StartupFunctionalityCatalog.GetByIdOrDefault(StartupFunctionalityIds.OceanyanFileHivemind);

            Assert.That(option.Id, Is.EqualTo(StartupFunctionalityIds.OceanyanFileHivemind));
            Assert.That(option.RequiresServerEndpoint, Is.False);
            Assert.That(option.DisplayName, Is.EqualTo("The Oceanyan File Hivemind"));

            Window window = StartupWindowLauncher.CreateStartupWindow(StartupFunctionalityIds.OceanyanFileHivemind);
            Assert.That(window, Is.TypeOf<GenericOceanyaWindow>());
            Assert.That(((GenericOceanyaWindow)window).BodyContent, Is.TypeOf<OceanyanFileHivemindWindow>());
            window.Close();

            Assert.That(
                StartupFunctionalityCatalog.Options.Any(o =>
                    string.Equals(o.Id, StartupFunctionalityIds.OceanyanFileHivemind, StringComparison.OrdinalIgnoreCase)),
                Is.True);
        }
    }

    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class AOCharacterFileCreatorWindowTests
    {
        private string tempRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "ao_char_creator_window_tests_" + Guid.NewGuid().ToString("N"));
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
        public void SetActiveSection_FileOrganization_RebuildsGeneratedButtonEntries()
        {
            string buttonPath = CreateSolidPng(Path.Combine(tempRoot, "button.png"), Colors.Orange);
            AOCharacterFileCreatorWindow window = new AOCharacterFileCreatorWindow();

            CharacterCreationEmoteViewModel emote = new CharacterCreationEmoteViewModel
            {
                Index = 1,
                Name = "Normal",
                ButtonIconMode = ButtonIconMode.SingleImage,
                ButtonSingleImageAssetSourcePath = buttonPath
            };

            FieldInfo? emotesField = typeof(AOCharacterFileCreatorWindow).GetField(
                "emotes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(emotesField, Is.Not.Null);
            var emotes = emotesField!.GetValue(window) as System.Collections.ObjectModel.ObservableCollection<CharacterCreationEmoteViewModel>;
            Assert.That(emotes, Is.Not.Null);
            emotes!.Add(emote);

            MethodInfo? setActiveSectionMethod = typeof(AOCharacterFileCreatorWindow).GetMethod(
                "SetActiveSection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setActiveSectionMethod, Is.Not.Null);

            setActiveSectionMethod!.Invoke(window, new object[] { "fileorganization" });

            FieldInfo? allEntriesField = typeof(AOCharacterFileCreatorWindow).GetField(
                "allFileOrganizationEntries",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(allEntriesField, Is.Not.Null);
            var entries = allEntriesField!.GetValue(window) as System.Collections.Generic.List<FileOrganizationEntryViewModel>;
            Assert.That(entries, Is.Not.Null);
            System.Collections.Generic.List<FileOrganizationEntryViewModel> safeEntries = entries!;
            Assert.That(
                safeEntries.Any(static entry => string.Equals(entry.RelativePath, "emotions/button1_on.png", StringComparison.OrdinalIgnoreCase)),
                Is.True);
            Assert.That(
                safeEntries.Any(static entry => string.Equals(entry.RelativePath, "emotions/button1_off.png", StringComparison.OrdinalIgnoreCase)),
                Is.True);

            window.Close();
        }

        [Test]
        public void RefreshButtonIconPreview_UsesAvailableTwoImageSideWithoutRequiringBothAssets()
        {
            string buttonOnPath = CreateSolidPng(Path.Combine(tempRoot, "button_on.png"), Colors.DeepSkyBlue);
            CharacterCreationEmoteViewModel emote = new CharacterCreationEmoteViewModel
            {
                Index = 1,
                Name = "Normal",
                IsSelected = true,
                ButtonIconMode = ButtonIconMode.TwoImages,
                ButtonTwoImagesOnAssetSourcePath = buttonOnPath
            };

            MethodInfo? refreshButtonPreviewMethod = typeof(AOCharacterFileCreatorWindow).GetMethod(
                "RefreshButtonIconPreview",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(refreshButtonPreviewMethod, Is.Not.Null);

            refreshButtonPreviewMethod!.Invoke(null, new object[] { emote });

            Assert.That(emote.ButtonIconPreview, Is.Not.Null);

            emote.IsSelected = false;
            refreshButtonPreviewMethod.Invoke(null, new object[] { emote });

            Assert.That(emote.ButtonIconPreview, Is.Null);
            Assert.That(emote.HasButtonIconValue, Is.True);
        }

        [Test]
        public void TryBuildButtonIconPair_TwoImagesMode_PreservesUploadedBrightness()
        {
            string buttonOnPath = CreateSolidPng(Path.Combine(tempRoot, "button_on_bright.png"), Color.FromRgb(240, 240, 240));
            string buttonOffPath = CreateSolidPng(Path.Combine(tempRoot, "button_off_dark.png"), Color.FromRgb(180, 180, 180));

            ButtonIconGenerationConfig config = new ButtonIconGenerationConfig
            {
                Mode = ButtonIconMode.TwoImages,
                TwoImagesOnPath = buttonOnPath,
                TwoImagesOffPath = buttonOffPath,
                OnEffect = new ButtonEffectConfig
                {
                    Mode = ButtonEffectsGenerationMode.Darken,
                    DarknessPercent = 80
                },
                OffEffect = new ButtonEffectConfig
                {
                    Mode = ButtonEffectsGenerationMode.Darken,
                    DarknessPercent = 10
                }
            };

            MethodInfo? buildPairMethod = typeof(AOCharacterFileCreatorWindow).GetMethod(
                "TryBuildButtonIconPair",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(buildPairMethod, Is.Not.Null);

            object?[] args = { config, null, null, null };
            object? successResult = buildPairMethod!.Invoke(null, args);
            Assert.That(successResult, Is.EqualTo(true));

            BitmapSource? onImage = args[1] as BitmapSource;
            BitmapSource? offImage = args[2] as BitmapSource;
            Assert.That(onImage, Is.Not.Null);
            Assert.That(offImage, Is.Not.Null);

            Color onCenter = SampleCenterColor(onImage!);
            Color offCenter = SampleCenterColor(offImage!);

            Assert.That(onCenter.R, Is.GreaterThan(offCenter.R));
            Assert.That(onCenter.G, Is.GreaterThan(offCenter.G));
            Assert.That(onCenter.B, Is.GreaterThan(offCenter.B));
        }

        /// <summary>
        /// R-023 — Button icon generation must always produce square output regardless of
        /// the source image's aspect ratio. AO2 button icons must be square; rectangular
        /// output breaks the emote grid display. This regressed when agent changes dropped
        /// the square-canvas constraint.
        /// </summary>
        [Test]
        public void ButtonIconGeneration_NonSquareInput_ProducesSquareOutput()
        {
            // 200 × 40 is strongly non-square; the generator must fit it into a square canvas.
            string nonSquarePath = CreateSolidPng(Path.Combine(tempRoot, "wide.png"), Colors.SteelBlue, width: 200, height: 40);

            ButtonIconGenerationConfig config = new ButtonIconGenerationConfig
            {
                Mode = ButtonIconMode.SingleImage,
                SingleImagePath = nonSquarePath
            };

            MethodInfo? buildPairMethod = typeof(AOCharacterFileCreatorWindow).GetMethod(
                "TryBuildButtonIconPair",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(buildPairMethod, Is.Not.Null, "TryBuildButtonIconPair must exist on AOCharacterFileCreatorWindow");

            object?[] args = { config, null, null, null };
            object? result = buildPairMethod!.Invoke(null, args);
            Assert.That(result, Is.EqualTo(true), "Button icon generation must succeed with a valid source image");

            BitmapSource? onImage = args[1] as BitmapSource;
            Assert.That(onImage, Is.Not.Null, "button_on image must be produced");
            Assert.That(onImage!.PixelWidth, Is.EqualTo(onImage.PixelHeight),
                "Button icon output must be square (PixelWidth == PixelHeight) regardless of input aspect ratio");
        }

        private static string CreateSolidPng(string path, Color color)
        {
            return CreateSolidPng(path, color, width: 16, height: 16);
        }

        private static string CreateSolidPng(string path, Color color, int width, int height)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = color.A;
            }

            BitmapSource bitmap = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32,
                null,
                pixels,
                width * 4);

            using FileStream stream = File.Create(path);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            return path;
        }

        private static Color SampleCenterColor(BitmapSource bitmap)
        {
            int x = Math.Max(0, bitmap.PixelWidth / 2);
            int y = Math.Max(0, bitmap.PixelHeight / 2);
            byte[] pixels = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
            return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
        }
    }
}
