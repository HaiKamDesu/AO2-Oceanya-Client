using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                Assert.That(partialEmote.Modifier, Is.EqualTo(ICMessage.EmoteModifiers.PlayPreanimation), "Modifier should default to PlayPreanimation");
                Assert.That(partialEmote.DeskMod, Is.EqualTo(ICMessage.DeskMods.Chat), "DeskMod should default to Chat");
            });
            
            // Test parsing with invalid numeric fields
            var invalidData = "invalid#pre#anim#invalid#invalid";
            var invalidEmote = Emote.ParseEmoteLine(invalidData);
            
            Assert.Multiple(() =>
            {
                Assert.That(invalidEmote.Name, Is.EqualTo("invalid"), "Name should be parsed correctly");
                Assert.That(invalidEmote.Modifier, Is.EqualTo(ICMessage.EmoteModifiers.PlayPreanimation), "Invalid modifier should default to PlayPreanimation");
                Assert.That(invalidEmote.DeskMod, Is.EqualTo(ICMessage.DeskMods.Chat), "Invalid deskMod should default to Chat");
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
