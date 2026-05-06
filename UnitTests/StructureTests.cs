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
                Assert.That(invalidEmote.DeskMod, Is.EqualTo(ICMessage.DeskMods.Unspecified), "Invalid deskMod should default to AO2 unspecified");
            });
        }
    }
    
    [TestFixture]
    public class BackgroundTests
    {
        // We'll need to mock some file system behavior for testing
        private string _tempDir = string.Empty;
        private List<string> _originalBaseFolders = new List<string>();
        
        [SetUp]
        public void SetUp()
        {
            // Store original base folders
            _originalBaseFolders = new List<string>(Globals.BaseFolders);
            
            // Create a temporary directory for testing
            _tempDir = Path.Combine(Path.GetTempPath(), $"bg_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
            
            // Override base folders to use our test directory
            Globals.BaseFolders = new List<string> { _tempDir };
            
            // Create a background directory structure
            var bgDir = Path.Combine(_tempDir, "background");
            Directory.CreateDirectory(bgDir);
            
            // Create a test background
            var testBgDir = Path.Combine(bgDir, "testbg");
            Directory.CreateDirectory(testBgDir);
            
            // Create some test background images
            CreateEmptyFile(Path.Combine(testBgDir, "defenseempty.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "witnessempty.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "prosecutorempty.png"));
            CreateEmptyFile(Path.Combine(testBgDir, "background.png"));
            
            // Create a desk image (should be excluded)
            CreateEmptyFile(Path.Combine(testBgDir, "defensedesk.png"));
        }
        
        [TearDown]
        public void TearDown()
        {
            // Restore original base folders
            Globals.BaseFolders = _originalBaseFolders;
            
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
            CreateEmptyFile(Path.Combine(characterDirectory, "(a)packet_anim.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "(b)packet_anim.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "pre_packet.png"));
            CreateEmptyFile(Path.Combine(characterDirectory, "pre_configured.png"));

            string iniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                iniPath,
                "[Options]\n" +
                "showname=Phoenix Wright\n" +
                "gender=unknown\n" +
                "side=def\n" +
                "[Time]\n" +
                "preanim=0\n" +
                "pre_packet=1200\n" +
                "[stay_time]\n" +
                "pre_packet=250\n" +
                "[Emotions]\n" +
                "number=1\n" +
                "1=normal#pre_configured#normal#1#1\n");

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
            Assert.That(background!.bgImages, Has.Count.EqualTo(4), "Background should have 4 images (excluding desk)");
            
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
        public void Test_AO2ViewportAssetResolver_DeskVisibilityMatchesAo2PostPreanimState()
        {
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Hidden, "def"), Is.False);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Shown, "def"), Is.True);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Chat, "def"), Is.True);
            Assert.That(AO2ViewportAssetResolver.ShouldShowDesk(ICMessage.DeskMods.Chat, "custom"), Is.False);
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
            Assert.That(
                AO2ViewportAssetResolver.ShouldShowDeskDuringPreAnimation(ICMessage.DeskMods.Chat, "def"),
                Is.True);
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
            File.WriteAllText(
                Path.Combine(viewportThemeDir, "courtroom_design.ini"),
                "ao2_chatbox=0,178,256,104\n" +
                "showname=1,0,46,15\n" +
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
            Assert.That(
                AO2ViewportAssetResolver.ResolveShoutOverlayImage(ICMessage.ShoutModifiers.Objection),
                Does.Contain("objection_bubble.gif"));
            Assert.That(
                AO2ViewportAssetResolver.ResolveShoutOverlayImage(ICMessage.ShoutModifiers.Nothing),
                Is.Null);
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
}
