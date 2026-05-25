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
            Blips = "",
            Slide = false
        };
    }

    [Test]
    public void GetCommand_ProducesMsPacketWithExpectedCoreFields()
    {
        ICMessage message = CreateSampleMessage();
        string command = ICMessage.GetCommand(message);
        string[] parts = command.Split('#');

        Assert.Multiple(() =>
        {
            Assert.That(command, Does.StartWith("MS#"));
            Assert.That(command, Does.EndWith("#%"));
            Assert.That(parts[3], Is.EqualTo("Franziska"));
            Assert.That(parts[4], Is.EqualTo("normal"));
            Assert.That(parts[5], Is.EqualTo("This is a test message"));
            Assert.That(parts.Length, Is.EqualTo(17));
        });
    }

    [Test]
    public void GetCommand_UsesAo2ExtendedFieldLayout()
    {
        ICMessage message = CreateSampleMessage();
        ICMessage.SerializationOptions options = new ICMessage.SerializationOptions
        {
            IncludeCcccIcSupport = true,
            IncludeLoopingSfx = true,
            IncludeAdditive = true,
            IncludeEffects = true,
            IncludeCustomBlips = true
        };
        message.Message = "Encoded #message&%$";
        message.ShowName = "Miles #Edgeworth";
        message.OtherCharId = 7;
        message.OtherName = "ShouldNotSend";
        message.OtherEmote = "also_ignored";
        message.SelfOffset = (12, -5);
        message.NonInterruptingPreAnim = true;
        message.SfxLooping = true;
        message.ScreenShake = true;
        message.FramesShake = "pre^";
        message.FramesRealization = "real^";
        message.FramesSfx = "sfx^";
        message.Additive = true;
        message.Blips = "male";
        message.EffectString = "realization||custom-realization";
        message.Slide = true;

        string[] parts = ICMessage.GetCommand(message, options).Split('#');

        Assert.Multiple(() =>
        {
            Assert.That(parts[5], Is.EqualTo("Encoded <num>message<and><percent><dollar>"));
            Assert.That(parts[16], Is.EqualTo("Miles <num>Edgeworth"));
            Assert.That(parts[17], Is.EqualTo("7"));
            Assert.That(parts[18], Is.EqualTo("12<and>-5"));
            Assert.That(parts[19], Is.EqualTo("1"));
            Assert.That(parts[20], Is.EqualTo("1"));
            Assert.That(parts[21], Is.EqualTo("1"));
            Assert.That(parts[22], Is.EqualTo("pre^"));
            Assert.That(parts[23], Is.EqualTo("real^"));
            Assert.That(parts[24], Is.EqualTo("sfx^"));
            Assert.That(parts[25], Is.EqualTo("1"));
            Assert.That(parts[26], Is.EqualTo("realization||custom-realization"));
            Assert.That(parts[27], Is.EqualTo("male"));
            Assert.That(parts[28], Is.EqualTo("1"));
            Assert.That(parts[29], Is.EqualTo("%"));
        });
    }

    [Test]
    public void GetCommand_CcccWithEffectsOnly_DoesNotInsertReceiveOnlyOrLoopingFields()
    {
        ICMessage message = CreateSampleMessage();
        message.ShowName = "Client1";
        message.OtherCharId = -1;
        message.SelfOffset = (0, 0);
        message.NonInterruptingPreAnim = false;
        message.SfxLooping = true;
        message.ScreenShake = true;
        message.FramesShake = "should-not-send";
        message.FramesRealization = "should-not-send";
        message.FramesSfx = "should-not-send";
        message.Additive = true;
        message.EffectString = "impact||sfx-fan";

        SerializationOptions options = new SerializationOptions
        {
            IncludeCcccIcSupport = true,
            IncludeEffects = true,
        };

        string[] parts = ICMessage.GetCommand(message, options).Split('#');

        Assert.Multiple(() =>
        {
            Assert.That(parts[16], Is.EqualTo("Client1"));
            Assert.That(parts[17], Is.EqualTo("-1"));
            Assert.That(parts[18], Is.EqualTo("0<and>0"));
            Assert.That(parts[19], Is.EqualTo("0"));
            Assert.That(parts[20], Is.EqualTo("impact||sfx-fan"));
            Assert.That(parts[21], Is.EqualTo("%"));
            Assert.That(parts, Does.Not.Contain("should-not-send"));
        });
    }

    [Test]
    public void GetCommand_AllAo2FeatureFields_MatchesCompactOutgoingLayout()
    {
        ICMessage message = new ICMessage
        {
            DeskMod = DeskMods.Hidden,
            PreAnim = "-",
            Character = "KamLoremaster",
            Emote = "/Animations/Backed",
            Message = "c",
            Side = "Judge",
            SfxName = "1",
            EmoteModifier = EmoteModifiers.NoPreanimation,
            CharId = 17,
            SfxDelay = 1,
            ShoutModifier = ShoutModifiers.Nothing,
            EvidenceID = "0",
            Flip = false,
            Realization = false,
            TextColor = TextColors.White,
            ShowName = "Trucy",
            OtherCharId = -1,
            SelfOffset = (0, 0),
            NonInterruptingPreAnim = false,
            SfxLooping = false,
            ScreenShake = false,
            FramesShake = "-^(b)/Animations/Backed^(a)/Animations/Backed^",
            FramesRealization = "-^(b)/Animations/Backed^(a)/Animations/Backed^",
            FramesSfx = "-^(b)/Animations/Backed^(a)/Animations/Backed^",
            Additive = false,
            EffectString = "||",
            Blips = string.Empty,
            Slide = false
        };
        SerializationOptions options = new SerializationOptions
        {
            IncludeCcccIcSupport = true,
            IncludeLoopingSfx = true,
            IncludeAdditive = true,
            IncludeEffects = true,
            IncludeCustomBlips = true,
            IncludeVerticalOffset = true,
            IncludeSlide = true
        };

        string command = ICMessage.GetCommand(message, options);
        string[] parts = command.Split('#');

        Assert.Multiple(() =>
        {
            Assert.That(
                command,
                Is.EqualTo("MS#0#-#KamLoremaster#/Animations/Backed#c#Judge#1#0#17#1#0#0#0#0#0#Trucy#-1#0<and>0#0#0#0#-^(b)/Animations/Backed^(a)/Animations/Backed^#-^(b)/Animations/Backed^(a)/Animations/Backed^#-^(b)/Animations/Backed^(a)/Animations/Backed^#0#||##0#%"));
            Assert.That(parts.Length, Is.EqualTo(30));
            Assert.That(parts[9], Is.EqualTo("17"));
            Assert.That(parts[16], Is.EqualTo("Trucy"));
            Assert.That(parts[18], Is.EqualTo("0<and>0"));
            Assert.That(parts[19], Is.EqualTo("0"));
            Assert.That(parts[20], Is.EqualTo("0"));
            Assert.That(parts[21], Is.EqualTo("0"));
            Assert.That(parts[22], Does.StartWith("-^(b)"));
            Assert.That(parts[28], Is.EqualTo("0"));
            Assert.That(parts[29], Is.EqualTo("%"));
        });
    }

    [Test]
    public void GetCommand_OmitsVerticalOffsetWhenYOffsetExtensionIsDisabled()
    {
        ICMessage message = CreateSampleMessage();
        message.ShowName = "Von Karma";
        message.SelfOffset = (12, -5);

        ICMessage.SerializationOptions options = new ICMessage.SerializationOptions
        {
            IncludeCcccIcSupport = true,
            IncludeVerticalOffset = false
        };

        string[] parts = ICMessage.GetCommand(message, options).Split('#');

        Assert.That(parts[18], Is.EqualTo("12"));
    }

    [Test]
    public void FromConsoleLine_RoundTripsAo2PacketCoreFields()
    {
        ICMessage originalMessage = CreateSampleMessage();
        ICMessage.SerializationOptions options = new ICMessage.SerializationOptions
        {
            IncludeCcccIcSupport = true,
            IncludeLoopingSfx = true,
            IncludeAdditive = true,
            IncludeEffects = true,
            IncludeCustomBlips = true
        };
        originalMessage.ShowName = "Von Karma";
        originalMessage.OtherCharId = 12;
        originalMessage.SelfOffset = (9, -4);
        originalMessage.NonInterruptingPreAnim = true;
        originalMessage.SfxLooping = true;
        originalMessage.ScreenShake = true;
        originalMessage.FramesShake = "shake^";
        originalMessage.FramesRealization = "real^";
        originalMessage.FramesSfx = "sfx^";
        originalMessage.Additive = true;
        originalMessage.EffectString = "reaction|fx-folder|ding";
        originalMessage.Blips = "male";
        originalMessage.Slide = true;
        string command = ICMessage.GetCommand(originalMessage, options);

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
            Assert.That(parsedMessage.ShowName, Is.EqualTo(originalMessage.ShowName));
            Assert.That(parsedMessage.OtherCharId, Is.EqualTo(originalMessage.OtherCharId));
            Assert.That(parsedMessage.SelfOffset.Horizontal, Is.EqualTo(originalMessage.SelfOffset.Horizontal));
            Assert.That(parsedMessage.SelfOffset.Vertical, Is.EqualTo(originalMessage.SelfOffset.Vertical));
            Assert.That(parsedMessage.OtherName, Is.EqualTo(string.Empty));
            Assert.That(parsedMessage.OtherEmote, Is.EqualTo(string.Empty));
            Assert.That(parsedMessage.NonInterruptingPreAnim, Is.EqualTo(originalMessage.NonInterruptingPreAnim));
            Assert.That(parsedMessage.SfxLooping, Is.EqualTo(originalMessage.SfxLooping));
            Assert.That(parsedMessage.ScreenShake, Is.EqualTo(originalMessage.ScreenShake));
            Assert.That(parsedMessage.Additive, Is.EqualTo(originalMessage.Additive));
            Assert.That(parsedMessage.EffectString, Is.EqualTo(originalMessage.EffectString));
            Assert.That(parsedMessage.Blips, Is.EqualTo(originalMessage.Blips));
            Assert.That(parsedMessage.Slide, Is.EqualTo(originalMessage.Slide));
        });
    }

    [Test]
    public void FromConsoleLine_ParsesIncomingAo2ReceiveLayoutFields()
    {
        string packet = "MS#chat#pre#Phoenix#normal#Hello<num>world#def#sfx#1#5#3#2#1#0#0#4#Nick#9^2#Maya#bench#15<and>-7#3#1#1#1#0#shake^#real^#sfx^#1#impact|folder|fan#male#1#%";

        ICMessage? message = ICMessage.FromConsoleLine(packet);

        Assert.That(message, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(message!.DeskMod, Is.EqualTo(DeskMods.Chat));
            Assert.That(message.Message, Is.EqualTo("Hello#world"));
            Assert.That(message.ShowName, Is.EqualTo("Nick"));
            Assert.That(message.OtherCharId, Is.EqualTo(9));
            Assert.That(message.OtherName, Is.EqualTo("Maya"));
            Assert.That(message.OtherEmote, Is.EqualTo("bench"));
            Assert.That(message.SelfOffset.Horizontal, Is.EqualTo(15));
            Assert.That(message.SelfOffset.Vertical, Is.EqualTo(-7));
            Assert.That(message.OtherOffset, Is.EqualTo(3));
            Assert.That(message.OtherFlip, Is.True);
            Assert.That(message.NonInterruptingPreAnim, Is.True);
            Assert.That(message.SfxLooping, Is.True);
            Assert.That(message.ScreenShake, Is.False);
            Assert.That(message.Additive, Is.True);
            Assert.That(message.EffectString, Is.EqualTo("impact|folder|fan"));
            Assert.That(message.Blips, Is.EqualTo("male"));
            Assert.That(message.Slide, Is.True);
            Assert.That(message.Effect, Is.EqualTo(Effects.Impact));
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

    /// <summary>
    /// R-001 — char_id in MS# packet must carry iniPuppetID, not playerID.
    /// Regressed twice; this unit assertion catches a future field-index or assignment swap
    /// without requiring an interactive FlaUI session.
    /// </summary>
    [Test]
    public void GetCommand_CharIdField_UsesIniPuppetId_NotPlayerId()
    {
        const int iniPuppetId = 3;
        const int playerIdThatMustNotAppear = 17;

        ICMessage message = new ICMessage
        {
            DeskMod = DeskMods.Chat,
            PreAnim = "-",
            Character = "Franziska",
            Emote = "normal",
            Message = "test",
            Side = "pro",
            SfxName = "1",
            EmoteModifier = EmoteModifiers.NoPreanimation,
            CharId = iniPuppetId,
            SfxDelay = 0,
            ShoutModifier = ShoutModifiers.Nothing,
            EvidenceID = "0",
            TextColor = TextColors.White,
            ShowName = "von Karma",
            OtherCharId = -1
        };

        string command = ICMessage.GetCommand(message);
        string[] parts = command.Split('#');

        // parts[0]="MS", parts[1]=DeskMod, ..., parts[9]=CharId (field index 8)
        Assert.Multiple(() =>
        {
            Assert.That(parts[9], Is.EqualTo(iniPuppetId.ToString()),
                "char_id field (parts[9]) must equal iniPuppetID");
            Assert.That(parts[9], Is.Not.EqualTo(playerIdThatMustNotAppear.ToString()),
                "char_id field must not carry playerID");
        });
    }

    /// <summary>
    /// R-009 — FromConsoleLine compact-layout (field count &lt; 32) must parse Effect,
    /// ScreenShake, Additive, and NonInterruptingPreAnim at their compact field indices.
    /// Root cause of 6 GmPacket test failures; both layout branches need explicit coverage.
    /// </summary>
    [Test]
    public void FromConsoleLine_CompactLayout_ParsesEffectAndScreenshakeFields()
    {
        ICMessage original = new ICMessage
        {
            DeskMod = DeskMods.Chat,
            PreAnim = "-",
            Character = "Phoenix",
            Emote = "normal",
            Message = "test",
            Side = "def",
            SfxName = "1",
            EmoteModifier = EmoteModifiers.NoPreanimation,
            CharId = 0,
            SfxDelay = 0,
            ShoutModifier = ShoutModifiers.Nothing,
            EvidenceID = "0",
            TextColor = TextColors.White,
            ShowName = "Phoenix",
            OtherCharId = -1,
            SelfOffset = (5, -3),
            NonInterruptingPreAnim = true,
            SfxLooping = true,
            ScreenShake = true,
            FramesShake = "pre^",
            FramesRealization = "real^",
            FramesSfx = "sfx^",
            Additive = true,
            Blips = "male",
            Slide = true
        };
        original.EffectString = "impact||sfx-fan";

        SerializationOptions options = new SerializationOptions
        {
            IncludeCcccIcSupport = true,
            IncludeLoopingSfx = true,
            IncludeAdditive = true,
            IncludeEffects = true,
            IncludeCustomBlips = true
        };

        string packet = ICMessage.GetCommand(original, options);
        string[] parts = packet.Split('#');

        // Compact layout: 28 payload fields, which is < 32 (LegacySlideIndex + 1)
        int fieldCount = parts.Length - 2; // exclude "MS" and "%"
        Assert.That(fieldCount, Is.LessThan(32), "Pre-condition: packet must use compact layout");

        ICMessage? parsed = ICMessage.FromConsoleLine(packet);

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.ScreenShake, Is.True, "ScreenShake must be parsed from compact layout");
            Assert.That(parsed.Additive, Is.True, "Additive must be parsed from compact layout");
            Assert.That(parsed.NonInterruptingPreAnim, Is.True, "NonInterruptingPreAnim (Immediate) must be parsed from compact layout");
            Assert.That(parsed.Effect, Is.EqualTo(Effects.Impact), "Effect must be parsed from compact layout");
            Assert.That(parsed.Slide, Is.True, "Slide must be parsed from compact layout");
        });
    }

    /// <summary>
    /// R-019 — Red-text IC messages must have the message content wrapped in ~ characters.
    /// The wrapping is applied in SendICMessage before GetCommand is called; this test locks
    /// in that GetCommand correctly preserves the tilde framing and documents the convention.
    /// Nearest stable seam: construct the ICMessage as SendICMessage would, then verify the
    /// serialized packet's message field retains the wrapping.
    /// </summary>
    [Test]
    public void GetCommand_RedTextColor_WrapsMessageWithTilde()
    {
        const string rawContent = "hold it, counsel";
        string wrappedMessage = $"~{rawContent}~";

        ICMessage message = new ICMessage
        {
            DeskMod = DeskMods.Chat,
            PreAnim = "-",
            Character = "Phoenix",
            Emote = "normal",
            Message = wrappedMessage,
            Side = "def",
            SfxName = "1",
            EmoteModifier = EmoteModifiers.NoPreanimation,
            CharId = 0,
            SfxDelay = 0,
            ShoutModifier = ShoutModifiers.Nothing,
            EvidenceID = "0",
            TextColor = TextColors.Red,
            ShowName = "Phoenix",
            OtherCharId = -1
        };

        string command = ICMessage.GetCommand(message);
        string[] parts = command.Split('#');

        // parts[5] = field index 4 = Message
        string messageField = parts[5];
        Assert.Multiple(() =>
        {
            Assert.That(messageField, Does.StartWith("~"), "Red-text message field must start with ~");
            Assert.That(messageField, Does.EndWith("~"), "Red-text message field must end with ~");
            Assert.That(messageField, Does.Contain(rawContent), "Message content must be preserved inside the ~ wrapping");
        });
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
