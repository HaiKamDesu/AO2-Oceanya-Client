using AOBot_Testing.Structures;
using System;
using System.Diagnostics;
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
        private readonly List<string> availableAreas = new List<string>();
        private readonly List<AreaInfo> availableAreaInfos = new List<AreaInfo>();
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

        public Action<string, string, string, string, int>? OnMessageReceived;
        public Action<ICMessage>? OnICMessageReceived;
        public Action<string, string, bool>? OnOOCMessageReceived;
        public Action<CharacterFolder>? OnChangedCharacter;
        public Action<string>? OnBGChange;
        public Action<string>? OnSideChange;
        public Action? OnINIPuppetChange;
        public Action<int>? OnReconnectionAttempt;
        public Action<int>? OnReconnectionAttemptFailed;
        public Action? OnReconnect;
        public Action? OnWebsocketDisconnect;
        public Action? OnDisconnect;
        public Action<string>? OnCurrentAreaChanged;
        public Action<IReadOnlyList<string>>? OnAvailableAreasUpdated;
        public Action<IReadOnlyList<AreaInfo>>? OnAvailableAreaInfosUpdated;

        public string CurrentArea
        {
            get
            {
                return currentArea;
            }
        }

        public IReadOnlyList<string> AvailableAreas
        {
            get
            {
                return availableAreas.AsReadOnly();
            }
        }

        public IReadOnlyList<AreaInfo> AvailableAreaInfos
        {
            get
            {
                return availableAreaInfos.AsReadOnly();
            }
        }

        private bool IsTransportConnected => transport != null && transport.IsConnected;

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
                msg.OtherCharId = -1;
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

                ICMessage.SerializationOptions serializationOptions = new ICMessage.SerializationOptions
                {
                    IncludeCcccIcSupport = SupportsServerFeature("CCCC_IC_SUPPORT"),
                    IncludeLoopingSfx = SupportsServerFeature("LOOPING_SFX"),
                    IncludeAdditive = SupportsServerFeature("ADDITIVE"),
                    IncludeEffects = SupportsServerFeature("EFFECTS"),
                    IncludeCustomBlips = SupportsServerFeature("CUSTOM_BLIPS"),
                    IncludeVerticalOffset = SupportsServerFeature("Y_OFFSET"),
                    IncludeSlide = SupportsServerFeature("CUSTOM_BLIPS")
                };
                string command = ICMessage.GetCommand(msg, serializationOptions);
                CustomConsole.Debug("Outgoing IC packet: " + command);

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
            if (switchPosWhenChangingINI || string.IsNullOrEmpty(curPos))
            {
                SetPos(character.configINI.Side);
            }


            OnChangedCharacter?.Invoke(character);
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

                CustomConsole.Debug("Server features: " + string.Join(", ", serverFeatures.OrderBy(feature => feature, StringComparer.OrdinalIgnoreCase)));
            }
            else if (message.StartsWith("MS#"))
            {
                ICMessage? icMessage = ICMessage.FromConsoleLine(message);
                if (icMessage != null)
                {
                    // Handle IC message
                    OnMessageReceived?.Invoke("IC", icMessage.Character, icMessage.ShowName, icMessage.Message, icMessage.CharId);
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

                if (messageText.ToLower().Contains("people in this area: ") && messageText.ToLower().Contains("===") && messageText.Split("\n").Length > 3)
                {
                    List<Player> players = AO2Parser.ParseGetArea(messageText);
                    Match areaMatch = Regex.Match(messageText, @"people in this area:\s*(.+?)\s*===", RegexOptions.IgnoreCase);
                    if (areaMatch.Success)
                    {
                        string parsedArea = areaMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(parsedArea))
                        {
                            if (ShouldIgnoreAreaDowngrade(parsedArea))
                            {
                                return;
                            }

                            SetCurrentArea(parsedArea);
                        }
                    }
                }

                // Handle OOC message
                //you cant get the char id from an ooc message, so just send -1
                OnMessageReceived?.Invoke("OOC", "", showname, messageText, -1);
                OnOOCMessageReceived?.Invoke(showname, messageText, fromServer);
            }
            else if (message.StartsWith("SP#"))
            {
                var fields = message.Split("#");
                var newPos = fields[1];

                if (!string.IsNullOrEmpty(newPos))
                {
                    SetPos(newPos);
                }
            }
            else if (message.StartsWith("BN#"))
            {
                var fields = message.Split("#");
                var newBG = fields[1];

                curBG = newBG;
                OnBGChange?.Invoke(newBG);
            }
        }

        #region Connection Related Methods
        public async Task Connect(int betweenHandshakeAndSetArea = 0, int betweenSetAreas = 0, int betweenAreasAndIniPuppet = 1000, int finalDelay = 1000)
        {
            aliveTime.Reset();
            dead = false;
            lastCharsCheck = string.Empty;
            serverFeatures.Clear();
            serverCharacterList.Clear();
            availableAreas.Clear();
            availableAreaInfos.Clear();

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

                await SelectFirstAvailableINIPuppet();
                CustomConsole.Info("===========================");

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

            return CurrentINI?.configINI?.ShowName ?? string.Empty;
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

            if (string.Equals(currentArea, newArea, StringComparison.Ordinal))
            {
                return;
            }

            currentArea = newArea;
            OnCurrentAreaChanged?.Invoke(currentArea);
        }

        private void ReplaceAvailableAreas(IEnumerable<string> areas)
        {
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
                availableAreaInfos.Add(new AreaInfo(areaName, 0, "Unknown", "Unknown", "Unknown"));
            }

            OnAvailableAreasUpdated?.Invoke(availableAreas.AsReadOnly());
            OnAvailableAreaInfosUpdated?.Invoke(availableAreaInfos.AsReadOnly());
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
            foreach (string entry in content)
            {
                if (LooksLikeMusicEntry(entry))
                {
                    break;
                }

                areas.Add(entry);
            }

            ReplaceAvailableAreas(areas);
        }

        private void ParseAreaUpdate(string message)
        {
            string[] content = message.Substring(5).TrimEnd('#', '%')
                .Split('#', StringSplitOptions.RemoveEmptyEntries);

            if (content.Length == 0)
            {
                return;
            }

            if (!int.TryParse(content[0], out int updateType))
            {
                CustomConsole.Warning($"Malformed ARUP packet type: {message}");
                return;
            }

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
                    targetArea.Status = value;
                }
                else if (updateType == 2)
                {
                    targetArea.CaseManager = value;
                }
                else if (updateType == 3)
                {
                    targetArea.LockState = value;
                }
            }

            OnAvailableAreaInfosUpdated?.Invoke(availableAreaInfos.AsReadOnly());
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
                string serverVersion = parts[2];
                CustomConsole.Info($"Assigned Player ID: {playerID} | Server Version: {serverVersion}");
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
        #endregion

        #region Helper methods

        public async Task SelectFirstAvailableINIPuppet(bool iniswapToSelected = true)
        {
            if (!IsTransportConnected)
            {
                CustomConsole.Error("Server connection is not active. Cannot select INI Puppet.");
                return;
            }

            if (!string.IsNullOrEmpty(lastCharsCheck) && lastCharsCheck.StartsWith("CharsCheck#"))
            {
                var parts = lastCharsCheck.Substring(11).TrimEnd('#', '%').Split('#');
                int maxIndex = Math.Min(parts.Length, serverCharacterList.Count);
                for (int i = 0; i < maxIndex; i++)
                {
                    if (parts[i] == "0")
                    {
                        // Select the first available character
                        var characterName = serverCharacterList.ElementAt(i).Key;

                        var ini = CharacterFolder.FullList.FirstOrDefault(c => c.Name == characterName);

                        //if the ini is null, it means you dont have it in your pc, meaning keep looking for an available one you DO have.
                        if (ini != null)
                        {
                            await SelectIniPuppet(i, iniswapToSelected);
                            return;
                        }
                    }
                }
            }
            else
            {
                // Some servers do not send CharsCheck in the same stage as before.
                // Fallback to first server-listed character present locally.
                for (int i = 0; i < serverCharacterList.Count; i++)
                {
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
