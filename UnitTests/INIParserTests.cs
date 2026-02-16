using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AOBot_Testing.Structures;
using NUnit.Framework;

namespace UnitTests;

[TestFixture]
public class INIParserTests
{
    private string? tempRoot;
    private List<string>? originalBaseFolders;

    [SetUp]
    public void SetUp()
    {
        originalBaseFolders = new List<string>(Globals.BaseFolders);
        tempRoot = Path.Combine(Path.GetTempPath(), $"ini_parser_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        CreateCharacter(tempRoot, "Franziska", "pro");
        CreateCharacter(tempRoot, "Phoenix", "def");

        Globals.BaseFolders = new List<string> { tempRoot };
        ResetCharacterCache();
        CharacterFolder.RefreshCharacterList();
    }

    [TearDown]
    public void TearDown()
    {
        Globals.BaseFolders = originalBaseFolders ?? new List<string>();
        ResetCharacterCache();

        if (tempRoot != null && Directory.Exists(tempRoot))
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
                // Ignore cleanup errors in tests.
            }
        }
    }

    [Test]
    public void CharacterIniLoading_LoadsCharactersFromConfiguredBaseFolder()
    {
        Assert.That(CharacterFolder.FullList, Has.Count.EqualTo(2));
        Assert.That(CharacterFolder.FullList.Exists(c => c.Name == "Franziska"), Is.True);
    }

    [Test]
    public void CharacterConfig_ParsesEmotionsAndOptions()
    {
        CharacterFolder character = CharacterFolder.FullList.Find(c => c.Name == "Franziska")!;

        Assert.Multiple(() =>
        {
            Assert.That(character.configINI.ShowName, Is.EqualTo("Franziska"));
            Assert.That(character.configINI.Side, Is.EqualTo("pro"));
            Assert.That(character.configINI.Emotions, Has.Count.EqualTo(2));
            Assert.That(character.configINI.Emotions[1].Name, Is.EqualTo("normal"));
            Assert.That(character.configINI.Emotions[2].Name, Is.EqualTo("smirk"));
        });
    }

    [Test]
    public void CharacterUpdate_ReloadsDataWithoutChangingIdentity()
    {
        CharacterFolder character = CharacterFolder.FullList.Find(c => c.Name == "Phoenix")!;
        string originalPath = character.PathToConfigIni;

        character.Update(character.PathToConfigIni, true);

        Assert.Multiple(() =>
        {
            Assert.That(character.Name, Is.EqualTo("Phoenix"));
            Assert.That(character.PathToConfigIni, Is.EqualTo(originalPath));
            Assert.That(character.configINI.Emotions[1].DisplayID, Does.Contain("normal"));
        });
    }

    private static void CreateCharacter(string basePath, string name, string side)
    {
        string charDir = Path.Combine(basePath, "characters", name);
        Directory.CreateDirectory(charDir);

        string iniPath = Path.Combine(charDir, "char.ini");
        string ini = "[Options]\n" +
                     $"showname={name}\n" +
                     "gender=unknown\n" +
                     $"side={side}\n" +
                     "[Time]\n" +
                     "preanim=0\n" +
                     "[Emotions]\n" +
                     "number=2\n" +
                     "1=normal#normal#normal#0#99\n" +
                     "2=smirk#smirk_pre#smirk#1#1\n" +
                     "[SoundN]\n" +
                     "1=1\n" +
                     "2=objection\n" +
                     "[SoundT]\n" +
                     "1=0\n" +
                     "2=5\n";

        File.WriteAllText(iniPath, ini);
    }

    private static void ResetCharacterCache()
    {
        Type type = typeof(CharacterFolder);
        FieldInfo? configsField = type.GetField("characterConfigs", BindingFlags.NonPublic | BindingFlags.Static);
        FieldInfo? cacheFileField = type.GetField("cacheFile", BindingFlags.NonPublic | BindingFlags.Static);

        configsField?.SetValue(null, new List<CharacterFolder>());

        string? cacheFile = cacheFileField?.GetValue(null) as string;
        if (!string.IsNullOrWhiteSpace(cacheFile) && File.Exists(cacheFile))
        {
            try
            {
                File.Delete(cacheFile);
            }
            catch
            {
                // Ignore cleanup errors in tests.
            }
        }
    }
}
