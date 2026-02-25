using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
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
            Assert.That(window, Is.TypeOf<AOCharacterFileCreatorWindow>());
            window.Close();

            Assert.That(
                StartupFunctionalityCatalog.Options.Any(o =>
                    string.Equals(o.Id, StartupFunctionalityIds.CharacterFileCreator, StringComparison.OrdinalIgnoreCase)),
                Is.True);
        }
    }
}
