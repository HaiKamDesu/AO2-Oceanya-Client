using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using OceanyaClient.Features.ChatPreview;
using OceanyaClient.Features.Viewport;

namespace UnitTests
{
    [TestFixture]
    public class EmoteTests
    {
        [Test]
        public void Test_Emote_Constructor()
        {
            // Create emote with ID
            var emote = new Emote(42);
            
            // Verify default values
            Assert.That(emote.ID, Is.EqualTo(42), "ID should be set from constructor");
            Assert.That(emote.Name, Is.EqualTo("normal"), "Default name should be 'normal'");
            Assert.That(emote.Animation, Is.EqualTo("normal"), "Default animation should be 'normal'");
            Assert.That(emote.PreAnimation, Is.EqualTo(""), "Default pre-animation should be empty");
            Assert.That(emote.sfxName, Is.EqualTo("1"), "Default SFX name should be '1'");
            Assert.That(emote.sfxDelay, Is.EqualTo(1), "Default SFX delay should be 1");
        }
        
        [Test]
        public void Test_Emote_DisplayID()
        {
            // Create emote
            var emote = new Emote(5)
            {
                Name = "thinking"
            };
            
            // Test DisplayID format
            Assert.That(emote.DisplayID, Is.EqualTo("5: thinking"), "DisplayID should be formatted as 'ID: Name'");
        }
        
        [Test]
        public void Test_Emote_ParseEmoteLine()
        {
            // Test parsing with all fields
            var data = "surprised#pre_surprised#surprised#1#0";
            var emote = Emote.ParseEmoteLine(data);
            
            Assert.Multiple(() =>
            {
                Assert.That(emote.Name, Is.EqualTo("surprised"), "Name should be parsed correctly");
                Assert.That(emote.PreAnimation, Is.EqualTo("pre_surprised"), "PreAnimation should be parsed correctly");
                Assert.That(emote.Animation, Is.EqualTo("surprised"), "Animation should be parsed correctly");
                Assert.That(emote.Modifier, Is.EqualTo(ICMessage.EmoteModifiers.PlayPreanimation), "Modifier should be parsed correctly");
                Assert.That(emote.DeskMod, Is.EqualTo(ICMessage.DeskMods.Hidden), "DeskMod should be parsed correctly");
            });
            
            // Test parsing with partial fields
            var partialData = "happy#pre_happy";
            var partialEmote = Emote.ParseEmoteLine(partialData);
            
            Assert.Multiple(() =>
            {
                Assert.That(partialEmote.Name, Is.EqualTo("happy"), "Name should be parsed correctly");
                Assert.That(partialEmote.PreAnimation, Is.EqualTo("pre_happy"), "PreAnimation should be parsed correctly");
                Assert.That(partialEmote.Animation, Is.EqualTo(""), "Animation should default to empty when not provided");
                Assert.That(partialEmote.Modifier, Is.EqualTo(ICMessage.EmoteModifiers.NoPreanimation), "Modifier should default to AO2 idle/no-preanimation");
                Assert.That(partialEmote.DeskMod, Is.EqualTo(ICMessage.DeskMods.Unspecified), "DeskMod should default to AO2 unspecified");
            });
            
            // Test parsing with invalid numeric fields
            var invalidData = "invalid#pre#anim#invalid#invalid";
            var invalidEmote = Emote.ParseEmoteLine(invalidData);
            
            Assert.Multiple(() =>
            {
                Assert.That(invalidEmote.Name, Is.EqualTo("invalid"), "Name should be parsed correctly");
                Assert.That(invalidEmote.Modifier, Is.EqualTo(ICMessage.EmoteModifiers.NoPreanimation), "Invalid modifier should default to AO2 idle/no-preanimation");
                Assert.That(
                    invalidEmote.DeskMod,
                    Is.EqualTo(ICMessage.DeskMods.Hidden),
                    "Non-empty non-numeric deskMod should match AO2 QString::toInt hidden behavior");
            });

            var blankDeskData = "invalid#pre#anim#0#";
            var blankDeskEmote = Emote.ParseEmoteLine(blankDeskData);
            Assert.That(
                blankDeskEmote.DeskMod,
                Is.EqualTo(ICMessage.DeskMods.Unspecified),
                "Blank deskMod should remain AO2 unspecified");
        }
    }
    
    [TestFixture]
    public class BackgroundTests
    {
        // We'll need to mock some file system behavior for testing
        private string _tempDir = string.Empty;
        private List<string> _originalBaseFolders = new List<string>();
        private string _originalPathToConfigIni = string.Empty;
        
        [SetUp]
        public void SetUp()
        {
            // Store original base folders
            _originalBaseFolders = new List<string>(Globals.BaseFolders);
            _originalPathToConfigIni = Globals.PathToConfigINI;
            
            // Create a temporary directory for testing
            _tempDir = Path.Combine(Path.GetTempPath(), $"bg_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
            
            // Override base folders to use our test directory
            Globals.BaseFolders = new List<string> { _tempDir };
            Globals.PathToConfigINI = Path.Combine(_tempDir, "config.ini");
            File.WriteAllText(Globals.PathToConfigINI, "theme=(714x688) FullChar\nsubtheme=default\n");
            
            // Create a background directory structure
            var bgDir = Path.Combine(_tempDir, "background");
            Directory.CreateDirectory(bgDir);
            
            // Create a test background
            var testBgDir = Path.Combine(bgDir, "testbg");
            Directory.CreateDirectory(testBgDir);
            
            // Create some test background images
            CreateEmptyFile(Path.Combine(testBgDir, "defenseempty.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "helperstand.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "jurystand.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "witnessempty.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "prosecutorempty.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "background.png"));
            
            // Create a desk image (should be excluded)
            CreateEmptyFile(Path.Combine(testBgDir, "defensedesk.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "helperdesk.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "jurydesk.png"));
            Background.RefreshCache();
        }
        
        [TearDown]
        public void TearDown()
        {
            // Restore original base folders
            Globals.BaseFolders = _originalBaseFolders;
            Globals.PathToConfigINI = _originalPathToConfigIni;
            
            // Clean up temporary directory
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        
        private void CreateEmptyFile(string filePath)
        {
            // Create an empty file at the specified path
            File.WriteAllBytes(filePath, new byte[0]);
        }

        private CharacterFolder CreateViewportTestCharacter()
        {
            string characterDirectory = Path.Combine(_tempDir, "characters", "ViewportPhoenix");
            Directory.CreateDirectory(characterDirectory);
            CreateEmptyFile(Path.Combine(characterDirectory, "(a)normal.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "(b)normal.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "(c)normal.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "(a)packet_anim.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "(b)packet_anim.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "pre_packet.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "pre_configured.png"));

            string iniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                iniPath,
                "[Options]\n" +
                "showname=Phoenix Wright\n" +
                "realization=phoenix-realization\n" +
                "gender=unknown\n" +
                "side=def\n" +
                "[Time]\n" +
                "preanim=0\n" +
                "pre_packet=1200\n" +
                "[stay_time]\n" +
                "pre_packet=250\n" +
                "[Emotions]\n" +
                "number=1\n" +
                "1=normal#pre_configured#normal#1#1\n" +
                "[(b)normal_FrameSFX]\n" +
                "2=phoenix-talk-hit\n" +
                "[(b)normal_FrameScreenshake]\n" +
                "3=1\n" +
                "[pre_configured_FrameSFX]\n" +
                "1=phoenix-pre-hit\n");

            return CharacterFolder.Create(iniPath);
        }
        
        [Test]
        public void Test_Background_FromBGPath()
        {
            // Load a background from the test path
            var background = Background.FromBGPath("testbg");
            
            // Verify background was loaded
            Assert.That(background, Is.Not.Null, "Background should be loaded from valid path");
            Assert.That(background, Is.Not.Null);
            Assert.That(background!.Name, Is.EqualTo("testbg"), "Background name should match the folder name");
            Assert.That(background!.bgImages, Has.Count.EqualTo(6), "Background should include only non-desk images");
            
            // Verify desk image was excluded
            Assert.That(background!.bgImages, Has.None.Contains("defensedesk"),
                "Background images should not include desk images");
        }
        
        [Test]
        public void Test_Background_GetBGImage()
        {
            // Load a background from the test path
            var background = Background.FromBGPath("testbg");
            Assert.That(background, Is.Not.Null);
            
            // Get an image for a position
            var defImage = background!.GetBGImage("def");
            
            // Verify position mapping worked
            Assert.That(defImage, Does.Contain("defenseempty"),
                "Position 'def' should map to 'defenseempty' image");
            
            // Test with a position that doesn't have a mapping
            var customImage = background.GetBGImage("custom");
            
            // Should return null since no mapping exists
            Assert.That(customImage, Is.Null, "Unknown position should return null");
        }
        
        [Test]
        public void Test_Background_GetPossiblePositions()
        {
            // Load a background from the test path
            var background = Background.FromBGPath("testbg");
            Assert.That(background, Is.Not.Null);
            
            // Get possible positions
            var positions = background!.GetPossiblePositions();
            
            // Verify common positions are available
            Assert.That(positions.ContainsKey("def"), Is.True, "Defense position should be available");
            Assert.That(positions.ContainsKey("wit"), Is.True, "Witness position should be available");
            Assert.That(positions.ContainsKey("pro"), Is.True, "Prosecutor position should be available");
            
            // Verify position mappings
            Assert.That(positions["def"], Does.Contain("defenseempty"), "Defense position should map to defenseempty image");
            Assert.That(positions["wit"], Does.Contain("witnessempty"), "Witness position should map to witnessempty image");
            Assert.That(positions["pro"], Does.Contain("prosecutorempty"), "Prosecutor position should map to prosecutorempty image");
        }
        
        [Test]
        public void Test_Background_NonExistentPath()
        {
            // Try to load a background from a non-existent path
            var background = Background.FromBGPath("nonexistent");
            
            // Should return null
            Assert.That(background, Is.Null, "Non-existent background path should return null");
        }

        [Test]
        public void Test_Background_FromBGPath_ResolvesDotSegmentPath()
        {
            string customDir = Path.Combine(_tempDir, "background", "custom");
            Directory.CreateDirectory(customDir);

            var background = Background.FromBGPath("custom/../testbg");

            Assert.That(background, Is.Not.Null);
            Assert.That(background!.Name, Is.EqualTo("testbg"));
            Assert.That(background.bgImages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Test_Background_UsesFirstMountedBackgroundWhenNamesCollide()
        {
            string secondRoot = Path.Combine(Path.GetTempPath(), $"bg_test_mount_{Guid.NewGuid()}");
            try
            {
                string firstBgDir = Path.Combine(_tempDir, "background", "testing");
                string secondBgDir = Path.Combine(secondRoot, "background", "testing");
                Directory.CreateDirectory(firstBgDir);
                Directory.CreateDirectory(secondBgDir);
                CreateEmptyFile(Path.Combine(firstBgDir, "firstonly.png"));
                CreateEmptyFile(Path.Combine(secondBgDir, "secondonly.png"));

                Globals.BaseFolders = new List<string> { _tempDir, secondRoot };
                Background.RefreshCache();

                Background? background = Background.FromBGPath("testing");
                Assert.That(background, Is.Not.Null);
                Assert.That(
                    background!.PathToFile,
                    Is.EqualTo(firstBgDir).IgnoreCase,
                    "AO2 resolves same-named backgrounds from the first matching mount path.");
                Assert.That(background.bgImages.Any(path => path.EndsWith("firstonly.png", StringComparison.OrdinalIgnoreCase)), Is.True);
                Assert.That(background.bgImages.Any(path => path.EndsWith("secondonly.png", StringComparison.OrdinalIgnoreCase)), Is.False);

                bool upserted = Background.TryUpsertBackgroundInCache(secondBgDir, out Background? refreshed, out string error);

                Assert.That(upserted, Is.True, error);
                Assert.That(refreshed, Is.Not.Null);
                Assert.That(
                    refreshed!.PathToFile,
                    Is.EqualTo(firstBgDir).IgnoreCase,
                    "Targeted refresh must not let a lower-priority duplicate mount replace the AO2-selected one.");
                Assert.That(Background.FromBGPath("testing")!.PathToFile, Is.EqualTo(firstBgDir).IgnoreCase);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(secondRoot))
                    {
                        Directory.Delete(secondRoot, true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors.
                }
            }
        }

        [Test]
        public void Test_Background_GetPossiblePositions_UsesDesignIniPositionsWhenPresent()
        {
            string testBgDir = Path.Combine(_tempDir, "background", "testbg");
            File.WriteAllText(Path.Combine(testBgDir, "design.ini"), "positions=def,custom:123,hld");
            CreateEmptyFile(Path.Combine(testBgDir, "custom.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "hld.png"));
            Background.RefreshCache();

            var background = Background.FromBGPath("testbg");
            Assert.That(background, Is.Not.Null);

            var positions = background!.GetPossiblePositions();

            Assert.That(positions.Keys, Has.Count.EqualTo(3));
            Assert.That(positions.ContainsKey("def"), Is.True);
            Assert.That(positions.ContainsKey("custom:123"), Is.True);
            Assert.That(positions.ContainsKey("hld"), Is.True);
            Assert.That(positions.ContainsKey("wit"), Is.False);
        }

        [Test]
        public void Test_Background_GetAo2PositionOptions_IncludesDefaultsDesignAndUndeclaredImages()
        {
            string testBgDir = Path.Combine(_tempDir, "background", "testbg");
            File.WriteAllText(Path.Combine(testBgDir, "design.ini"), "positions=custom:123,hld");
            CreateEmptyFile(Path.Combine(testBgDir, "custom.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "cage.png"));
            Background.RefreshCache();

            var background = Background.FromBGPath("testbg");
            Assert.That(background, Is.Not.Null);

            var positions = background!.GetAo2PositionOptions().Select(position => position.Name).ToList();

            Assert.That(positions, Does.Contain("def"));
            Assert.That(positions, Does.Contain("wit"));
            Assert.That(positions, Does.Contain("pro"));
            Assert.That(positions, Does.Contain("custom:123"));
            Assert.That(positions, Does.Contain("cage"));
            Assert.That(positions.IndexOf("def"), Is.LessThan(positions.IndexOf("custom:123")));
            Assert.That(positions.IndexOf("custom:123"), Is.LessThan(positions.IndexOf("cage")));
        }

        [Test]
        public void Test_Background_GetAo2PositionOptions_IncludesCourtOriginDefaultPositions()
        {
            string courtBgDir = Path.Combine(_tempDir, "background", "courtbg");
            Directory.CreateDirectory(courtBgDir);
            CreateEmptyFile(Path.Combine(courtBgDir, "court.png"));
            File.WriteAllText(Path.Combine(courtBgDir, "design.ini"), "court:def/origin=128");
            Background.RefreshCache();

            var background = Background.FromBGPath("courtbg");
            Assert.That(background, Is.Not.Null);

            Background.PositionOption? defPosition = background!.GetAo2PositionOptions()
                .FirstOrDefault(position => position.Name == "def");

            Assert.That(defPosition, Is.Not.Null);
            Assert.That(defPosition!.ImagePath, Does.EndWith("court.png"));
        }

        [Test]
        public void Test_Background_GetPossiblePositions_FallsBackToImagesWhenDesignIniMissing()
        {
            string testBgDir = Path.Combine(_tempDir, "background", "testbg");
            string designPath = Path.Combine(testBgDir, "design.ini");
            if (File.Exists(designPath))
            {
                File.Delete(designPath);
            }
            Background.RefreshCache();

            var background = Background.FromBGPath("testbg");
            Assert.That(background, Is.Not.Null);

            var positions = background!.GetPossiblePositions();

            Assert.That(positions.ContainsKey("def"), Is.True);
            Assert.That(positions.ContainsKey("wit"), Is.True);
            Assert.That(positions.ContainsKey("pro"), Is.True);
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_ResolvesBackgroundAndDeskImages()
        {
            string testBgDir = Path.Combine(_tempDir, "background", "testbg");

            string? backgroundPath = AO2ViewportAssetResolver.ResolveBackgroundImage("testbg", "def");
            string? deskPath = AO2ViewportAssetResolver.ResolveDeskImage("testbg", "def");

            Assert.That(backgroundPath, Is.Not.Null);
            Assert.That(backgroundPath, Does.EndWith("defenseempty.png"));
            Assert.That(deskPath, Is.Not.Null);
            Assert.That(deskPath, Is.EqualTo(Path.Combine(testBgDir, "defensedesk.png")));
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_ResolvesDesignOverlayWithExtension()
        {
            string testBgDir = Path.Combine(_tempDir, "background", "testbg");
            CreateEmptyFile(Path.Combine(testBgDir, "snow2.apng"));
            File.WriteAllText(
                Path.Combine(testBgDir, "design.ini"),
                "positions=taiga_ws_day_1" + Environment.NewLine
                + Environment.NewLine
                + "[Overlays]" + Environment.NewLine
                + "taiga_ws_day_1=snow2.apng" + Environment.NewLine);
            CreateEmptyFile(Path.Combine(testBgDir, "taiga_ws_day_1.webp"));
            Background.RefreshCache();

            string? backgroundPath = AO2ViewportAssetResolver.ResolveBackgroundImage("testbg", "taiga_ws_day_1");
            string? overlayPath = AO2ViewportAssetResolver.ResolveDeskImage("testbg", "taiga_ws_day_1");

            Assert.That(backgroundPath, Is.EqualTo(Path.Combine(testBgDir, "taiga_ws_day_1.webp")));
            Assert.That(overlayPath, Is.EqualTo(Path.Combine(testBgDir, "snow2.apng")));
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_UsesAo2HelperAndJuryDeskNames()
        {
            string testBgDir = Path.Combine(_tempDir, "background", "testbg");

            Assert.That(
                AO2ViewportAssetResolver.ResolveDeskImage("testbg", "hld"),
                Is.EqualTo(Path.Combine(testBgDir, "helperdesk.png")));
            Assert.That(
                AO2ViewportAssetResolver.ResolveDeskImage("testbg", "jur"),
                Is.EqualTo(Path.Combine(testBgDir, "jurydesk.png")));
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_DeskVisibilityMatchesAo2PostPreanimState()
        {
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Hidden, "def"), Is.False);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Shown, "def"), Is.True);
            // AO2 legacy "chat": position-dependent — show for all except jud/hld/hlp
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Chat, "def"), Is.True);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Chat, "wit"), Is.True);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Chat, "null"), Is.True);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Chat, "jud"), Is.False);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Chat, "hld"), Is.False);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Chat, "hlp"), Is.False);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.ShownDuringPreanimHiddenAfter, "def"), Is.False);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.HiddenDuringPreanimShownAfter, "def"), Is.True);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.ShownDuringPreanimCenteredAfter, "def"), Is.False);
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_DeskVisibilityMatchesAo2PreanimState()
        {
            Assert.That(
                AO2ViewportAssetResolver.ShouldShowDeskDuringPreAnimation(
                    ICMessage.DeskMods.HiddenDuringPreanimShownAfter,
                    "def"),
                Is.False);
            Assert.That(
                AO2ViewportAssetResolver.ShouldShowDeskDuringPreAnimation(
                    ICMessage.DeskMods.ShownDuringPreanimHiddenAfter,
                    "def"),
                Is.True);
            // AO2 legacy "chat": position-dependent during preanim too
            Assert.That(
                AO2ViewportAssetResolver.ShouldShowDeskDuringPreAnimation(ICMessage.DeskMods.Chat, "def"),
                Is.True);
            Assert.That(
                AO2ViewportAssetResolver.ShouldShowDeskDuringPreAnimation(ICMessage.DeskMods.Chat, "jud"),
                Is.False);
            Assert.That(
                AO2ViewportAssetResolver.ShouldShowDeskDuringPreAnimation(
                    ICMessage.DeskMods.ShownDuringPreanimCenteredAfter,
                    "def"),
                Is.True);
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_UsesPacketPreanimTokenAndTiming()
        {
            CharacterFolder character = CreateViewportTestCharacter();

            string? preAnimationPath = AO2ViewportAssetResolver.ResolveCharacterPreAnimation(character, "pre_packet");
            string? emoteNamePath = AO2ViewportAssetResolver.ResolveCharacterPreAnimation(character, "normal");

            Assert.That(preAnimationPath, Is.EqualTo(Path.Combine(character.DirectoryPath, "pre_packet.png")));
            Assert.That(emoteNamePath, Is.Null);
            Assert.That(
                AO2ViewportAssetResolver.GetPreAnimationDuration(character, "pre_packet"),
                Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
            Assert.That(
                AO2ViewportAssetResolver.GetPreAnimationWaitDuration(character, "pre_packet"),
                Is.EqualTo(TimeSpan.FromMilliseconds(10000)));
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_DialogAnimationMatchesTalkingState()
        {
            CharacterFolder character = CreateViewportTestCharacter();

            string? talkingPath = AO2ViewportAssetResolver.ResolveCharacterDialogAnimation(character, "normal", talking: true);
            string? idlePath = AO2ViewportAssetResolver.ResolveCharacterDialogAnimation(character, "normal", talking: false);
            string? packetTalkingPath = AO2ViewportAssetResolver.ResolveCharacterDialogAnimation(
                character,
                "packet_anim",
                talking: true);

            Assert.That(talkingPath, Is.EqualTo(Path.Combine(character.DirectoryPath, "(b)normal.png")));
            Assert.That(idlePath, Is.EqualTo(Path.Combine(character.DirectoryPath, "(a)normal.png")));
            Assert.That(packetTalkingPath, Is.EqualTo(Path.Combine(character.DirectoryPath, "(b)packet_anim.png")));
            Assert.That(
                AO2ViewportAssetResolver.ResolveCharacterPostAnimation(character, "normal"),
                Is.EqualTo(Path.Combine(character.DirectoryPath, "(c)normal.png")));
            Assert.That(AO2ViewportAssetResolver.ResolveCharacterPostAnimation(character, "packet_anim"), Is.Null);
            Assert.That(AO2ViewportAssetResolver.IsTextColorTalking(ICMessage.TextColors.White), Is.True);
            Assert.That(AO2ViewportAssetResolver.IsTextColorTalking(ICMessage.TextColors.Blue), Is.False);
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_NormalizesAo2EmoteModifiers()
        {
            Assert.That(
                AO2ViewportAssetResolver.NormalizeEmoteModifier(ICMessage.EmoteModifiers.PlayPreanimationAndObjection),
                Is.EqualTo(AO2ViewportAssetResolver.NormalizedEmoteModifier.PreAnimation));
            Assert.That(
                AO2ViewportAssetResolver.NormalizeEmoteModifier(ICMessage.EmoteModifiers.Unused4),
                Is.EqualTo(AO2ViewportAssetResolver.NormalizedEmoteModifier.PreAnimationZoom));
            Assert.That(
                AO2ViewportAssetResolver.ShouldBlockForPreAnimation(ICMessage.EmoteModifiers.Unused4),
                Is.True);
            Assert.That(
                AO2ViewportAssetResolver.ShouldPlayImmediatePreAnimation(
                    ICMessage.EmoteModifiers.NoPreanimation,
                    immediate: true),
                Is.True);
            Assert.That(
                AO2ViewportAssetResolver.ShouldCenterAndHidePairDuringSpeaking(
                    ICMessage.DeskMods.ShownDuringPreanimCenteredAfter,
                    ICMessage.EmoteModifiers.NoPreanimation),
                Is.True);
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_UsesFullCharViewportThemeGeometry()
        {
            string defaultThemeDir = Path.Combine(_tempDir, "themes", "default");
            Directory.CreateDirectory(defaultThemeDir);
            File.WriteAllText(
                Path.Combine(defaultThemeDir, "courtroom_fonts.ini"),
                "showname=8\n" +
                "showname_font=Arial\n" +
                "message=10\n" +
                "message_font=Arial\n" +
                "message_color=0,0,0\n");

            string viewportThemeDir = Path.Combine(_tempDir, "themes", "(714x688) FullChar");
            Directory.CreateDirectory(viewportThemeDir);
            CreateEmptyFile(Path.Combine(viewportThemeDir, "chat.png"));
            CreateEmptyFile(Path.Combine(viewportThemeDir, "chatmed.png"));
            File.WriteAllText(
                Path.Combine(viewportThemeDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "showname=1,0,46,15\n" +
                "showname_extra_width=32\n" +
                "showname_align=center\n" +
                "message=10,12,242,89\n");

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve("default", hasShowname: true, preferViewportTheme: true);

            Assert.That(
                style.ChatboxImagePath,
                Is.EqualTo(Path.Combine(viewportThemeDir, "chat.png")).IgnoreCase);
            Assert.That(style.ChatboxBounds, Is.EqualTo(new AO2ChatPreviewBounds(0, 178, 256, 104)));
            Assert.That(style.ShownameBounds, Is.EqualTo(new AO2ChatPreviewBounds(1, 0, 46, 15)));
            Assert.That(style.MessageBounds, Is.EqualTo(new AO2ChatPreviewBounds(10, 12, 242, 89)));
            Assert.That(style.MessageFontFamily, Is.EqualTo("Arial"));
            Assert.That(style.MessageFontSize, Is.EqualTo(10 * 96d / 72d).Within(0.001d));
            Assert.That(style.ShownameTextAlignment, Is.EqualTo(TextAlignment.Center));
            Assert.That(style.ShownameExtraWidth, Is.EqualTo(32));
            Assert.That(
                AO2ChatPreviewResolver.ResolveSiblingImageVariant(style.ChatboxImagePath, "med"),
                Is.EqualTo(Path.Combine(viewportThemeDir, "chatmed.png")).IgnoreCase);
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_ViewportLayoutPlacesChatboxBelowByDefault()
        {
            string viewportThemeDir = Path.Combine(_tempDir, "themes", "(714x688) FullChar");
            Directory.CreateDirectory(viewportThemeDir);
            CreateEmptyFile(Path.Combine(viewportThemeDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(viewportThemeDir, "courtroom_design.ini"),
                "viewport=10,20,714,400\n" +
                "ao2_chatbox=10,420,714,178\n" +
                "showname=4,0,120,28\n" +
                "message=28,30,650,125\n" +
                "chat_arrow=680,140,24,20\n");

            AO2ViewportThemeLayout layout = AO2ChatPreviewResolver.ResolveViewportLayout(
                "default",
                hasShowname: true,
                chatboxOverlapsViewport: false);

            Assert.That(layout.ViewportBounds, Is.EqualTo(new AO2ChatPreviewBounds(10, 20, 714, 400)));
            Assert.That(layout.SurfaceWidth, Is.EqualTo(714));
            Assert.That(layout.SurfaceHeight, Is.EqualTo(578));
            Assert.That(layout.ChatboxLeft, Is.EqualTo(0));
            Assert.That(layout.ChatboxTop, Is.EqualTo(400));
            Assert.That(layout.ChatArrowBounds, Is.EqualTo(new AO2ChatPreviewBounds(680, 540, 24, 20)));
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_ViewportLayoutCanOverlapChatboxAtAo2Position()
        {
            string viewportThemeDir = Path.Combine(_tempDir, "themes", "(714x688) FullChar");
            Directory.CreateDirectory(viewportThemeDir);
            CreateEmptyFile(Path.Combine(viewportThemeDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(viewportThemeDir, "courtroom_design.ini"),
                "viewport=10,20,714,400\n" +
                "ao2_chatbox=10,320,714,178\n" +
                "showname=4,0,120,28\n" +
                "message=28,30,650,125\n" +
                "chat_arrow=680,140,24,20\n");

            AO2ViewportThemeLayout layout = AO2ChatPreviewResolver.ResolveViewportLayout(
                "default",
                hasShowname: true,
                chatboxOverlapsViewport: true);

            Assert.That(layout.SurfaceWidth, Is.EqualTo(714));
            Assert.That(layout.SurfaceHeight, Is.EqualTo(478));
            Assert.That(layout.ChatboxLeft, Is.EqualTo(0));
            Assert.That(layout.ChatboxTop, Is.EqualTo(300));
            Assert.That(layout.ChatArrowBounds, Is.EqualTo(new AO2ChatPreviewBounds(680, 440, 24, 20)));
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_CustomChatboxOverridesThemeDefaultArt()
        {
            string viewportThemeDir = Path.Combine(_tempDir, "themes", "(714x688) FullChar");
            Directory.CreateDirectory(viewportThemeDir);
            CreateEmptyFile(Path.Combine(viewportThemeDir, "chat.png"));

            string customChatboxDir = Path.Combine(_tempDir, "misc", "P5");
            Directory.CreateDirectory(customChatboxDir);
            CreateEmptyFile(Path.Combine(customChatboxDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(customChatboxDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "message=20,21,200,60\n");

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve("P5", hasShowname: true, preferViewportTheme: true);

            Assert.That(
                style.ChatboxImagePath,
                Is.EqualTo(Path.Combine(customChatboxDir, "chat.png")).IgnoreCase);
            Assert.That(style.MessageBounds, Is.EqualTo(new AO2ChatPreviewBounds(20, 21, 200, 60)));
            Assert.That(
                AO2ChatPreviewResolver.ResolveChatboxDirectoryPath("P5", preferViewportTheme: true),
                Is.EqualTo(customChatboxDir).IgnoreCase);
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_CustomChatboxWithoutGeometryKeepsThemeGeometry()
        {
            string viewportThemeDir = Path.Combine(_tempDir, "themes", "(714x688) FullChar");
            Directory.CreateDirectory(viewportThemeDir);
            CreateEmptyFile(Path.Combine(viewportThemeDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(viewportThemeDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "showname=1,0,46,15\n" +
                "message=10,12,242,89\n");

            string defaultChatboxDir = Path.Combine(_tempDir, "misc", "default");
            Directory.CreateDirectory(defaultChatboxDir);
            File.WriteAllText(
                Path.Combine(defaultChatboxDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "showname=5,6,70,16\n" +
                "message=30,31,180,40\n");

            string customChatboxDir = Path.Combine(_tempDir, "misc", "P5");
            Directory.CreateDirectory(customChatboxDir);
            CreateEmptyFile(Path.Combine(customChatboxDir, "chat.png"));

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve("P5", hasShowname: true, preferViewportTheme: true);

            Assert.That(
                style.ChatboxImagePath,
                Is.EqualTo(Path.Combine(customChatboxDir, "chat.png")).IgnoreCase);
            Assert.That(style.ShownameBounds, Is.EqualTo(new AO2ChatPreviewBounds(1, 0, 46, 15)));
            Assert.That(style.MessageBounds, Is.EqualTo(new AO2ChatPreviewBounds(10, 12, 242, 89)));
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_EmptyChatTokenUsesThemeArtNotMiscDefault()
        {
            string viewportThemeDir = Path.Combine(_tempDir, "themes", "(714x688) FullChar");
            Directory.CreateDirectory(viewportThemeDir);
            CreateEmptyFile(Path.Combine(viewportThemeDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(viewportThemeDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "showname=1,0,46,15\n" +
                "message=10,12,242,89\n");

            string defaultChatboxDir = Path.Combine(_tempDir, "misc", "default");
            Directory.CreateDirectory(defaultChatboxDir);
            CreateEmptyFile(Path.Combine(defaultChatboxDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(defaultChatboxDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "showname=5,6,70,16\n" +
                "message=30,31,180,40\n");
            CreateEmptyFile(Path.Combine(_tempDir, "misc", "chat.png"));

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve(string.Empty, hasShowname: true, preferViewportTheme: true);

            Assert.That(
                style.ChatboxImagePath,
                Is.EqualTo(Path.Combine(viewportThemeDir, "chat.png")).IgnoreCase);
            Assert.That(style.ShownameBounds, Is.EqualTo(new AO2ChatPreviewBounds(1, 0, 46, 15)));
            Assert.That(style.MessageBounds, Is.EqualTo(new AO2ChatPreviewBounds(10, 12, 242, 89)));
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_ChatDefaultTokenUsesThemeDefaultException()
        {
            string characterDirectory = Path.Combine(_tempDir, "characters", "LesbianGf");
            Directory.CreateDirectory(characterDirectory);
            string iniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                iniPath,
                "[Options]\n" +
                "name=LesbianGf\n" +
                "showname=Lesbian GF\n" +
                "chat=default\n" +
                "[Emotions]\n" +
                "number=1\n" +
                "1=normal#-#normal#0#1\n");

            CharacterFolder character = CharacterFolder.Create(iniPath);

            Assert.That(AO2ViewportAssetResolver.ResolveCharacterChatToken(character), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_ReadsCaseInsensitiveCustomConfigNames()
        {
            string viewportThemeDir = Path.Combine(_tempDir, "themes", "(714x688) FullChar");
            Directory.CreateDirectory(viewportThemeDir);
            CreateEmptyFile(Path.Combine(viewportThemeDir, "chat.png"));

            string customChatboxDir = Path.Combine(_tempDir, "misc", "P5");
            Directory.CreateDirectory(customChatboxDir);
            CreateEmptyFile(Path.Combine(customChatboxDir, "Chat.png"));
            File.WriteAllText(
                Path.Combine(customChatboxDir, "Courtroom_Fonts.ini"),
                "message=12\n" +
                "message_font=Times New Roman\n" +
                "showname=9\n");

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve("P5", hasShowname: true, preferViewportTheme: true);

            Assert.That(
                style.ChatboxImagePath,
                Is.EqualTo(Path.Combine(customChatboxDir, "Chat.png")).IgnoreCase);
            Assert.That(style.MessageFontFamily, Is.EqualTo("Times New Roman"));
            Assert.That(style.MessageFontSize, Is.EqualTo(12 * 96d / 72d).Within(0.001d));
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_UsesConfiguredThemeInsteadOfPreferredThemeGuess()
        {
            File.WriteAllText(Globals.PathToConfigINI, "theme=ActiveTheme\nsubtheme=default\n");

            string activeThemeDir = Path.Combine(_tempDir, "themes", "ActiveTheme");
            Directory.CreateDirectory(activeThemeDir);
            CreateEmptyFile(Path.Combine(activeThemeDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(activeThemeDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "showname=3,4,50,16\n" +
                "message=11,12,213,67\n");

            string nonActivePreferredThemeDir = Path.Combine(_tempDir, "themes", "(714x688) FullChar");
            Directory.CreateDirectory(nonActivePreferredThemeDir);
            CreateEmptyFile(Path.Combine(nonActivePreferredThemeDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(nonActivePreferredThemeDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "showname=30,40,50,16\n" +
                "message=110,120,113,47\n");

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve("default", hasShowname: true, preferViewportTheme: true);

            Assert.That(
                style.ChatboxImagePath,
                Is.EqualTo(Path.Combine(activeThemeDir, "chat.png")).IgnoreCase);
            Assert.That(style.ShownameBounds, Is.EqualTo(new AO2ChatPreviewBounds(3, 4, 50, 16)));
            Assert.That(style.MessageBounds, Is.EqualTo(new AO2ChatPreviewBounds(11, 12, 213, 67)));
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_ConfiguredSubthemeOverridesThemeDefault()
        {
            File.WriteAllText(Globals.PathToConfigINI, "theme=ActiveTheme\nsubtheme=Thin\n");

            string activeThemeDir = Path.Combine(_tempDir, "themes", "ActiveTheme");
            Directory.CreateDirectory(activeThemeDir);
            CreateEmptyFile(Path.Combine(activeThemeDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(activeThemeDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "message=10,12,242,89\n");

            string activeSubthemeDir = Path.Combine(activeThemeDir, "Thin");
            Directory.CreateDirectory(activeSubthemeDir);
            CreateEmptyFile(Path.Combine(activeSubthemeDir, "chat.png"));
            File.WriteAllText(
                Path.Combine(activeSubthemeDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "message=20,22,200,55\n");

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve("default", hasShowname: true, preferViewportTheme: true);

            Assert.That(
                style.ChatboxImagePath,
                Is.EqualTo(Path.Combine(activeSubthemeDir, "chat.png")).IgnoreCase);
            Assert.That(style.MessageBounds, Is.EqualTo(new AO2ChatPreviewBounds(20, 22, 200, 55)));
        }

        [Test]
        public void Test_AO2ChatPreviewResolver_ReadsChatColorMarkup()
        {
            string viewportThemeDir = Path.Combine(_tempDir, "themes", "(714x688) FullChar");
            Directory.CreateDirectory(viewportThemeDir);
            File.WriteAllText(
                Path.Combine(viewportThemeDir, "chat_config.ini"),
                "c0=10,20,30\n" +
                "c1=40,50,60\n" +
                "c1_start=[g]\n" +
                "c1_end=[/g]\n" +
                "c1_remove=1\n" +
                "c1_talking=0\n");

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve("default", hasShowname: true, preferViewportTheme: true);

            Assert.That(style.ChatColors[0], Is.EqualTo(System.Windows.Media.Color.FromRgb(10, 20, 30)));
            Assert.That(style.ChatColors[1], Is.EqualTo(System.Windows.Media.Color.FromRgb(40, 50, 60)));
            Assert.That(style.ChatMarkupStart[1], Is.EqualTo("[g]"));
            Assert.That(style.ChatMarkupEnd[1], Is.EqualTo("[/g]"));
            Assert.That(style.ChatMarkupRemove[1], Is.True);
            Assert.That(style.ChatMarkupTalking[1], Is.False);
        }

        [Test]
        public void Test_AO2ChatTextFormatter_AppliesViewportMarkupRules()
        {
            AO2ChatPreviewStyle style = new AO2ChatPreviewStyle();
            style.ChatColors[0] = Colors.White;
            style.ChatColors[1] = Colors.Lime;
            style.ChatColors[2] = Colors.Red;
            style.ChatMarkupStart[2] = "~";
            style.ChatMarkupEnd[2] = "~";
            style.ChatMarkupRemove[2] = true;

            List<AO2FormattedTextSegment> segments = AO2ChatTextFormatter.EnumerateFormattedTextSegments(
                style,
                "Tranquility ~Extasis~",
                0).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(string.Concat(segments.Select(segment => segment.Text)), Is.EqualTo("Tranquility Extasis"));
                Assert.That(
                    string.Concat(segments.Where(segment => segment.Color == Colors.Red).Select(segment => segment.Text)),
                    Is.EqualTo("Extasis"));
            });
        }

        [Test]
        public void Test_AO2ChatTextFormatter_StripsAo2VisibilityTokens()
        {
            AO2ChatPreviewStyle style = new AO2ChatPreviewStyle();
            style.ChatColors[0] = Colors.White;

            List<AO2FormattedTextSegment> segments = AO2ChatTextFormatter.EnumerateFormattedTextSegments(
                style,
                "Wait\\nnow\\s{fast}\\f\\p!",
                0).ToList();

            Assert.That(string.Concat(segments.Select(segment => segment.Text)), Is.EqualTo("Wait" + Environment.NewLine + "nowfast!"));
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_ResolvesEffectLayerMetadata()
        {
            string miscEffectsDir = Path.Combine(_tempDir, "misc", "customfx", "effects");
            Directory.CreateDirectory(miscEffectsDir);
            CreateEmptyFile(Path.Combine(miscEffectsDir, "spark.png"));
            File.WriteAllText(
                Path.Combine(_tempDir, "misc", "customfx", "effects.ini"),
                "[0]\nname=spark\nlayer=behind\nstretch=true\nrespect_flip=true\nrespect_offset=true\n");

            AO2ViewportAssetResolver.ViewportEffect effect =
                AO2ViewportAssetResolver.ResolveEffect("spark|customfx|sfx-spark", ICMessage.Effects.None, null, true);

            Assert.That(effect.ImagePath, Is.EqualTo(Path.Combine(miscEffectsDir, "spark.png")));
            Assert.That(effect.Layer, Is.EqualTo(AO2ViewportAssetResolver.EffectLayer.BehindCharacter));
            Assert.That(effect.Stretch, Is.True);
            Assert.That(effect.RespectFlip, Is.True);
            Assert.That(effect.RespectOffset, Is.True);
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_ResolvesEffectSoundToken()
        {
            CharacterFolder character = CreateViewportTestCharacter();

            Assert.Multiple(() =>
            {
                Assert.That(
                    AO2ViewportAssetResolver.ResolveEffectSoundToken("spark|customfx|sfx-spark", ICMessage.Effects.None, null),
                    Is.EqualTo("sfx-spark"));
                Assert.That(
                    AO2ViewportAssetResolver.ResolveEffectSoundToken(null, ICMessage.Effects.Realization, character),
                    Is.EqualTo("phoenix-realization"));
                Assert.That(
                    AO2ViewportAssetResolver.ResolveEffectSoundToken(null, ICMessage.Effects.Reaction, character),
                    Is.EqualTo("sfx-reactionding"));
            });
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_ParsesPairOrderingAndZoomNames()
        {
            Assert.That(
                AO2ViewportAssetResolver.GetPairOrdering("12^1"),
                Is.EqualTo(AO2ViewportAssetResolver.PairOrdering.PairInFront));
            Assert.That(
                AO2ViewportAssetResolver.GetPairOrdering("12^0"),
                Is.EqualTo(AO2ViewportAssetResolver.PairOrdering.PairBehind));
            Assert.That(AO2ViewportAssetResolver.ResolveSpeedlinesName("pro"), Is.EqualTo("prosecution_speedlines"));
            Assert.That(AO2ViewportAssetResolver.ResolveSpeedlinesName("hlp"), Is.EqualTo("prosecution_speedlines"));
            Assert.That(AO2ViewportAssetResolver.ResolveSpeedlinesName("def"), Is.EqualTo("defense_speedlines"));
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_ResolvesShoutOverlayDefaults()
        {
            string themeDirectory = Path.Combine(_tempDir, "themes", "default");
            Directory.CreateDirectory(themeDirectory);
            string themeShoutPath = Path.Combine(themeDirectory, "objection_bubble.gif");
            File.WriteAllBytes(themeShoutPath, new byte[] { 1, 2, 3, 4 });

            Assert.That(
                AO2ViewportAssetResolver.ResolveShoutOverlayImage(ICMessage.ShoutModifiers.Objection),
                Is.EqualTo(themeShoutPath));
            Assert.That(
                AO2ViewportAssetResolver.ResolveShoutOverlayImage(ICMessage.ShoutModifiers.Nothing),
                Is.Null);
            Assert.That(
                AO2ViewportAssetResolver.GetShoutDuration(),
                Is.EqualTo(TimeSpan.FromMilliseconds(724)));
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_DoesNotUseBundledShoutDefaults()
        {
            Assert.That(
                AO2ViewportAssetResolver.ResolveShoutOverlayImage(ICMessage.ShoutModifiers.Objection),
                Is.Null);
        }

        [Test]
        public void Test_AO2ViewportAssetResolver_ResolvesCharacterShoutOverlayLikeAo2()
        {
            string characterDirectory = Path.Combine(_tempDir, "characters", "ViewportPhoenix");
            Directory.CreateDirectory(characterDirectory);
            string characterShoutPath = Path.Combine(characterDirectory, "objection_bubble.gif");
            File.WriteAllBytes(characterShoutPath, new byte[] { 1, 2, 3, 4 });

            string? resolved = AO2ViewportAssetResolver.ResolveShoutOverlayImage(
                ICMessage.ShoutModifiers.Objection,
                "ViewportPhoenix",
                "default");

            Assert.That(resolved, Is.EqualTo(characterShoutPath));
        }

        [Test]
        public void Test_AO2ViewportAudioResolver_ResolvesCharacterShoutLikeAo2()
        {
            string characterSoundsDirectory = Path.Combine(_tempDir, "characters", "ViewportPhoenix", "sounds");
            Directory.CreateDirectory(characterSoundsDirectory);
            string characterShoutPath = Path.Combine(characterSoundsDirectory, "objection.ogg");
            File.WriteAllBytes(characterShoutPath, new byte[] { 1, 2, 3, 4 });

            string? resolved = AO2ViewportAudioResolver.ResolveCharacterShoutPath(
                "objection",
                "ViewportPhoenix",
                "default");

            Assert.That(resolved, Is.EqualTo(characterShoutPath));
        }

        [Test]
        public void Test_ICMessage_FromConsoleLine_PreservesPairOrderAndVerticalOffset()
        {
            List<string> fields = Enumerable.Repeat(string.Empty, 32).ToList();
            fields[0] = "chat";
            fields[2] = "Phoenix";
            fields[3] = "normal";
            fields[4] = "Hello";
            fields[5] = "def";
            fields[8] = "1";
            fields[16] = "12^1";
            fields[17] = "Maya";
            fields[18] = "normal";
            fields[19] = "10&5";
            fields[20] = "-20&8";

            ICMessage? message = ICMessage.FromConsoleLine("MS#" + string.Join("#", fields) + "#%");

            Assert.That(message, Is.Not.Null);
            Assert.That(message!.OtherCharIdRaw, Is.EqualTo("12^1"));
            Assert.That(message.OtherCharId, Is.EqualTo(12));
            Assert.That(message.OtherOffset, Is.EqualTo(-20));
            Assert.That(message.OtherOffsetVertical, Is.EqualTo(8));
            Assert.That(message.SelfOffset, Is.EqualTo((10, 5)));
        }
    }

    [TestFixture]
    public class CharacterAssetPathResolverTests
    {
        private string tempRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "asset_resolver_tests_" + Guid.NewGuid().ToString("N"));
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
                // best-effort cleanup
            }
        }

        [Test]
        public void ResolveCharacterAnimationPath_LeadingSlashToken_ResolvesToIdlePrefixAsset()
        {
            string characterDirectory = Path.Combine(tempRoot, "Akechi");
            Directory.CreateDirectory(characterDirectory);

            string expectedPath = Path.Combine(characterDirectory, "(a)Normal.webp");
            File.WriteAllBytes(expectedPath, new byte[] { 1, 2, 3, 4 });

            string resolvedPath = CharacterAssetPathResolver.ResolveCharacterAnimationPath(
                characterDirectory,
                "/Normal",
                includePlaceholder: false);

            Assert.That(resolvedPath, Is.EqualTo(expectedPath));
        }
    }

    [TestFixture]
    public class AO2ViewportTimingTests
    {
        private string tempRoot = string.Empty;
        private string originalConfigIniPath = string.Empty;
        private List<string> originalBaseFolders = new List<string>();

        private static void CreateEmptyFile(string filePath)
        {
            File.WriteAllBytes(filePath, new byte[0]);
        }

        private CharacterFolder CreateViewportTestCharacter()
        {
            string characterDirectory = Path.Combine(tempRoot, "characters", "ViewportPhoenix");
            Directory.CreateDirectory(characterDirectory);
            CreateEmptyFile(Path.Combine(characterDirectory, "(a)normal.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "(b)normal.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "(a)packet_anim.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "(b)packet_anim.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "pre_packet.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "pre_configured.png"));

            string iniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                iniPath,
                "[Options]\n" +
                "showname=Phoenix Wright\n" +
                "realization=phoenix-realization\n" +
                "gender=unknown\n" +
                "side=def\n" +
                "[Time]\n" +
                "preanim=0\n" +
                "pre_packet=1200\n" +
                "[stay_time]\n" +
                "pre_packet=250\n" +
                "[Emotions]\n" +
                "number=1\n" +
                "1=normal#pre_configured#normal#1#1\n" +
                "[(b)normal_FrameSFX]\n" +
                "2=phoenix-talk-hit\n" +
                "[(b)normal_FrameScreenshake]\n" +
                "3=1\n" +
                "[pre_configured_FrameSFX]\n" +
                "1=phoenix-pre-hit\n");

            return CharacterFolder.Create(iniPath);
        }

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "viewport_timing_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            originalConfigIniPath = Globals.PathToConfigINI;
            originalBaseFolders = new List<string>(Globals.BaseFolders);
        }

        [TearDown]
        public void TearDown()
        {
            Globals.PathToConfigINI = originalConfigIniPath;
            Globals.BaseFolders = originalBaseFolders;

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [Test]
        public void GetTextCrawlMilliseconds_UsesConfigIniValue()
        {
            string configPath = Path.Combine(tempRoot, "config.ini");
            File.WriteAllText(
                configPath,
                "; viewport timing test\n" +
                "stay_time=200\n" +
                "text_crawl=64\n");

            Globals.PathToConfigINI = configPath;

            Assert.That(AO2ViewportAssetResolver.GetTextCrawlMilliseconds(), Is.EqualTo(64));
        }

        [Test]
        public void GetTextCrawlMilliseconds_FallsBackToDefaultWhenMissing()
        {
            Globals.PathToConfigINI = Path.Combine(tempRoot, "missing-config.ini");

            Assert.That(AO2ViewportAssetResolver.GetTextCrawlMilliseconds(), Is.EqualTo(40));
        }

        [Test]
        public void ViewportTimingSettings_UseConfigIniValues()
        {
            string configPath = Path.Combine(tempRoot, "config.ini");
            File.WriteAllText(
                configPath,
                "text_crawl=52\n" +
                "blip_rate=4\n" +
                "blank_blip=true\n" +
                "shake=0\n");

            Globals.PathToConfigINI = configPath;

            Assert.Multiple(() =>
            {
                Assert.That(AO2ViewportAssetResolver.GetTextCrawlMilliseconds(), Is.EqualTo(52));
                Assert.That(AO2ViewportAssetResolver.GetBlipRate(), Is.EqualTo(4));
                Assert.That(AO2ViewportAssetResolver.GetBlankBlipEnabled(), Is.True);
                Assert.That(AO2ViewportAssetResolver.GetScreenShakeEnabled(), Is.False);
            });
        }

        [Test]
        public void ViewportTimingSettings_ReadQtSettingsGeneralSection()
        {
            string configPath = Path.Combine(tempRoot, "config.ini");
            File.WriteAllText(
                configPath,
                "[General]\n" +
                "text_crawl=48\n" +
                "blip_rate=3\n" +
                "blank_blip=false\n");

            Globals.PathToConfigINI = configPath;

            Assert.Multiple(() =>
            {
                Assert.That(AO2ViewportAssetResolver.GetTextCrawlMilliseconds(), Is.EqualTo(48));
                Assert.That(AO2ViewportAssetResolver.GetBlipRate(), Is.EqualTo(3));
                Assert.That(AO2ViewportAssetResolver.GetBlankBlipEnabled(), Is.False);
            });
        }

        [Test]
        public void ResolveCharacterDialogAnimationDetails_ReturnsMatchedAo2Token()
        {
            CharacterFolder character = CreateViewportTestCharacter();

            AO2ViewportAssetResolver.ResolvedCharacterAnimation resolved =
                AO2ViewportAssetResolver.ResolveCharacterDialogAnimationDetails(
                    character,
                    "normal",
                    talking: true);

            Assert.Multiple(() =>
            {
                Assert.That(resolved.AssetPath, Does.EndWith("(b)normal.png"));
                Assert.That(resolved.ResolvedToken, Is.EqualTo("(b)normal"));
            });
        }

        [Test]
        public void ResolveCharacterFrameEffects_ReadsCharIniEntriesForResolvedToken()
        {
            CharacterFolder character = CreateViewportTestCharacter();

            IReadOnlyList<AO2ViewportAssetResolver.ViewportFrameEffect> resolved =
                AO2ViewportAssetResolver.ResolveCharacterFrameEffects(
                    character,
                    "(b)normal",
                    "_FrameSFX");

            Assert.That(resolved, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(resolved[0].FrameNumber, Is.EqualTo(2));
                Assert.That(resolved[0].Value, Is.EqualTo("phoenix-talk-hit"));
            });
        }

        [Test]
        public void ResolveCharacterBlipToken_UsesPerEmoteOverrideBeforeDefault()
        {
            string characterDirectory = Path.Combine(tempRoot, "characters", "BlipPhoenix");
            Directory.CreateDirectory(characterDirectory);
            string iniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                iniPath,
                "[Options]\n" +
                "blips=male\n" +
                "[Emotions]\n" +
                "number=1\n" +
                "1=normal#-#normal#0#1\n" +
                "[OptionsN]\n" +
                "1=1\n" +
                "[Options1]\n" +
                "blips=custom-voice\n");

            CharacterFolder character = CharacterFolder.Create(iniPath);

            string token = AO2ViewportAssetResolver.ResolveCharacterBlipToken(character, "normal");

            Assert.That(token, Is.EqualTo("custom-voice"));
        }

        [Test]
        public void ResolveCharacterBlipToken_FallsBackToGenderThenMale()
        {
            string femaleDirectory = Path.Combine(tempRoot, "characters", "BlipFranziska");
            Directory.CreateDirectory(femaleDirectory);
            string femaleIniPath = Path.Combine(femaleDirectory, "char.ini");
            File.WriteAllText(
                femaleIniPath,
                "[Options]\n" +
                "gender=female\n" +
                "[Emotions]\n" +
                "number=1\n" +
                "1=normal#-#normal#0#1\n");

            string noGenderDirectory = Path.Combine(tempRoot, "characters", "BlipJudge");
            Directory.CreateDirectory(noGenderDirectory);
            string noGenderIniPath = Path.Combine(noGenderDirectory, "char.ini");
            File.WriteAllText(
                noGenderIniPath,
                "[Options]\n" +
                "[Emotions]\n" +
                "number=1\n" +
                "1=normal#-#normal#0#1\n");

            CharacterFolder femaleCharacter = CharacterFolder.Create(femaleIniPath);
            CharacterFolder noGenderCharacter = CharacterFolder.Create(noGenderIniPath);

            Assert.Multiple(() =>
            {
                Assert.That(AO2ViewportAssetResolver.ResolveCharacterBlipToken(femaleCharacter, "normal"), Is.EqualTo("female"));
                Assert.That(AO2ViewportAssetResolver.ResolveCharacterBlipToken(noGenderCharacter, "normal"), Is.EqualTo("male"));
            });
        }

        [Test]
        public void ResolveViewportBlipToken_IgnoresPacketZeroAndFallsBackToCharacterBlip()
        {
            string characterDirectory = Path.Combine(tempRoot, "characters", "BlipFranziska");
            Directory.CreateDirectory(characterDirectory);
            string iniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                iniPath,
                "[Options]\n" +
                "blips=female\n" +
                "[Emotions]\n" +
                "number=1\n" +
                "1=normal#-#normal#0#1\n");

            CharacterFolder character = CharacterFolder.Create(iniPath);
            ICMessage message = new ICMessage
            {
                Emote = "normal",
                Blips = "0"
            };

            string token = AO2ViewportControl.ResolveViewportBlipToken(character, message);

            Assert.That(token, Is.EqualTo("female"));
        }

        [Test]
        public void ResolveBlipPath_ResolvesAo2StyleRelativeCharacterPath()
        {
            string customBlipPath = Path.Combine(tempRoot, "characters", "BlipPhoenix", "voice", "custom.opus");
            Directory.CreateDirectory(Path.GetDirectoryName(customBlipPath)!);
            File.WriteAllBytes(customBlipPath, new byte[] { 1, 2, 3, 4 });
            Globals.BaseFolders = new List<string> { tempRoot };

            string? resolved = AO2ViewportAudioResolver.ResolveBlipPath("../../characters/BlipPhoenix/voice/custom");

            Assert.That(resolved, Is.EqualTo(customBlipPath));
        }
    }
}
