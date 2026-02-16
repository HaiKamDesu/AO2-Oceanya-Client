using System.Collections.Generic;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests;

[TestFixture]
public class ServerEndpointCatalogTests
{
    [Test]
    public void GetProbeFollowUpPackets_DecryptorPacket_SendsHiOnly()
    {
        string hdid = "test-hdid";

        List<string> packets = ServerEndpointCatalog.GetProbeFollowUpPackets("decryptor#abc#", hdid);

        Assert.That(packets, Is.EqualTo(new[] { "HI#test-hdid#%" }));
    }

    [Test]
    public void GetProbeFollowUpPackets_IdPacket_MatchesAoClientIdentityFlow()
    {
        string hdid = "ignored";

        List<string> packets = ServerEndpointCatalog.GetProbeFollowUpPackets("ID#17#tsuserver#7#", hdid);

        Assert.That(packets, Is.EqualTo(new[] { "ID#AO2#2.11.0#%", "askchaa#%" }));
    }

    [Test]
    public void GetProbeFollowUpPackets_IdPacket_DoesNotSendKeepAliveDuringProbe()
    {
        string hdid = "ignored";

        List<string> packets = ServerEndpointCatalog.GetProbeFollowUpPackets("ID#23#server#9#", hdid);

        Assert.That(packets, Has.None.EqualTo("CH#23#%"));
    }

    [Test]
    public void TryParseProbePlayerCountPacket_ValidPnPacket_ParsesPlayersAndCapacity()
    {
        bool success = ServerEndpointCatalog.TryParseProbePlayerCountPacket("PN#14#100#", out int players, out int maxPlayers);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(players, Is.EqualTo(14));
            Assert.That(maxPlayers, Is.EqualTo(100));
        });
    }

    [Test]
    public void TryParseProbePlayerCountPacket_EmptyMaxField_MatchesAoBehaviorAsZero()
    {
        bool success = ServerEndpointCatalog.TryParseProbePlayerCountPacket("PN#4##", out int players, out int maxPlayers);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(players, Is.EqualTo(4));
            Assert.That(maxPlayers, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryParseProbePlayerCountPacket_NonPnPacket_ReturnsFalse()
    {
        bool success = ServerEndpointCatalog.TryParseProbePlayerCountPacket("ID#0#server#1#", out int players, out int maxPlayers);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(players, Is.EqualTo(0));
            Assert.That(maxPlayers, Is.EqualTo(0));
        });
    }
}
