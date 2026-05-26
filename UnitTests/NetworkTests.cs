using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using NUnit.Framework;

namespace UnitTests;

[TestFixture]
[Category("NoNetworkCall")]
public class NetworkTests
{
    [Test]
    public async Task HandleMessage_ParsesScDescriptionsIntoCharacterNames()
    {
        var client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("SC#Phoenix&Defense Attorney#Franziska&Prosecutor#%");

        var parsed = GetServerCharacterList(client);

        Assert.That(parsed.Keys, Is.EquivalentTo(new[] { "Phoenix", "Franziska" }));
    }

    [Test]
    public async Task HandleMessage_UpdatesCharacterAvailabilityFromCharsCheck()
    {
        var client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("SC#Phoenix#Franziska#Miles#%");
        await client.HandleMessage("CharsCheck#0#1#0#%");

        var parsed = GetServerCharacterList(client);

        Assert.Multiple(() =>
        {
            Assert.That(parsed["Phoenix"], Is.True);
            Assert.That(parsed["Franziska"], Is.False);
            Assert.That(parsed["Miles"], Is.True);
        });
    }

    [Test]
    public async Task ServerCharacterAvailability_DeduplicatesCaseVariantServerNames()
    {
        var client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("SC#Phoenix#Beppo#beppo#%");
        await client.HandleMessage("CharsCheck#0#1#0#%");

        IReadOnlyDictionary<string, bool> availability = client.ServerCharacterAvailability;

        Assert.Multiple(() =>
        {
            Assert.That(availability.Keys, Is.EquivalentTo(new[] { "Phoenix", "Beppo" }));
            Assert.That(availability["Beppo"], Is.True);
        });
    }

    [Test]
    public async Task SelectIniPuppet_UsesAvailableCaseVariantWhenFirstMatchTaken()
    {
        var client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("SC#Phoenix#Beppo#beppo#%");
        await client.HandleMessage("CharsCheck#0#1#0#%");

        await client.SelectIniPuppet("Beppo", iniswapToSelected: false);

        Assert.That(client.iniPuppetID, Is.EqualTo(2));
    }

    [Test]
    public async Task SelectFirstAvailableINIPuppet_DoesNotThrow_WhenCharsCheckHasMoreSlotsThanCharacterList()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("SC#Phoenix#Franziska#%");
        await client.HandleMessage("CharsCheck#1#1#0#0#%");

        Assert.DoesNotThrowAsync(async () => await client.SelectFirstAvailableINIPuppet());
    }

    [Test]
    public async Task HandleMessage_RaisesBackgroundAndPositionEvents()
    {
        var client = new AOClient("ws://localhost:10001/");

        string? bg = null;
        string? side = null;

        client.OnBGChange += newBg => bg = newBg;
        client.OnSideChange += newPos => side = newPos;
        string? serverPosition = null;
        client.OnServerPositionReceived += newPos => serverPosition = newPos;

        await client.HandleMessage("BN#Courtroom_bg#%");
        await client.HandleMessage("SP#wit#%");

        Assert.Multiple(() =>
        {
            Assert.That(bg, Is.EqualTo("Courtroom_bg"));
            Assert.That(side, Is.EqualTo("wit"));
            Assert.That(serverPosition, Is.EqualTo("wit"));
        });
    }

    [Test]
    public async Task HandleMessage_AcceptsEmptyServerPositionPacket()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        string? side = null;
        client.OnSideChange += newPos => side = newPos;

        await client.HandleMessage("SP#wit#%");
        await client.HandleMessage("SP##%");

        Assert.Multiple(() =>
        {
            Assert.That(client.curPos, Is.EqualTo(string.Empty));
            Assert.That(side, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void SetCharacter_PreservesDefaultPositionMode_WhenPositionIsEmpty()
    {
        AOClient client = new AOClient("ws://localhost:10001/")
        {
            curPos = string.Empty
        };
        CharacterFolder firstCharacter = CreateCharacterFolder("Franziska", "wit");
        CharacterFolder secondCharacter = CreateCharacterFolder("KamLoremaster", "jud");

        string? side = null;
        client.OnSideChange += newPos => side = newPos;

        client.SetCharacter(firstCharacter);
        client.SetCharacter(secondCharacter);

        Assert.Multiple(() =>
        {
            Assert.That(client.curPos, Is.EqualTo(string.Empty));
            Assert.That(client.currentINI?.configINI.Side, Is.EqualTo("jud"));
            Assert.That(side, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void SetCharacter_PreservesManualPosition_WhenPositionIsNonEmpty()
    {
        AOClient client = new AOClient("ws://localhost:10001/")
        {
            curPos = "cage"
        };
        CharacterFolder character = CreateCharacterFolder("KamLoremaster", "jud");

        client.SetCharacter(character);

        Assert.That(client.curPos, Is.EqualTo("cage"));
    }

    [Test]
    public async Task HandleMessage_FaListInfersDefaultCurrentArea_WhenServerSendsNoExplicitArea()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        string? currentArea = null;
        client.OnCurrentAreaChanged += area => currentArea = area;

        await client.HandleMessage("FA#Lobby#Courtroom#Basement#%");

        Assert.Multiple(() =>
        {
            Assert.That(client.CurrentArea, Is.EqualTo("Lobby"));
            Assert.That(currentArea, Is.EqualTo("Lobby"));
        });
    }

    private static CharacterFolder CreateCharacterFolder(string name, string side)
    {
        return new CharacterFolder
        {
            Name = name,
            configINI = new CharacterConfigINI(string.Empty)
            {
                Name = name,
                Side = side,
                Emotions =
                {
                    [1] = new Emote(1)
                    {
                        Name = "normal",
                        Animation = "normal"
                    }
                }
            }
        };
    }

    [Test]
    public async Task HandleMessage_GetAreaOocUsesAreaHeader_NotPlayerCount()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("FA#Lobby#Courtroom#%");

        await client.HandleMessage(
            "CT#Server#People in this area: 2\n=== Courtroom ===\n[court]: [2 Users][CASING][LOCKED]\n[CM] [5] Franziska#1#%");

        AreaInfo courtroom = client.AvailableAreaInfos.Single(area => area.Name == "Courtroom");
        Assert.Multiple(() =>
        {
            Assert.That(client.CurrentArea, Is.EqualTo("Courtroom"));
            Assert.That(courtroom.Players, Is.EqualTo(2));
            Assert.That(courtroom.Status, Is.EqualTo("CASING"));
            Assert.That(courtroom.LockState, Is.EqualTo("LOCKED"));
        });
    }

    [Test]
    public void ParseGetArea_SupportsSingleLineAsgOutput()
    {
        string output = "$ASG: People in this area: 2 === Private Suite 3 === [PS3]: [2 Users][IDLE][LOCKED] [CM][0] Franziska (Kam) [3] Trucy (Gedge)";

        AOBot_Testing.AO2Parser.GetAreaParseResult result = AOBot_Testing.AO2Parser.ParseGetAreaDetailed(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsGetAreaReport, Is.True);
            Assert.That(result.AreaName, Is.EqualTo("Private Suite 3"));
            Assert.That(result.ParsedPlayers, Is.True);
            Assert.That(result.Players.Select(player => player.ICCharacterName), Is.EqualTo(new[] { "Franziska", "Trucy" }));
            Assert.That(result.Players.Select(player => player.CharacterId), Is.EqualTo(new[] { 0, 3 }));
            Assert.That(result.Players[0].IsCM, Is.True);
            Assert.That(result.Players[0].OOCShowname, Is.EqualTo("Kam"));
            Assert.That(result.Players[0].RawGetAreaLine, Is.EqualTo("[CM][0] Franziska (Kam)"));
            Assert.That(result.Players[1].OOCShowname, Is.EqualTo("Gedge"));
        });
    }

    [Test]
    public void ParseGetArea_SupportsTsuserverCcHeaderOutput()
    {
        string output = "$H: People in this area: 3\n\n=== Lobby ===\n\n[LOB]: [3 Users][IDLE]\n\n[2] EmaSkye\n\n[0] Franny\n\n[1] Franziska";

        AOBot_Testing.AO2Parser.GetAreaParseResult result = AOBot_Testing.AO2Parser.ParseGetAreaDetailed(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsGetAreaReport, Is.True);
            Assert.That(result.AreaName, Is.EqualTo("Lobby"));
            Assert.That(result.ParsedPlayers, Is.True);
            Assert.That(result.Players.Select(player => player.ICCharacterName), Is.EqualTo(new[] { "EmaSkye", "Franny", "Franziska" }));
            Assert.That(result.Players.Select(player => player.CharacterId), Is.EqualTo(new[] { 2, 0, 1 }));
            Assert.That(result.Players[0].RawGetAreaLine, Is.EqualTo("[2] EmaSkye"));
        });
    }

    [Test]
    public void ParseGetArea_SupportsTsuserverCcFlaggedRowsWithoutSpaces()
    {
        string output = "People in this area: 2\r\n=== Lobby ===\r\n[LOB]: [2 Users][IDLE][LOCKED]\r\n[CM][M][AFK][4] Judge (Host)\r\n[Hidden][7] Witness (Watcher) [discord]";

        AOBot_Testing.AO2Parser.GetAreaParseResult result = AOBot_Testing.AO2Parser.ParseGetAreaDetailed(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.ParsedPlayers, Is.True);
            Assert.That(result.Players.Select(player => player.ICCharacterName), Is.EqualTo(new[] { "Judge", "Witness" }));
            Assert.That(result.Players.Select(player => player.CharacterId), Is.EqualTo(new[] { 4, 7 }));
            Assert.That(result.Players[0].IsCM, Is.True);
            Assert.That(result.Players[0].OOCShowname, Is.EqualTo("Host"));
            Assert.That(result.Players[0].RawGetAreaLine, Is.EqualTo("[CM][M][AFK][4] Judge (Host)"));
            Assert.That(result.Players[1].OOCShowname, Is.EqualTo("Watcher"));
        });
    }

    [Test]
    public void ParseGetArea_SupportsTsuserver3LowercaseUsersOutput()
    {
        string output = "People in this area: 2\r\n=== Lobby ===\r\n[LOB]: [2 users][IDLE]\r\n[CM] [4] Judge (Host)\r\n [7] Witness";

        AOBot_Testing.AO2Parser.GetAreaParseResult result = AOBot_Testing.AO2Parser.ParseGetAreaDetailed(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsGetAreaReport, Is.True);
            Assert.That(result.AreaName, Is.EqualTo("Lobby"));
            Assert.That(result.ParsedPlayers, Is.True);
            Assert.That(result.Players.Select(player => player.ICCharacterName), Is.EqualTo(new[] { "Judge", "Witness" }));
            Assert.That(result.Players.Select(player => player.CharacterId), Is.EqualTo(new[] { 4, 7 }));
            Assert.That(result.Players[0].IsCM, Is.True);
            Assert.That(result.Players[0].OOCShowname, Is.EqualTo("Host"));
        });
    }

    [Test]
    public void ParseGetArea_SupportsVidyaClientsInHeaderOutput()
    {
        string output = "$H: = Clients in [8] Lounge (users: 1) [IDLE] = \r\n  :white_medium_small_square: [4] Trucy ";

        AOBot_Testing.AO2Parser.GetAreaParseResult result = AOBot_Testing.AO2Parser.ParseGetAreaDetailed(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsGetAreaReport, Is.True);
            Assert.That(result.AreaName, Is.EqualTo("Lounge"));
            Assert.That(result.ParsedPlayers, Is.True);
            Assert.That(result.Players.Select(player => player.ICCharacterName), Is.EqualTo(new[] { "Trucy" }));
            Assert.That(result.Players.Select(player => player.CharacterId), Is.EqualTo(new[] { 4 }));
            Assert.That(result.Players[0].RawGetAreaLine, Is.EqualTo("[4] Trucy"));
        });
    }

    [Test]
    public void ParseGetArea_SupportsVanillaAreaHeadingOutput()
    {
        string output = "AO Official Server (Vanilla): === Basement ===\r\n\r\n"
            + "[8 users][IDLE]\r\n\r\n"
            + "[6] Spectator\r\n\r\n"
            + "[15] Apollo\r\n\r\n"
            + "[4] Adrian (Taura)\r\n\r\n"
            + "[12] Alita (Baude)\r\n\r\n"
            + "[5] Elise\r\n\r\n"
            + "[2] Atmey\r\n\r\n"
            + "[30] Polly\r\n\r\n"
            + "[21] Franziska";

        AOBot_Testing.AO2Parser.GetAreaParseResult result = AOBot_Testing.AO2Parser.ParseGetAreaDetailed(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsGetAreaReport, Is.True);
            Assert.That(result.AreaName, Is.EqualTo("Basement"));
            Assert.That(result.ParsedPlayers, Is.True);
            Assert.That(result.Players.Select(player => player.ICCharacterName), Is.EqualTo(new[]
            {
                "Spectator",
                "Apollo",
                "Adrian",
                "Alita",
                "Elise",
                "Atmey",
                "Polly",
                "Franziska"
            }));
            Assert.That(result.Players.Select(player => player.CharacterId), Is.EqualTo(new[] { 6, 15, 4, 12, 5, 2, 30, 21 }));
            Assert.That(result.Players[2].OOCShowname, Is.EqualTo("Taura"));
            Assert.That(result.Players[3].OOCShowname, Is.EqualTo("Baude"));
        });
    }

    [Test]
    public async Task HandleMessage_VidyaGetAreaUpdatesAreaAndRoster()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("FA#Lounge#Courtroom#%");

        await client.HandleMessage("CT#Server#$H: = Clients in [8] Lounge (users: 1) [IDLE] = \r\n  :white_medium_small_square: [4] Trucy #1#%");

        AreaInfo lounge = client.AvailableAreaInfos.Single(area => area.Name == "Lounge");
        Assert.Multiple(() =>
        {
            Assert.That(client.CurrentArea, Is.EqualTo("Lounge"));
            Assert.That(lounge.Players, Is.EqualTo(1));
            Assert.That(lounge.Status, Is.EqualTo("IDLE"));
            Assert.That(client.CurrentAreaPlayers.Select(player => player.ICCharacterName), Is.EqualTo(new[] { "Trucy" }));
            Assert.That(client.LastGetAreaParseSucceeded, Is.True);
        });
    }

    [Test]
    public async Task HandleMessage_VanillaGetAreaUpdatesAreaInfoAndRoster()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("FA#Lobby#Basement#%");

        await client.HandleMessage(
            "CT#Server#AO Official Server (Vanilla): === Basement ===\r\n\r\n"
            + "[8 users][IDLE]\r\n\r\n"
            + "[6] Spectator\r\n\r\n"
            + "[15] Apollo\r\n\r\n"
            + "[4] Adrian (Taura)\r\n\r\n"
            + "[12] Alita (Baude)\r\n\r\n"
            + "[5] Elise\r\n\r\n"
            + "[2] Atmey\r\n\r\n"
            + "[30] Polly\r\n\r\n"
            + "[21] Franziska#1#%");

        AreaInfo basement = client.AvailableAreaInfos.Single(area => area.Name == "Basement");
        Assert.Multiple(() =>
        {
            Assert.That(client.CurrentArea, Is.EqualTo("Basement"));
            Assert.That(basement.Players, Is.EqualTo(8));
            Assert.That(basement.Status, Is.EqualTo("IDLE"));
            Assert.That(client.CurrentAreaPlayers.Select(player => player.ICCharacterName), Is.EqualTo(new[]
            {
                "Spectator",
                "Apollo",
                "Adrian",
                "Alita",
                "Elise",
                "Atmey",
                "Polly",
                "Franziska"
            }));
            Assert.That(client.LastGetAreaParseSucceeded, Is.True);
        });
    }

    [Test]
    public async Task HandleMessage_InternalGetAreaRefresh_UpdatesRosterWithoutLoggingOoc()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        bool sawOoc = false;
        IReadOnlyList<Player>? updatedPlayers = null;
        bool? parsedSuccessfully = null;
        client.OnOOCMessageReceived += (_, _, _) => sawOoc = true;
        client.OnCurrentAreaPlayersUpdated += (players, parsed) =>
        {
            updatedPlayers = players;
            parsedSuccessfully = parsed;
        };

        await client.RequestCurrentAreaPlayersRefreshAsync();
        await client.HandleMessage("CT#Server#People in this area: 3 === Private Suite 3 === [PS3]: [3 Users][IDLE][LOCKED] [CM][0] Franziska (Kam) [3] Trucy (Gedge) [7] Phoenix (Wright)#1#%");

        Assert.Multiple(() =>
        {
            Assert.That(sawOoc, Is.False);
            Assert.That(client.LastGetAreaParseSucceeded, Is.True);
            Assert.That(client.CurrentAreaPlayers.Select(player => player.ICCharacterName), Is.EqualTo(new[] { "Franziska", "Trucy", "Phoenix" }));
            Assert.That(parsedSuccessfully, Is.True);
            Assert.That(updatedPlayers, Is.Not.Null);
            Assert.That(updatedPlayers!.Select(player => player.ICCharacterName), Is.EqualTo(new[] { "Franziska", "Trucy", "Phoenix" }));
        });
    }

    [Test]
    public async Task HandleMessage_AreaUpdatePreservesEmptySlots()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("FA#Lobby#Courtroom#Basement#%");

        await client.HandleMessage("ARUP#0#3##7#%");
        await client.HandleMessage("ARUP#2#FREE##Franziska#%");

        Assert.Multiple(() =>
        {
            Assert.That(client.AvailableAreaInfos[0].Players, Is.EqualTo(3));
            Assert.That(client.AvailableAreaInfos[1].Players, Is.EqualTo(-1));
            Assert.That(client.AvailableAreaInfos[2].Players, Is.EqualTo(7));
            Assert.That(client.AvailableAreaInfos[0].CaseManager, Is.EqualTo("FREE"));
            Assert.That(client.AvailableAreaInfos[1].CaseManager, Is.EqualTo("Unknown"));
            Assert.That(client.AvailableAreaInfos[2].CaseManager, Is.EqualTo("Franziska"));
        });
    }

    [Test]
    public async Task HandleMessage_FaRefreshPreservesKnownAreaStatusByName()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("FA#Lobby#Courtroom#Basement#%");
        await client.HandleMessage("ARUP#0#4#2#7#%");
        await client.HandleMessage("ARUP#1#FREE#CASING#RP#%");
        await client.HandleMessage("ARUP#2#FREE#Franziska#FREE#%");
        await client.HandleMessage("ARUP#3#OPEN#LOCKED#OPEN#%");

        await client.HandleMessage("FA#Courtroom#Lobby#New Area#%");

        AreaInfo courtroom = client.AvailableAreaInfos[0];
        AreaInfo lobby = client.AvailableAreaInfos[1];
        AreaInfo newArea = client.AvailableAreaInfos[2];
        Assert.Multiple(() =>
        {
            Assert.That(courtroom.Name, Is.EqualTo("Courtroom"));
            Assert.That(courtroom.Players, Is.EqualTo(2));
            Assert.That(courtroom.Status, Is.EqualTo("CASING"));
            Assert.That(courtroom.CaseManager, Is.EqualTo("Franziska"));
            Assert.That(courtroom.LockState, Is.EqualTo("LOCKED"));

            Assert.That(lobby.Name, Is.EqualTo("Lobby"));
            Assert.That(lobby.Players, Is.EqualTo(4));
            Assert.That(lobby.Status, Is.EqualTo("FREE"));

            Assert.That(newArea.Name, Is.EqualTo("New Area"));
            Assert.That(newArea.Players, Is.EqualTo(-1));
            Assert.That(newArea.Status, Is.EqualTo("Unknown"));
        });
    }

    [Test]
    public async Task HandleMessage_SmSplitsAreasAndMusicLikeAo2Client()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        await client.HandleMessage("SM#Lobby#Courtroom#Investigation#pwr/trial.mp3#calm.ogg#Drama#cornered.opus#%");

        Assert.Multiple(() =>
        {
            Assert.That(client.AvailableAreas, Is.EqualTo(new[] { "Lobby", "Courtroom" }));
            Assert.That(client.AvailableMusic, Is.EqualTo(new[] { "Investigation", "pwr/trial.mp3", "calm.ogg", "Drama", "cornered.opus" }));
        });
    }

    [Test]
    public async Task HandleMessage_FmRefreshesMusicList()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        await client.HandleMessage("FM#Investigation#pwr/trial.mp3#Drama#cornered.opus#%");

        Assert.That(client.AvailableMusic, Is.EqualTo(new[] { "Investigation", "pwr/trial.mp3", "Drama", "cornered.opus" }));
    }

    [Test]
    public async Task HandleMessage_AssStoresServerAssetUrl()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        await client.HandleMessage("ASS#https://assets.example.test/base/#%");

        Assert.That(client.ServerAssetUrl, Is.EqualTo("https://assets.example.test/base/"));
    }

    [Test]
    public async Task HandleMessage_ServerAreaListOocUpdatesAreaInfos()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("FA#Lobby#Courtroom#%");

        await client.HandleMessage(
            "CT#Server#=== Areas ===\r\n" +
            "Area l: Lobby (users: 4) [FREE][FREE]\r\n" +
            "Area c: Courtroom (users: 2) [CASING][CMs: Franziska][LOCKED] [*]#1#%");

        AreaInfo lobby = client.AvailableAreaInfos.Single(area => area.Name == "Lobby");
        AreaInfo courtroom = client.AvailableAreaInfos.Single(area => area.Name == "Courtroom");
        Assert.Multiple(() =>
        {
            Assert.That(client.CurrentArea, Is.EqualTo("Courtroom"));
            Assert.That(lobby.Players, Is.EqualTo(4));
            Assert.That(lobby.Status, Is.EqualTo("FREE"));
            Assert.That(lobby.CaseManager, Is.EqualTo("FREE"));
            Assert.That(lobby.LockState, Is.EqualTo("OPEN"));

            Assert.That(courtroom.Players, Is.EqualTo(2));
            Assert.That(courtroom.Status, Is.EqualTo("CASING"));
            Assert.That(courtroom.CaseManager, Is.EqualTo("Franziska"));
            Assert.That(courtroom.LockState, Is.EqualTo("LOCKED"));
        });
    }

    [Test]
    public async Task HandleMessage_ParsesOocPacketAndDecodesSymbols()
    {
        var client = new AOClient("ws://localhost:10001/");

        string? showname = null;
        string? message = null;
        bool? fromServer = null;

        client.OnOOCMessageReceived += (sn, msg, fs) =>
        {
            showname = sn;
            message = msg;
            fromServer = fs;
        };

        await client.HandleMessage("CT#Test<num>User#hello<and>bye#1#%");

        Assert.Multiple(() =>
        {
            Assert.That(showname, Is.EqualTo("Test#User"));
            Assert.That(message, Is.EqualTo("hello&bye"));
            Assert.That(fromServer, Is.True);
        });
    }

    [Test]
    public async Task HandleMessage_FallsBackToCharacterIniShowname_WhenPacketShownameIsBlank()
    {
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "Franziska"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "Franziska", "char.ini"),
                "[Options]\nshowname=von Karma\nside=pro\ngender=female\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");

            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            AOClient client = new AOClient("ws://localhost:10001/");
            await client.HandleMessage("SC#Franziska#%");

            ICMessage? received = null;
            client.OnICMessageReceived += message => received = message;

            await client.HandleMessage("MS#chat#-#Franziska#normal#Test#wit#1#0#0#0#0#0#0#0###-1###0&0#0#0#0#0###0##0#0#%");

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.ShowName, Is.EqualTo("von Karma"));
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    [Test]
    public async Task HandleMessage_RaisesMusicActionMessages()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("SC#Franziska#%");

        string? showName = null;
        string? action = null;

        client.OnIcActionReceived += (sn, msg, _, _) =>
        {
            showName = sn;
            action = msg;
        };

        await client.HandleMessage("MC#pwr/trial.mp3#0#von Karma#%");

        Assert.Multiple(() =>
        {
            Assert.That(showName, Is.EqualTo("von Karma"));
            Assert.That(action, Is.EqualTo("has played a song trial"));
        });
    }

    [Test]
    public async Task HandleMessage_RaisesEvidenceActionMessages()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("LE#Knife&A sharp blade&knife.png#Badge&A lawyer badge&badge.png#%");

        List<string> actions = new List<string>();
        client.OnIcActionReceived += (_, msg, _, _) => actions.Add(msg);

        ICMessage message = new ICMessage
        {
            DeskMod = ICMessage.DeskMods.Chat,
            PreAnim = "-",
            Character = "Phoenix",
            Emote = "normal",
            Message = "Take a look",
            Side = "wit",
            SfxName = "1",
            EmoteModifier = ICMessage.EmoteModifiers.NoPreanimation,
            CharId = 0,
            SfxDelay = 0,
            ShoutModifier = ICMessage.ShoutModifiers.Objection,
            EvidenceID = "2",
            TextColor = ICMessage.TextColors.White,
            ShowName = "Phoenix"
        };

        await client.HandleMessage(ICMessage.GetCommand(message));

        Assert.That(actions, Does.Contain("has presented evidence Badge"));
    }

    [Test]
    public async Task HandleMessage_ShoutModifier_ProducesAo2StyleActionLine()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("SC#Phoenix#%");

        string? showName = null;
        string? action = null;

        client.OnIcActionReceived += (sn, msg, _, _) =>
        {
            showName = sn;
            action = msg;
        };

        ICMessage message = new ICMessage
        {
            DeskMod = ICMessage.DeskMods.Chat,
            PreAnim = "-",
            Character = "Phoenix",
            Emote = "normal",
            Message = "Take that",
            Side = "wit",
            SfxName = "1",
            EmoteModifier = ICMessage.EmoteModifiers.NoPreanimation,
            CharId = 0,
            SfxDelay = 0,
            ShoutModifier = ICMessage.ShoutModifiers.Objection,
            EvidenceID = "0",
            TextColor = ICMessage.TextColors.White,
            ShowName = "Phoenix"
        };

        await client.HandleMessage(ICMessage.GetCommand(message));

        Assert.Multiple(() =>
        {
            Assert.That(showName, Is.EqualTo("Phoenix"));
            Assert.That(action, Is.EqualTo("shouts OBJECTION!"));
        });
    }

    [Test]
    public void HandleMessage_IgnoresMalformedOrUnknownPackets()
    {
        var client = new AOClient("ws://localhost:10001/");

        Assert.DoesNotThrowAsync(async () =>
        {
            await client.HandleMessage("garbage");
            await client.HandleMessage("XX#something#%");
            await client.HandleMessage("MS#too#short#%");
            await client.HandleMessage("CT#too_short#%");
        });
    }

    /// <summary>
    /// R-004 — Feature flags from a prior connection must be cleared on disconnect/reconnect.
    /// Stale flags caused unsupported IC extensions to be sent to servers that never
    /// advertised them, resulting in silently rejected IC messages.
    /// The minimal IC packet (no options) must have exactly 15 payload fields (MS + 15 + %).
    /// </summary>
    [Test]
    public async Task FeatureFlags_ClearedOnReconnect_ProducesMinimalIcPacket()
    {
        AOClient client = new AOClient("ws://localhost:10001/");

        await client.HandleMessage("FL#noencryption#cccc_ic_support#looping_sfx#additive#effects#custom_blips#%");
        Assert.That(client.ServerFeatures, Is.Not.Empty, "Pre-condition: FL# must populate ServerFeatures");

        // DisconnectWebsocket clears serverFeatures even without an active transport.
        await client.DisconnectWebsocket();

        Assert.That(client.ServerFeatures, Is.Empty,
            "Feature flags must be cleared after disconnect; stale flags corrupt IC on next server");

        // Serializing with no features enabled must yield a minimal 15-field packet.
        ICMessage msg = new ICMessage
        {
            Character = "Franziska",
            Emote = "normal",
            CharId = 0,
            ShowName = "von Karma",
            OtherCharId = -1
        };
        string command = ICMessage.GetCommand(msg, new ICMessage.SerializationOptions());
        string[] parts = command.Split('#');

        // MS + 15 payload fields + % = 17 parts
        Assert.That(parts.Length, Is.EqualTo(17),
            "Minimal IC packet (no feature flags) must have exactly 15 payload fields");
    }

    [Test]
    public async Task DisconnectWebsocket_ClearsTransientServerSceneState()
    {
        AOClient client = new AOClient("ws://localhost:10001/");
        await client.HandleMessage("FL#cccc_ic_support#effects#%");
        await client.HandleMessage("SC#Franziska#Trucy#%");
        await client.HandleMessage("FA#Lounge#Courtroom#%");
        await client.HandleMessage("BN#vidya_lounge#%");
        await client.HandleMessage("CT#Server#$H: = Clients in [8] Lounge (users: 1) [IDLE] = \r\n  :white_medium_small_square: [0] Franziska #1#%");

        await client.DisconnectWebsocket();

        Assert.Multiple(() =>
        {
            Assert.That(client.ServerFeatures, Is.Empty);
            Assert.That(client.ServerCharacterAvailability, Is.Empty);
            Assert.That(client.AvailableAreas, Is.Empty);
            Assert.That(client.AvailableAreaInfos, Is.Empty);
            Assert.That(client.CurrentAreaPlayers, Is.Empty);
            Assert.That(client.CurrentArea, Is.Empty);
            Assert.That(client.curBG, Is.Empty);
            Assert.That(client.playerID, Is.EqualTo(-1));
            Assert.That(client.iniPuppetID, Is.EqualTo(-1));
        });
    }

    /// <summary>
    /// R-005 — When ICShowname is blank, the outgoing IC packet showname field must fall
    /// back to the char.ini ShowName value. Blank showname displayed as empty string was
    /// visible to all players in the chat log.
    /// ResolveShowNameForPacket is private; tested via reflection as the nearest stable seam.
    /// </summary>
    [Test]
    public void ShowName_BlankShowname_FallsBackToCharIniShowname()
    {
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "Franziska"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "Franziska", "char.ini"),
                "[Options]\nshowname=von Karma\nside=pro\ngender=female\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");

            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            AOClient client = new AOClient("ws://localhost:10001/");
            client.SetCharacter("Franziska");
            client.ICShowname = string.Empty;

            MethodInfo? resolveMethod = typeof(AOClient).GetMethod(
                "ResolveShowNameForPacket",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(resolveMethod, Is.Not.Null, "ResolveShowNameForPacket must exist on AOClient");

            string? resolved = resolveMethod!.Invoke(client, null) as string;

            Assert.That(resolved, Is.EqualTo("von Karma"),
                "Blank ICShowname must fall back to char.ini ShowName, not display as empty string");
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    /// <summary>
    /// R-005 — If both ICShowname and char.ini ShowName are blank, the outgoing IC packet
    /// must fall back to the selected character name instead of sending an empty showname.
    /// ResolveShowNameForPacket is private; tested via reflection as the nearest stable seam.
    /// </summary>
    [Test]
    public void ShowName_BlankCharIniShowname_FallsBackToCharacterName()
    {
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "Phoenix"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "Phoenix", "char.ini"),
                "[Options]\nshowname=\nside=def\ngender=male\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");

            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            AOClient client = new AOClient("ws://localhost:10001/");
            client.SetCharacter("Phoenix");
            client.ICShowname = string.Empty;

            MethodInfo? resolveMethod = typeof(AOClient).GetMethod(
                "ResolveShowNameForPacket",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(resolveMethod, Is.Not.Null, "ResolveShowNameForPacket must exist on AOClient");

            string? resolved = resolveMethod!.Invoke(client, null) as string;

            Assert.That(resolved, Is.EqualTo("Phoenix"),
                "Blank ICShowname and blank char.ini ShowName must fall back to the character name");
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    [Test]
    public async Task ShowName_BlankShowname_FallsBackToCurrentCharacterShowname()
    {
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "Franziska"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "Franziska", "char.ini"),
                "[Options]\nshowname=Von Karma\nside=pro\ngender=female\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");

            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "VydValkKam"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "VydValkKam", "char.ini"),
                "[Options]\nshowname=VydValkKam\nside=def\ngender=male\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");

            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            AOClient client = new AOClient("ws://localhost:10001/");
            await client.HandleMessage("SC#Franziska#VydValkKam#%");
            client.iniPuppetID = 0;
            client.SetCharacter("VydValkKam");
            client.ICShowname = string.Empty;

            MethodInfo? resolveMethod = typeof(AOClient).GetMethod(
                "ResolveShowNameForPacket",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(resolveMethod, Is.Not.Null, "ResolveShowNameForPacket must exist on AOClient");

            string? resolved = resolveMethod!.Invoke(client, null) as string;

            Assert.That(resolved, Is.EqualTo("VydValkKam"),
                "Blank ICShowname must use the current displayed character showname, matching AO2 iniswap behavior");
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    [Test]
    public async Task ShowName_BlankShowname_UsesCurrentCharacterShownameOverride()
    {
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "Franziska"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "Franziska", "char.ini"),
                "[Options]\nshowname=Loremaster\nside=pro\ngender=female\n"
                + "[OptionsN]\n1=2\n"
                + "[Options2]\nshowname=von Karma\n"
                + "[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");

            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "KamLoremaster"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "KamLoremaster", "char.ini"),
                "[Options]\nshowname=Loremaster\nside=jud\ngender=female\n"
                + "[OptionsN]\n1=2\n"
                + "[Options2]\nshowname=Judge Loremaster\n"
                + "[Emotions]\nnumber=1\n1=normal#-#normal#0#../../background/default/defensedesk\n");

            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            AOClient client = new AOClient("ws://localhost:10001/");
            await client.HandleMessage("SC#Franziska#%");
            client.iniPuppetID = 0;
            client.SetCharacter("KamLoremaster");
            client.ICShowname = string.Empty;

            MethodInfo? resolveMethod = typeof(AOClient).GetMethod(
                "ResolveShowNameForPacket",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(resolveMethod, Is.Not.Null, "ResolveShowNameForPacket must exist on AOClient");

            string? resolved = resolveMethod!.Invoke(client, null) as string;

            Assert.That(resolved, Is.EqualTo("Judge Loremaster"));
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    [Test]
    public async Task SelectIniPuppet_DoesNotOverwriteIcShowname()
    {
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "Franziska"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "Franziska", "char.ini"),
                "[Options]\nshowname=Von Karma\nside=pro\ngender=female\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");

            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            AOClient client = new AOClient("ws://localhost:10001/");
            await client.HandleMessage("SC#Franziska#%");

            client.ICShowname = string.Empty;
            await client.SelectIniPuppet(0);
            Assert.That(client.ICShowname, Is.Empty, "AO2 keeps an empty IC showname textbox empty after character selection.");

            client.SetICShowname("Manual Name");
            await client.SelectIniPuppet(0);
            Assert.That(client.ICShowname, Is.EqualTo("Manual Name"), "Selecting an INIPuppet must not replace an explicit IC showname.");
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    [Test]
    public async Task HandleMessage_PvUpdatesConfirmedIniPuppet()
    {
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "Franziska"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "Franziska", "char.ini"),
                "[Options]\nshowname=Von Karma\nside=pro\ngender=female\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");

            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            AOClient client = new AOClient("ws://localhost:10001/");
            client.playerID = 42;
            await client.HandleMessage("SC#Franziska#Trucy#%");
            await client.HandleMessage("PV#42#CID#0#%");

            Assert.Multiple(() =>
            {
                Assert.That(client.iniPuppetID, Is.EqualTo(0));
                Assert.That(client.iniPuppetName, Is.EqualTo("Franziska"));
            });
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    [Test]
    [CancelAfter(15000)]
    public async Task Connect_TcpEndpoint_UsesAoCompatibleHandshakeFlow()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        List<string> receivedPackets = new List<string>();
        Task serverTask = RunAoCompatibleTcpServerAsync(listener, receivedPackets, CancellationToken.None);

        AOClient client = new AOClient($"tcp://127.0.0.1:{port}");
        try
        {
            await client.Connect(0, 0, 0, 0);

            Assert.That(client.playerID, Is.EqualTo(17));
        }
        finally
        {
            await client.Disconnect();
            await serverTask;
        }

        int hiIndex = receivedPackets.FindIndex(packet => packet.StartsWith("HI#", StringComparison.Ordinal));
        int idIndex = receivedPackets.FindIndex(packet => string.Equals(packet, "ID#AO2#2.11.0#%", StringComparison.Ordinal));
        int askchaaIndex = receivedPackets.FindIndex(packet => string.Equals(packet, "askchaa#%", StringComparison.Ordinal));
        int rcIndex = receivedPackets.FindIndex(packet => string.Equals(packet, "RC#%", StringComparison.Ordinal));
        int rmIndex = receivedPackets.FindIndex(packet => string.Equals(packet, "RM#%", StringComparison.Ordinal));
        int rdIndex = receivedPackets.FindIndex(packet => string.Equals(packet, "RD#%", StringComparison.Ordinal));
        int ctIndex = receivedPackets.FindIndex(packet => packet.StartsWith("CT#OceanyaBot##%", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(hiIndex, Is.GreaterThanOrEqualTo(0), "Expected HI packet.");
            Assert.That(idIndex, Is.GreaterThan(hiIndex), "Expected client identity after HI.");
            Assert.That(askchaaIndex, Is.GreaterThan(idIndex), "Expected askchaa after client identity.");
            Assert.That(rcIndex, Is.GreaterThan(askchaaIndex), "Expected RC after askchaa/SI.");
            Assert.That(rmIndex, Is.GreaterThan(rcIndex), "Expected RM after SC.");
            Assert.That(rdIndex, Is.GreaterThan(rmIndex), "Expected RD after SM/FA.");
            Assert.That(ctIndex, Is.GreaterThan(rdIndex), "Expected empty CT bootstrap after RD.");
        });
    }

    [Test]
    [CancelAfter(15000)]
    public async Task Connect_TcpEndpoint_DispatchesOocReceivedDuringHandshake()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        List<string> receivedPackets = new List<string>();
        Task serverTask = RunAoCompatibleTcpServerAsync(
            listener,
            receivedPackets,
            CancellationToken.None,
            sendWelcomeBeforeDone: true);

        AOClient client = new AOClient($"tcp://127.0.0.1:{port}");
        string? receivedShowname = null;
        string? receivedMessage = null;
        bool? receivedFromServer = null;
        client.OnOOCMessageReceived += (showname, message, fromServer) =>
        {
            receivedShowname = showname;
            receivedMessage = message;
            receivedFromServer = fromServer;
        };

        try
        {
            await client.Connect(0, 0, 0, 0);
        }
        finally
        {
            await client.Disconnect();
            await serverTask;
        }

        Assert.Multiple(() =>
        {
            Assert.That(receivedShowname, Is.EqualTo("Server"));
            Assert.That(receivedMessage, Is.EqualTo("Welcome to the courtroom."));
            Assert.That(receivedFromServer, Is.True);
        });
    }

    [Test]
    [CancelAfter(15000)]
    public async Task Connect_TcpEndpoint_SendsHiBeforeServerSpeaks_WhenLegacyServerWaitsForClient()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        List<string> receivedPackets = new List<string>();
        Task serverTask = RunHiFirstTcpServerAsync(listener, receivedPackets, CancellationToken.None);

        AOClient client = new AOClient($"tcp://127.0.0.1:{port}");
        try
        {
            await client.Connect(0, 0, 0, 0);

            Assert.That(client.playerID, Is.EqualTo(29));
        }
        finally
        {
            await client.Disconnect();
            await serverTask;
        }

        Assert.That(receivedPackets[0], Does.StartWith("HI#"));
        Assert.That(receivedPackets, Does.Contain("ID#AO2#2.11.0#%"));
    }

    [Test]
    [CancelAfter(15000)]
    public void Connect_TcpEndpoint_ReturnsHelpfulError_WhenEndpointIsActuallyHttp()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task serverTask = RunHttpTcpServerAsync(listener, CancellationToken.None);

        AOClient client = new AOClient($"tcp://127.0.0.1:{port}");

        InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await client.Connect(0, 0, 0, 0))!;
        Assert.That(ex.Message, Does.Contain("HTTP response"));

        serverTask.GetAwaiter().GetResult();
    }

    /// <summary>
    /// R-002 — The CC# character-select packet must match the AO2-compliant format
    /// CC#playerID#charID#hdid#%. Sending the wrong format (e.g. CC#0#...) prevented
    /// servers from registering the character selection.
    /// A local character folder is created so SelectFirstAvailableINIPuppet can select it
    /// and actually emit the CC# packet.
    /// </summary>
    [Test]
    [CancelAfter(15000)]
    public async Task SendCharacterSelect_EmitsCorrectCcPacketFormat()
    {
        const string testCharName = "R002CcPacketTestChar";
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", testCharName));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", testCharName, "char.ini"),
                "[Options]\nshowname=TestChar\nside=def\ngender=male\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");
            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            List<string> receivedPackets = new List<string>();
            Task serverTask = RunR002TcpServerAsync(listener, receivedPackets, testCharName, CancellationToken.None);

            AOClient client = new AOClient($"tcp://127.0.0.1:{port}");
            try
            {
                await client.Connect(0, 0, 0, 0);
            }
            finally
            {
                await client.Disconnect();
                await serverTask;
            }

            // SelectFirstAvailableINIPuppet (called during Connect) emits CC#.
            string? ccPacket = receivedPackets.FirstOrDefault(
                p => p.StartsWith("CC#", StringComparison.Ordinal));
            Assert.That(ccPacket, Is.Not.Null, "CC# packet must be emitted after character selection");

            string[] parts = ccPacket!.Split('#');

            Assert.Multiple(() =>
            {
                Assert.That(parts[0], Is.EqualTo("CC"), "Packet header must be CC");
                // CC#<playerID>#<charID>#<hdid>#% → 5 parts when split by '#'
                Assert.That(parts.Length, Is.EqualTo(5),
                    "CC packet must contain exactly 3 fields: CC#playerID#charID#hdid#%");
                Assert.That(parts[4], Is.EqualTo("%"), "Packet must terminate with %");
                Assert.That(int.TryParse(parts[1], out int parsedPlayerId), Is.True,
                    "playerID field must be a parseable integer");
                // The test server's ID response sets playerID = 42
                Assert.That(parsedPlayerId, Is.EqualTo(42),
                    "playerID in CC# must match the ID assigned by the server");
                Assert.That(int.TryParse(parts[2], out _), Is.True,
                    "charID field must be a parseable integer");
            });
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    [Test]
    [CancelAfter(15000)]
    public async Task SendICMessage_CcccEffectsOnly_EmitsAo2FeatureGatedPacket()
    {
        const string testCharName = "VidyaIcPacketTestChar";
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", testCharName));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", testCharName, "char.ini"),
                "[Options]\nshowname=TestChar\nside=def\ngender=male\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");
            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            List<string> receivedPackets = new List<string>();
            Task serverTask = RunR002TcpServerAsync(
                listener,
                receivedPackets,
                testCharName,
                CancellationToken.None,
                "FL#noencryption#cccc_ic_support#effects#y_offset#%");

            AOClient client = new AOClient($"tcp://127.0.0.1:{port}");
            try
            {
                await client.Connect(0, 0, 0, 0);
                client.SetICShowname("Client1");
                await client.SendICMessage("hello from oceanya");
            }
            finally
            {
                await client.Disconnect();
                await serverTask;
            }

            string? msPacket = receivedPackets.FirstOrDefault(packet =>
                packet.StartsWith("MS#", StringComparison.Ordinal));
            Assert.That(msPacket, Is.Not.Null, "MS# packet must be emitted after SendICMessage");

            string[] parts = msPacket!.Split('#');
            Assert.Multiple(() =>
            {
                Assert.That(parts.Length, Is.EqualTo(22));
                Assert.That(parts[16], Is.EqualTo("Client1"));
                Assert.That(parts[17], Is.EqualTo("-1"));
                Assert.That(parts[18], Is.EqualTo("0<and>0"));
                Assert.That(parts[19], Is.EqualTo("0"));
                Assert.That(parts[20], Is.EqualTo("||"));
                Assert.That(parts[21], Is.EqualTo("%"));
                Assert.That(parts, Does.Not.Contain("-^(b)normal^(a)normal^"));
            });
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    [Test]
    [CancelAfter(15000)]
    public async Task SendICMessage_KfoServer_OmitsImplicitShownameAndCustomBlips()
    {
        const string testCharName = "April";
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", testCharName));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", testCharName, "char.ini"),
                "[Options]\nshowname=April\nside=wit\ngender=female\n[Emotions]\nnumber=1\n1=normal#bouncing#april-normal#0#1\n");
            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            List<string> receivedPackets = new List<string>();
            Task serverTask = RunR002TcpServerAsync(
                listener,
                receivedPackets,
                testCharName,
                CancellationToken.None,
                "FL#noencryption#cccc_ic_support#looping_sfx#additive#effects#custom_blips#y_offset#%",
                serverSoftware: "KFO-Server");

            AOClient client = new AOClient($"tcp://127.0.0.1:{port}");
            try
            {
                await client.Connect(0, 0, 0, 0);
                await client.SendICMessage("a");
            }
            finally
            {
                await client.Disconnect();
                await serverTask;
            }

            string? msPacket = receivedPackets.FirstOrDefault(packet =>
                packet.StartsWith("MS#", StringComparison.Ordinal));
            Assert.That(msPacket, Is.Not.Null, "MS# packet must be emitted after SendICMessage");

            string[] parts = msPacket!.Split('#');
            Assert.Multiple(() =>
            {
                Assert.That(parts.Length, Is.EqualTo(28));
                Assert.That(parts[16], Is.EqualTo(string.Empty), "KFO showname field should stay blank unless user typed one.");
                Assert.That(parts[17], Is.EqualTo("-1"));
                Assert.That(parts[18], Is.EqualTo("0<and>0"));
                Assert.That(parts[26], Is.EqualTo("||"));
                Assert.That(parts[27], Is.EqualTo("%"), "KFO packet should stop after effects instead of appending custom_blips/slide.");
            });
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    [Test]
    [CancelAfter(15000)]
    public async Task SendICMessage_CurrentCharacterAvailable_AlignsIniPuppetBeforeSending()
    {
        string baseRoot = CreateAoBaseRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "Franziska"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "Franziska", "char.ini"),
                "[Options]\nshowname=Von Karma\nside=pro\ngender=female\n[Emotions]\nnumber=1\n1=normal#-#normal#0#1\n");
            Directory.CreateDirectory(Path.Combine(baseRoot, "characters", "KamLoremaster"));
            File.WriteAllText(
                Path.Combine(baseRoot, "characters", "KamLoremaster", "char.ini"),
                "[Options]\nshowname=Kam\nside=jud\ngender=male\n[Emotions]\nnumber=1\n1=Backed#-#/Animations/Backed#0#1\n");
            Globals.BaseFolders = new List<string> { baseRoot };
            CharacterFolder.RefreshCharacterList();

            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            List<string> receivedPackets = new List<string>();
            Task serverTask = RunR002TcpServerAsync(
                listener,
                receivedPackets,
                "Franziska",
                CancellationToken.None,
                "FL#noencryption#cccc_ic_support#effects#y_offset#%",
                characterListPacket: "SC#Franziska#KamLoremaster#%",
                charsCheckPacket: "CharsCheck#0#0#%");

            AOClient client = new AOClient($"tcp://127.0.0.1:{port}");
            try
            {
                await client.Connect(0, 0, 0, 0);
                client.SetCharacter("KamLoremaster");
                await client.SendICMessage("test");
            }
            finally
            {
                await client.Disconnect();
                await serverTask;
            }

            Assert.That(
                receivedPackets.Any(packet => packet.StartsWith("CC#42#1#", StringComparison.Ordinal)),
                Is.True,
                "Client must request the current character's INIPuppet before sending IC.");
            string? msPacket = receivedPackets.FirstOrDefault(packet =>
                packet.StartsWith("MS#", StringComparison.Ordinal));
            Assert.That(msPacket, Is.Not.Null);
            string[] parts = msPacket!.Split('#');
            Assert.That(parts[9], Is.EqualTo("1"), "MS# char_id must match the current character after auto-alignment.");
        }
        finally
        {
            Globals.BaseFolders = new List<string>();
            CharacterFolder.RefreshCharacterList();
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, true);
            }
        }
    }

    private static async Task RunR002TcpServerAsync(
        TcpListener listener,
        List<string> receivedPackets,
        string characterName,
        CancellationToken cancellationToken,
        string featurePacket = "FL#noencryption#%",
        string serverSoftware = "tsuserver",
        string? characterListPacket = null,
        string charsCheckPacket = "CharsCheck#0#%")
    {
        using TcpClient serverClient = await listener.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = serverClient.GetStream();
        StringBuilder packetBuffer = new StringBuilder();
        await SendPacketAsync(stream, "decryptor#NOENCRYPT#%", cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? packet = await ReadPacketAsync(stream, packetBuffer, cancellationToken);
            if (string.IsNullOrEmpty(packet))
            {
                return;
            }

            receivedPackets.Add(packet);

            if (packet.StartsWith("HI#", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, $"ID#42#{serverSoftware}#7#%", cancellationToken);
            }
            else if (string.Equals(packet, "ID#AO2#2.11.0#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "PN#1#100#%", cancellationToken);
                await SendPacketAsync(stream, featurePacket, cancellationToken);
            }
            else if (string.Equals(packet, "askchaa#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SI#1#0#1#%", cancellationToken);
            }
            else if (string.Equals(packet, "RC#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, characterListPacket ?? $"SC#{characterName}#%", cancellationToken);
            }
            else if (string.Equals(packet, "RM#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SM#Lobby#%", cancellationToken);
            }
            else if (string.Equals(packet, "RD#%", StringComparison.Ordinal))
            {
                // Send CharsCheck marking the character as available (0 = available)
                await SendPacketAsync(stream, charsCheckPacket, cancellationToken);
                await SendPacketAsync(stream, "DONE#%", cancellationToken);
            }
            else if (packet.StartsWith("CC#", StringComparison.Ordinal))
            {
                string[] parts = packet.Split('#');
                string charId = parts.Length > 2 ? parts[2] : "0";
                await SendPacketAsync(stream, $"PV#42#CID#{charId}#%", cancellationToken);
            }
        }
    }

    private static Dictionary<string, bool> GetServerCharacterList(AOClient client)
    {
        FieldInfo? field = typeof(AOClient).GetField("serverCharacterList", BindingFlags.NonPublic | BindingFlags.Instance);
        return (Dictionary<string, bool>)field!.GetValue(client)!;
    }

    private static string CreateAoBaseRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "ao_network_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "characters"));
        Directory.CreateDirectory(Path.Combine(root, "background"));
        return root;
    }

    private static async Task RunAoCompatibleTcpServerAsync(
        TcpListener listener,
        List<string> receivedPackets,
        CancellationToken cancellationToken,
        bool sendWelcomeBeforeDone = false)
    {
        using TcpClient serverClient = await listener.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = serverClient.GetStream();
        StringBuilder packetBuffer = new StringBuilder();
        await SendPacketAsync(stream, "decryptor#NOENCRYPT#%", cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? packet = await ReadPacketAsync(stream, packetBuffer, cancellationToken);
            if (string.IsNullOrEmpty(packet))
            {
                return;
            }

            receivedPackets.Add(packet);

            if (packet.StartsWith("HI#", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "ID#17#tsuserver#7#%", cancellationToken);
            }
            else if (string.Equals(packet, "ID#AO2#2.11.0#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "PN#4#100#%", cancellationToken);
                await SendPacketAsync(stream, "FL#noencryption#fastloading#%", cancellationToken);
            }
            else if (string.Equals(packet, "askchaa#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SI#1#0#1#%", cancellationToken);
            }
            else if (string.Equals(packet, "RC#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SC#TcpHandshakeCharacterThatShouldNotExistLocally#%", cancellationToken);
            }
            else if (string.Equals(packet, "RM#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SM#Lobby#theme.ogg#%", cancellationToken);
            }
            else if (string.Equals(packet, "RD#%", StringComparison.Ordinal))
            {
                if (sendWelcomeBeforeDone)
                {
                    await SendPacketAsync(stream, "CT#Server#Welcome to the courtroom.#1#%", cancellationToken);
                }

                await SendPacketAsync(stream, "DONE#%", cancellationToken);
            }
        }
    }

    private static async Task RunHiFirstTcpServerAsync(
        TcpListener listener,
        List<string> receivedPackets,
        CancellationToken cancellationToken)
    {
        using TcpClient serverClient = await listener.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = serverClient.GetStream();
        StringBuilder packetBuffer = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            string? packet = await ReadPacketAsync(stream, packetBuffer, cancellationToken);
            if (string.IsNullOrEmpty(packet))
            {
                return;
            }

            receivedPackets.Add(packet);

            if (packet.StartsWith("HI#", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "ID#29#legacyserver#7#%", cancellationToken);
            }
            else if (string.Equals(packet, "ID#AO2#2.11.0#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "PN#6#80#%", cancellationToken);
                await SendPacketAsync(stream, "FL#legacy#%", cancellationToken);
            }
            else if (string.Equals(packet, "askchaa#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SI#1#0#1#%", cancellationToken);
            }
            else if (string.Equals(packet, "RC#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SC#LegacyTcpOnlyCharacter#%", cancellationToken);
            }
            else if (string.Equals(packet, "RM#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SM#Lobby#theme.ogg#%", cancellationToken);
            }
            else if (string.Equals(packet, "RD#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "DONE#%", cancellationToken);
            }
        }
    }

    private static async Task RunHttpTcpServerAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using TcpClient serverClient = await listener.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = serverClient.GetStream();
        await SendPacketAsync(stream, "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n", cancellationToken);
        await Task.Delay(250, cancellationToken);
    }

    private static async Task<string?> ReadPacketAsync(
        NetworkStream stream,
        StringBuilder packetBuffer,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];

        string current = packetBuffer.ToString();
        int existingPacketEnd = current.IndexOf("#%", StringComparison.Ordinal);
        if (existingPacketEnd >= 0)
        {
            string existingPacket = current.Substring(0, existingPacketEnd + 2);
            packetBuffer.Remove(0, existingPacketEnd + 2);
            return existingPacket;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead <= 0)
            {
                return null;
            }

            packetBuffer.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            current = packetBuffer.ToString();
            int packetEnd = current.IndexOf("#%", StringComparison.Ordinal);
            if (packetEnd < 0)
            {
                continue;
            }

            string packet = current.Substring(0, packetEnd + 2);
            packetBuffer.Remove(0, packetEnd + 2);
            return packet;
        }

        return null;
    }

    private static async Task SendPacketAsync(NetworkStream stream, string packet, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(packet);
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
