using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using OceanyaClient.Features.Viewport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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
public class AO2ViewportParityAssetResolverTests
{
    private string tempDir = string.Empty;
    private List<string> originalBaseFolders = new List<string>();

    [SetUp]
    public void SetUp()
    {
        originalBaseFolders = new List<string>(Globals.BaseFolders);
        tempDir = Path.Combine(Path.GetTempPath(), "vp_resolver_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Globals.BaseFolders = new List<string> { tempDir };
        Background.RefreshCache();
    }

    [TearDown]
    public void TearDown()
    {
        Globals.BaseFolders = originalBaseFolders;
        Background.RefreshCache();
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
