using System;
using System.Threading.Tasks;
using AOBot_Testing.Structures;
using Common;
using NUnit.Framework;
using static AOBot_Testing.Structures.ICMessage;

namespace UnitTests;

[TestFixture]
public class ICMessageTests
{
    private static ICMessage CreateSampleMessage()
    {
        return new ICMessage
        {
            DeskMod = DeskMods.Shown,
            PreAnim = "happy",
            Character = "Franziska",
            Emote = "normal",
            Message = "This is a test message",
            Side = "wit",
            SfxName = "whip1",
            EmoteModifier = EmoteModifiers.PlayPreanimation,
            CharId = 1,
            SfxDelay = 0,
            ShoutModifier = ShoutModifiers.Objection,
            EvidenceID = "0",
            Flip = false,
            Realization = false,
            TextColor = TextColors.White,
            ShowName = "TestShowname",
            OtherCharId = -1,
            SelfOffset = (0, 0),
            NonInterruptingPreAnim = false,
            SfxLooping = false,
            ScreenShake = false,
            FramesShake = "happy^(b)normal^(a)normal^",
            FramesRealization = "happy^(b)normal^(a)normal^",
            FramesSfx = "happy^(b)normal^(a)normal^",
            Additive = false,
            Effect = Effects.None,
            Blips = ""
        };
    }

    [Test]
    public void GetCommand_ProducesMsPacketWithExpectedCoreFields()
    {
        ICMessage message = CreateSampleMessage();
        string command = ICMessage.GetCommand(message);

        Assert.Multiple(() =>
        {
            Assert.That(command, Does.StartWith("MS#"));
            Assert.That(command, Does.EndWith("%"));
            Assert.That(command, Does.Contain("#Franziska#"));
            Assert.That(command, Does.Contain("#normal#"));
            Assert.That(command, Does.Contain("#This is a test message#"));
            Assert.That(command.Split('#').Length, Is.GreaterThanOrEqualTo(34));
        });
    }

    [Test]
    public void GetCommand_UsesAo2ExtendedFieldLayout()
    {
        ICMessage message = CreateSampleMessage();
        message.ScreenShake = true;
        message.FramesShake = string.Empty;
        message.FramesRealization = string.Empty;
        message.FramesSfx = string.Empty;
        message.Blips = "male";
        message.EffectString = "realization||custom-realization";

        string[] parts = ICMessage.GetCommand(message).Split('#');

        Assert.Multiple(() =>
        {
            Assert.That(parts[17], Is.EqualTo("-1"));
            Assert.That(parts[18], Is.EqualTo(string.Empty));
            Assert.That(parts[19], Is.EqualTo(string.Empty));
            Assert.That(parts[20], Is.EqualTo("0<and>0"));
            Assert.That(parts[21], Is.EqualTo("0"));
            Assert.That(parts[22], Is.EqualTo("0"));
            Assert.That(parts[23], Is.EqualTo("0"));
            Assert.That(parts[24], Is.EqualTo("0"));
            Assert.That(parts[25], Is.EqualTo("1"));
            Assert.That(parts[26], Is.EqualTo(string.Empty));
            Assert.That(parts[27], Is.EqualTo(string.Empty));
            Assert.That(parts[28], Is.EqualTo(string.Empty));
            Assert.That(parts[29], Is.EqualTo("0"));
            Assert.That(parts[30], Is.EqualTo("realization||custom-realization"));
            Assert.That(parts[31], Is.EqualTo("male"));
            Assert.That(parts[32], Is.EqualTo(string.Empty));
            Assert.That(parts[33], Is.EqualTo("%"));
        });
    }

    [Test]
    public void FromConsoleLine_RoundTripsCompactPacket()
    {
        ICMessage originalMessage = CreateSampleMessage();
        string command = ICMessage.GetCommand(originalMessage);

        ICMessage? parsedMessage = ICMessage.FromConsoleLine(command);

        Assert.That(parsedMessage, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsedMessage!.Character, Is.EqualTo(originalMessage.Character));
            Assert.That(parsedMessage.Emote, Is.EqualTo(originalMessage.Emote));
            Assert.That(parsedMessage.Message, Is.EqualTo(originalMessage.Message));
            Assert.That(parsedMessage.DeskMod, Is.EqualTo(originalMessage.DeskMod));
            Assert.That(parsedMessage.EmoteModifier, Is.EqualTo(originalMessage.EmoteModifier));
            Assert.That(parsedMessage.ShoutModifier, Is.EqualTo(originalMessage.ShoutModifier));
            Assert.That(parsedMessage.SelfOffset.Horizontal, Is.EqualTo(originalMessage.SelfOffset.Horizontal));
            Assert.That(parsedMessage.SelfOffset.Vertical, Is.EqualTo(originalMessage.SelfOffset.Vertical));
            Assert.That(parsedMessage.Effect, Is.EqualTo(originalMessage.Effect));
        });
    }

    [Test]
    public void FromConsoleLine_ReturnsNullForInvalidHeaderOrTooFewFields()
    {
        ICMessage? invalidHeader = ICMessage.FromConsoleLine("INVALID#1#pre#char#emote#message#%");
        ICMessage? tooFewFields = ICMessage.FromConsoleLine("MS#1#pre#char#emote#message#%");

        Assert.Multiple(() =>
        {
            Assert.That(invalidHeader, Is.Null);
            Assert.That(tooFewFields, Is.Null);
        });
    }

    [Test]
    public void EffectString_PreservesAo2PayloadWhileParsingEffectType()
    {
        ICMessage message = new ICMessage
        {
            EffectString = "realization|custom-folder|../../characters/test/realization"
        };

        Assert.Multiple(() =>
        {
            Assert.That(message.Effect, Is.EqualTo(Effects.Realization));
            Assert.That(
                message.EffectString,
                Is.EqualTo("realization|custom-folder|../../characters/test/realization"));
        });
    }

    [Test]
    public void GetColorFromTextColor_ReturnsColorForAllValues()
    {
        foreach (TextColors color in Enum.GetValues(typeof(TextColors)))
        {
            Assert.That(ICMessage.GetColorFromTextColor(color), Is.Not.EqualTo(default(System.Drawing.Color)));
        }
    }
}

[TestFixture]
public class CountdownTimerTests
{
    [Test]
    public async Task CountdownTimer_BasicElapsedFlow()
    {
        var timer = new CountdownTimer(TimeSpan.FromMilliseconds(300));
        bool elapsed = false;

        timer.TimerElapsed += () => elapsed = true;
        timer.Start();

        await Task.Delay(450);
        Assert.That(elapsed, Is.True);
    }

    [Test]
    public async Task CountdownTimer_ResetDefersElapsed()
    {
        var timer = new CountdownTimer(TimeSpan.FromMilliseconds(400));
        bool elapsed = false;

        timer.TimerElapsed += () => elapsed = true;
        timer.Start();

        await Task.Delay(200);
        timer.Reset(TimeSpan.FromMilliseconds(400));

        await Task.Delay(250);
        Assert.That(elapsed, Is.False);

        await Task.Delay(250);
        Assert.That(elapsed, Is.True);
    }

    [Test]
    public async Task CountdownTimer_StopPreventsElapsed()
    {
        var timer = new CountdownTimer(TimeSpan.FromMilliseconds(300));
        bool elapsed = false;

        timer.TimerElapsed += () => elapsed = true;
        timer.Start();
        await Task.Delay(100);
        timer.Stop();

        await Task.Delay(300);
        Assert.That(elapsed, Is.False);
    }
}
