using System;
using System.Collections.Generic;
using System.Reflection;
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
        var client = new AOClient("ws://localhost:10001/", "Basement");
        await client.HandleMessage("SC#Phoenix&Defense Attorney#Franziska&Prosecutor#%");

        var parsed = GetServerCharacterList(client);

        Assert.That(parsed.Keys, Is.EquivalentTo(new[] { "Phoenix", "Franziska" }));
    }

    [Test]
    public async Task HandleMessage_UpdatesCharacterAvailabilityFromCharsCheck()
    {
        var client = new AOClient("ws://localhost:10001/", "Basement");
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
        AOClient client = new AOClient("ws://localhost:10001/", "Basement");
        await client.HandleMessage("SC#Phoenix#Franziska#%");
        await client.HandleMessage("CharsCheck#1#1#0#0#%");

        Assert.DoesNotThrowAsync(async () => await client.SelectFirstAvailableINIPuppet());
    }

    [Test]
    public async Task HandleMessage_RaisesBackgroundAndPositionEvents()
    {
        var client = new AOClient("ws://localhost:10001/", "Basement");

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
        var client = new AOClient("ws://localhost:10001/", "Basement");

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
        var client = new AOClient("ws://localhost:10001/", "Basement");

        Assert.DoesNotThrowAsync(async () =>
        {
            await client.HandleMessage("garbage");
            await client.HandleMessage("XX#something#%");
            await client.HandleMessage("MS#too#short#%");
            await client.HandleMessage("CT#too_short#%");
        });
    }

    private static Dictionary<string, bool> GetServerCharacterList(AOClient client)
    {
        FieldInfo? field = typeof(AOClient).GetField("serverCharacterList", BindingFlags.NonPublic | BindingFlags.Instance);
        return (Dictionary<string, bool>)field!.GetValue(client)!;
    }
}
