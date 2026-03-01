using System;
using System.IO;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests;

[TestFixture]
public sealed class AO2BlipPreviewRuntimeTests
{
    [Test]
    public void NativeBassLibraries_ArePresentInOceanyaClientOutput()
    {
        string clientOutputDirectory = ResolveClientOutputDirectory();
        string bassPath = Path.Combine(clientOutputDirectory, "bass.dll");
        string bassOpusPath = Path.Combine(clientOutputDirectory, "bassopus.dll");

        Assert.That(File.Exists(bassPath), Is.True, "Missing bass.dll in client output.");
        Assert.That(File.Exists(bassOpusPath), Is.True, "Missing bassopus.dll in client output.");
        Assert.That(ReadMachineType(bassPath), Is.EqualTo(0x8664), "bass.dll must be x64 (Machine=0x8664).");
        Assert.That(ReadMachineType(bassOpusPath), Is.EqualTo(0x8664), "bassopus.dll must be x64 (Machine=0x8664).");
    }

    [Test]
    public void ManagedBassOpusAssembly_IsNotShipped()
    {
        string clientOutputDirectory = ResolveClientOutputDirectory();
        string managedBassOpusPath = Path.Combine(clientOutputDirectory, "ManagedBass.Opus.dll");

        Assert.That(File.Exists(managedBassOpusPath), Is.False, "ManagedBass.Opus.dll should not be shipped.");
    }

    [Test]
    public void TrySetBlip_InvalidPath_DoesNotThrow()
    {
        using AO2BlipPreviewPlayer player = new AO2BlipPreviewPlayer();
        Assert.DoesNotThrow(() =>
        {
            bool result = player.TrySetBlip("Z:/this/path/does/not/exist/fake_blip.opus");
            Assert.That(result, Is.False);
        });
    }

    private static string ResolveClientOutputDirectory()
    {
        string directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            string? parent = Directory.GetParent(directory)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                break;
            }

            directory = parent;
            string solutionPath = Path.Combine(directory, "AOBot-Testing.sln");
            if (File.Exists(solutionPath))
            {
                string rootPath = Path.Combine(directory, "OceanyaClient", "bin", "Debug", "net8.0-windows");
                string ridPath = Path.Combine(rootPath, "win-x64");
                return Directory.Exists(ridPath) ? ridPath : rootPath;
            }
        }

        string fallbackRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OceanyaClient", "bin", "Debug", "net8.0-windows");
        string fallbackRid = Path.Combine(fallbackRoot, "win-x64");
        return Directory.Exists(fallbackRid) ? fallbackRid : fallbackRoot;
    }

    private static ushort ReadMachineType(string pePath)
    {
        using FileStream stream = File.OpenRead(pePath);
        using BinaryReader reader = new BinaryReader(stream);
        stream.Seek(0x3C, SeekOrigin.Begin);
        int peHeaderOffset = reader.ReadInt32();
        stream.Seek(peHeaderOffset + 4, SeekOrigin.Begin);
        return reader.ReadUInt16();
    }
}
