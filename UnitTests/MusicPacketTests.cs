using AOBot_Testing.Agents;
using Common;
using NUnit.Framework;
using OceanyaClient.Features.Viewport;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace UnitTests;

/// <summary>
/// Validates MC# music packet handling in AOClient.
/// Covers OnMusicChanged and OnIcActionReceived event contracts used by the viewport
/// and the MainWindow music footer.
/// </summary>
[TestFixture]
[Category("NoNetworkCall")]
public class MusicPacketTests
{
    [Test]
    public async Task HandleMessage_McPacket_Song_FiresOnMusicChanged()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedSong = null;
        bool eventFired = false;
        client.OnMusicChanged += (_, song, _, _, _) => { capturedSong = song; eventFired = true; };

        await client.HandleMessage("MC#pwr/trial.mp3#0#showname#0#0#0#%");

        Assert.Multiple(() =>
        {
            Assert.That(eventFired, Is.True);
            Assert.That(capturedSong, Is.EqualTo("pwr/trial.mp3"));
        });
    }

    [Test]
    public void ResolveMusicPath_MissingLocalServerMusic_StreamsFromServerAssetUrl()
    {
        List<string>? originalBaseFolders = Globals.BaseFolders;
        string tempRoot = Path.Combine(Path.GetTempPath(), "OceanyaMusicAssetUrlTest_" + Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "sounds", "music"));
            Globals.BaseFolders = new List<string> { tempRoot };

            string? resolved = AO2ViewportAudioResolver.ResolveMusicPath(
                "pwr/trial.mp3",
                "https://assets.example.test/base");

            Assert.That(resolved, Is.EqualTo("https://assets.example.test/base/sounds/music/pwr/trial.mp3"));
        }
        finally
        {
            Globals.BaseFolders = originalBaseFolders;
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void ResolveMusicPath_LocalMusicWinsOverServerAssetUrl()
    {
        List<string>? originalBaseFolders = Globals.BaseFolders;
        string tempRoot = Path.Combine(Path.GetTempPath(), "OceanyaMusicLocalWinsTest_" + Path.GetRandomFileName());
        string localMusic = Path.Combine(tempRoot, "sounds", "music", "pwr", "trial.mp3");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localMusic)!);
            File.WriteAllBytes(localMusic, new byte[] { 0 });
            Globals.BaseFolders = new List<string> { tempRoot };

            string? resolved = AO2ViewportAudioResolver.ResolveMusicPath(
                "pwr/trial.mp3",
                "https://assets.example.test/base/");

            Assert.That(resolved, Is.EqualTo(localMusic));
        }
        finally
        {
            Globals.BaseFolders = originalBaseFolders;
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void NetworkPacketLog_LabelsMusicPacketAndKeepsRawPacket()
    {
        List<CustomConsole.LogEntry> originalEntries = CustomConsole.GetLogEntriesSnapshot();
        try
        {
            CustomConsole.logEntries.Clear();
            MethodInfo? logMethod = typeof(AOClient).GetMethod(
                "LogNetworkPacket",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(logMethod, Is.Not.Null);
            logMethod!.Invoke(null, new object[] { "IN", "MC#pwr/trial.mp3#0#showname#0#0#0#%" });

            List<CustomConsole.LogEntry> entries = CustomConsole.GetLogEntriesSnapshot();
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(entries[0].Category, Is.EqualTo(CustomConsole.LogCategory.Network));
                Assert.That(entries[0].Text, Does.Contain("Packet IN [MC/Music/Area]"));
                Assert.That(entries[0].Text, Does.Contain("MC#pwr/trial.mp3#0#showname#0#0#0#%"));
            });
        }
        finally
        {
            CustomConsole.logEntries.Clear();
            CustomConsole.logEntries.AddRange(originalEntries);
        }
    }

    [Test]
    public async Task HandleMessage_McPacket_Song_FiresOnIcActionReceived_HasPlayedSong()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedMessage = null;
        client.OnIcActionReceived += (_, msg, _, _) => { capturedMessage = msg; };

        // Non-empty showname triggers the action message.
        await client.HandleMessage("MC#pwr/trial.mp3#0#Franziska#0#0#0#%");

        // Song filename without extension ("trial") appears in the action message.
        Assert.That(capturedMessage, Does.Contain("trial"));
    }

    [Test]
    public async Task HandleMessage_McPacket_TildeStopMp3_FiresOnMusicChanged_WithNullSong()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool nullSongReceived = false;
        client.OnMusicChanged += (_, song, _, _, _) => { nullSongReceived = song == null; };

        await client.HandleMessage("MC#~stop.mp3#0#Franziska#0#0#0#%");

        Assert.That(nullSongReceived, Is.True);
    }

    [Test]
    public async Task HandleMessage_McPacket_TildeStopMp3_FiresOnIcActionReceived_HasStoppedMusic()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedMessage = null;
        client.OnIcActionReceived += (_, msg, _, _) => { capturedMessage = msg; };

        await client.HandleMessage("MC#~stop.mp3#0#Franziska#0#0#0#%");

        Assert.That(capturedMessage, Does.Contain("stopped"));
    }

    [Test]
    public async Task HandleMessage_McPacket_ServerInitiated_NegativeCharId_FiresOnMusicChangedWithEmptyDisplayName()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedDisplayName = "not-set";
        client.OnMusicChanged += (displayName, _, _, _, _) => { capturedDisplayName = displayName; };

        // charId=-1 with empty showname is the server-initiated area-sync pattern.
        await client.HandleMessage("MC#pwr/trial.mp3#-1##0#0#0#%");

        Assert.That(capturedDisplayName, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task HandleMessage_McPacket_ServerInitiated_DoesNotFireOnIcActionReceived()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool actionFired = false;
        client.OnIcActionReceived += (_, _, _, _) => { actionFired = true; };

        // Empty display name → no IcAction per AOClient contract.
        await client.HandleMessage("MC#pwr/trial.mp3#-1##0#0#0#%");

        Assert.That(actionFired, Is.False);
    }

    [Test]
    public async Task HandleMessage_McPacket_LoopEnabledField_ParsedCorrectly()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool? capturedLoop = null;
        client.OnMusicChanged += (_, _, loopEnabled, _, _) => { capturedLoop = loopEnabled; };

        // fields[4] = "1" → loop enabled
        await client.HandleMessage("MC#pwr/trial.mp3#0#showname#1#0#0#%");

        Assert.That(capturedLoop, Is.True);
    }

    [Test]
    public async Task HandleMessage_McPacket_UrlWithEscapedAmpersands_ParsesLoopAndEffects()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        string? capturedSong = null;
        bool? capturedLoop = null;
        int capturedEffects = -1;
        client.OnMusicChanged += (_, song, loopEnabled, _, effectFlags) =>
        {
            capturedSong = song;
            capturedLoop = loopEnabled;
            capturedEffects = effectFlags;
        };

        await client.HandleMessage("MC#https://cdn.discordapp.com/attachments/test/1UPSfx.mp3?ex=1<and>is=2<and>hm=3<and>#12##1#0#2#%");

        Assert.Multiple(() =>
        {
            Assert.That(capturedSong, Is.EqualTo("https://cdn.discordapp.com/attachments/test/1UPSfx.mp3?ex=1&is=2&hm=3&"));
            Assert.That(capturedLoop, Is.True);
            Assert.That(capturedEffects, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleMessage_McPacket_ChannelField_ParsedCorrectly()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        int capturedChannel = -1;
        client.OnMusicChanged += (_, _, _, channel, _) => { capturedChannel = channel; };

        // fields[5] = "2" → channel 2
        await client.HandleMessage("MC#pwr/trial.mp3#0#showname#0#2#0#%");

        Assert.That(capturedChannel, Is.EqualTo(2));
    }

    [Test]
    public async Task HandleMessage_McPacket_EffectFlagsField_ParsedCorrectly()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        int capturedEffects = -1;
        client.OnMusicChanged += (_, _, _, _, effectFlags) => { capturedEffects = effectFlags; };

        // fields[6] = "3" → FADE_IN | FADE_OUT (bitmask 3)
        await client.HandleMessage("MC#pwr/trial.mp3#0#showname#0#0#3#%");

        Assert.That(capturedEffects, Is.EqualTo(3));
    }

    [Test]
    public async Task HandleMessage_McPacket_TooFewFields_DoesNotFireOnMusicChanged()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool eventFired = false;
        client.OnMusicChanged += (_, _, _, _, _) => { eventFired = true; };

        // MC + only 1 data field — fields.Length < 3
        await client.HandleMessage("MC#%");

        Assert.That(eventFired, Is.False);
    }

    [Test]
    public async Task HandleMessage_McPacket_NonNumericCharId_DoesNotFireOnMusicChanged()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool eventFired = false;
        client.OnMusicChanged += (_, _, _, _, _) => { eventFired = true; };

        // fields[2] = "notanumber" → int.TryParse fails → early return
        await client.HandleMessage("MC#pwr/trial.mp3#notanumber#%");

        Assert.That(eventFired, Is.False);
    }

    [Test]
    public async Task HandleMessage_McPacket_LoopNotEnabled_WhenLoopFieldIsZero()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool? capturedLoop = null;
        client.OnMusicChanged += (_, _, loopEnabled, _, _) => { capturedLoop = loopEnabled; };

        await client.HandleMessage("MC#pwr/trial.mp3#0#showname#0#0#0#%");

        Assert.That(capturedLoop, Is.False);
    }

    [Test]
    public async Task HandleMessage_McPacket_LoopDisabled_WhenLoopFieldAbsent()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool? capturedLoop = null;
        client.OnMusicChanged += (_, _, loopEnabled, _, _) => { capturedLoop = loopEnabled; };

        // AO2 parity: minimal packets from outdated servers do not loop.
        await client.HandleMessage("MC#pwr/trial.mp3#0#%");

        Assert.That(capturedLoop, Is.False);
    }

    [Test]
    public async Task HandleMessage_McPacket_LoopDisabled_WhenLoopFieldIsLegacyLength()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool? capturedLoop = null;
        client.OnMusicChanged += (_, _, loopEnabled, _, _) => { capturedLoop = loopEnabled; };

        // AO2 only treats literal "1" as client-side loop.
        await client.HandleMessage("MC#pwr/trial.mp3#0#showname#345#0#0#%");

        Assert.That(capturedLoop, Is.False);
    }
}
