using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AOBot_Testing.Agents;
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

    private static Dictionary<string, bool> GetServerCharacterList(AOClient client)
    {
        FieldInfo? field = typeof(AOClient).GetField("serverCharacterList", BindingFlags.NonPublic | BindingFlags.Instance);
        return (Dictionary<string, bool>)field!.GetValue(client)!;
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
