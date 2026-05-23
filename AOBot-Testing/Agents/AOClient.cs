using AOBot_Testing.Structures;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common;

namespace AOBot_Testing.Agents
{
    public partial class AOClient(string serverAddress)
    {
        private const int textCrawlSpeed = 35;
        private readonly Uri serverUri = new Uri(serverAddress);
        private IAOClientTransport? transport;
        public Stopwatch aliveTime = new Stopwatch();
        private CountdownTimer? speakTimer;
        bool AbleToSpeak { get; set; } = true;
        private readonly HashSet<string> serverFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


        /// <summary>
        /// Key: Name of the character
        /// Value: Whether the character is available for selection
        /// </summary>
        Dictionary<string, bool> serverCharacterList = new Dictionary<string, bool>();
        public int iniPuppetID = -1;
        public string iniPuppetName
        {
            get
            {
                if (iniPuppetID < 0 || iniPuppetID >= serverCharacterList.Count)
                {
                    return string.Empty;
                }

                return serverCharacterList.ElementAt(iniPuppetID).Key;
            }
        }
        public static string lastCharsCheck = string.Empty;

        string hdid = string.Empty;

        public int playerID;
        private string currentArea = string.Empty;
        private readonly object availableStateLock = new object();
        private readonly List<string> availableAreas = new List<string>();
        private readonly List<AreaInfo> availableAreaInfos = new List<AreaInfo>();
        private readonly List<string> availableMusic = new List<string>();
        private readonly List<Player> currentAreaPlayers = new List<Player>();
        private int pendingInternalGetAreaRefreshes;
        private bool lastGetAreaParseSucceeded;
        private readonly List<string> currentEvidenceNames = new List<string>();
        private readonly List<string> currentEvidenceImages = new List<string>();
        private string serverAssetUrl = string.Empty;
        private string serverSoftware = string.Empty;
        public CharacterFolder? currentINI;
        public Emote? currentEmote;

        public string clientName = "DefaultClientName";

        public string OOCShowname = "OceanyaBot";
        //Limit on this is 22
        public string ICShowname = "";
        public string curPos = "";
        public string curBG = "";
        public string curSFX = "";

        public ICMessage.DeskMods deskMod = ICMessage.DeskMods.Chat;
        public ICMessage.EmoteModifiers emoteMod = ICMessage.EmoteModifiers.NoPreanimation;
        public ICMessage.ShoutModifiers shoutModifiers = ICMessage.ShoutModifiers.Nothing;
        public bool flip = false;
        public ICMessage.Effects effect = ICMessage.Effects.None;
        public bool screenshake = false;
        public ICMessage.TextColors textColor = ICMessage.TextColors.White;
        public bool PreanimEnabled = false;
        public bool Immediate = false;
        public bool Additive = false;
        public (int Horizontal, int Vertical) SelfOffset;
        public int PairTargetCharId = -1;
        public string PairTargetCharacterName = string.Empty;
        public int PairLayerOrder = 0;
        public bool switchPosWhenChangingINI = false;

        private CharacterFolder? CurrentINI
        {
            get
            {
                return currentINI;
            }
            set
            {
                currentINI = value;

                if (currentINI != null)
                {
                    currentEmote = currentINI.configINI.Emotions.Values.FirstOrDefault();
                }
            }
        }

        public Action<string, string, string, string, int, bool>? OnMessageReceived;
        public Action<ICMessage>? OnICMessageReceived;
        public Action<string, string, bool, ICMessage.TextColors>? OnIcActionReceived;
        public Action<string, string, bool>? OnOOCMessageReceived;
        public Action<CharacterFolder>? OnChangedCharacter;
        public Action<string>? OnBGChange;
        public Action<string>? OnSideChange;
        public Action<string>? OnServerPositionReceived;
        public Action? OnINIPuppetChange;
        public Action<int>? OnReconnectionAttempt;
        public Action<int>? OnReconnectionAttemptFailed;
        public Action? OnReconnect;
        public Action? OnWebsocketDisconnect;
        public Action? OnDisconnect;
        public Action<string>? OnCurrentAreaChanged;
        public Action<IReadOnlyList<string>>? OnAvailableAreasUpdated;
        public Action<IReadOnlyList<string>>? OnAvailableMusicUpdated;
        public Action<IReadOnlyList<Player>, bool>? OnCurrentAreaPlayersUpdated;

        /// <summary>
        /// Raised when an MC# packet is received.
        /// Parameters: displayName, songPath (null = stop), loopEnabled, channel, effectFlags (FADE_IN=1, FADE_OUT=2, SYNC_POS=4).
        /// </summary>
        public Action<string, string?, bool, int, int>? OnMusicChanged;

        /// <summary>
        /// Raised when an RT# packet is received.
        /// Parameters: content (e.g. "testimony1", "testimony2", "judgeruling"), variant (integer suffix, typically 0 or 1).
        /// </summary>
        public Action<string, int>? OnRtReceived;

        public Action<IReadOnlyList<AreaInfo>>? OnAvailableAreaInfosUpdated;

        /// <summary>
        /// Optional provider for character selection frequency hints.
        /// When set, <see cref="SelectFirstAvailableINIPuppet"/> prefers the most-used characters first.
        /// Injected by the host application to avoid a direct dependency on save-file infrastructure.
        /// </summary>
        public Func<IReadOnlyDictionary<string, int>>? FrequencyHintsProvider { get; set; }

        public string CurrentArea
        {
            get
            {
                lock (availableStateLock)
                {
                    return currentArea;
                }
            }
        }

        public IReadOnlyList<string> AvailableAreas
        {
            get
            {
                lock (availableStateLock)
                {
                    return availableAreas.ToList();
                }
            }
        }

        public IReadOnlyList<AreaInfo> AvailableAreaInfos
        {
            get
            {
                lock (availableStateLock)
                {
                    return CloneAreaInfos(availableAreaInfos);
                }
            }
        }

        public IReadOnlyList<string> AvailableMusic
        {
            get
            {
                lock (availableStateLock)
                {
                    return availableMusic.ToList();
                }
            }
        }

        /// <summary>
        /// Returns a stable copy of the currently advertised server music list.
        /// </summary>
        public IReadOnlyList<string> GetAvailableMusicSnapshot()
        {
            return AvailableMusic;
        }

        public IReadOnlyList<Player> CurrentAreaPlayers
        {
            get
            {
                return currentAreaPlayers.AsReadOnly();
            }
        }

        public bool LastGetAreaParseSucceeded => lastGetAreaParseSucceeded;

        public IReadOnlyDictionary<string, bool> ServerCharacterAvailability
        {
            get
            {
                return new Dictionary<string, bool>(serverCharacterList, StringComparer.OrdinalIgnoreCase);
            }
        }

        public IReadOnlyCollection<string> ServerFeatures
        {
            get
            {
                return serverFeatures.ToList().AsReadOnly();
            }
        }

        public string ServerAssetUrl => serverAssetUrl;

        /// <summary>
        /// The server software identifier from the ID# handshake (e.g. "tsuserver3", "tsuservercc").
        /// </summary>
        public string ServerSoftware => serverSoftware;

        /// <summary>
        /// True when the connected server is tsuserverCC (adds "custom/" prefix when broadcasting /play).
        /// </summary>
        public bool IsTsuServerCC => serverSoftware.IndexOf("cc", StringComparison.OrdinalIgnoreCase) >= 0;

        public bool IsTransportConnected => transport != null && transport.IsConnected;

        private string TransportName => transport?.TransportName ?? ResolveTransportName(serverUri);

        private static string ResolveTransportName(Uri uri)
        {
            if (string.Equals(uri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase))
            {
                return "TCP";
            }

            return "WebSocket";
        }

        private List<string> pendingMessages = new List<string>();
        #region Send Message Methods
        public async Task SendICMessage(string showname, string message, bool queueMessage = false)
        {
            SetICShowname(showname);
            await SendICMessage(message, queueMessage);
        }
        private bool isProcessingMessages = false;

        public async Task SendICMessage(string message, bool queueMessage = false)
        {
            if (IsTransportConnected)
            {
                if (CurrentINI == null || currentEmote == null)
                {
                    CustomConsole.Error("Cannot send IC message without selected character/emote.");
                    return;
                }

                ICMessage msg = new ICMessage();
                msg.DeskMod = ResolveDeskModForPacket(currentEmote.Modifier, currentEmote.DeskMod);
                msg.PreAnim = currentEmote.PreAnimation;
                msg.Character = CurrentINI.Name;
                msg.Emote = currentEmote.Animation;
                msg.Message = textColor == ICMessage.TextColors.Red
                    ? (message.StartsWith("~", StringComparison.Ordinal) ? message + "~" : "~" + message + "~")
                    : message;

                msg.Side = ResolveCurrentOrDefaultSide();
                msg.SfxName = "1";
                msg.EmoteModifier = ResolveEmoteModifierForPacket(currentEmote.Modifier);
                msg.CharId = iniPuppetID;
                msg.SfxDelay = currentEmote.sfxDelay;
                msg.ShoutModifier = ResolveShoutModifierForPacket();
                msg.EvidenceID = "0";
                msg.Flip = SupportsServerFeature("FLIPPING") && flip;
                msg.FlipFieldRaw = SupportsServerFeature("FLIPPING")
                    ? string.Empty
                    : iniPuppetID.ToString();
                msg.Realization = effect == ICMessage.Effects.Realization;
                msg.TextColor = NormalizeTextColorForPacket(textColor);
                msg.ShowName = ResolveShowNameForPacket();
                if (SupportsServerFeature("CCCC_IC_SUPPORT")
                    && PairTargetCharId > -1
                    && PairTargetCharId != iniPuppetID)
                {
                    msg.OtherCharId = PairTargetCharId;
                    msg.OtherCharIdRaw = SupportsServerFeature("EFFECTS")
                        ? $"{PairTargetCharId}^{Math.Clamp(PairLayerOrder, 0, 1)}"
                        : PairTargetCharId.ToString();
                }
                else
                {
                    msg.OtherCharId = -1;
                    msg.OtherCharIdRaw = string.Empty;
                }
                msg.SelfOffset = SelfOffset;
                msg.NonInterruptingPreAnim = PreanimEnabled && Immediate;
                msg.SfxLooping = false;
                msg.ScreenShake = screenshake;
                msg.FramesShake = $"{currentEmote.PreAnimation}^(b){currentEmote.Animation}^(a){currentEmote.Animation}^";
                msg.FramesRealization = $"{currentEmote.PreAnimation}^(b){currentEmote.Animation}^(a){currentEmote.Animation}^";
                msg.FramesSfx = $"{currentEmote.PreAnimation}^(b){currentEmote.Animation}^(a){currentEmote.Animation}^";
                msg.Additive = Additive;
                msg.Effect = effect;
                msg.EffectString = ResolveEffectStringForPacket(hasSelectedCustomSfx: false);
                msg.Blips = "";
                msg.Slide = false;

                if (PreanimEnabled)
                {
                    msg.SfxName = ResolveSelectedSfxName();
                }

                bool hasSelectedCustomSfx =
                    !string.IsNullOrWhiteSpace(curSFX) &&
                    !string.Equals(curSFX, "Default", StringComparison.OrdinalIgnoreCase);

                if (hasSelectedCustomSfx)
                {
                    msg.SfxName = ResolveSelectedSfxName();
                    if (msg.EmoteModifier == ICMessage.EmoteModifiers.NoPreanimation
                        || msg.EmoteModifier == ICMessage.EmoteModifiers.NoPreanimationAndZoom)
                    {
                        msg.PreAnim = string.Empty;
                        msg.SfxDelay = 0;
                        msg.EmoteModifier = msg.EmoteModifier == ICMessage.EmoteModifiers.NoPreanimationAndZoom
                            ? ICMessage.EmoteModifiers.ObjectionAndZoomNoPreanim
                            : ICMessage.EmoteModifiers.PlayPreanimation;
                    }
                }

                msg.EffectString = ResolveEffectStringForPacket(hasSelectedCustomSfx);

                bool includeLoopingSfx = SupportsServerFeature("LOOPING_SFX");
                bool includeAdditive = SupportsServerFeature("ADDITIVE");
                bool includeEffects = SupportsServerFeature("EFFECTS");
                bool includeCustomBlips = SupportsServerFeature("CUSTOM_BLIPS");
                bool includeExtendedIcFields = includeLoopingSfx || includeAdditive || includeEffects || includeCustomBlips;

                ICMessage.SerializationOptions serializationOptions = new ICMessage.SerializationOptions
                {
                    IncludeCcccIcSupport = SupportsServerFeature("CCCC_IC_SUPPORT"),
                    IncludeLoopingSfx = includeExtendedIcFields,
                    IncludeAdditive = includeExtendedIcFields,
                    IncludeEffects = includeExtendedIcFields,
                    IncludeCustomBlips = includeCustomBlips,
                    IncludeVerticalOffset = SupportsServerFeature("Y_OFFSET"),
                    IncludeSlide = includeCustomBlips
                };
                string command = ICMessage.GetCommand(msg, serializationOptions);
                CustomConsole.Info(
                    $"Outgoing IC packet metadata: character=\"{msg.Character}\" iniPuppet=\"{iniPuppetName}\" iniPuppetId={iniPuppetID} emote=\"{msg.Emote}\" showname=\"{msg.ShowName}\" pairTarget={msg.OtherCharIdRaw} selfOffset={ICMessage.BuildOffsetDebugString(msg.SelfOffset, serializationOptions.IncludeVerticalOffset)} cccc={serializationOptions.IncludeCcccIcSupport} extended={includeExtendedIcFields} connected={IsTransportConnected}",
                    Common.CustomConsole.LogCategory.IC);
                CustomConsole.Info(
                    $"[PAIR] Outgoing IC pair state. client=\"{clientName}\" iniPuppetID={iniPuppetID} pairTarget={PairTargetCharId} pairRaw=\"{msg.OtherCharIdRaw}\" pairName=\"{PairTargetCharacterName}\" pairOrder={PairLayerOrder} selfOffset=({SelfOffset.Horizontal},{SelfOffset.Vertical}) cccc={serializationOptions.IncludeCcccIcSupport} effects={includeEffects} yOffset={serializationOptions.IncludeVerticalOffset}",
                    Common.CustomConsole.LogCategory.PairingStudio);
                CustomConsole.Info("Outgoing IC packet: " + command, Common.CustomConsole.LogCategory.Network);

                /// If the message is queued, add it to the list of pending messages.
                /// It'll be sent when the current messages are done processing.
                /// It's not super accurate, though.
                if (queueMessage)
                {
                    lock (pendingMessages)
                    {
                        pendingMessages.Add(command);
                    }

                    // **Ensure message processing starts**
                    if (!isProcessingMessages)
                    {
                        isProcessingMessages = true;
                        _ = Task.Run(ProcessPendingMessages);
                    }
                }
                else
                {
                    await SendPacket(command);
                    await Task.Delay(500);
                }
            }
            else
            {
                CustomConsole.Error("Server connection is not active. Cannot send message.");
            }
        }

        private async Task ProcessPendingMessages()
        {
            while (true)
            {
                string? messageToSend = null;

                lock (pendingMessages)
                {
                    if (pendingMessages.Count > 0)
                    {
                        messageToSend = pendingMessages[0];
                    }
                    else
                    {
                        isProcessingMessages = false;
                        return;
                    }
                }

                while (!AbleToSpeak)
                {
                    if (speakTimer == null)
                    {
                        break;
                    }

                    await Task.Delay(100);
                }

                lock (pendingMessages)
                {
                    if (pendingMessages.Count > 0)
                    {
                        pendingMessages.RemoveAt(0);
                    }
                }

                if (!string.IsNullOrEmpty(messageToSend))
                {
                    await SendPacket(messageToSend);
                }
                await Task.Delay(500); // Wait for command to process server-side
            }
        }


        public async Task SendOOCMessage(string message)
        {
            await SendOOCMessage(OOCShowname, message);
        }
        public async Task SendOOCMessage(string showname, string message)
        {
            if (IsTransportConnected)
            {
                OOCShowname = showname;
                string oocMessage = $"CT#{Globals.ReplaceSymbolsForText(showname)}#{Globals.ReplaceSymbolsForText(message)}#%";
                await SendPacket(oocMessage);
                await Task.Delay(500);
            }
            else
            {
                CustomConsole.Error("Server connection is not active. Cannot send message.");
            }
        }
        #endregion

        #region Set Methods
        public async Task SetArea(string areaName, int delayBetweenAreas = 500)
        {
            if (IsTransportConnected)
            {
                string[] areas = areaName.Split('/');
                foreach (var area in areas)
                {
                    string switchRoomCommand = $"MC#{area}#{playerID}#%";
                    await SendPacket(switchRoomCommand);
                    CustomConsole.Info($"Switched to room: {area}");
                    // Allow some time between room switches  
                    await Task.Delay(delayBetweenAreas);
                }

                SetCurrentArea(areaName);
            }
            else
            {
                CustomConsole.Error("Server connection is not active. Cannot switch rooms.");
            }
        }

        /// <summary>
        /// Applies deterministic local area state without requiring a live transport connection.
        /// </summary>
        /// <param name="currentAreaName">Current area to expose.</param>
        /// <param name="areas">Available area names to expose.</param>
        public void ApplyAreaStateForTests(string currentAreaName, IEnumerable<string> areas)
        {
            ReplaceAvailableAreas(areas);
            SetCurrentArea(currentAreaName);
        }

        /// <summary>
        /// Applies deterministic local character availability without requiring a live transport connection.
        /// </summary>
        /// <param name="characters">Available server character names to expose.</param>
        public void ApplyCharacterAvailabilityForTests(IEnumerable<string> characters)
        {
            serverCharacterList.Clear();
            foreach (string character in characters)
            {
                string normalized = character?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    serverCharacterList[normalized] = true;
                }
            }
        }

        public void ApplyServerFeaturesForTests(IEnumerable<string> features)
        {
            serverFeatures.Clear();
            foreach (string feature in features)
            {
                string normalized = feature?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    serverFeatures.Add(normalized);
                }
            }
        }

        public async Task RequestCurrentAreaPlayersRefreshAsync()
        {
            Interlocked.Increment(ref pendingInternalGetAreaRefreshes);
            CustomConsole.Info(
                $"[PAIR] Sending internal /getarea refresh. client={clientName} iniPuppetID={iniPuppetID} currentArea=\"{currentArea}\" connected={IsTransportConnected}",
                CustomConsole.LogCategory.PairingStudio);
            await SendOOCMessage("/getarea");
        }

        /// <summary>
        /// Sends an AO2 music change packet for the selected INI puppet.
        /// </summary>
        /// <param name="musicToken">Song or server-recognized category token.</param>
        /// <param name="effectFlags">AO2 music flags: FADE_IN=1, FADE_OUT=2, SYNC_POS=4.</param>
        public async Task PlayMusic(string musicToken, int effectFlags = 2)
        {
            if (!IsTransportConnected)
            {
                CustomConsole.Error("Server connection is not active. Cannot change music.");
                return;
            }

            // Music tokens are file paths; only '#' and '%' are protocol separators
            // and those rarely appear in filenames. Send as-is, matching AO2 client behavior.
            string token = musicToken?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            string showName = SupportsServerFeature("CCCC_IC_SUPPORT") || SupportsServerFeature("EFFECTS")
                ? Globals.ReplaceSymbolsForText(ResolveShowNameForPacket())
                : string.Empty;

            string packet = SupportsServerFeature("EFFECTS")
                ? $"MC#{token}#{iniPuppetID}#{showName}#{effectFlags}#%"
                : string.IsNullOrWhiteSpace(showName)
                    ? $"MC#{token}#{iniPuppetID}#%"
                    : $"MC#{token}#{iniPuppetID}#{showName}#%";

            CustomConsole.Info(
                $"[MUSIC OUT] token=\"{token}\" iniPuppetID={iniPuppetID} showName=\"{showName}\" effectFlags={effectFlags} hasEFFECTS={SupportsServerFeature("EFFECTS")} packet={packet}",
                Common.CustomConsole.LogCategory.MusicList);
            await SendPacket(packet);
        }

        /// <summary>
        /// Sends the AO2 stop-music packet.
        /// </summary>
        /// <param name="effectFlags">AO2 music flags. FADE_OUT=2 is the normal AO2 default.</param>
        public async Task StopMusic(int effectFlags = 2)
        {
            await PlayMusic("~stop.mp3", effectFlags);
        }

        public void SetCharacter(string characterName)
        {
            CharacterFolder? newChar = CharacterFolder.FullList.FirstOrDefault(c => c.Name == characterName)
                ?? CharacterFolder.FullList.FirstOrDefault(c => c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            SetCharacter(newChar);
        }
        public void SetCharacter(CharacterFolder? character)
        {
            if (character == null)
            {
                return;
            }

            CurrentINI = character;
            if (switchPosWhenChangingINI)
            {
                SetPos(character.configINI.Side);
            }
            else if (string.IsNullOrEmpty(curPos))
            {
                OnSideChange?.Invoke(string.Empty);
            }


            OnChangedCharacter?.Invoke(character);
        }

        public void ClearCharacter()
        {
            CurrentINI = null;
            currentEmote = null;
        }

        public void SetPos(string newPos, bool callEvent = true)
        {
            if (curPos == newPos) return;

            curPos = newPos;
            if (callEvent)
            {
                OnSideChange?.Invoke(newPos);
            }
        }

        public async Task SetServerPositionAsync(string newPos)
        {
            string normalizedPos = newPos?.Trim() ?? string.Empty;
            SetPos(normalizedPos);

            if (!IsTransportConnected)
            {
                return;
            }

            string showname = string.IsNullOrWhiteSpace(OOCShowname)
                ? clientName
                : OOCShowname;
            string command = string.IsNullOrWhiteSpace(normalizedPos)
                ? "/pos"
                : "/pos " + normalizedPos;
            await SendOOCMessage(showname, command);
        }

        public void SetICShowname(string newShowname)
        {
            ICShowname = newShowname;
        }
        public void SetEmote(string emoteDisplayID)
        {
            if (CurrentINI == null)
            {
                return;
            }

            currentEmote = CurrentINI.configINI.Emotions.Values.FirstOrDefault(e => e.DisplayID == emoteDisplayID);
            if (currentEmote == null)
            {
                return;
            }

            deskMod = currentEmote.DeskMod;
            emoteMod = currentEmote.Modifier;
        }
        #endregion

        public async Task HandleMessage(string message)
        {
            if (message.StartsWith("CharsCheck#"))
            {
                // Handle CharsCheck response
                lastCharsCheck = message;
                var parts = message.Substring(11).TrimEnd('#', '%').Split('#');
                int index = 0;
                foreach (var key in serverCharacterList.Keys.ToList())
                {
                    if (index < parts.Length)
                    {
                        serverCharacterList[key] = parts[index] == "0";
                        index++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            else if (message.StartsWith("SC#"))
            {
                serverCharacterList.Clear();
                var characters = message.Substring(3).TrimEnd('#', '%').Split('#', StringSplitOptions.RemoveEmptyEntries);
                foreach (var characterEntry in characters)
                {
                    // SC entries can include metadata after '&' (e.g. "Phoenix&Description").
                    var character = characterEntry.Split('&')[0];
                    character = Globals.ReplaceTextForSymbols(character).Trim();
                    if (string.IsNullOrWhiteSpace(character))
                    {
                        continue;
                    }

                    //Start every character as available to select
                    serverCharacterList[character] = true;
                }
                CustomConsole.Info("Server Character List updated.");
                Volatile.Read(ref _characterListRefreshTcs)?.TrySetResult(true);
            }
            else if (message.StartsWith("FL#"))
            {
                string[] featureParts = message.Substring(3).TrimEnd('#', '%')
                    .Split('#', StringSplitOptions.RemoveEmptyEntries);
                serverFeatures.Clear();
                foreach (string feature in featureParts)
                {
                    string trimmedFeature = Globals.ReplaceTextForSymbols(feature).Trim();
                    if (!string.IsNullOrWhiteSpace(trimmedFeature))
                    {
                        serverFeatures.Add(trimmedFeature);
                    }
                }

                CustomConsole.Info("Server features: " + string.Join(", ", serverFeatures.OrderBy(feature => feature, StringComparer.OrdinalIgnoreCase)), Common.CustomConsole.LogCategory.System);
            }
            else if (message.StartsWith("ASS#"))
            {
                string[] fields = message.Substring(4).TrimEnd('#', '%')
                    .Split('#', StringSplitOptions.RemoveEmptyEntries);
                serverAssetUrl = fields.Length > 0
                    ? Globals.ReplaceTextForSymbols(fields[0]).Trim()
                    : string.Empty;
            }
            else if (message.StartsWith("MS#"))
            {
                CustomConsole.Info("Incoming IC packet: " + message, Common.CustomConsole.LogCategory.Network);
                ICMessage? icMessage = ICMessage.FromConsoleLine(message);
                if (icMessage != null)
                {
                    icMessage.ShowName = ResolveIncomingIcDisplayName(icMessage);
                    EmitIcActionMessages(icMessage);

                    // Handle IC message
                    OnMessageReceived?.Invoke("IC", icMessage.Character, icMessage.ShowName, icMessage.Message, icMessage.CharId, false);
                    OnICMessageReceived?.Invoke(icMessage);

                    AbleToSpeak = false;
                    var delayTime = new TimeSpan(0, 0, 0, 0, (textCrawlSpeed * 2) * (icMessage.Message.Length));
                    //var formattedDelayTime = $"{delayTime.Hours}h {delayTime.Minutes}m {delayTime.Seconds}s {delayTime.Milliseconds}ms";
                    //CustomConsole.WriteLine($"[[SPEAK TIMER START - Duration: {formattedDelayTime}]]");

                    if (speakTimer == null)
                    {
                        speakTimer = new CountdownTimer(delayTime);
                        speakTimer.TimerElapsed += () =>
                        {
                            //CustomConsole.WriteLine("[[SPEAK TIMER ELAPSED]]");
                            AbleToSpeak = true;
                        };
                        speakTimer.Start();
                    }
                    else
                    {
                        speakTimer.Reset(delayTime); // Reset already starts the timer in the new class
                    }
                }
            }
            else if (message.StartsWith("SM#"))
            {
                ParseAreaListFromSm(message);
            }
            else if (message.StartsWith("FA#"))
            {
                ParseAreaListFromFa(message);
            }
            else if (message.StartsWith("FM#"))
            {
                ParseMusicListFromFm(message);
            }
            else if (message.StartsWith("ARUP#"))
            {
                ParseAreaUpdate(message);
            }
            else if (message.StartsWith("CT#"))
            {
                var fields = message.Split("#");
                if (fields.Length < 4)
                {
                    CustomConsole.Warning($"Malformed CT packet: {message}");
                    return;
                }

                var showname = Globals.ReplaceTextForSymbols(fields[1]);
                var messageText = Globals.ReplaceTextForSymbols(fields[2]);
                var fromServer = fields[3].ToString() == "1";

                AO2Parser.GetAreaParseResult getAreaResult = AO2Parser.ParseGetAreaDetailed(messageText);
                bool suppressInternalGetAreaMessage = ShouldSuppressInternalGetAreaOoc(messageText, getAreaResult);
                if (getAreaResult.IsGetAreaReport)
                {
                    List<Player> players = getAreaResult.Players;
                    lastGetAreaParseSucceeded = getAreaResult.ParsedPlayers;
                    CustomConsole.Info(
                        $"[PAIR] Parsed /getarea report. parsed={getAreaResult.ParsedPlayers} area=\"{getAreaResult.AreaName}\" players={players.Count} suppress={suppressInternalGetAreaMessage} fromServer={fromServer} text=\"{TruncateForLog(messageText, 240)}\"",
                        CustomConsole.LogCategory.PairingStudio);
                    if (!getAreaResult.ParsedPlayers && !string.IsNullOrWhiteSpace(getAreaResult.FailureReason))
                    {
                        CustomConsole.Warning(
                            "[PAIR] /getarea parse failed: " + getAreaResult.FailureReason,
                            category: CustomConsole.LogCategory.PairingStudio);
                    }

                    string parsedArea = getAreaResult.AreaName.Trim();
                    if (!string.IsNullOrWhiteSpace(parsedArea))
                    {
                        if (ShouldIgnoreAreaDowngrade(parsedArea))
                        {
                            CustomConsole.Warning(
                                $"[PAIR] Ignoring /getarea area downgrade. current=\"{currentArea}\" parsed=\"{parsedArea}\"",
                                category: CustomConsole.LogCategory.PairingStudio);
                            return;
                        }

                        SetCurrentArea(parsedArea);
                        ApplyAreaInfoFromGetAreaMessage(parsedArea, messageText);
                    }

                    currentAreaPlayers.Clear();
                    currentAreaPlayers.AddRange(players);
                    if (players.Count > 0)
                    {
                        CustomConsole.Info(
                            "[PAIR] Current-area players: " + string.Join(", ", players.Select(player => $"[{player.CharacterId}] {player.ICCharacterName}")),
                            CustomConsole.LogCategory.PairingStudio);
                    }

                    OnCurrentAreaPlayersUpdated?.Invoke(currentAreaPlayers.ToList().AsReadOnly(), lastGetAreaParseSucceeded);
                }
                else if (fromServer && messageText.Contains("=== Areas ===", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyAreaInfosFromAreaListMessage(messageText);
                }

                if (suppressInternalGetAreaMessage)
                {
                    return;
                }

                // Handle OOC message
                //you cant get the char id from an ooc message, so just send -1
                OnMessageReceived?.Invoke("OOC", "", showname, messageText, -1, fromServer);
                OnOOCMessageReceived?.Invoke(showname, messageText, fromServer);
            }
            else if (message.StartsWith("SP#"))
            {
                var fields = message.Split("#");
                var newPos = fields[1];

                SetPos(newPos);
                OnServerPositionReceived?.Invoke(newPos);
            }
            else if (message.StartsWith("BN#"))
            {
                var fields = message.Split("#");
                var newBG = fields[1];

                curBG = newBG;
                OnBGChange?.Invoke(newBG);
            }
            else if (message.StartsWith("RT#"))
            {
                string[] fields = message.Split('#');
                if (fields.Length >= 3)
                {
                    string content = Globals.ReplaceTextForSymbols(fields[1]).Trim();
                    int.TryParse(fields[2].TrimEnd('%'), out int variant);
                    OnRtReceived?.Invoke(content, variant);
                }
            }
            else if (message.StartsWith("LE#"))
            {
                ParseEvidenceList(message);
            }
            else if (message.StartsWith("MC#"))
            {
                HandleMusicPacket(message);
            }
        }

        #region Connection Related Methods
        public async Task Connect(int betweenHandshakeAndSetArea = 0, int betweenSetAreas = 0, int betweenAreasAndIniPuppet = 1000, int finalDelay = 1000, bool autoSelectCharacter = true)
        {
            aliveTime.Reset();
            dead = false;
            lastCharsCheck = string.Empty;
            serverFeatures.Clear();
            serverCharacterList.Clear();
            lock (availableStateLock)
            {
                availableAreas.Clear();
                availableAreaInfos.Clear();
                availableMusic.Clear();
            }

            transport = AOClientTransportFactory.Create(serverUri);
            try
            {
                await transport.ConnectAsync(serverUri, CancellationToken.None);
                aliveTime.Start();
                CustomConsole.Info("===========================");
                CustomConsole.Info($"Connected to AO server via {TransportName}.");
                CustomConsole.Info("===========================");

                await PerformHandshake();
                CustomConsole.Info("===========================");

                await Task.Delay(betweenHandshakeAndSetArea);
                if (!string.IsNullOrWhiteSpace(currentArea))
                {
                    await SetArea(currentArea, betweenSetAreas);
                    CustomConsole.Info("===========================");
                }

                await Task.Delay(betweenAreasAndIniPuppet);

                if (autoSelectCharacter)
                {
                    await SelectFirstAvailableINIPuppet();
                    CustomConsole.Info("===========================");
                }

                _ = Task.Run(() => ListenForMessages());
                _ = Task.Run(() => KeepAlive());

                await Task.Delay(finalDelay);
            }
            catch (Exception ex)
            {
                if (transport != null)
                {
                    await transport.CloseAsync(CancellationToken.None);
                    transport = null;
                }

                CustomConsole.Error($"Connection Error", ex);
                throw;
            }
        }

        private bool SupportsServerFeature(string featureName)
        {
            return serverFeatures.Contains(featureName?.Trim() ?? string.Empty);
        }

        private string ResolveCurrentOrDefaultSide()
        {
            if (!string.IsNullOrWhiteSpace(curPos))
            {
                return curPos;
            }

            return CurrentINI?.configINI?.Side ?? string.Empty;
        }

        private string ResolveSelectedSfxName()
        {
            if (string.IsNullOrWhiteSpace(curSFX) || string.Equals(curSFX, "Default", StringComparison.OrdinalIgnoreCase))
            {
                return currentEmote?.sfxName ?? "1";
            }

            if (string.Equals(curSFX, "Nothing", StringComparison.OrdinalIgnoreCase))
            {
                return "1";
            }

            return curSFX;
        }

        private string ResolveShowNameForPacket()
        {
            string explicitShowName = ICShowname?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(explicitShowName))
            {
                return explicitShowName;
            }

            CharacterFolder? showNameCharacter = ResolveIniPuppetCharacter() ?? CurrentINI;
            if (showNameCharacter?.configINI != null)
            {
                return showNameCharacter.configINI.ResolveShowNameForEmote(currentEmote?.ID ?? -1);
            }

            return showNameCharacter?.Name ?? CurrentINI?.Name ?? string.Empty;
        }

        private CharacterFolder? ResolveIniPuppetCharacter()
        {
            if (iniPuppetID < 0 || iniPuppetID >= serverCharacterList.Count)
            {
                return null;
            }

            string characterName = serverCharacterList.ElementAt(iniPuppetID).Key;
            return FindCharacterByFolderOrIniName(characterName);
        }

        private string ResolveIncomingIcDisplayName(ICMessage icMessage)
        {
            string packetShowName = icMessage.ShowName?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(packetShowName))
            {
                return packetShowName;
            }

            string characterName = ResolveIncomingIcCharacterName(icMessage);
            if (string.IsNullOrWhiteSpace(characterName))
            {
                return string.Empty;
            }

            CharacterFolder? matchingCharacter = FindCharacterByFolderOrIniName(characterName);
            if (matchingCharacter?.configINI != null)
            {
                return matchingCharacter.configINI.ResolveShowNameForEmote(-1);
            }

            return characterName;
        }

        private string ResolveIncomingIcCharacterName(ICMessage icMessage)
        {
            string characterName = icMessage.Character?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(characterName))
            {
                return characterName;
            }

            if (icMessage.CharId >= 0 && icMessage.CharId < serverCharacterList.Count)
            {
                return serverCharacterList.ElementAt(icMessage.CharId).Key;
            }

            return string.Empty;
        }

        private void EmitIcActionMessages(ICMessage icMessage)
        {
            string displayName = icMessage.ShowName ?? string.Empty;
            bool isSentFromSelf = icMessage.CharId == iniPuppetID;

            string shoutMessage = ResolveShoutLogMessage(icMessage);
            if (!string.IsNullOrWhiteSpace(shoutMessage))
            {
                OnIcActionReceived?.Invoke(displayName, "shouts " + shoutMessage, isSentFromSelf, ICMessage.TextColors.White);
            }

            string evidenceMessage = ResolveEvidenceLogMessage(icMessage);
            if (!string.IsNullOrWhiteSpace(evidenceMessage))
            {
                OnIcActionReceived?.Invoke(displayName, "has presented evidence " + evidenceMessage, isSentFromSelf, ICMessage.TextColors.White);
            }
        }

        private bool ShouldSuppressInternalGetAreaOoc(string messageText, AO2Parser.GetAreaParseResult getAreaResult)
        {
            if (Volatile.Read(ref pendingInternalGetAreaRefreshes) <= 0)
            {
                return false;
            }

            bool isCommandEcho = string.Equals(messageText?.Trim(), "/getarea", StringComparison.OrdinalIgnoreCase);
            bool isAreaResponse = getAreaResult.IsGetAreaReport;
            if (!isCommandEcho && !isAreaResponse)
            {
                return false;
            }

            if (isAreaResponse)
            {
                Interlocked.Decrement(ref pendingInternalGetAreaRefreshes);
            }

            return true;
        }

        private static string TruncateForLog(string value, int maxLength)
        {
            string normalized = (value ?? string.Empty)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
            return normalized.Length <= maxLength
                ? normalized
                : normalized.Substring(0, maxLength) + "...";
        }

        private string ResolveShoutLogMessage(ICMessage icMessage)
        {
            if (icMessage.ShoutModifier == ICMessage.ShoutModifiers.Nothing)
            {
                return string.Empty;
            }

            string characterName = ResolveIncomingIcCharacterName(icMessage);
            CharacterFolder? matchingCharacter = CharacterFolder.FullList.FirstOrDefault(character =>
                string.Equals(character.Name, characterName, StringComparison.OrdinalIgnoreCase));

            return icMessage.ShoutModifier switch
            {
                ICMessage.ShoutModifiers.HoldIt => ReadShoutOverrideOrDefault(matchingCharacter, "holdit_message", "HOLD IT!"),
                ICMessage.ShoutModifiers.Objection => ReadShoutOverrideOrDefault(matchingCharacter, "objection_message", "OBJECTION!"),
                ICMessage.ShoutModifiers.TakeThat => ReadShoutOverrideOrDefault(matchingCharacter, "takethat_message", "TAKE THAT!"),
                ICMessage.ShoutModifiers.Custom => ReadShoutOverrideOrDefault(matchingCharacter, "custom_message", "CUSTOM OBJECTION!"),
                _ => string.Empty
            };
        }

        private static string ReadShoutOverrideOrDefault(CharacterFolder? characterFolder, string key, string fallback)
        {
            string pathToIni = characterFolder?.configINI?.PathToConfigINI ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pathToIni) || !File.Exists(pathToIni))
            {
                return fallback;
            }

            try
            {
                IniDocument document = IniDocument.Load(pathToIni);
                string value = document.GetLatestValueOrDefault("Shouts", key).Trim();
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        private string ResolveEvidenceLogMessage(ICMessage icMessage)
        {
            if (!int.TryParse((icMessage.EvidenceID ?? string.Empty).Trim(), out int evidenceId)
                || evidenceId <= 0
                || evidenceId > currentEvidenceNames.Count)
            {
                return string.Empty;
            }

            return currentEvidenceNames[evidenceId - 1];
        }

        private void ParseEvidenceList(string message)
        {
            string[] fields = message.Split('#');
            currentEvidenceNames.Clear();
            currentEvidenceImages.Clear();

            foreach (string field in fields.Skip(1))
            {
                if (string.Equals(field, "%", StringComparison.Ordinal))
                {
                    break;
                }

                string[] subFields = field.Split('&');
                if (subFields.Length < 3)
                {
                    continue;
                }

                currentEvidenceNames.Add(Globals.ReplaceTextForSymbols(subFields[0]));
                currentEvidenceImages.Add(subFields.Length > 2 ? Globals.ReplaceTextForSymbols(subFields[2]).Trim() : string.Empty);
            }
        }

        /// <summary>
        /// Returns the image file name for a 1-based evidence ID from the last LE# packet, or null if not found.
        /// </summary>
        public string? GetEvidenceImagePath(int evidenceId)
        {
            if (evidenceId <= 0 || evidenceId > currentEvidenceImages.Count)
            {
                return null;
            }

            string image = currentEvidenceImages[evidenceId - 1];
            return string.IsNullOrWhiteSpace(image) ? null : image;
        }

        private void HandleMusicPacket(string message)
        {
            string[] fields = message.Split('#');
            if (fields.Length < 3)
            {
                return;
            }

            string song = Globals.ReplaceTextForSymbols(fields[1]).Trim();
            if (!int.TryParse(fields[2], out int characterId))
            {
                return;
            }

            CustomConsole.Info(
                $"[MUSIC IN] song=\"{song}\" charId={characterId} isSelf={characterId == iniPuppetID} myIniPuppetID={iniPuppetID} raw={message}",
                Common.CustomConsole.LogCategory.MusicList);

            bool loopEnabled = fields.Length > 4 && fields[4].Trim() == "1";
            int channel = 0;
            if (fields.Length > 5 && int.TryParse(fields[5].Trim(), out int parsedChannel))
            {
                channel = parsedChannel;
            }

            int effectFlags = 0;
            if (fields.Length > 6 && int.TryParse(fields[6].Trim(), out int parsedEffects))
            {
                effectFlags = parsedEffects;
            }

            // displayName is empty for server-initiated events (char_id = -1, e.g. area sync).
            // Those still propagate to OnMusicChanged — we just skip the chat log entry.
            string displayName = ResolveDisplayNameForMusicPacket(characterId, fields.Length > 3
                ? Globals.ReplaceTextForSymbols(fields[3]).Trim()
                : string.Empty);
            bool isSentFromSelf = characterId == iniPuppetID;

            bool isStop = string.IsNullOrWhiteSpace(song)
                || string.Equals(song, "~stop.mp3", StringComparison.OrdinalIgnoreCase);

            if (isStop)
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    OnIcActionReceived?.Invoke(displayName, "has stopped the music.", isSentFromSelf, ICMessage.TextColors.White);
                }

                OnMusicChanged?.Invoke(displayName, null, false, channel, effectFlags);
                return;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                string songName = Path.GetFileNameWithoutExtension(song);
                if (!string.IsNullOrWhiteSpace(songName))
                {
                    OnIcActionReceived?.Invoke(displayName, "has played a song " + songName, isSentFromSelf, ICMessage.TextColors.White);
                }
            }

            OnMusicChanged?.Invoke(displayName, song, loopEnabled, channel, effectFlags);
        }

        private string ResolveDisplayNameForMusicPacket(int characterId, string packetShowName)
        {
            if (!string.IsNullOrWhiteSpace(packetShowName))
            {
                return packetShowName;
            }

            if (characterId < 0 || characterId >= serverCharacterList.Count)
            {
                return string.Empty;
            }

            string characterName = serverCharacterList.ElementAt(characterId).Key;
            CharacterFolder? matchingCharacter = FindCharacterByFolderOrIniName(characterName);
            if (matchingCharacter?.configINI != null)
            {
                return matchingCharacter.configINI.ResolveShowNameForEmote(-1);
            }

            return characterName;
        }

        private static CharacterFolder? FindCharacterByFolderOrIniName(string characterName)
        {
            string normalized = characterName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return CharacterFolder.FullList.FirstOrDefault(character =>
                    string.Equals(character.Name, normalized, StringComparison.OrdinalIgnoreCase))
                ?? CharacterFolder.FullList.FirstOrDefault(character =>
                    string.Equals(character.configINI?.Name, normalized, StringComparison.OrdinalIgnoreCase));
        }

        private string ResolveEffectStringForPacket(bool hasSelectedCustomSfx)
        {
            string effectName = effect switch
            {
                ICMessage.Effects.Realization => "realization",
                ICMessage.Effects.Hearts => "hearts",
                ICMessage.Effects.Reaction => "reaction",
                ICMessage.Effects.Impact => "impact",
                _ => string.Empty
            };

            string effectFolder = CurrentINI?.configINI?.EffectsFolder ?? string.Empty;
            string sound = effect switch
            {
                ICMessage.Effects.Realization => string.IsNullOrWhiteSpace(CurrentINI?.configINI?.Realization)
                    ? "sfx-realization"
                    : CurrentINI.configINI.Realization,
                ICMessage.Effects.Hearts => "sfx-squee",
                ICMessage.Effects.Reaction => "sfx-reactionding",
                ICMessage.Effects.Impact => "sfx-fan",
                _ => string.Empty
            };

            if (!PreanimEnabled && hasSelectedCustomSfx)
            {
                sound = "0";
            }

            return $"{effectName}|{effectFolder}|{sound}";
        }

        private ICMessage.EmoteModifiers ResolveEmoteModifierForPacket(ICMessage.EmoteModifiers baseModifier)
        {
            ICMessage.EmoteModifiers resolved = baseModifier;

            if (resolved == ICMessage.EmoteModifiers.PlayPreanimationAndObjection)
            {
                resolved = ICMessage.EmoteModifiers.PlayPreanimation;
            }
            else if (resolved == ICMessage.EmoteModifiers.Unused3)
            {
                resolved = ICMessage.EmoteModifiers.NoPreanimation;
            }
            else if (resolved == ICMessage.EmoteModifiers.Unused4)
            {
                resolved = ICMessage.EmoteModifiers.NoPreanimationAndZoom;
            }

            if (PreanimEnabled && !Immediate)
            {
                if (resolved == ICMessage.EmoteModifiers.NoPreanimation)
                {
                    resolved = ICMessage.EmoteModifiers.PlayPreanimation;
                }
                else if (resolved == ICMessage.EmoteModifiers.NoPreanimationAndZoom
                    && SupportsServerFeature("PREZOOM"))
                {
                    resolved = ICMessage.EmoteModifiers.ObjectionAndZoomNoPreanim;
                }
            }
            else
            {
                if (resolved == ICMessage.EmoteModifiers.PlayPreanimation)
                {
                    resolved = ICMessage.EmoteModifiers.NoPreanimation;
                }
                else if (resolved == ICMessage.EmoteModifiers.ObjectionAndZoomNoPreanim)
                {
                    resolved = ICMessage.EmoteModifiers.NoPreanimationAndZoom;
                }
            }

            return resolved;
        }

        private ICMessage.DeskMods ResolveDeskModForPacket(
            ICMessage.EmoteModifiers baseModifier,
            ICMessage.DeskMods baseDeskMod)
        {
            if (!SupportsServerFeature("DESKMOD"))
            {
                return ICMessage.DeskMods.Shown;
            }

            ICMessage.DeskMods resolved = baseDeskMod;
            if (!SupportsServerFeature("EXPANDED_DESK_MODS"))
            {
                if (resolved == ICMessage.DeskMods.HiddenDuringPreanimShownAfter
                    || resolved == ICMessage.DeskMods.HiddenDuringPreanimCenteredAfter)
                {
                    resolved = ICMessage.DeskMods.Hidden;
                }
                else if (resolved == ICMessage.DeskMods.ShownDuringPreanimHiddenAfter
                    || resolved == ICMessage.DeskMods.ShownDuringPreanimCenteredAfter)
                {
                    resolved = ICMessage.DeskMods.Shown;
                }
            }

            if (resolved == ICMessage.DeskMods.Unspecified
                && (baseModifier == ICMessage.EmoteModifiers.NoPreanimationAndZoom
                    || baseModifier == ICMessage.EmoteModifiers.ObjectionAndZoomNoPreanim))
            {
                return ICMessage.DeskMods.Hidden;
            }

            if (resolved == ICMessage.DeskMods.Unspecified)
            {
                return ICMessage.DeskMods.Shown;
            }

            if (resolved == ICMessage.DeskMods.Chat)
            {
                return ICMessage.DeskMods.Shown;
            }

            return resolved;
        }

        private ICMessage.ShoutModifiers ResolveShoutModifierForPacket()
        {
            if (shoutModifiers == ICMessage.ShoutModifiers.Custom
                && !SupportsServerFeature("CUSTOMOBJECTIONS"))
            {
                return ICMessage.ShoutModifiers.Nothing;
            }

            return shoutModifiers;
        }

        private static ICMessage.TextColors NormalizeTextColorForPacket(ICMessage.TextColors color)
        {
            int colorValue = (int)color;
            return colorValue is < 0 or >= 9
                ? ICMessage.TextColors.White
                : color;
        }


        private void SetCurrentArea(string newArea)
        {
            if (string.IsNullOrWhiteSpace(newArea))
            {
                return;
            }

            bool changed;
            lock (availableStateLock)
            {
                if (string.Equals(currentArea, newArea, StringComparison.Ordinal))
                {
                    return;
                }

                currentArea = newArea;
                currentAreaPlayers.Clear();
                changed = true;
            }

            if (changed)
            {
                OnCurrentAreaChanged?.Invoke(newArea);
            }
        }

        private void ReplaceAvailableAreas(IEnumerable<string> areas)
        {
            List<string> areaSnapshot;
            List<AreaInfo> areaInfoSnapshot;
            string? defaultArea = null;
            lock (availableStateLock)
            {
                Dictionary<string, AreaInfo> previousAreaInfos = availableAreaInfos
                    .Where(areaInfo => !string.IsNullOrWhiteSpace(areaInfo.Name))
                    .GroupBy(areaInfo => areaInfo.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                availableAreas.Clear();
                availableAreaInfos.Clear();

                foreach (string area in areas)
                {
                    if (string.IsNullOrWhiteSpace(area))
                    {
                        continue;
                    }

                    string areaName = area.Trim();
                    availableAreas.Add(areaName);
                    if (previousAreaInfos.TryGetValue(areaName, out AreaInfo? previousAreaInfo))
                    {
                        previousAreaInfo.Name = areaName;
                        availableAreaInfos.Add(previousAreaInfo);
                    }
                    else
                    {
                        availableAreaInfos.Add(new AreaInfo(areaName, -1, "Unknown", "Unknown", "Unknown"));
                    }
                }

                areaSnapshot = availableAreas.ToList();
                areaInfoSnapshot = CloneAreaInfos(availableAreaInfos);
                if (string.IsNullOrWhiteSpace(currentArea) && availableAreas.Count > 0)
                {
                    defaultArea = availableAreas[0];
                }
            }

            OnAvailableAreasUpdated?.Invoke(areaSnapshot);
            OnAvailableAreaInfosUpdated?.Invoke(areaInfoSnapshot);
            if (!string.IsNullOrWhiteSpace(defaultArea))
            {
                SetCurrentArea(defaultArea);
            }
        }

        private void ParseAreaListFromFa(string message)
        {
            string[] content = message.Substring(3).TrimEnd('#', '%')
                .Split('#', StringSplitOptions.RemoveEmptyEntries);

            ReplaceAvailableAreas(content);
        }

        private void ParseAreaListFromSm(string message)
        {
            string[] content = message.Substring(3).TrimEnd('#', '%')
                .Split('#', StringSplitOptions.RemoveEmptyEntries);

            List<string> areas = new List<string>();
            List<string> music = new List<string>();
            bool musicStarted = false;
            foreach (string entry in content)
            {
                string decodedEntry = Globals.ReplaceTextForSymbols(entry).Trim();
                if (musicStarted)
                {
                    music.Add(decodedEntry);
                    continue;
                }

                if (LooksLikeMusicEntry(decodedEntry))
                {
                    musicStarted = true;
                    if (areas.Count > 0)
                    {
                        string previousArea = areas[^1];
                        areas.RemoveAt(areas.Count - 1);
                        music.Add(previousArea);
                    }

                    music.Add(decodedEntry);
                    continue;
                }

                areas.Add(decodedEntry);
            }

            ReplaceAvailableAreas(areas);
            ReplaceAvailableMusic(music);
        }

        private void ParseMusicListFromFm(string message)
        {
            string[] content = message.Substring(3).TrimEnd('#', '%')
                .Split('#', StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => Globals.ReplaceTextForSymbols(entry).Trim())
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .ToArray();

            ReplaceAvailableMusic(content);
        }

        private void ReplaceAvailableMusic(IEnumerable<string> music)
        {
            List<string> musicSnapshot;
            lock (availableStateLock)
            {
                availableMusic.Clear();
                foreach (string entry in music)
                {
                    string trimmedEntry = entry.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedEntry))
                    {
                        continue;
                    }

                    availableMusic.Add(trimmedEntry);
                }

                musicSnapshot = availableMusic.ToList();
            }

            OnAvailableMusicUpdated?.Invoke(musicSnapshot);
        }

        private void ParseAreaUpdate(string message)
        {
            string[] content = message.Substring(5).TrimEnd('#', '%')
                .Split('#');

            if (content.Length == 0)
            {
                return;
            }

            if (!int.TryParse(content[0], out int updateType))
            {
                CustomConsole.Warning($"Malformed ARUP packet type: {message}");
                return;
            }

            List<AreaInfo> areaInfoSnapshot;
            lock (availableStateLock)
            {
                for (int nElement = 1; nElement < content.Length; nElement++)
                {
                    int areaIndex = nElement - 1;
                    if (areaIndex >= availableAreaInfos.Count)
                    {
                        break;
                    }

                    string value = Globals.ReplaceTextForSymbols(content[nElement]).Trim();
                    AreaInfo targetArea = availableAreaInfos[areaIndex];

                    if (updateType == 0)
                    {
                        if (int.TryParse(value, out int players))
                        {
                            targetArea.Players = players;
                        }
                    }
                    else if (updateType == 1)
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            targetArea.Status = value;
                        }
                    }
                    else if (updateType == 2)
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            targetArea.CaseManager = value;
                        }
                    }
                    else if (updateType == 3)
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            targetArea.LockState = value;
                        }
                    }
                }

                areaInfoSnapshot = CloneAreaInfos(availableAreaInfos);
            }

            OnAvailableAreaInfosUpdated?.Invoke(areaInfoSnapshot);
        }

        private void ApplyAreaInfosFromAreaListMessage(string messageText)
        {
            string[] lines = messageText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            bool updated = false;
            string changedArea = string.Empty;
            List<string> areaSnapshot;
            List<AreaInfo> areaInfoSnapshot;

            lock (availableStateLock)
            {
                foreach (string line in lines)
                {
                    Match areaLine = Regex.Match(
                        line,
                        @"^Area\s+[^:]+:\s*(?<name>.+?)\s*\(users:\s*(?<players>\d+)\)\s*\[(?<status>[^\]]*)\]\[(?<cm>[^\]]*)\](?<lock>\[[^\]]+\])?(?<current>\s+\[\*\])?\s*$",
                        RegexOptions.IgnoreCase);
                    if (!areaLine.Success)
                    {
                        continue;
                    }

                    string areaName = areaLine.Groups["name"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(areaName))
                    {
                        continue;
                    }

                    AreaInfo targetArea = EnsureAreaInfo(areaName);
                    if (int.TryParse(areaLine.Groups["players"].Value, out int players))
                    {
                        targetArea.Players = players;
                    }

                    string status = areaLine.Groups["status"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(status))
                    {
                        targetArea.Status = status;
                    }

                    string caseManager = areaLine.Groups["cm"].Value.Trim();
                    if (caseManager.StartsWith("CMs:", StringComparison.OrdinalIgnoreCase))
                    {
                        caseManager = caseManager.Substring(4).Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(caseManager))
                    {
                        targetArea.CaseManager = caseManager;
                    }

                    string lockState = areaLine.Groups["lock"].Value.Trim().Trim('[', ']');
                    targetArea.LockState = string.IsNullOrWhiteSpace(lockState) ? "OPEN" : lockState;
                    updated = true;

                    if (areaLine.Groups["current"].Success
                        && !string.Equals(currentArea, areaName, StringComparison.Ordinal))
                    {
                        currentArea = areaName;
                        currentAreaPlayers.Clear();
                        changedArea = areaName;
                    }
                }

                areaSnapshot = availableAreas.ToList();
                areaInfoSnapshot = CloneAreaInfos(availableAreaInfos);
            }

            if (updated)
            {
                OnAvailableAreasUpdated?.Invoke(areaSnapshot);
                OnAvailableAreaInfosUpdated?.Invoke(areaInfoSnapshot);
            }

            if (!string.IsNullOrWhiteSpace(changedArea))
            {
                OnCurrentAreaChanged?.Invoke(changedArea);
            }
        }

        private void ApplyAreaInfoFromGetAreaMessage(string areaName, string messageText)
        {
            Match areaHeader = Regex.Match(
                messageText,
                @"\[[^\]]*\]:\s*\[(?<players>\d+)\s+Users?\]\[(?<status>[^\]]*)\](?:\[(?<lock>[^\]]*)\])?",
                RegexOptions.IgnoreCase);
            if (!areaHeader.Success)
            {
                return;
            }

            List<AreaInfo> areaInfoSnapshot;
            lock (availableStateLock)
            {
                AreaInfo targetArea = EnsureAreaInfo(areaName);
                if (int.TryParse(areaHeader.Groups["players"].Value, out int players))
                {
                    targetArea.Players = players;
                }

                string status = areaHeader.Groups["status"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(status))
                {
                    targetArea.Status = status;
                }

                string lockState = areaHeader.Groups["lock"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(lockState))
                {
                    targetArea.LockState = lockState;
                }

                areaInfoSnapshot = CloneAreaInfos(availableAreaInfos);
            }

            OnAvailableAreaInfosUpdated?.Invoke(areaInfoSnapshot);
        }

        private AreaInfo EnsureAreaInfo(string areaName)
        {
            AreaInfo? targetArea = availableAreaInfos.FirstOrDefault(area =>
                string.Equals(area.Name, areaName, StringComparison.OrdinalIgnoreCase));
            if (targetArea != null)
            {
                return targetArea;
            }

            availableAreas.Add(areaName);
            targetArea = new AreaInfo(areaName, -1, "Unknown", "Unknown", "Unknown");
            availableAreaInfos.Add(targetArea);
            return targetArea;
        }

        private static List<AreaInfo> CloneAreaInfos(IEnumerable<AreaInfo> areaInfos)
        {
            return areaInfos
                .Select(areaInfo => new AreaInfo(
                    areaInfo.Name,
                    areaInfo.Players,
                    areaInfo.Status,
                    areaInfo.CaseManager,
                    areaInfo.LockState))
                .ToList();
        }

        private bool ShouldIgnoreAreaDowngrade(string parsedArea)
        {
            if (string.IsNullOrWhiteSpace(currentArea) || string.IsNullOrWhiteSpace(parsedArea))
            {
                return false;
            }

            string normalizedCurrentArea = currentArea.Trim();
            string normalizedParsedArea = parsedArea.Trim();

            if (string.Equals(normalizedCurrentArea, normalizedParsedArea, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!normalizedCurrentArea.Contains('/'))
            {
                return false;
            }

            if (normalizedParsedArea.Contains('/'))
            {
                return false;
            }

            string[] currentAreaPath = normalizedCurrentArea.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (currentAreaPath.Length == 0)
            {
                return false;
            }

            return string.Equals(currentAreaPath[0], normalizedParsedArea, StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeMusicEntry(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".opus", StringComparison.OrdinalIgnoreCase);
        }

        private async Task PerformHandshake()
        {
            if (!IsTransportConnected)
            {
                CustomConsole.Error("Server connection is not active. Cannot perform handshake.");
                return;
            }

            hdid = Guid.NewGuid().ToString();
            bool hiSent = false;

            // Some legacy TCP servers wait for HI immediately instead of leading with decryptor/ID.
            if (string.Equals(serverUri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase))
            {
                await SendPacket($"HI#{hdid}#%");
                hiSent = true;
            }

            string response = await WaitForPacketAsync(
                packet => packet.StartsWith("decryptor#", StringComparison.OrdinalIgnoreCase)
                    || packet.StartsWith("ID#", StringComparison.OrdinalIgnoreCase),
                "decryptor/ID",
                timeoutMs: 5000,
                throwOnTimeout: false);

            if (response.StartsWith("decryptor#", StringComparison.OrdinalIgnoreCase))
            {
                if (!hiSent)
                {
                    await SendPacket($"HI#{hdid}#%");
                    hiSent = true;
                }

                response = await WaitForPacketAsync(
                    packet => packet.StartsWith("ID#", StringComparison.OrdinalIgnoreCase),
                    "ID");
            }
            else if (string.IsNullOrWhiteSpace(response))
            {
                if (!hiSent)
                {
                    await SendPacket($"HI#{hdid}#%");
                    hiSent = true;
                }

                response = await WaitForPacketAsync(
                    packet => packet.StartsWith("ID#", StringComparison.OrdinalIgnoreCase),
                    "ID");
            }

            string[] parts = response.Split('#');
            if (parts.Length >= 3 && int.TryParse(parts[1], out int parsedPlayerId))
            {
                playerID = parsedPlayerId;
                serverSoftware = parts[2];
                string serverVersion = parts.Length >= 4 ? parts[3] : string.Empty;
                CustomConsole.Info($"Assigned Player ID: {playerID} | Server Software: {serverSoftware} | Server Version: {serverVersion}");
            }

            await SendPacket($"ID#{AOClientProtocolConstants.ClientName}#{AOClientProtocolConstants.ClientVersion}#%");
            await SendPacket("askchaa#%");

            response = await WaitForPacketAsync(
                packet => packet.StartsWith("SI#", StringComparison.OrdinalIgnoreCase)
                    || packet.StartsWith("SC#", StringComparison.OrdinalIgnoreCase),
                "SI/SC",
                timeoutMs: 5000,
                throwOnTimeout: false);

            if (response.StartsWith("SI#", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(response))
            {
                await SendPacket("RC#%");
                response = await WaitForPacketAsync(
                    packet => packet.StartsWith("SC#", StringComparison.OrdinalIgnoreCase),
                    "SC");
            }

            await SendPacket("RM#%");

            await WaitForPacketAsync(
                packet => packet.StartsWith("SM#", StringComparison.OrdinalIgnoreCase)
                    || packet.StartsWith("FA#", StringComparison.OrdinalIgnoreCase),
                "SM/FA");

            await SendPacket("RD#%");

            string encodedShowname = Globals.ReplaceSymbolsForText(OOCShowname ?? string.Empty);
            await SendPacket($"CT#{encodedShowname}##%");

            await WaitForPacketAsync(
                packet => packet.StartsWith("DONE#", StringComparison.OrdinalIgnoreCase),
                "DONE");

            CustomConsole.Info("Handshake completed successfully!");
        }

        private async Task<string> WaitForPacketAsync(
            Func<string, bool> predicate,
            string expectedPacketHeader,
            int timeoutMs = 10000,
            bool throwOnTimeout = true)
        {
            var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < timeoutAt)
            {
                int remainingMs = (int)(timeoutAt - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= 0)
                {
                    break;
                }

                using CancellationTokenSource receiveTimeout = new CancellationTokenSource(remainingMs);
                string response;
                try
                {
                    response = await ReceiveMessageAsync(receiveTimeout.Token);
                }
                catch (OperationCanceledException) when (receiveTimeout.IsCancellationRequested)
                {
                    break;
                }

                if (!string.IsNullOrEmpty(response) && predicate(response))
                {
                    return response;
                }
            }

            if (throwOnTimeout)
            {
                throw new TimeoutException($"Timed out waiting for handshake packet: {expectedPacketHeader}");
            }

            return string.Empty;
        }


        private static SemaphoreSlim _reconnectQueue = new SemaphoreSlim(1, 1);

        private async Task Reconnect()
        {
            await _reconnectQueue.WaitAsync();
            try
            {
                int retryCount = 0;
                while (retryCount < 5)
                {
                    try
                    {
                        OnReconnectionAttempt?.Invoke(retryCount + 1);
                        await Connect(0, 0, 5000, 2000);
                        if (IsTransportConnected)
                        {
                            CustomConsole.Info($"Reconnected via {TransportName}.");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        CustomConsole.Error($"Reconnection attempt {retryCount + 1} failed", ex);
                    }

                    OnReconnectionAttemptFailed?.Invoke(retryCount + 1);
                    retryCount++;
                    await Task.Delay(2000);
                }

                CustomConsole.Error("Failed to reconnect after multiple attempts.");
                await Disconnect();
            }
            finally
            {
                await Task.Delay(200);
                _reconnectQueue.Release();
            }
        }

        private async Task KeepAlive()
        {
            while (IsTransportConnected)
            {
                await SendPacket($"CH#{playerID}#%");
                await Task.Delay(10000);
            }
        }

        bool dead = false;

        public async Task Disconnect()
        {
            dead = true;
            aliveTime.Stop();

            if (transport != null)
            {
                await transport.CloseAsync(CancellationToken.None);
                transport = null;
                CustomConsole.Info($"Disconnected from {ResolveTransportName(serverUri)}.");
            }
            else
            {
                CustomConsole.Info("Server connection is not active.");
            }

            serverFeatures.Clear();
            OnDisconnect?.Invoke();
        }

        public async Task DisconnectWebsocket()
        {
            if (transport != null)
            {
                await transport.CloseAsync(CancellationToken.None);
                transport = null;
            }
            else
            {
                CustomConsole.Info("Server connection is not active.");
            }

            serverFeatures.Clear();
        }

        public async Task CloseForShutdownAsync()
        {
            dead = true;
            aliveTime.Stop();
            await DisconnectWebsocket();
        }
        #endregion

        #region Helper methods

        public async Task SelectFirstAvailableINIPuppet(bool iniswapToSelected = true)
        {
            if (!IsTransportConnected)
            {
                CustomConsole.Error("Server connection is not active. Cannot select INI Puppet.");
                return;
            }

            // Build name→index map for quick lookup
            var nameToIndex = new Dictionary<string, int>(serverCharacterList.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < serverCharacterList.Count; i++)
            {
                nameToIndex[serverCharacterList.ElementAt(i).Key] = i;
            }

            // Candidate order: frequently used chars first (by count desc), then remaining server order.
            // FrequencyHintsProvider is injected by the host application (e.g. MainWindow sets it).
            IReadOnlyDictionary<string, int> frequencyHints = FrequencyHintsProvider?.Invoke()
                ?? new Dictionary<string, int>();

            var frequentIndices = frequencyHints
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => nameToIndex.TryGetValue(kvp.Key, out int idx) ? idx : -1)
                .Where(idx => idx >= 0)
                .ToList();

            var allIndices = frequentIndices
                .Concat(Enumerable.Range(0, serverCharacterList.Count).Except(frequentIndices));

            if (!string.IsNullOrEmpty(lastCharsCheck) && lastCharsCheck.StartsWith("CharsCheck#"))
            {
                var parts = lastCharsCheck.Substring(11).TrimEnd('#', '%').Split('#');
                int maxIndex = Math.Min(parts.Length, serverCharacterList.Count);

                foreach (int i in allIndices)
                {
                    if (i >= maxIndex || parts[i] != "0")
                    {
                        continue;
                    }

                    var characterName = serverCharacterList.ElementAt(i).Key;
                    var ini = CharacterFolder.FullList.FirstOrDefault(c => c.Name == characterName);
                    if (ini != null)
                    {
                        await SelectIniPuppet(i, iniswapToSelected);
                        return;
                    }
                }
            }
            else
            {
                // Some servers do not send CharsCheck in the same stage as before.
                // Fallback to first server-listed character present locally.
                foreach (int i in allIndices)
                {
                    if (i >= serverCharacterList.Count)
                    {
                        continue;
                    }

                    var characterName = serverCharacterList.ElementAt(i).Key;
                    var ini = CharacterFolder.FullList.FirstOrDefault(c => c.Name == characterName);
                    if (ini != null)
                    {
                        await SelectIniPuppet(i, iniswapToSelected);
                        return;
                    }
                }

                CustomConsole.Warning("Server did not provide CharsCheck during connect. Skipping automatic INI selection.");
            }

            CustomConsole.Warning("No available INI Puppets to select.");
        }

        public async Task SelectIniPuppet(string iniPuppetName, bool iniswapToSelected = true)
        {
            var serverCharID = 0;
            foreach (var kvp in serverCharacterList)
            {
                var name = kvp.Key;
                var available = kvp.Value;
                if (name.ToLower() == iniPuppetName.Trim().ToLower())
                {
                    if (!available)
                    {
                        throw new Exception($"Character \"{iniPuppetName}\" is already taken!");
                    }
                    else
                    {
                        await SelectIniPuppet(serverCharID, iniswapToSelected);
                        return;
                    }
                        
                }
                serverCharID++;
            }

            throw new Exception($"Character \"{iniPuppetName}\" not found in server character list.");
        }
        public async Task SelectIniPuppet(int serverCharID, bool iniswapToSelected = true)
        {
            if (serverCharID < 0 || serverCharID >= serverCharacterList.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(serverCharID),
                    $"Requested server character index {serverCharID} but available range is 0..{Math.Max(0, serverCharacterList.Count - 1)}.");
            }

            await SendPacket($"CC#{playerID}#{serverCharID}#{hdid}#%");

            var characterName = serverCharacterList.ElementAt(serverCharID).Key;

            var ini = CharacterFolder.FullList.FirstOrDefault(c => c.Name == characterName);

            if (ini != null)
            {
                iniPuppetID = serverCharID;
                OnINIPuppetChange?.Invoke();
                if (iniswapToSelected)
                {
                    CurrentINI = ini;
                    ICShowname = CurrentINI?.configINI.ShowName ?? characterName;
                }
                CustomConsole.Info($"Selected INI Puppet: \"{characterName}\" (Server Index: {serverCharID})");
            }
        }

        private TaskCompletionSource<bool>? _characterListRefreshTcs;

        /// <summary>
        /// Sends <c>RC#%</c> to ask the server to re-send its character list.
        /// Awaits the <c>SC#</c> response via the normal <see cref="ListenForMessages"/> loop
        /// (no concurrent transport reads) so <see cref="ServerCharacterAvailability"/> is
        /// up to date when this returns.
        /// </summary>
        public async Task RequestFreshCharacterListAsync(int timeoutMs = 5000)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _characterListRefreshTcs, tcs);
            try
            {
                await SendPacket("RC#%");
                using CancellationTokenSource cts = new CancellationTokenSource(timeoutMs);
                cts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);
                await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CustomConsole.Warning("Character list refresh from server timed out.");
            }
            finally
            {
                Volatile.Write(ref _characterListRefreshTcs, null);
            }
        }

        public async Task SendPacket(string packet)
        {
            if (transport != null && transport.IsConnected)
            {
                await transport.SendPacketAsync(packet, CancellationToken.None);
            }
            else
            {
                CustomConsole.Error("Server connection is not active. Cannot send message.");
            }
        }

        public async Task RequestAreaList()
        {
            await SendPacket("RM#%");
        }

        /// <summary>
        /// Requests the AO2 music list. Tsuserver forks refresh AO2 music through <c>RM</c>/<c>SM</c>, not AO1 <c>AM</c>.
        /// </summary>
        public async Task RequestMusicList()
        {
            await SendPacket("RM#%");
        }

        private async Task<string> ReceiveMessageAsync(CancellationToken cancellationToken = default)
        {
            if (transport != null && transport.IsConnected)
            {
                string? message = await transport.ReceivePacketAsync(cancellationToken);
                if (!string.IsNullOrEmpty(message))
                {
                    await HandleMessage(message);
                    return message;
                }
            }

            return string.Empty;
        }

        private async Task ListenForMessages()
        {
            try
            {
                while (IsTransportConnected)
                {
                    await ReceiveMessageAsync(CancellationToken.None);
                    if (!IsTransportConnected)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Error("===========================");
                CustomConsole.Error("Message listener encountered an error", ex);
                CustomConsole.Error("===========================");
            }
            finally
            {
                if (!IsTransportConnected)
                {
                    aliveTime.Stop();
                    OnWebsocketDisconnect?.Invoke();

                    if (!dead)
                    {
                        CustomConsole.Warning($"{TransportName} connection lost. Attempting to reconnect...");
                        CustomConsole.Info("===========================");

                        #region Save the state before reconnecting
                        var prevIni = currentINI;
                        var prevEmote = currentEmote;
                        var prevBool = switchPosWhenChangingINI;
                        var prevICShowname = ICShowname;
                        var prevOOCShowname = OOCShowname;
                        var prevCurPos = curPos;
                        var prevCurBG = curBG;
                        var prevDeskMod = deskMod;
                        var prevEmoteMod = emoteMod;
                        var prevShoutModifiers = shoutModifiers;
                        var prevFlip = flip;
                        var prevEffect = effect;
                        var prevScreenshake = screenshake;
                        var prevTextColor = textColor;
                        var prevImmediate = Immediate;
                        var prevAdditive = Additive;
                        var prevSelfOffset = SelfOffset;
                        var prevSelectedCharacterIndex = iniPuppetID;
                        #endregion

                        await Reconnect();

                        if (!dead)
                        {
                            #region Reapply the previous state
                            SetICShowname(prevICShowname);
                            OOCShowname = prevOOCShowname;
                            deskMod = prevDeskMod;
                            emoteMod = prevEmoteMod;
                            shoutModifiers = prevShoutModifiers;
                            flip = prevFlip;
                            effect = prevEffect;
                            screenshake = prevScreenshake;
                            textColor = prevTextColor;
                            Immediate = prevImmediate;
                            Additive = prevAdditive;
                            SelfOffset = prevSelfOffset;

                            SetCharacter(prevIni);
                            if (prevEmote != null)
                            {
                                SetEmote(prevEmote.DisplayID);
                            }
                            SetPos(prevCurPos);
                            CustomConsole.Info("State reapplied after reconnecting.");
                            OnReconnect?.Invoke();

                            #endregion
                        }



                        CustomConsole.Info("===========================");
                    }

                }
            }
        }

        #endregion
    }
}
