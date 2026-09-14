using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;

namespace UnitTests;

[TestFixture]
public sealed class SmokeFixtureCharacterFolderTests
{
    [Test]
    public void SmokeFixture_CharacterFolderRefresh_LoadsSmokeEdgeworthAndSmokePhoenix()
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        string configIniPath = Path.Combine(repositoryRoot, "UnitTests", "TestAssets", "FlaUISmoke", "config.ini");

        Globals.UpdateConfigINI(configIniPath);
        CharacterFolder.RefreshCharacterList();

        List<string> names = CharacterFolder.Index.Select(character => character.Name).OrderBy(name => name).ToList();

        Assert.That(names, Does.Contain("SmokePhoenix"));
        Assert.That(names, Does.Contain("SmokeEdgeworth"));
    }

    /// <summary>
    /// Index hands its backing list to callers that enumerate it outside the cache lock (character grids,
    /// viewport asset resolution, the refresh scan). An upsert that mutated that instance in place threw
    /// "Collection was modified" inside whichever reader was mid-enumeration - reproducible by downloading
    /// a streamed character and then running a refresh. Upserts must publish a new list.
    /// </summary>
    [Test]
    public void CharacterIndexUpsert_DoesNotMutateThePreviouslyHandedOutList()
    {
        Globals.UpdateConfigINI(ResolveSmokeConfigIniPath());
        CharacterFolder.RefreshCharacterList();

        IReadOnlyList<CharacterIndexEntry> handedOutIndex = CharacterFolder.Index;
        int countBeforeUpsert = handedOutIndex.Count;
        string? smokePhoenixDirectory = handedOutIndex
            .FirstOrDefault(character => string.Equals(character.Name, "SmokePhoenix", StringComparison.OrdinalIgnoreCase))
            ?.DirectoryPath;
        Assert.That(smokePhoenixDirectory, Is.Not.Null.And.Not.Empty);

        bool upserted = CharacterFolder.TryUpsertCharacterFolderInCache(
            smokePhoenixDirectory!,
            previousCharacterDirectoryPath: null,
            out CharacterFolder? _,
            out string upsertError);

        Assert.Multiple(() =>
        {
            Assert.That(upserted, Is.True, upsertError);
            Assert.That(handedOutIndex, Has.Count.EqualTo(countBeforeUpsert), "the previously handed-out index was mutated in place");
            Assert.That(CharacterFolder.Index, Is.Not.SameAs(handedOutIndex), "the upsert did not publish a new index");
            Assert.That(
                CharacterFolder.Index.Select(character => character.Name),
                Does.Contain("SmokePhoenix"));
        });
    }

    /// <summary>Removal is copy-on-write for the same reason as the upsert path.</summary>
    [Test]
    public void CharacterIndexRemoval_DoesNotMutateThePreviouslyHandedOutList()
    {
        Globals.UpdateConfigINI(ResolveSmokeConfigIniPath());
        CharacterFolder.RefreshCharacterList();

        IReadOnlyList<CharacterIndexEntry> handedOutIndex = CharacterFolder.Index;
        int countBeforeRemoval = handedOutIndex.Count;

        try
        {
            bool removeSucceeded = CharacterFolder.TryRemoveCharacterFolderFromCache(
                targetCharacterDirectoryPath: null,
                characterName: "SmokePhoenix",
                out bool removedAny,
                out string removeError);

            Assert.Multiple(() =>
            {
                Assert.That(removeSucceeded, Is.True, removeError);
                Assert.That(removedAny, Is.True);
                Assert.That(handedOutIndex, Has.Count.EqualTo(countBeforeRemoval), "the previously handed-out index was mutated in place");
                Assert.That(
                    CharacterFolder.Index.Select(character => character.Name),
                    Does.Not.Contain("SmokePhoenix"));
            });
        }
        finally
        {
            CharacterFolder.RefreshCharacterList();
        }
    }

    private static string ResolveSmokeConfigIniPath()
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        return Path.Combine(repositoryRoot, "UnitTests", "TestAssets", "FlaUISmoke", "config.ini");
    }
}
