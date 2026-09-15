using System.Drawing;
using System.Drawing.Imaging;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests;

/// <summary>
/// Character assets must never stay locked by the client.
/// </summary>
/// <remarks>
/// Reported as "you cannot edit a character folder the client has used this session - access to the path is
/// denied". <c>System.Drawing.Image.FromFile</c> keeps the FILE open for the lifetime of the image, and an
/// animation player lives for as long as its sprite or background is on screen, so a character's .gif stayed
/// locked and the editor's folder replacement failed.
/// </remarks>
[TestFixture]
public sealed class AssetFileLockTests
{
    private string temporaryDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), "OceanyaAssetLockTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Writes a small real GIF the animation player can decode.</summary>
    private string CreateGif(string fileName)
    {
        string path = Path.Combine(temporaryDirectory, fileName);
        using (Bitmap bitmap = new Bitmap(4, 4))
        {
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    bitmap.SetPixel(x, y, Color.FromArgb(255, x * 60, y * 60, 128));
                }
            }

            bitmap.Save(path, ImageFormat.Gif);
        }

        return path;
    }

    /// <summary>Whether the file can be opened for exclusive write, i.e. nothing else holds a handle.</summary>
    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    [Test]
    public void GifAnimationPlayer_DoesNotLockTheSourceFileWhileAlive()
    {
        string gifPath = CreateGif("locked.gif");
        Assert.That(CanOpenExclusively(gifPath), Is.True, "precondition: the fixture file starts unlocked");

        GifAnimationPlayer player = GifAnimationPlayer.CreateFullFidelity(gifPath, loop: true);
        try
        {
            Assert.That(
                CanOpenExclusively(gifPath),
                Is.True,
                "a live animation player must not hold the character's file open - that is what made a used "
                + "character folder impossible to edit");
        }
        finally
        {
            player.Stop();
        }

        Assert.That(CanOpenExclusively(gifPath), Is.True);
    }

    /// <summary>
    /// The static preview and APNG-detection caches are keyed by PATH ALONE, with no write timestamp, so
    /// without an explicit purge they keep serving the pre-edit image forever. This is the "changes don't
    /// follow through" half of the report, separate from the file lock.
    /// </summary>
    [Test]
    public void ReleaseCachedAssetsUnder_DropsCachesForTheEditedFolder()
    {
        string gifPath = CreateGif("cached.gif");

        System.Windows.Media.ImageSource first = Ao2AnimationPreview.LoadStaticPreviewImage(gifPath, 32);
        Assert.That(first, Is.Not.Null);
        Assert.That(
            Ao2AnimationPreview.TryLoadCachedStaticPreviewImage(gifPath, 32, out System.Windows.Media.ImageSource? cached),
            Is.True,
            "precondition: the preview must be cached before the release");
        Assert.That(cached, Is.Not.Null);

        Ao2AnimationPreview.ReleaseCachedAssetsUnder(temporaryDirectory);

        Assert.That(
            Ao2AnimationPreview.TryLoadCachedStaticPreviewImage(gifPath, 32, out _),
            Is.False,
            "the cached preview must be gone so the edited art is re-read");
    }

    [Test]
    public void ReleaseCachedAssetsUnder_LeavesOtherFoldersAlone()
    {
        string keptPath = CreateGif("kept.gif");
        _ = Ao2AnimationPreview.LoadStaticPreviewImage(keptPath, 32);
        Assert.That(Ao2AnimationPreview.TryLoadCachedStaticPreviewImage(keptPath, 32, out _), Is.True);

        Ao2AnimationPreview.ReleaseCachedAssetsUnder(Path.Combine(temporaryDirectory, "someOtherCharacter"));

        Assert.That(
            Ao2AnimationPreview.TryLoadCachedStaticPreviewImage(keptPath, 32, out _),
            Is.True,
            "releasing one character's assets must not purge another's");
    }

    /// <summary>
    /// The asset must also be replaceable, which is what the character editor actually does when applying
    /// an edit (it moves the folder aside and moves a staged copy into place).
    /// </summary>
    [Test]
    public void GifAnimationPlayer_LeavesTheSourceFileReplaceableWhileAlive()
    {
        string gifPath = CreateGif("replaceable.gif");
        string replacementPath = CreateGif("replacement.gif");

        GifAnimationPlayer player = GifAnimationPlayer.CreateFullFidelity(gifPath, loop: true);
        try
        {
            Assert.DoesNotThrow(
                () => File.Copy(replacementPath, gifPath, overwrite: true),
                "overwriting a character asset that is currently displayed must succeed");
            Assert.DoesNotThrow(
                () => File.Delete(gifPath),
                "deleting a character asset that is currently displayed must succeed");
        }
        finally
        {
            player.Stop();
        }
    }
}
