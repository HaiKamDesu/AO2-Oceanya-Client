using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests;

/// <summary>
/// A sprite rewritten in place must decode again, not come back from a cache.
/// </summary>
/// <remarks>
/// Reported as "I edit the emote that is on screen, it lets me, and the viewport keeps showing the old
/// image until I restart". Editing a character rewrites its sprites IN PLACE - the file organization keeps
/// the destination name, so the path the viewport draws is byte-for-byte the same string. The decode caches
/// were keyed by path alone, so the pre-edit bitmap was served for the rest of the session. Animated emotes
/// went through the timestamped animation cache and self-healed, which is why this looked intermittent.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class EditedAssetCacheInvalidationTests
{
    private string temporaryDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), "OceanyaEditedAsset_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Writes a solid-colour PNG, so two revisions are trivially distinguishable.</summary>
    private void WritePng(string path, Color color)
    {
        using Bitmap bitmap = new Bitmap(8, 8);
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                bitmap.SetPixel(x, y, color);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static Color FirstPixelOf(System.Windows.Media.ImageSource image)
    {
        System.Windows.Media.Imaging.BitmapSource bitmap =
            (System.Windows.Media.Imaging.BitmapSource)image;
        System.Windows.Media.Imaging.FormatConvertedBitmap converted =
            new System.Windows.Media.Imaging.FormatConvertedBitmap(
                bitmap,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                0);
        byte[] pixels = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixels, 4, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    [Test]
    public void StaticSpriteRewrittenInPlace_DecodesTheNewImage()
    {
        string spritePath = Path.Combine(temporaryDirectory, "Attack.png");
        WritePng(spritePath, Color.FromArgb(255, 200, 40, 40));

        System.Windows.Media.ImageSource before =
            Ao2AnimationPreview.LoadStaticPreviewImage(spritePath, decodePixelWidth: 0);
        Assert.That(FirstPixelOf(before).R, Is.EqualTo(200), "precondition: the pre-edit image decoded");

        // The edit: same path, different bytes, exactly as the character editor applies it.
        WritePng(spritePath, Color.FromArgb(255, 40, 200, 40));
        File.SetLastWriteTimeUtc(spritePath, DateTime.UtcNow.AddSeconds(5));

        System.Windows.Media.ImageSource after =
            Ao2AnimationPreview.LoadStaticPreviewImage(spritePath, decodePixelWidth: 0);

        Assert.That(
            FirstPixelOf(after).G,
            Is.EqualTo(200),
            "the edited sprite must decode again; a path-only cache key served the pre-edit image until a restart");
    }

    [Test]
    public void StaticSpriteUntouched_IsStillServedFromCache()
    {
        string spritePath = Path.Combine(temporaryDirectory, "Normal.png");
        WritePng(spritePath, Color.FromArgb(255, 10, 20, 30));

        System.Windows.Media.ImageSource first =
            Ao2AnimationPreview.LoadStaticPreviewImage(spritePath, decodePixelWidth: 0);
        System.Windows.Media.ImageSource second =
            Ao2AnimationPreview.LoadStaticPreviewImage(spritePath, decodePixelWidth: 0);

        Assert.That(
            second,
            Is.SameAs(first),
            "an unchanged file must still hit the cache, or every render would re-decode");
    }

    [Test]
    public void CachedLookupForARewrittenSprite_ReportsAMiss()
    {
        string spritePath = Path.Combine(temporaryDirectory, "Hurt.png");
        WritePng(spritePath, Color.FromArgb(255, 1, 2, 3));
        _ = Ao2AnimationPreview.LoadStaticPreviewImage(spritePath, decodePixelWidth: 0);
        Assert.That(
            Ao2AnimationPreview.TryLoadCachedStaticPreviewImage(spritePath, 0, out _),
            Is.True,
            "precondition: the decode was cached");

        WritePng(spritePath, Color.FromArgb(255, 3, 2, 1));
        File.SetLastWriteTimeUtc(spritePath, DateTime.UtcNow.AddSeconds(5));

        Assert.That(
            Ao2AnimationPreview.TryLoadCachedStaticPreviewImage(spritePath, 0, out _),
            Is.False,
            "the viewport's fast path must miss for a rewritten sprite, or it assigns the pre-edit bitmap");
    }
}
