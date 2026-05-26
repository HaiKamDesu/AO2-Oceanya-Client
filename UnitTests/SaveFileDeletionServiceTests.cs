using NUnit.Framework;
using OceanyaClient.Features.Startup;
using System;
using System.IO;

namespace UnitTests;

[TestFixture]
[Category("NoNetworkCall")]
public class SaveFileDeletionServiceTests
{
    [Test]
    public void ResolveSaveDirectory_AllowsOceanyaClientAppDataFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "OceanyaClient", "savefile.json");

        string directory = SaveFileDeletionService.ResolveSaveDirectory(path);

        Assert.That(Path.GetFileName(directory), Is.EqualTo("OceanyaClient"));
    }

    [Test]
    public void ResolveSaveDirectory_AllowsOceanyaClientDevAppDataFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "OceanyaClientDev", "savefile.json");

        string directory = SaveFileDeletionService.ResolveSaveDirectory(path);

        Assert.That(Path.GetFileName(directory), Is.EqualTo("OceanyaClientDev"));
    }

    [Test]
    public void ResolveSaveDirectory_BlocksNonSavefilePath()
    {
        string path = Path.Combine(Path.GetTempPath(), "OceanyaClient", "other.json");

        Assert.Throws<InvalidOperationException>(() => SaveFileDeletionService.ResolveSaveDirectory(path));
    }

    [Test]
    public void ResolveSaveDirectory_BlocksNonAppDataFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "SomeOtherFolder", "savefile.json");

        Assert.Throws<InvalidOperationException>(() => SaveFileDeletionService.ResolveSaveDirectory(path));
    }
}
