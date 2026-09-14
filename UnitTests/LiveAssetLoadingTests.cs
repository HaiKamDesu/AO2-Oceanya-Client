using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Assets;

namespace UnitTests;

/// <summary>
/// Covers the real-time asset loading path: change classification for the live watcher, on-demand
/// resolution of characters that appeared after the last scan, and the refresh-completed notification the
/// UI relies on to fold background refreshes in.
/// </summary>
[TestFixture]
public sealed class LiveAssetLoadingTests
{
    private static string ResolveSmokeConfigIniPath()
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        return Path.Combine(repositoryRoot, "UnitTests", "TestAssets", "FlaUISmoke", "config.ini");
    }

    [Test]
    public void ClassifyChange_MapsCharacterFileToItsCharacterFolder()
    {
        string baseFolder = Path.Combine("C:", "AO", "base");
        string changedPath = Path.Combine(baseFolder, "characters", "SmokePhoenix", "(a)normal.png");

        AssetChangeTarget? target = LiveAssetWatcher.ClassifyChange(baseFolder, changedPath);

        Assert.That(target, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(target!.Category, Is.EqualTo(AssetChangeCategory.Character));
            Assert.That(target.EntryName, Is.EqualTo("SmokePhoenix"));
        });
    }

    [Test]
    public void ClassifyChange_MapsBackgroundAndBlipAndMiscChanges()
    {
        string baseFolder = Path.Combine("C:", "AO", "base");

        AssetChangeTarget? background = LiveAssetWatcher.ClassifyChange(
            baseFolder,
            Path.Combine(baseFolder, "background", "gs4", "defenseempty.png"));
        AssetChangeTarget? blip = LiveAssetWatcher.ClassifyChange(
            baseFolder,
            Path.Combine(baseFolder, "sounds", "blips", "male.opus"));
        AssetChangeTarget? misc = LiveAssetWatcher.ClassifyChange(
            baseFolder,
            Path.Combine(baseFolder, "misc", "p5", "effects.ini"));

        Assert.Multiple(() =>
        {
            Assert.That(background?.Category, Is.EqualTo(AssetChangeCategory.Background));
            Assert.That(background?.EntryName, Is.EqualTo("gs4"));
            Assert.That(blip?.Category, Is.EqualTo(AssetChangeCategory.Blips));
            Assert.That(misc?.Category, Is.EqualTo(AssetChangeCategory.Misc));
        });
    }

    /// <summary>
    /// A change directly under <c>characters/</c> cannot name one character, so the whole category has to
    /// be rescanned - that is how a newly extracted folder appearing at the root is picked up.
    /// </summary>
    [Test]
    public void ClassifyChange_CategoryRootChangeRequestsAFullCategoryRefresh()
    {
        string baseFolder = Path.Combine("C:", "AO", "base");

        AssetChangeTarget? target = LiveAssetWatcher.ClassifyChange(
            baseFolder,
            Path.Combine(baseFolder, "characters"));

        Assert.That(target, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(target!.Category, Is.EqualTo(AssetChangeCategory.Character));
            Assert.That(target.EntryName, Is.Empty);
        });
    }

    /// <summary>
    /// The character editor stages and backs up folders beside the real one while applying an edit. Those
    /// must not be classified as characters, or every edit would register a bogus character and then a
    /// deletion of it.
    /// </summary>
    [Test]
    public void ClassifyChange_IgnoresEditorScratchFoldersAndShellJunk()
    {
        string baseFolder = Path.Combine("C:", "AO", "base");

        Assert.Multiple(() =>
        {
            Assert.That(
                LiveAssetWatcher.ClassifyChange(
                    baseFolder,
                    Path.Combine(baseFolder, ".oceanya_character_edit_staging_abc123", "characters", "Phoenix", "char.ini")),
                Is.Null);
            Assert.That(
                LiveAssetWatcher.ClassifyChange(
                    baseFolder,
                    Path.Combine(baseFolder, "characters", "Phoenix.oceanya_edit_backup_20260101000000", "char.ini")),
                Is.Null);
            Assert.That(
                LiveAssetWatcher.ClassifyChange(
                    baseFolder,
                    Path.Combine(baseFolder, "characters", "Phoenix", "desktop.ini")),
                Is.Null);
        });
    }

    [Test]
    public void ClassifyChange_IgnoresPathsOutsideTheWatchedCategories()
    {
        string baseFolder = Path.Combine("C:", "AO", "base");

        Assert.Multiple(() =>
        {
            Assert.That(
                LiveAssetWatcher.ClassifyChange(baseFolder, Path.Combine(baseFolder, "themes", "default", "courtroom_design.ini")),
                Is.Null);
            Assert.That(
                LiveAssetWatcher.ClassifyChange(baseFolder, Path.Combine(baseFolder, "sounds", "general", "sfx-realization.opus")),
                Is.Null);
        });
    }

    /// <summary>
    /// The cache is built from a scan, so a character added afterwards is invisible to every lookup. The
    /// on-demand probe is what lets the very next message render it instead of requiring a manual refresh.
    /// </summary>
    [Test]
    public void ResolveOnDemand_FindsACharacterThatIsMissingFromTheCache()
    {
        Globals.UpdateConfigINI(ResolveSmokeConfigIniPath());
        CharacterFolder.RefreshCharacterList();
        CharacterFolder.ClearOnDemandMissCache();

        try
        {
            Assert.That(
                CharacterFolder.TryRemoveCharacterFolderFromCache(null, "SmokePhoenix", out bool removedAny, out string removeError),
                Is.True,
                removeError);
            Assert.That(removedAny, Is.True);
            Assert.That(
                CharacterFolder.Index.Select(character => character.Name),
                Does.Not.Contain("SmokePhoenix"),
                "precondition: the character must be absent from the index");

            CharacterFolder? resolved = CharacterFolder.ResolveOnDemand("SmokePhoenix");

            Assert.That(resolved, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(resolved!.Name, Is.EqualTo("SmokePhoenix"));
                Assert.That(
                    CharacterFolder.Index.Select(character => character.Name),
                    Does.Contain("SmokePhoenix"),
                    "the on-demand resolve must also add the character to the index");
            });
        }
        finally
        {
            CharacterFolder.RefreshCharacterList();
            CharacterFolder.ClearOnDemandMissCache();
        }
    }

    [Test]
    public void ResolveOnDemand_ReturnsNullForACharacterThatIsNotInstalled()
    {
        Globals.UpdateConfigINI(ResolveSmokeConfigIniPath());
        CharacterFolder.RefreshCharacterList();
        CharacterFolder.ClearOnDemandMissCache();

        Assert.That(CharacterFolder.ResolveOnDemand("ThisCharacterIsNotInstalledAnywhere"), Is.Null);
    }

    /// <summary>
    /// The index must answer name, showname and icon questions WITHOUT parsing char.ini - that is the whole
    /// point of the split. Parsing every character to build a dropdown was 677 ms of deserialize on the
    /// launch path plus a permanently resident object graph.
    /// </summary>
    [Test]
    public void Index_CarriesLookupDataWithoutParsingCharacters()
    {
        Globals.UpdateConfigINI(ResolveSmokeConfigIniPath());
        CharacterFolder.RefreshCharacterList();
        CharacterFolder.ClearParsedCache();

        IReadOnlyList<CharacterIndexEntry> index = CharacterFolder.Index;

        Assert.Multiple(() =>
        {
            Assert.That(index.Select(entry => entry.Name), Does.Contain("SmokePhoenix"));
            Assert.That(
                index.All(entry => !string.IsNullOrWhiteSpace(entry.DirectoryPath)),
                Is.True,
                "every index entry must know where its folder is");
            Assert.That(
                index.All(entry => !string.IsNullOrWhiteSpace(entry.PathToConfigIni)),
                Is.True,
                "every index entry must know where its char.ini is");
            Assert.That(
                CharacterFolder.ParsedCharacterCount,
                Is.Zero,
                "reading the index must not parse any character");
            Assert.That(CharacterFolder.Exists("SmokePhoenix"), Is.True);
            Assert.That(CharacterFolder.ParsedCharacterCount, Is.Zero, "an existence check must not parse either");
        });
    }

    [Test]
    public void GetByName_ParsesOnDemandAndCachesTheResult()
    {
        Globals.UpdateConfigINI(ResolveSmokeConfigIniPath());
        CharacterFolder.RefreshCharacterList();
        CharacterFolder.ClearParsedCache();

        CharacterFolder? first = CharacterFolder.GetByName("SmokePhoenix");
        int parsedAfterFirst = CharacterFolder.ParsedCharacterCount;
        CharacterFolder? second = CharacterFolder.GetByName("SmokePhoenix");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.configINI, Is.Not.Null);
            Assert.That(parsedAfterFirst, Is.EqualTo(1), "exactly the requested character should be parsed");
            Assert.That(second, Is.SameAs(first), "a second lookup must reuse the parsed instance");
            Assert.That(CharacterFolder.ParsedCharacterCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetByName_ReturnsNullForAnUnknownCharacter()
    {
        Globals.UpdateConfigINI(ResolveSmokeConfigIniPath());
        CharacterFolder.RefreshCharacterList();

        Assert.That(CharacterFolder.GetByName("NoSuchCharacterAnywhere"), Is.Null);
    }

    /// <summary>
    /// Background refreshes (startup scan, live watcher, Drive sync) have no UI of their own, so the UI
    /// only learns about them through this event. Without it a character refreshed in the background sat
    /// in the cache until something else happened to rebuild a dropdown.
    /// </summary>
    [Test]
    public async Task RefreshCharactersAndBackgroundsAsync_RaisesAssetsRefreshed()
    {
        Globals.UpdateConfigINI(ResolveSmokeConfigIniPath());

        AssetRefreshCompletedEventArgs? observed = null;
        void Handler(AssetRefreshCompletedEventArgs args) => observed = args;

        ClientAssetRefreshService.AssetsRefreshed += Handler;
        try
        {
            await ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync(null!);
        }
        finally
        {
            ClientAssetRefreshService.AssetsRefreshed -= Handler;
        }

        Assert.That(observed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(observed!.RefreshedAllCharacters, Is.True);
            Assert.That(observed.RefreshedAllBackgrounds, Is.True);
        });
    }
}
