using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Viewport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UnitTests;

/// <summary>
/// Validates AO2 packet handling newly added for viewport parity: RT# testimony/verdict
/// packets and LE# evidence list packets. These tests cover the AOClient event contract
/// that the viewport control subscribes to.
/// </summary>
[TestFixture]
[Category("NoNetworkCall")]
public class AO2ViewportParityPacketTests
{
    // ─── RT# (testimony/verdict overlay) ────────────────────────────────────────

    [Test]
    public async Task HandleMessage_RtPacket_Testimony1_FiresOnRtReceived()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedContent = null;
        int capturedVariant = -1;
        client.OnRtReceived += (content, variant) => { capturedContent = content; capturedVariant = variant; };

        await client.HandleMessage("RT#testimony1#0#%");

        Assert.Multiple(() =>
        {
            Assert.That(capturedContent, Is.EqualTo("testimony1"));
            Assert.That(capturedVariant, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task HandleMessage_RtPacket_Testimony2_FiresOnRtReceived()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedContent = null;
        int capturedVariant = -1;
        client.OnRtReceived += (content, variant) => { capturedContent = content; capturedVariant = variant; };

        await client.HandleMessage("RT#testimony2#0#%");

        Assert.Multiple(() =>
        {
            Assert.That(capturedContent, Is.EqualTo("testimony2"));
            Assert.That(capturedVariant, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task HandleMessage_RtPacket_JudgeRuling_Variant0_FiresOnRtReceived()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedContent = null;
        int capturedVariant = -1;
        client.OnRtReceived += (content, variant) => { capturedContent = content; capturedVariant = variant; };

        await client.HandleMessage("RT#judgeruling#0#%");

        Assert.Multiple(() =>
        {
            Assert.That(capturedContent, Is.EqualTo("judgeruling"));
            Assert.That(capturedVariant, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task HandleMessage_RtPacket_JudgeRuling_Variant1_FiresOnRtReceived()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedContent = null;
        int capturedVariant = -1;
        client.OnRtReceived += (content, variant) => { capturedContent = content; capturedVariant = variant; };

        await client.HandleMessage("RT#judgeruling#1#%");

        Assert.Multiple(() =>
        {
            Assert.That(capturedContent, Is.EqualTo("judgeruling"));
            Assert.That(capturedVariant, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleMessage_RtPacket_TooFewFields_DoesNotFireOnRtReceived()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool eventFired = false;
        client.OnRtReceived += (_, _) => { eventFired = true; };

        // RT#% splits to ["RT", "%"] — only 2 elements, so fields.Length >= 3 fails.
        await client.HandleMessage("RT#%");

        Assert.That(eventFired, Is.False);
    }

    [Test]
    public async Task HandleMessage_RtPacket_NonNumericVariant_DefaultsToZero()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        int capturedVariant = -1;
        client.OnRtReceived += (_, variant) => { capturedVariant = variant; };

        await client.HandleMessage("RT#judgeruling#notanumber#%");

        Assert.That(capturedVariant, Is.EqualTo(0));
    }

    [Test]
    public async Task HandleMessage_RtPacket_SymbolEscaping_DecodesContentField()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedContent = null;
        client.OnRtReceived += (content, _) => { capturedContent = content; };

        // <num> decodes to # per AO2 symbol escaping
        await client.HandleMessage("RT#testimony<num>1#0#%");

        Assert.That(capturedContent, Is.EqualTo("testimony#1"));
    }

    // ─── LE# (evidence list) ────────────────────────────────────────────────────

    [Test]
    public async Task HandleMessage_LePacket_SingleItem_GetEvidenceImagePathReturnsImage()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        await client.HandleMessage("LE#Autopsy Report&Murdered at dawn&report.png#%");

        Assert.That(client.GetEvidenceImagePath(1), Is.EqualTo("report.png"));
    }

    [Test]
    public async Task HandleMessage_LePacket_MultipleItems_OneBasedLookupWorks()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        await client.HandleMessage("LE#Photo&Crime scene&photo.png#Knife&Murder weapon&knife.png#%");

        Assert.Multiple(() =>
        {
            Assert.That(client.GetEvidenceImagePath(1), Is.EqualTo("photo.png"));
            Assert.That(client.GetEvidenceImagePath(2), Is.EqualTo("knife.png"));
        });
    }

    [Test]
    public async Task HandleMessage_LePacket_ItemWithLessThanThreeSubFields_IsSkipped()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        // First item has only 2 sub-fields (name&desc, no image) and is skipped by the parser.
        // Second item has 3 sub-fields and becomes evidence id=1.
        await client.HandleMessage("LE#BadItem&OnlyDesc#ValidItem&Desc&valid.png#%");

        Assert.Multiple(() =>
        {
            Assert.That(client.GetEvidenceImagePath(1), Is.EqualTo("valid.png"));
            Assert.That(client.GetEvidenceImagePath(2), Is.Null);
        });
    }

    [Test]
    public async Task HandleMessage_LePacket_BlankImageField_GetEvidenceImagePathReturnsNull()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        await client.HandleMessage("LE#Item&Description&   #%");

        // Whitespace-only image field treated as absent
        Assert.That(client.GetEvidenceImagePath(1), Is.Null);
    }

    [Test]
    public async Task HandleMessage_LePacket_SymbolEscapingAppliedToImageField()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        // <and> decodes to &
        await client.HandleMessage("LE#Item&Description&path<and>file.png#%");

        Assert.That(client.GetEvidenceImagePath(1), Is.EqualTo("path&file.png"));
    }

    [Test]
    public async Task GetEvidenceImagePath_ZeroId_ReturnsNull()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("LE#Photo&Crime scene&photo.png#%");

        Assert.That(client.GetEvidenceImagePath(0), Is.Null);
    }

    [Test]
    public async Task GetEvidenceImagePath_NegativeId_ReturnsNull()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("LE#Photo&Crime scene&photo.png#%");

        Assert.That(client.GetEvidenceImagePath(-1), Is.Null);
    }

    [Test]
    public async Task GetEvidenceImagePath_OutOfRangeId_ReturnsNull()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("LE#Photo&Crime scene&photo.png#%");

        // Only 1 item, id=2 is out of range
        Assert.That(client.GetEvidenceImagePath(2), Is.Null);
    }

    [Test]
    public async Task HandleMessage_NewLePacket_ReplacesOldEvidence()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        await client.HandleMessage("LE#OldEvidence&OldDesc&old.png#%");
        Assert.That(client.GetEvidenceImagePath(1), Is.EqualTo("old.png"), "Precondition: old evidence set.");

        await client.HandleMessage("LE#NewEvidence&NewDesc&new.png#%");

        Assert.Multiple(() =>
        {
            Assert.That(client.GetEvidenceImagePath(1), Is.EqualTo("new.png"), "New evidence should replace old.");
            Assert.That(client.GetEvidenceImagePath(2), Is.Null, "Old second slot should not survive the replacement.");
        });
    }
}

/// <summary>
/// Validates the AO2ViewportAssetResolver methods added for viewport parity:
/// testimony/WTCE overlays, character stickers, chat arrow, evidence icon/presentation.
/// Uses a temp filesystem so there are no real AO2 asset dependencies.
/// </summary>
[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public class AO2ViewportParityAssetResolverTests
{
    private string tempDir = string.Empty;
    private List<string> originalBaseFolders = new List<string>();
    private string originalConfigIniPath = string.Empty;
    private string originalSaveFilePath = string.Empty;
    private int originalViewportWidth;
    private int originalViewportHeight;
    private int originalToolWidth;
    private int originalToolHeight;
    private int originalChatboxHeight;

    [SetUp]
    public void SetUp()
    {
        originalBaseFolders = new List<string>(Globals.BaseFolders);
        originalConfigIniPath = Globals.PathToConfigINI;
        originalSaveFilePath = SaveFile.CurrentStoragePath;
        originalViewportWidth = AO2ViewportAssetResolver.ViewportWidth;
        originalViewportHeight = AO2ViewportAssetResolver.ViewportHeight;
        originalToolWidth = AO2ViewportAssetResolver.ViewportToolWidth;
        originalToolHeight = AO2ViewportAssetResolver.ViewportToolHeight;
        originalChatboxHeight = AO2ViewportAssetResolver.ChatboxHeight;
        tempDir = Path.Combine(Path.GetTempPath(), "vp_resolver_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Globals.BaseFolders = new List<string> { tempDir };
        Globals.PathToConfigINI = Path.Combine(tempDir, "config.ini");
        File.WriteAllText(Globals.PathToConfigINI, "theme=(714x688) FullChar\nsubtheme=default\n");
        SaveFile.ConfigureStoragePathForTests(Path.Combine(tempDir, "savefile.json"));
        SaveFile.ResetForTests(new SaveData(), persist: false);
        Background.RefreshCache();
    }

    [TearDown]
    public void TearDown()
    {
        Globals.BaseFolders = originalBaseFolders;
        Globals.PathToConfigINI = originalConfigIniPath;
        AO2ViewportAssetResolver.SetViewportSurfaceDimensions(
            originalViewportWidth,
            originalViewportHeight,
            originalToolWidth,
            originalToolHeight,
            originalChatboxHeight);
        Background.RefreshCache();
        SaveFile.ConfigureStoragePathForTests(originalSaveFilePath);
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private void CreateEmptyFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Array.Empty<byte>());
    }

    private static void CreatePng(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x20;
            pixels[i + 1] = 0x40;
            pixels[i + 2] = 0x80;
            pixels[i + 3] = 0xFF;
        }

        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    [Test]
    public void AO2ThemeCatalog_EnumeratesThemesFromConfigBaseBeforeMounts()
    {
        string mountDir = Path.Combine(tempDir, "mount");
        Directory.CreateDirectory(Path.Combine(tempDir, "themes", "AOHD"));
        Directory.CreateDirectory(Path.Combine(tempDir, "themes", "Theme 10"));
        Directory.CreateDirectory(Path.Combine(tempDir, "themes", "Theme 2"));
        Directory.CreateDirectory(Path.Combine(mountDir, "themes", "Mounted"));
        Directory.CreateDirectory(Path.Combine(mountDir, "themes", "AOHD"));
        File.WriteAllText(Globals.PathToConfigINI, "mount_paths=" + mountDir + "\n");
        Globals.UpdateConfigINI(Globals.PathToConfigINI);

        IReadOnlyList<string> themes = AO2ThemeCatalog.GetThemes();

        Assert.That(themes, Does.Contain("AOHD"));
        Assert.That(themes, Does.Contain("Mounted"));
        Assert.That(themes.ToList().IndexOf("Theme 2"), Is.LessThan(themes.ToList().IndexOf("Theme 10")));
        Assert.That(themes.Count(theme => string.Equals(theme, "AOHD", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
    }

    [Test]
    public void AO2ThemeCatalog_SubthemesMatchAo2SelectorRules()
    {
        string themeRoot = Path.Combine(tempDir, "themes", "AOHD");
        Directory.CreateDirectory(Path.Combine(themeRoot, "server"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "default"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "effects"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "misc"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "wide"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "thin"));

        IReadOnlyList<AO2SubthemeOption> subthemes = AO2ThemeCatalog.GetSubthemes("AOHD");

        Assert.That(subthemes.Select(option => option.DisplayName), Is.EqualTo(new[] { "server", "default", "thin", "wide" }));
        Assert.That(subthemes[0].Value, Is.EqualTo("server"));
        Assert.That(subthemes[1].Value, Is.EqualTo("server"));
    }

    [Test]
    public void ResolveBackgroundPlacement_NoStretchScalesByViewportHeightAndCenters()
    {
        int originalViewportWidth = AO2ViewportAssetResolver.ViewportWidth;
        int originalViewportHeight = AO2ViewportAssetResolver.ViewportHeight;
        int originalToolWidth = AO2ViewportAssetResolver.ViewportToolWidth;
        int originalToolHeight = AO2ViewportAssetResolver.ViewportToolHeight;
        int originalChatboxHeight = AO2ViewportAssetResolver.ChatboxHeight;
        try
        {
            AO2ViewportAssetResolver.SetViewportSurfaceDimensions(768, 576, 768, 716, 140);
            string bgDir = Path.Combine(tempDir, "background", "widebg");
            CreatePng(Path.Combine(bgDir, "wit.png"), 1024, 576);
            File.WriteAllText(Path.Combine(bgDir, "design.ini"), "scaling=smooth\n");
            Background.RefreshCache();

            AO2ViewportAssetResolver.ViewportImagePlacement placement =
                AO2ViewportAssetResolver.ResolveBackgroundPlacement("widebg", "wit");

            Assert.Multiple(() =>
            {
                Assert.That(placement.Left, Is.EqualTo(-128));
                Assert.That(placement.Top, Is.EqualTo(0));
                Assert.That(placement.Width, Is.EqualTo(1024));
                Assert.That(placement.Height, Is.EqualTo(576));
            });
        }
        finally
        {
            AO2ViewportAssetResolver.SetViewportSurfaceDimensions(
                originalViewportWidth,
                originalViewportHeight,
                originalToolWidth,
                originalToolHeight,
                originalChatboxHeight);
        }
    }

    [Test]
    public void ResolveBackgroundPlacement_StretchTrueUsesViewportWidgetSizeWithoutExtraCentering()
    {
        int originalViewportWidth = AO2ViewportAssetResolver.ViewportWidth;
        int originalViewportHeight = AO2ViewportAssetResolver.ViewportHeight;
        int originalToolWidth = AO2ViewportAssetResolver.ViewportToolWidth;
        int originalToolHeight = AO2ViewportAssetResolver.ViewportToolHeight;
        int originalChatboxHeight = AO2ViewportAssetResolver.ChatboxHeight;
        try
        {
            AO2ViewportAssetResolver.SetViewportSurfaceDimensions(768, 576, 768, 716, 140);
            string bgDir = Path.Combine(tempDir, "background", "stretchbg");
            CreatePng(Path.Combine(bgDir, "wit.png"), 1024, 576);
            File.WriteAllText(Path.Combine(bgDir, "design.ini"), "scaling=smooth\nstretch=true\n");
            Background.RefreshCache();

            AO2ViewportAssetResolver.ViewportImagePlacement placement =
                AO2ViewportAssetResolver.ResolveBackgroundPlacement("stretchbg", "wit");

            Assert.Multiple(() =>
            {
                Assert.That(placement.Left, Is.EqualTo(0));
                Assert.That(placement.Top, Is.EqualTo(0));
                Assert.That(placement.Width, Is.EqualTo(768));
                Assert.That(placement.Height, Is.EqualTo(576));
            });
        }
        finally
        {
            AO2ViewportAssetResolver.SetViewportSurfaceDimensions(
                originalViewportWidth,
                originalViewportHeight,
                originalToolWidth,
                originalToolHeight,
                originalChatboxHeight);
        }
    }

    [Test]
    public void ResolveBackgroundPlacement_PrequalifiedCourtPositionUsesCourtOrigin()
    {
        int originalViewportWidth = AO2ViewportAssetResolver.ViewportWidth;
        int originalViewportHeight = AO2ViewportAssetResolver.ViewportHeight;
        int originalToolWidth = AO2ViewportAssetResolver.ViewportToolWidth;
        int originalToolHeight = AO2ViewportAssetResolver.ViewportToolHeight;
        int originalChatboxHeight = AO2ViewportAssetResolver.ChatboxHeight;
        try
        {
            AO2ViewportAssetResolver.SetViewportSurfaceDimensions(1363, 705, 1363, 865, 160);
            string bgDir = Path.Combine(tempDir, "background", "courtbg");
            CreatePng(Path.Combine(bgDir, "court.png"), 2000, 705);
            File.WriteAllText(Path.Combine(bgDir, "design.ini"), "scaling=smooth\ncourt:def/origin=1000\n");
            Background.RefreshCache();

            AO2ViewportAssetResolver.ViewportImagePlacement placement =
                AO2ViewportAssetResolver.ResolveBackgroundPlacement("courtbg", "court:def");

            Assert.Multiple(() =>
            {
                Assert.That(placement.ImagePath, Does.EndWith("court.png").IgnoreCase);
                Assert.That(placement.Left, Is.EqualTo(-318));
                Assert.That(placement.Top, Is.EqualTo(0));
                Assert.That(placement.Width, Is.EqualTo(2000));
                Assert.That(placement.Height, Is.EqualTo(705));
            });
        }
        finally
        {
            AO2ViewportAssetResolver.SetViewportSurfaceDimensions(
                originalViewportWidth,
                originalViewportHeight,
                originalToolWidth,
                originalToolHeight,
                originalChatboxHeight);
        }
    }

    [Test]
    public void ResolveDeskPlacement_NoStretchScalesOverlayByBackgroundWidgetHeightAndCenters()
    {
        int originalViewportWidth = AO2ViewportAssetResolver.ViewportWidth;
        int originalViewportHeight = AO2ViewportAssetResolver.ViewportHeight;
        int originalToolWidth = AO2ViewportAssetResolver.ViewportToolWidth;
        int originalToolHeight = AO2ViewportAssetResolver.ViewportToolHeight;
        int originalChatboxHeight = AO2ViewportAssetResolver.ChatboxHeight;
        try
        {
            AO2ViewportAssetResolver.SetViewportSurfaceDimensions(768, 576, 768, 716, 140);
            string bgDir = Path.Combine(tempDir, "background", "overlaybg");
            CreatePng(Path.Combine(bgDir, "wit.png"), 1024, 576);
            CreatePng(Path.Combine(bgDir, "wit_overlay.png"), 800, 400);
            File.WriteAllText(Path.Combine(bgDir, "design.ini"), "scaling=smooth\n");
            Background.RefreshCache();

            AO2ViewportAssetResolver.ViewportImagePlacement placement =
                AO2ViewportAssetResolver.ResolveDeskPlacement("overlaybg", "wit");

            Assert.Multiple(() =>
            {
                Assert.That(placement.Left, Is.EqualTo(-192));
                Assert.That(placement.Top, Is.EqualTo(0));
                Assert.That(placement.Width, Is.EqualTo(1152));
                Assert.That(placement.Height, Is.EqualTo(576));
            });
        }
        finally
        {
            AO2ViewportAssetResolver.SetViewportSurfaceDimensions(
                originalViewportWidth,
                originalViewportHeight,
                originalToolWidth,
                originalToolHeight,
                originalChatboxHeight);
        }
    }

    [Test]
    public void AO2ViewportControl_RenderMessage_AndChatboxOverlapToggle_DoNotSquashWideBackground()
    {
        _ = WpfTestApplicationContext.EnsureCreated();
        File.WriteAllText(Globals.PathToConfigINI, "theme=AOHD\nsubtheme=default\n");

        string themeDir = Path.Combine(tempDir, "themes", "AOHD");
        Directory.CreateDirectory(themeDir);
        CreateEmptyFile(Path.Combine(themeDir, "chat.png"));
        File.WriteAllText(
            Path.Combine(themeDir, "courtroom_design.ini"),
            "viewport=0,0,768,576\n" +
            "ao2_chatbox=0,576,768,140\n" +
            "showname=4,0,120,28\n" +
            "message=28,30,650,125\n" +
            "chat_arrow=680,110,24,20\n");

        string bgDir = Path.Combine(tempDir, "background", "widebg");
        CreatePng(Path.Combine(bgDir, "wit.png"), 1024, 576);
        File.WriteAllText(Path.Combine(bgDir, "design.ini"), "scaling=smooth\n");
        Background.RefreshCache();

        AOClient client = new AOClient("ws://localhost:10001/");
        client.curBG = "widebg";

        AO2ViewportControl control = new AO2ViewportControl();
        control.AttachClient(client);
        control.PreviewMessage(new ICMessage
        {
            Character = "Battler Ushiromiya",
            ShowName = "Battler",
            Message = "Test",
            Side = "wit",
            DeskMod = ICMessage.DeskMods.Hidden
        });

        AssertWideBackgroundPlacement(control);

        control.ChatboxOverlapsViewport = true;
        AssertWideBackgroundPlacement(control);

        control.ChatboxOverlapsViewport = false;
        AssertWideBackgroundPlacement(control);
    }

    private static void AssertWideBackgroundPlacement(AO2ViewportControl control)
    {
        Assert.Multiple(() =>
        {
            Assert.That(control.BackgroundImage.Width, Is.EqualTo(1024));
            Assert.That(control.BackgroundImage.Height, Is.EqualTo(576));
            Assert.That(Canvas.GetLeft(control.BackgroundImage), Is.EqualTo(-128));
            Assert.That(Canvas.GetTop(control.BackgroundImage), Is.EqualTo(0));
        });
    }

    // ─── ResolveWtceOverlayImage ─────────────────────────────────────────────────

    [Test]
    public void ResolveWtceOverlayImage_FindsAsset_InMiscDefaultFolder()
    {
        // With no background specified bgMisc="" so EnumerateWtceImageRoots starts at misc/default.
        CreateEmptyFile(Path.Combine(tempDir, "misc", "default", "testimony.png"));

        string? result = AO2ViewportAssetResolver.ResolveWtceOverlayImage("testimony");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith("testimony.png").IgnoreCase);
    }

    [Test]
    public void ResolveWtceOverlayImage_WhenBgHasMiscSetting_PrefersMiscBgFolder()
    {
        // Background with design.ini misc=courtroom — courtroom folder should be searched first.
        string bgDir = Path.Combine(tempDir, "background", "courthouse");
        Directory.CreateDirectory(bgDir);
        CreateEmptyFile(Path.Combine(bgDir, "background.png"));
        File.WriteAllText(Path.Combine(bgDir, "design.ini"), "misc=courtroom\n");
        Background.RefreshCache();

        // Place asset only in misc/courtroom — if the bg misc is respected it should be found.
        CreateEmptyFile(Path.Combine(tempDir, "misc", "courtroom", "testimony.png"));

        string? result = AO2ViewportAssetResolver.ResolveWtceOverlayImage("testimony", "courthouse");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("courtroom").IgnoreCase);
    }

    [Test]
    public void ResolveWtceOverlayImage_FallsBackToMiscDefault_WhenBgMiscFolderHasNoAsset()
    {
        string bgDir = Path.Combine(tempDir, "background", "courthouse");
        Directory.CreateDirectory(bgDir);
        CreateEmptyFile(Path.Combine(bgDir, "background.png"));
        File.WriteAllText(Path.Combine(bgDir, "design.ini"), "misc=courtroom\n");
        Background.RefreshCache();

        // Asset only in misc/default — misc/courtroom is absent.
        CreateEmptyFile(Path.Combine(tempDir, "misc", "default", "testimony.png"));

        string? result = AO2ViewportAssetResolver.ResolveWtceOverlayImage("testimony", "courthouse");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("default").IgnoreCase);
    }

    [Test]
    public void ResolveWtceOverlayImage_ReturnsNull_WhenAssetAbsent()
    {
        string? result = AO2ViewportAssetResolver.ResolveWtceOverlayImage("testimony");

        Assert.That(result, Is.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ResolveWtceOverlayImage_ReturnsNull_ForBlankStem(string stem)
    {
        CreateEmptyFile(Path.Combine(tempDir, "misc", "default", "testimony.png"));

        Assert.That(AO2ViewportAssetResolver.ResolveWtceOverlayImage(stem), Is.Null);
    }

    [Test]
    public void ResolveTestimonyOverlayImage_FindsTestimonyAsset()
    {
        // ResolveTestimonyOverlayImage delegates to ResolveWtceOverlayImage("testimony").
        CreateEmptyFile(Path.Combine(tempDir, "misc", "default", "testimony.png"));

        string? result = AO2ViewportAssetResolver.ResolveTestimonyOverlayImage();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith("testimony.png").IgnoreCase);
    }

    // ─── ResolveStickerImage ─────────────────────────────────────────────────────

    [Test]
    public void ResolveStickerImage_FindsSticker_WhenPresent()
    {
        // sticker/{characterName} is resolved from AO image roots, including baseFolder.
        CreateEmptyFile(Path.Combine(tempDir, "sticker", "Phoenix.png"));

        string? result = AO2ViewportAssetResolver.ResolveStickerImage("Phoenix");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith("Phoenix.png").IgnoreCase);
    }

    [Test]
    public void ResolveStickerImage_ReturnsNull_WhenAbsent()
    {
        string? result = AO2ViewportAssetResolver.ResolveStickerImage("Phoenix");

        Assert.That(result, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ResolveStickerImage_ReturnsNull_ForNullOrBlankInput(string? name)
    {
        CreateEmptyFile(Path.Combine(tempDir, "sticker", "Phoenix.png"));

        Assert.That(AO2ViewportAssetResolver.ResolveStickerImage(name), Is.Null);
    }

    // ─── ResolveChatArrowImage ───────────────────────────────────────────────────

    [Test]
    public void ResolveChatArrowImage_FindsChatArrow_WhenPresent()
    {
        // chat_arrow resolved from AO image roots; baseFolder is always searched.
        CreateEmptyFile(Path.Combine(tempDir, "chat_arrow.png"));

        string? result = AO2ViewportAssetResolver.ResolveChatArrowImage();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith("chat_arrow.png").IgnoreCase);
    }

    [Test]
    public void ResolveChatArrowImage_UsesCustomMiscArrowBeforeThemeArrow()
    {
        CreateEmptyFile(Path.Combine(tempDir, "themes", "(714x688) FullChar", "chat_arrow.png"));
        CreateEmptyFile(Path.Combine(tempDir, "misc", "p4", "Chat_Arrow.gif"));

        string? result = AO2ViewportAssetResolver.ResolveChatArrowImage("P4");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith(Path.Combine("misc", "p4", "Chat_Arrow.gif")).IgnoreCase);
    }

    [Test]
    public void ResolveChatArrowImage_UsesActiveThemeArrowForThemeDefaultChatbox()
    {
        CreateEmptyFile(Path.Combine(tempDir, "themes", "default", "chat_arrow.png"));
        CreateEmptyFile(Path.Combine(tempDir, "themes", "(714x688) FullChar", "Chat_Arrow.gif"));

        string? result = AO2ViewportAssetResolver.ResolveChatArrowImage();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith(Path.Combine("themes", "(714x688) FullChar", "Chat_Arrow.gif")).IgnoreCase);
    }

    [Test]
    public void ResolveChatArrowImage_ReturnsNull_WhenAbsent()
    {
        string? result = AO2ViewportAssetResolver.ResolveChatArrowImage();

        Assert.That(result, Is.Null);
    }

    // ─── ResolveEvidenceIconImage ────────────────────────────────────────────────

    [Test]
    public void ResolveEvidenceIconImage_FindsFile_ByExactFilename()
    {
        CreateEmptyFile(Path.Combine(tempDir, "evidence", "knife.png"));

        string? result = AO2ViewportAssetResolver.ResolveEvidenceIconImage("knife.png");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith("knife.png").IgnoreCase);
    }

    [Test]
    public void ResolveEvidenceIconImage_FindsFile_ByExtensionlessLookup()
    {
        // AO2 evidence tokens sometimes omit extension; resolver should still find the file.
        CreateEmptyFile(Path.Combine(tempDir, "evidence", "knife.png"));

        string? result = AO2ViewportAssetResolver.ResolveEvidenceIconImage("knife");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith("knife.png").IgnoreCase);
    }

    [Test]
    public void ResolveEvidenceIconImage_ReturnsNull_WhenFileAbsent()
    {
        string? result = AO2ViewportAssetResolver.ResolveEvidenceIconImage("missing.png");

        Assert.That(result, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ResolveEvidenceIconImage_ReturnsNull_ForNullOrBlankInput(string? file)
    {
        Assert.That(AO2ViewportAssetResolver.ResolveEvidenceIconImage(file), Is.Null);
    }

    // ─── ResolveEvidencePresentationImage ────────────────────────────────────────

    [Test]
    public void ResolveEvidencePresentationImage_LeftSide_UsesEvidenceAppearLeft()
    {
        CreateEmptyFile(Path.Combine(tempDir, "misc", "default", "evidence_appear_left.png"));

        string? result = AO2ViewportAssetResolver.ResolveEvidencePresentationImage(leftSide: true);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith("evidence_appear_left.png").IgnoreCase);
    }

    [Test]
    public void ResolveEvidencePresentationImage_RightSide_UsesEvidenceAppearRight()
    {
        CreateEmptyFile(Path.Combine(tempDir, "misc", "default", "evidence_appear_right.png"));

        string? result = AO2ViewportAssetResolver.ResolveEvidencePresentationImage(leftSide: false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith("evidence_appear_right.png").IgnoreCase);
    }

    [Test]
    public void ResolveEvidencePresentationImage_ReturnsNull_WhenAssetAbsent()
    {
        Assert.That(AO2ViewportAssetResolver.ResolveEvidencePresentationImage(leftSide: true), Is.Null);
        Assert.That(AO2ViewportAssetResolver.ResolveEvidencePresentationImage(leftSide: false), Is.Null);
    }
}
