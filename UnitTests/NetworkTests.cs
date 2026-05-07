using System;
using System.Collections.Generic;
using System.IO;
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

        await client.HandleMessage("BN#Courtroom_bg#%");
        await client.HandleMessage("SP#wit#%");

        Assert.Multiple(() =>
        {
            Assert.That(bg, Is.EqualTo("Courtroom_bg"));
            Assert.That(side, Is.EqualTo("wit"));
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
    public async Task ShowName_BlankShowname_FallsBackToIniPuppetShowname()
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

            Assert.That(resolved, Is.EqualTo("Von Karma"),
                "Blank ICShowname must use the selected INI puppet showname, matching AO2 iniswap behavior");
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
    public async Task ShowName_BlankShowname_UsesIniPuppetEmoteShownameOverride()
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

            Assert.That(resolved, Is.EqualTo("von Karma"));
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

    private static async Task RunR002TcpServerAsync(
        TcpListener listener,
        List<string> receivedPackets,
        string characterName,
        CancellationToken cancellationToken)
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
                await SendPacketAsync(stream, "ID#42#tsuserver#7#%", cancellationToken);
            }
            else if (string.Equals(packet, "ID#AO2#2.11.0#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "PN#1#100#%", cancellationToken);
                await SendPacketAsync(stream, "FL#noencryption#%", cancellationToken);
            }
            else if (string.Equals(packet, "askchaa#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SI#1#0#1#%", cancellationToken);
            }
            else if (string.Equals(packet, "RC#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, $"SC#{characterName}#%", cancellationToken);
            }
            else if (string.Equals(packet, "RM#%", StringComparison.Ordinal))
            {
                await SendPacketAsync(stream, "SM#Lobby#%", cancellationToken);
            }
            else if (string.Equals(packet, "RD#%", StringComparison.Ordinal))
            {
                // Send CharsCheck marking the character as available (0 = available)
                await SendPacketAsync(stream, "CharsCheck#0#%", cancellationToken);
                await SendPacketAsync(stream, "DONE#%", cancellationToken);
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
        CancellationToken cancellationToken)
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
