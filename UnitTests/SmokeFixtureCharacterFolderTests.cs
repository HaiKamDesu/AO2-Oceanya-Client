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

        List<string> names = CharacterFolder.FullList.Select(character => character.Name).OrderBy(name => name).ToList();

        Assert.That(names, Does.Contain("SmokePhoenix"));
        Assert.That(names, Does.Contain("SmokeEdgeworth"));
    }
}
