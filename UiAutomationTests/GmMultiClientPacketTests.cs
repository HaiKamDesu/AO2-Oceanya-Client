using System.Threading;
using AOBot_Testing.Structures;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using NUnit.Framework;

namespace UiAutomationTests;

[TestFixture]
[Category("Online")]
[Category("GmPacket")]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class GmMultiClientPacketTests
{
    private FlaUiSmokeApp? app;

    [TearDown]
    public void TearDown()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != NUnit.Framework.Interfaces.TestStatus.Passed)
        {
            app?.CaptureFailureScreenshot(TestContext.CurrentContext.Test.Name);
        }

        app?.KillImmediately();
        app = null;
    }

    [Test]
    public async Task GmPacket_AddingTwoClients_UsesSelectedClientIniPuppetForOutboundPacket()
    {
        using GmPacketLoopbackServer server = new GmPacketLoopbackServer();
        Window mainWindow = LaunchAndConnect(server);

        GmPacketUiDriver.AddClient(app!, mainWindow, "PacketClientOne", "SmokePhoenix");
        GmPacketUiDriver.WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);
        GmPacketUiDriver.AddClient(app!, mainWindow, "PacketClientTwo", "SmokeEdgeworth");

        GmPacketUiDriver.Click(mainWindow, "Main.Client.1");
        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Character", "SmokePhoenix");
        GmPacketUiDriver.Click(mainWindow, "Main.Client.2");
        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Character", "SmokeEdgeworth");

        CapturedPacket firstClientPacket = await SendPrefixedIcAndCapturePacketAsync(
            mainWindow,
            "PacketClientOne",
            "gm-client-one",
            server);

        CapturedPacket secondClientPacket = await SendPrefixedIcAndCapturePacketAsync(
            mainWindow,
            "PacketClientTwo",
            "gm-client-two",
            server);

        ICMessage firstMessage = RequireParsedIcMessage(firstClientPacket);
        ICMessage secondMessage = RequireParsedIcMessage(secondClientPacket);

        Assert.Multiple(() =>
        {
            Assert.That(firstClientPacket.ConnectionId, Is.EqualTo(1), "First GM client should send on the first transport connection.");
            Assert.That(firstMessage.CharId, Is.EqualTo(0), "First GM client should send with the first selected INI puppet.");
            Assert.That(firstMessage.Character, Is.EqualTo("SmokePhoenix"));
            Assert.That(firstMessage.ShowName, Is.EqualTo("PacketClientOne"));

            Assert.That(secondClientPacket.ConnectionId, Is.EqualTo(2), "Second GM client should send on the second transport connection.");
            Assert.That(secondMessage.CharId, Is.EqualTo(1), "Second GM client should send with the second selected INI puppet.");
            Assert.That(secondMessage.Character, Is.EqualTo("SmokeEdgeworth"));
            Assert.That(secondMessage.ShowName, Is.EqualTo("PacketClientTwo"));
        });
    }

    [Test]
    public async Task GmPacket_WebSocketEndpoint_IcSend_IsCapturedByControlledServer()
    {
        using GmPacketLoopbackServer server = new GmPacketLoopbackServer(useWebSocket: true);
        Window mainWindow = LaunchAndConnect(server);
        GmPacketUiDriver.AddClient(app!, mainWindow, "WebSocketClient", "SmokePhoenix");
        GmPacketUiDriver.WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);

        CapturedPacket packet = await GmPacketUiDriver.SendIcAndCapturePacketAsync(
            mainWindow,
            "websocket-ic-send",
            server);
        ICMessage message = RequireParsedIcMessage(packet);

        Assert.Multiple(() =>
        {
            Assert.That(packet.ConnectionId, Is.EqualTo(1));
            Assert.That(message.Character, Is.EqualTo("SmokePhoenix"));
            Assert.That(message.ShowName, Is.EqualTo("WebSocketClient"));
            Assert.That(message.Message, Is.EqualTo("websocket-ic-send"));
        });
    }

    [Test]
    public async Task GmPacket_CharacterSwapAndSwitchPosOnIniSwap_MapToCharacterAndSideFields()
    {
        using GmPacketLoopbackServer server = new GmPacketLoopbackServer();
        Window mainWindow = LaunchAndConnect(server);
        GmPacketUiDriver.AddClient(app!, mainWindow, "SwapClient", "SmokePhoenix");
        GmPacketUiDriver.WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);

        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Position", "jud");
        GmPacketUiDriver.Toggle(mainWindow, "Main.Options.SwitchPosOnIniSwap", ToggleState.Off);
        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Character", "SmokeEdgeworth");

        ICMessage switchOffMessage = RequireParsedIcMessage(
            await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "switch-pos-off", server));

        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Position", "jud");
        GmPacketUiDriver.Toggle(mainWindow, "Main.Options.SwitchPosOnIniSwap", ToggleState.On);
        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Character", "SmokePhoenix");

        ICMessage switchOnMessage = RequireParsedIcMessage(
            await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "switch-pos-on", server));

        Assert.Multiple(() =>
        {
            Assert.That(switchOffMessage.Character, Is.EqualTo("SmokeEdgeworth"));
            Assert.That(switchOffMessage.Side, Is.EqualTo("jud"), "Position should remain unchanged when INI-swap position sync is off.");
            Assert.That(switchOnMessage.Character, Is.EqualTo("SmokePhoenix"));
            Assert.That(switchOnMessage.Side, Is.EqualTo("def"), "Position should reset to the character default when INI-swap position sync is on.");
        });
    }

    [TestCase("Main.Ic.EmoteGrid.2__talk", "2: talk", "talk", "pre_talk")]
    [TestCase("combo:2: talk", "2: talk", "talk", "pre_talk")]
    public async Task GmPacket_EmoteSelectionSources_MapToEmoteAndPreanimFields(
        string selectionSource,
        string selectedDisplayId,
        string expectedEmote,
        string expectedPreanim)
    {
        using GmPacketLoopbackServer server = new GmPacketLoopbackServer();
        Window mainWindow = LaunchAndConnect(server);
        GmPacketUiDriver.AddClient(app!, mainWindow, "EmoteClient", "SmokePhoenix");
        GmPacketUiDriver.WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);

        if (selectionSource.StartsWith("combo:", StringComparison.Ordinal))
        {
            GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Emote", selectionSource.Substring("combo:".Length));
        }
        else
        {
            GmPacketUiDriver.Click(mainWindow, selectionSource);
        }

        ICMessage message = RequireParsedIcMessage(
            await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "emote-source-" + expectedEmote, server));

        Assert.Multiple(() =>
        {
            Assert.That(message.Emote, Is.EqualTo(expectedEmote));
            Assert.That(message.PreAnim, Is.EqualTo(expectedPreanim));
            Assert.That(message.EmoteModifier, Is.EqualTo(ICMessage.EmoteModifiers.NoPreanimation));
            Assert.That(selectedDisplayId, Is.Not.Empty);
        });
    }

    [Test]
    public async Task GmPacket_TextColorAndSfxCombobox_MapToMessageColorAndSfxFields()
    {
        using GmPacketLoopbackServer server = new GmPacketLoopbackServer();
        Window mainWindow = LaunchAndConnect(server);
        GmPacketUiDriver.AddClient(app!, mainWindow, "ColorSfxClient", "SmokePhoenix");
        GmPacketUiDriver.WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);

        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.TextColor", "Red");
        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Sfx", "GM Test Whoosh");

        ICMessage message = RequireParsedIcMessage(
            await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "red-sfx-message", server));

        Assert.Multiple(() =>
        {
            Assert.That(message.Message, Is.EqualTo("~red-sfx-message~"), "Red text should be wrapped in AO2 red-message markers.");
            Assert.That(message.TextColor, Is.EqualTo(ICMessage.TextColors.Red));
            Assert.That(message.SfxName, Is.EqualTo("dramatic_whoosh"));
            Assert.That(message.PreAnim, Is.EqualTo(string.Empty), "Custom SFX on a no-preanim emote should force an empty preanim field.");
            Assert.That(message.EmoteModifier, Is.EqualTo(ICMessage.EmoteModifiers.PlayPreanimation));
        });
    }

    [Test]
    public async Task GmPacket_EffectComboboxAndEffectToggles_MapToEffectAndScreenshakeFields()
    {
        using GmPacketLoopbackServer server = new GmPacketLoopbackServer();
        Window mainWindow = LaunchAndConnect(server);
        GmPacketUiDriver.AddClient(app!, mainWindow, "EffectClient", "SmokePhoenix");
        GmPacketUiDriver.WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);

        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Effect", "Hearts");
        GmPacketUiDriver.Toggle(mainWindow, "Main.Ic.Screenshake", ToggleState.On);

        ICMessage heartsMessage = RequireParsedIcMessage(
            await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "hearts-effect", server));

        GmPacketUiDriver.Toggle(mainWindow, "Main.Ic.Realization", ToggleState.On);
        ICMessage realizationMessage = RequireParsedIcMessage(
            await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "realization-effect", server));

        Assert.Multiple(() =>
        {
            Assert.That(heartsMessage.Effect, Is.EqualTo(ICMessage.Effects.Hearts));
            Assert.That(heartsMessage.EffectString, Does.StartWith("hearts|"));
            Assert.That(heartsMessage.ScreenShake, Is.True);
            Assert.That(realizationMessage.Effect, Is.EqualTo(ICMessage.Effects.Realization));
            Assert.That(realizationMessage.Realization, Is.True);
            Assert.That(realizationMessage.EffectString, Does.StartWith("realization|"));
        });
    }

    [Test]
    public async Task GmPacket_ModifierCheckboxes_MapToPreanimFlipAdditiveAndImmediateFields()
    {
        using GmPacketLoopbackServer server = new GmPacketLoopbackServer();
        Window mainWindow = LaunchAndConnect(server);
        GmPacketUiDriver.AddClient(app!, mainWindow, "CheckboxClient", "SmokePhoenix");
        GmPacketUiDriver.WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);

        GmPacketUiDriver.Toggle(mainWindow, "Main.Ic.Preanim", ToggleState.On);
        GmPacketUiDriver.Toggle(mainWindow, "Main.Ic.Flip", ToggleState.On);
        GmPacketUiDriver.Toggle(mainWindow, "Main.Ic.Additive", ToggleState.On);
        GmPacketUiDriver.Toggle(mainWindow, "Main.Ic.Immediate", ToggleState.On);

        CapturedPacket packet = await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "checkbox-modifiers", server);
        ICMessage message = RequireParsedIcMessage(packet);

        Assert.Multiple(() =>
        {
            Assert.That(message.Flip, Is.True);
            Assert.That(message.PreAnim, Is.EqualTo("pre_normal"));
            Assert.That(packet.GetField(18), Is.EqualTo("1"), "Immediate should serialize in the CCCC_IC_SUPPORT extension field.");
            Assert.That(packet.GetField(24), Is.EqualTo("1"), "Additive should serialize in the ADDITIVE extension field.");
        });
    }

    [TestCase("Main.Shout.HoldIt", ICMessage.ShoutModifiers.HoldIt)]
    [TestCase("Main.Shout.Objection", ICMessage.ShoutModifiers.Objection)]
    [TestCase("Main.Shout.TakeThat", ICMessage.ShoutModifiers.TakeThat)]
    [TestCase("Main.Shout.Custom", ICMessage.ShoutModifiers.Custom)]
    public async Task GmPacket_ShoutModifiers_MapToOutboundShoutField(
        string automationId,
        ICMessage.ShoutModifiers expectedModifier)
    {
        using GmPacketLoopbackServer server = new GmPacketLoopbackServer();
        Window mainWindow = LaunchAndConnect(server);
        GmPacketUiDriver.AddClient(app!, mainWindow, "ShoutClient", "SmokePhoenix");
        GmPacketUiDriver.WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);

        GmPacketUiDriver.Toggle(mainWindow, automationId, ToggleState.On);

        ICMessage message = RequireParsedIcMessage(
            await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "shout-" + expectedModifier, server));

        Assert.That(message.ShoutModifier, Is.EqualTo(expectedModifier));
    }

    [Test]
    public async Task GmPacket_StickyEffects_PersistsPacketAffectingSelectionsAcrossConsecutiveSends()
    {
        using GmPacketLoopbackServer server = new GmPacketLoopbackServer();
        Window mainWindow = LaunchAndConnect(server);
        GmPacketUiDriver.AddClient(app!, mainWindow, "StickyClient", "SmokePhoenix");
        GmPacketUiDriver.WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);

        GmPacketUiDriver.Toggle(mainWindow, "Main.Options.StickyEffects", ToggleState.On);
        GmPacketUiDriver.Toggle(mainWindow, "Main.Shout.HoldIt", ToggleState.On);
        GmPacketUiDriver.Toggle(mainWindow, "Main.Ic.Screenshake", ToggleState.On);
        GmPacketUiDriver.SelectComboBoxItem(mainWindow, "Main.Ic.Effect", "Hearts");

        ICMessage firstMessage = RequireParsedIcMessage(
            await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "sticky-first", server));
        ICMessage secondMessage = RequireParsedIcMessage(
            await GmPacketUiDriver.SendIcAndCapturePacketAsync(mainWindow, "sticky-second", server));

        Assert.Multiple(() =>
        {
            Assert.That(firstMessage.ShoutModifier, Is.EqualTo(ICMessage.ShoutModifiers.HoldIt));
            Assert.That(secondMessage.ShoutModifier, Is.EqualTo(ICMessage.ShoutModifiers.HoldIt));
            Assert.That(firstMessage.ScreenShake, Is.True);
            Assert.That(secondMessage.ScreenShake, Is.True);
            Assert.That(firstMessage.Effect, Is.EqualTo(ICMessage.Effects.Hearts));
            Assert.That(secondMessage.Effect, Is.EqualTo(ICMessage.Effects.Hearts));
        });
    }

    private Window LaunchAndConnect(GmPacketLoopbackServer server)
    {
        app = FlaUiSmokeApp.Launch(OnlineFixturePaths.BuildArguments(server.Endpoint));
        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        return mainWindow;
    }

    private static ICMessage RequireParsedIcMessage(CapturedPacket packet)
    {
        return packet.TryParseIcMessage()
            ?? throw new InvalidOperationException("Expected an IC message packet but got: " + packet.Packet);
    }

    private static async Task<CapturedPacket> SendPrefixedIcAndCapturePacketAsync(
        Window mainWindow,
        string clientName,
        string actualMessage,
        GmPacketLoopbackServer server)
    {
        string sendText = clientName + ": " + actualMessage;
        FlaUI.Core.AutomationElements.TextBox messageBox = GmPacketUiDriver.SetText(mainWindow, "Main.Ic.Message", sendText);
        GmPacketUiDriver.PressEnter(messageBox);

        return await server.WaitForPacketAsync(packet =>
            packet.Packet.StartsWith("MS#", StringComparison.Ordinal)
            && (packet.Packet.Contains("#" + actualMessage + "#", StringComparison.Ordinal)
                || packet.Packet.Contains("#~" + actualMessage + "~#", StringComparison.Ordinal)));
    }
}
