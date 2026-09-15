using System;
using System.IO;
using System.Threading;
using AOBot_Testing.Structures;
using NUnit.Framework;
using OceanyaClient.Features.Viewport;

namespace UnitTests
{
    /// <summary>
    /// Editing a character the viewport is currently showing must repaint with the NEW art.
    /// </summary>
    /// <remarks>
    /// Reported as "I edit emote 1 to use image 2, hit done, and the viewport still shows image 1 - it only
    /// applies after restarting the client". Dropping the decode caches was not enough: a
    /// <see cref="CharacterFolder"/> resolves each emote's sprite paths ONCE at parse time, and the viewport
    /// repaints from the instance it captured when the scene was drawn, so it replayed the pre-edit folder.
    /// </remarks>
    [TestFixture]
    public class InUseCharacterEditRefreshTests
    {
        private string temporaryDirectory = string.Empty;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "OceanyaInUseEdit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        /// <summary>Writes a character folder whose only emote points at <paramref name="spriteName"/>.</summary>
        private string WriteCharacter(string spriteName)
        {
            string characterDirectory = Path.Combine(temporaryDirectory, "characters", "Edited");
            Directory.CreateDirectory(characterDirectory);
            File.WriteAllBytes(Path.Combine(characterDirectory, "(a)" + spriteName + ".png"), new byte[] { 1, 2, 3, 4 });

            string iniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                iniPath,
                "[Options]\n"
                + "showname=Edited\n"
                + "gender=unknown\n"
                + "side=def\n"
                + "[Emotions]\n"
                + "number=1\n"
                + $"1=Emote One#-#{spriteName}#0#1\n");

            return iniPath;
        }

        [Test]
        public void VisualRefresh_ReresolvesTheCharacterInsteadOfReplayingTheCapturedInstance()
        {
            string iniPath = WriteCharacter("imageOne");
            CharacterFolder capturedByTheScene = CharacterFolder.Create(iniPath);
            Assert.That(
                EmoteSpritePathOf(capturedByTheScene),
                Does.Contain("imageOne"),
                "precondition: the scene was drawn with the pre-edit folder");

            // The edit: same emote, different image, exactly as the character editor writes it.
            string editedIniPath = WriteCharacter("imageTwo");
            CharacterFolder reindexed = CharacterFolder.Create(editedIniPath);

            CharacterFolder? resolved = AO2ViewportControl.ResolveCharacterForVisualRefresh(
                capturedByTheScene,
                messageCharacterName: null,
                resolve: _ => reindexed);

            Assert.That(resolved, Is.SameAs(reindexed));
            Assert.That(
                EmoteSpritePathOf(resolved!),
                Does.Contain("imageTwo"),
                "the repaint must use the re-read folder, or it shows the pre-edit art until a restart");
        }

        [Test]
        public void VisualRefresh_FallsBackToTheMessageCharacterNameWhenNoInstanceWasCaptured()
        {
            string capturedName = string.Empty;
            CharacterFolder expected = CharacterFolder.Create(WriteCharacter("imageOne"));

            CharacterFolder? resolved = AO2ViewportControl.ResolveCharacterForVisualRefresh(
                capturedCharacter: null,
                messageCharacterName: "Edited",
                resolve: name =>
                {
                    capturedName = name ?? string.Empty;
                    return expected;
                });

            Assert.That(capturedName, Is.EqualTo("Edited"));
            Assert.That(resolved, Is.SameAs(expected));
        }

        [Test]
        public void VisualRefresh_WithNothingToIdentifyTheCharacter_ResolvesNothing()
        {
            bool resolverCalled = false;

            CharacterFolder? resolved = AO2ViewportControl.ResolveCharacterForVisualRefresh(
                capturedCharacter: null,
                messageCharacterName: "   ",
                resolve: _ =>
                {
                    resolverCalled = true;
                    return null;
                });

            Assert.That(resolved, Is.Null);
            Assert.That(resolverCalled, Is.False, "an empty name must not reach the on-demand character probe");
        }

        /// <summary>
        /// The scene draws the animation token the MESSAGE carried, so re-reading the character is not
        /// enough on its own: repointing an emote at a different image has to move the token too.
        /// </summary>
        [Test]
        public void VisualRefresh_RepointedEmote_MapsTheRenderedTokenToTheNewImage()
        {
            CharacterFolder before = CharacterFolder.Create(WriteCharacter("imageOne"));
            CharacterFolder after = CharacterFolder.Create(WriteCharacter("imageTwo"));

            string? remapped = AO2ViewportControl.ResolveEmoteTokenForVisualRefresh(
                before,
                after,
                renderedEmoteToken: "imageOne");

            Assert.That(
                remapped,
                Is.EqualTo("imageTwo"),
                "the viewport keeps drawing the old file unless the token itself is remapped");
        }

        [Test]
        public void VisualRefresh_EmoteThatDidNotChange_KeepsItsToken()
        {
            CharacterFolder before = CharacterFolder.Create(WriteCharacter("imageOne"));
            CharacterFolder after = CharacterFolder.Create(WriteCharacter("imageOne"));

            Assert.That(
                AO2ViewportControl.ResolveEmoteTokenForVisualRefresh(before, after, "imageOne"),
                Is.Null,
                "an unchanged emote must not churn the scene");
        }

        [Test]
        public void VisualRefresh_TokenFromAnotherCharacter_IsLeftAlone()
        {
            CharacterFolder before = CharacterFolder.Create(WriteCharacter("imageOne"));
            CharacterFolder after = CharacterFolder.Create(WriteCharacter("imageTwo"));

            Assert.That(
                AO2ViewportControl.ResolveEmoteTokenForVisualRefresh(before, after, "someoneElsesSprite"),
                Is.Null,
                "a token that matches no emote of this character cannot be mapped and must not be guessed");
        }

        private static string EmoteSpritePathOf(CharacterFolder character)
        {
            Assert.That(character.configINI?.Emotions, Is.Not.Null.And.Not.Empty);
            foreach (var emote in character.configINI!.Emotions)
            {
                return emote.Value.Animation ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
