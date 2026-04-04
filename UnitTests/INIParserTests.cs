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
    public void CharacterConfig_BlankEmotionNumber_InferCountFromHighestEmotionEntry()
    {
        string inferredCharacterDir = Path.Combine(tempRoot!, "characters", "InferredEmotions");
        Directory.CreateDirectory(inferredCharacterDir);
        File.WriteAllText(
            Path.Combine(inferredCharacterDir, "char.ini"),
            "[Options]\nshowname=InferredEmotions\ngender=unknown\nside=def\n" +
            "[Emotions]\nnumber=\n1=normal#-#normal#0#99\n2=smirk#-#smirk#0#99\n");

        ResetCharacterCache();
        CharacterFolder.RefreshCharacterList();

        CharacterFolder character = CharacterFolder.FullList.Find(c => c.Name == "InferredEmotions")!;
        Assert.Multiple(() =>
        {
            Assert.That(character.configINI.EmotionsCount, Is.EqualTo(2));
            Assert.That(character.configINI.Emotions, Has.Count.EqualTo(2));
            Assert.That(character.configINI.Emotions[1].Name, Is.EqualTo("normal"));
            Assert.That(character.configINI.Emotions[2].Name, Is.EqualTo("smirk"));
        });
    }

    [Test]
    public void CharacterConfig_BlankEmotionNumber_DoesNotUseSoundSectionsForInference()
    {
        string inferredCharacterDir = Path.Combine(tempRoot!, "characters", "InferredFromEmotionsOnly");
        Directory.CreateDirectory(inferredCharacterDir);
        File.WriteAllText(
            Path.Combine(inferredCharacterDir, "char.ini"),
            "[Options]\nshowname=InferredFromEmotionsOnly\ngender=unknown\nside=def\n" +
            "[Emotions]\nnumber=\n1=normal#-#normal#0#99\n2=smirk#-#smirk#0#99\n" +
            "[SoundT]\n99=0\n");

        ResetCharacterCache();
        CharacterFolder.RefreshCharacterList();

        CharacterFolder character = CharacterFolder.FullList.Find(c => c.Name == "InferredFromEmotionsOnly")!;
        Assert.Multiple(() =>
        {
            Assert.That(character.configINI.EmotionsCount, Is.EqualTo(2));
            Assert.That(character.configINI.Emotions.ContainsKey(99), Is.False);
            Assert.That(character.configINI.Emotions, Has.Count.EqualTo(2));
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

    [Test]
    public void CharacterIcon_FallsBackToFirstEmoteIcon_WhenCharIconMissing()
    {
        string fallbackCharacterDir = Path.Combine(tempRoot!, "characters", "FallbackIcon");
        Directory.CreateDirectory(Path.Combine(fallbackCharacterDir, "Emotions"));
        File.WriteAllText(Path.Combine(fallbackCharacterDir, "char.ini"),
            "[Options]\nshowname=FallbackIcon\ngender=unknown\nside=def\n[Emotions]\nnumber=2\n1=normal#normal#normal#0#99\n2=smirk#smirk#smirk#0#99\n");
        File.WriteAllBytes(Path.Combine(fallbackCharacterDir, "Emotions", "button1_off.png"), new byte[] { 1, 2, 3, 4 });

        ResetCharacterCache();
        CharacterFolder.RefreshCharacterList();

        CharacterFolder character = CharacterFolder.FullList.Find(c => c.Name == "FallbackIcon")!;
        string expectedPath = Path.Combine(fallbackCharacterDir, "Emotions", "button1_off.png");
        Assert.That(character.CharIconPath, Is.EqualTo(expectedPath));
    }

    [Test]
    public void RefreshCharacterList_SkipsBrokenCharacterFolder_AndKeepsValidOnes()
    {
        string validCharacterDir = Path.Combine(tempRoot!, "characters", "GoodFolder");
        Directory.CreateDirectory(validCharacterDir);
        File.WriteAllText(Path.Combine(validCharacterDir, "char.ini"),
            "[Options]\nshowname=GoodFolder\ngender=unknown\nside=pro\n[Emotions]\nnumber=1\n1=normal#normal#normal#0#99\n");

        string brokenCharacterDir = Path.Combine(tempRoot!, "characters", "BrokenFolder");
        Directory.CreateDirectory(brokenCharacterDir);
        string brokenIniPath = Path.Combine(brokenCharacterDir, "char.ini");
        File.WriteAllText(brokenIniPath,
            "[Options]\nshowname=BrokenFolder\ngender=unknown\nside=pro\n[Emotions]\nnumber=1\n1=normal#normal#normal#0#99\n");

        using FileStream lockStream = new FileStream(
            brokenIniPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        ResetCharacterCache();
        CharacterFolder.RefreshCharacterList();

        Assert.That(CharacterFolder.FullList.Exists(c => c.Name == "GoodFolder"), Is.True);
        Assert.That(CharacterFolder.FullList.Exists(c => c.Name == "BrokenFolder"), Is.False);
    }

    [Test]
    public void RefreshCharacterList_PrefersFirstMountedFolderForDuplicateName()
    {
        string overrideRoot = Path.Combine(Path.GetTempPath(), $"ini_parser_override_{Guid.NewGuid():N}");
        Directory.CreateDirectory(overrideRoot);

        try
        {
            CreateCharacter(tempRoot!, "OrderCheck", "def", "BaseVersion");
            CreateCharacter(overrideRoot, "OrderCheck", "pro", "OverrideVersion");

            Globals.BaseFolders = new List<string> { tempRoot!, overrideRoot };
            ResetCharacterCache();
            CharacterFolder.RefreshCharacterList();

            CharacterFolder character = CharacterFolder.FullList.Find(c => c.Name == "OrderCheck")!;
            Assert.Multiple(() =>
            {
                Assert.That(character, Is.Not.Null);
                Assert.That(character.configINI.ShowName, Is.EqualTo("BaseVersion"));
                Assert.That(character.DirectoryPath, Is.EqualTo(Path.Combine(tempRoot!, "characters", "OrderCheck")));
            });
        }
        finally
        {
            try
            {
                if (Directory.Exists(overrideRoot))
                {
                    Directory.Delete(overrideRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests.
            }
        }
    }

    [Test]
    public void CharacterConfig_DuplicateKeysUseLatestValueLikeAo2QSettings()
    {
        string characterDir = Path.Combine(tempRoot!, "characters", "DuplicateKeys");
        Directory.CreateDirectory(characterDir);
        File.WriteAllText(
            Path.Combine(characterDir, "char.ini"),
            "[Options]\n" +
            "showname=Old Name\n" +
            "showname=New Name\n" +
            "side=wit\n" +
            "side=pro\n" +
            "[Emotions]\n" +
            "number=1\n" +
            "1=normal#-#old_anim#0#99\n" +
            "1=normal#-#new_anim#1#1\n" +
            "[SoundN]\n" +
            "1=old_sfx\n" +
            "1=new_sfx\n" +
            "[SoundT]\n" +
            "1=2\n" +
            "1=5\n" +
            "[SoundL]\n" +
            "1=0\n" +
            "1=1\n");

        ResetCharacterCache();
        CharacterFolder.RefreshCharacterList();

        CharacterFolder character = CharacterFolder.FullList.Find(c => c.Name == "DuplicateKeys")!;
        Assert.Multiple(() =>
        {
            Assert.That(character.configINI.ShowName, Is.EqualTo("New Name"));
            Assert.That(character.configINI.Side, Is.EqualTo("pro"));
            Assert.That(character.configINI.Emotions[1].Animation, Is.EqualTo("new_anim"));
            Assert.That(character.configINI.Emotions[1].Modifier, Is.EqualTo(ICMessage.EmoteModifiers.PlayPreanimation));
            Assert.That(character.configINI.Emotions[1].DeskMod, Is.EqualTo(ICMessage.DeskMods.Shown));
            Assert.That(character.configINI.Emotions[1].sfxName, Is.EqualTo("new_sfx"));
            Assert.That(character.configINI.Emotions[1].sfxDelay, Is.EqualTo(5));
            Assert.That(character.configINI.Emotions[1].sfxLooping, Is.EqualTo("1"));
        });
    }

    [Test]
    public void GetBaseFolders_UsesLastConfiguredMountPathAsHighestPriorityLikeAo2()
    {
        string baseRoot = Path.Combine(Path.GetTempPath(), $"ini_parser_mount_base_{Guid.NewGuid():N}");
        string firstMount = Path.Combine(Path.GetTempPath(), $"ini_parser_mount_first_{Guid.NewGuid():N}");
        string secondMount = Path.Combine(Path.GetTempPath(), $"ini_parser_mount_second_{Guid.NewGuid():N}");

        Directory.CreateDirectory(baseRoot);
        Directory.CreateDirectory(firstMount);
        Directory.CreateDirectory(secondMount);

        string configPath = Path.Combine(baseRoot, "config.ini");
        File.WriteAllText(configPath, "mount_paths=" + firstMount + "," + secondMount + "\n");

        try
        {
            List<string> result = Globals.GetBaseFolders(configPath);
            Assert.That(result, Is.EqualTo(new[] { secondMount, firstMount, baseRoot }));
        }
        finally
        {
            TryDeleteDirectory(baseRoot);
            TryDeleteDirectory(firstMount);
            TryDeleteDirectory(secondMount);
        }
    }

    private static void CreateCharacter(string basePath, string name, string side, string? showName = null)
    {
        string charDir = Path.Combine(basePath, "characters", name);
        Directory.CreateDirectory(charDir);

        string iniPath = Path.Combine(charDir, "char.ini");
        string ini = "[Options]\n" +
                     $"showname={showName ?? name}\n" +
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests.
        }
    }
}
