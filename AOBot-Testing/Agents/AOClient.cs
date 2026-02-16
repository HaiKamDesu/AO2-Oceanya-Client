using AOBot_Testing.Structures;
using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
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
        private ClientWebSocket? ws; // WebSocket instance
        public Stopwatch aliveTime = new Stopwatch();
        private CountdownTimer? speakTimer;
        bool AbleToSpeak { get; set; } = true;


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
            if (ws != null && ws.State == WebSocketState.Open)
            {
                if (CurrentINI == null || currentEmote == null)
                {
                    CustomConsole.Error("Cannot send IC message without selected character/emote.");
                    return;
                }

                ICMessage msg = new ICMessage();
                msg.DeskMod = deskMod;
                msg.PreAnim = currentEmote.PreAnimation;
                msg.Character = CurrentINI.Name;
                msg.Emote = currentEmote.Animation;

                // Fix for red text color not applying
                if (textColor == ICMessage.TextColors.Red)
                {
                    msg.Message = message.StartsWith("~") ? $"{message}~" : $"~{message}~";
                }
                else
                {
                    msg.Message = message;
                }

                msg.Side = curPos;
                msg.SfxName = curSFX == "Default" ? currentEmote.sfxName : curSFX == "Nothing" ? "1" : curSFX;
                msg.EmoteModifier = Immediate ? ICMessage.EmoteModifiers.NoPreanimation : emoteMod;
                msg.CharId = iniPuppetID;
                msg.SfxDelay = currentEmote.sfxDelay;
                msg.ShoutModifier = shoutModifiers;
                msg.EvidenceID = "0";
                msg.Flip = flip;
                msg.Realization = effect == ICMessage.Effects.Realization;
                msg.TextColor = textColor;
                msg.ShowName = ICShowname;
                msg.OtherCharId = -1;
                msg.SelfOffset = SelfOffset;
                msg.NonInterruptingPreAnim = Immediate;
                msg.SfxLooping = false;
                msg.ScreenShake = screenshake;
                msg.FramesShake = $"{currentEmote.PreAnimation}^(b){currentEmote.Animation}^(a){currentEmote.Animation}^";
                msg.FramesRealization = $"{currentEmote.PreAnimation}^(b){currentEmote.Animation}^(a){currentEmote.Animation}^";
                msg.FramesSfx = $"{currentEmote.PreAnimation}^(b){currentEmote.Animation}^(a){currentEmote.Animation}^";
                msg.Additive = Additive;
                msg.Effect = effect;
                msg.Blips = "";

                string command = ICMessage.GetCommand(msg);

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
                CustomConsole.Error("WebSocket is not connected. Cannot send message.");
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
                    if (Globals.DebugMode)
                    {
                        if (speakTimer == null)
                        {
                            break;
                        }

                        var remainingTime = speakTimer.GetRemainingTime();
                        var formattedTime = $"{remainingTime.Hours}h {remainingTime.Minutes}m {remainingTime.Seconds}s {remainingTime.Milliseconds}ms";
                        CustomConsole.Debug($"Cannot speak yet, waiting for {formattedTime}...");
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
            if (ws != null && ws.State == WebSocketState.Open)
            {
                OOCShowname = showname;
                string oocMessage = $"CT#{Globals.ReplaceSymbolsForText(showname)}#{Globals.ReplaceSymbolsForText(message)}#%";
                await SendPacket(oocMessage);
                await Task.Delay(500);
            }
            else
            {
                CustomConsole.Error("WebSocket is not connected. Cannot send message.");
            }
        }
        #endregion

        #region Set Methods
        public async Task SetArea(string areaName, int delayBetweenAreas = 500)
        {
            if (ws != null && ws.State == WebSocketState.Open)
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
                CustomConsole.Error("WebSocket is not connected. Cannot switch rooms.");
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
            lastCharsCheck = string.Empty;
            ws = new ClientWebSocket();
            // Add required headers (modify as needed)
            ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            ws.Options.SetRequestHeader("Origin", "http://example.com");
            try
            {
                await ws.ConnectAsync(serverUri, CancellationToken.None);
                aliveTime.Start();
                CustomConsole.Info("===========================");
                CustomConsole.Info("Connected to AO2's Server WebSocket!");
                CustomConsole.Info("===========================");

                // Start the handshake process
                await PerformHandshake();
                CustomConsole.Info("===========================");

                await Task.Delay(betweenHandshakeAndSetArea);
                if (!string.IsNullOrWhiteSpace(currentArea))
                {
                    await SetArea(currentArea, betweenSetAreas);
                    CustomConsole.Info("===========================");
                }

                //Allow some time for server to update area info

                await Task.Delay(betweenAreasAndIniPuppet);

                await SelectFirstAvailableINIPuppet();
                CustomConsole.Info("===========================");

                // Start listening for messages
                _ = Task.Run(() => ListenForMessages());
                _ = Task.Run(() => KeepAlive());

                // Allow some time for connection
                await Task.Delay(finalDelay);
            }
            catch (Exception ex)
            {
                CustomConsole.Error($"Connection Error", ex);
                throw;
            }
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
            if (ws == null || ws.State != WebSocketState.Open)
            {
                CustomConsole.Error("WebSocket is not connected. Cannot perform handshake.");
                return;
            }

            if (Globals.DebugMode) CustomConsole.Debug("Starting handshake...");

            // Step 1: Send Hard Drive ID (HDID) - Can be anything unique
            hdid = Guid.NewGuid().ToString(); // Generate a unique HDID
            await SendPacket($"HI#{hdid}#%");

            // Step 2: Receive Server Version and Player Number
            string response = await WaitForPacketAsync(packet => packet.StartsWith("ID#"), "ID");

            var parts = response.Split('#');
            if (parts.Length >= 3)
            {
                playerID = int.Parse(parts[1]);
                string serverVersion = parts[2];
                CustomConsole.Info($"Assigned Player ID: {playerID} | Server Version: {serverVersion}");
            }

            // Step 3: Send Client Version Info (Server doesn't really care)
            await SendPacket($"ID#MyBotClient#1.0#%");

            // Step 4: Request Character List (Important for selection)
            await SendPacket("RC#%");

            // Step 5: Receive Character List
            response = await WaitForPacketAsync(packet => packet.StartsWith("SC#"), "SC");

            // Step 8: Send Ready Signal
            await SendPacket("RD#%");

            // Step 9: Wait for final confirmation
            response = await WaitForPacketAsync(packet => packet.StartsWith("DONE#"), "DONE");

            CustomConsole.Info("Handshake completed successfully!");
        }

        private async Task<string> WaitForPacketAsync(Func<string, bool> predicate, string expectedPacketHeader, int timeoutMs = 10000)
        {
            var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < timeoutAt)
            {
                int remainingMs = (int)(timeoutAt - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= 0)
                {
                    break;
                }

                var receiveTask = ReceiveMessageAsync();
                var completedTask = await Task.WhenAny(receiveTask, Task.Delay(remainingMs));
                if (completedTask != receiveTask)
                {
                    break;
                }

                string response = await receiveTask;
                if (!string.IsNullOrEmpty(response) && predicate(response))
                {
                    return response;
                }
            }

            throw new TimeoutException($"Timed out waiting for handshake packet: {expectedPacketHeader}");
        }


        private static SemaphoreSlim _reconnectQueue = new SemaphoreSlim(1, 1);

        private async Task Reconnect()
        {
            // Wait for our turn in the reconnection queue.
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
                        if (ws != null && ws.State == WebSocketState.Open)
                        {
                            CustomConsole.Info("Reconnected to WebSocket!");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        
                        CustomConsole.Error($"Reconnection attempt {retryCount + 1} failed", ex);
                    }
                    OnReconnectionAttemptFailed?.Invoke(retryCount + 1);
                    retryCount++;
                    await Task.Delay(2000); // Delay before retrying this client
                }
                CustomConsole.Error("Failed to reconnect after multiple attempts.");
                await Disconnect();
            }
            finally
            {
                // Ensure a gap between different clients' reconnection attempts.
                await Task.Delay(200);
                _reconnectQueue.Release();
            }
        }
        private async Task KeepAlive()
        {
            while (ws != null && ws.State == WebSocketState.Open)
            {
                await SendPacket($"CH#{playerID}#%");
                await Task.Delay(10000); // Enviar un ping cada 10 segundos
            }
        }
        bool dead = false;

        public async Task Disconnect()
        {
            dead = true;
            if (ws != null && ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                ws.Dispose();
                ws = null;
                CustomConsole.Info("Disconnected from WebSocket.");
            }
            else
            {
                CustomConsole.Info("WebSocket is not connected.");
            }

            OnDisconnect?.Invoke();
        }

        public async Task DisconnectWebsocket()
        {
            if (ws != null && ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
            }
            else
            {
                CustomConsole.Info("WebSocket is not connected.");
            }
        }
        #endregion

        #region Helper methods

        public async Task SelectFirstAvailableINIPuppet(bool iniswapToSelected = true)
        {
            if (ws == null || ws.State != WebSocketState.Open)
            {
                CustomConsole.Error("WebSocket is not connected. Cannot select INI Puppet.");
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

            await SendPacket($"CC#0#{serverCharID}#{hdid}#%");

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
            if (ws != null && ws.State == WebSocketState.Open)
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(packet);
                await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                if (Globals.DebugMode) CustomConsole.Debug($"Sent: {packet}");
            }
            else
            {
                CustomConsole.Error("WebSocket is not connected. Cannot send message.");
            }
        }

        public async Task RequestAreaList()
        {
            await SendPacket("RM#%");
        }

        private async Task<string> ReceiveMessageAsync()
        {
            if (ws != null && ws.State == WebSocketState.Open)
            {
                byte[] buffer = new byte[4096];
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.Count > 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (Globals.DebugMode) CustomConsole.Debug($"Received: {message}");

                    await HandleMessage(message);

                    return message;
                }
            }
            return string.Empty;
        }

        private async Task ListenForMessages()
        {
            CustomConsole.Debug("Listening for messages...");
            try
            {
                while (ws != null && ws.State == WebSocketState.Open)
                {
                    string message = await ReceiveMessageAsync();
                    if (!string.IsNullOrEmpty(message))
                    {
                        // Handle incoming messages here
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
                if (ws == null || ws.State != WebSocketState.Open)
                {
                    aliveTime.Stop();
                    OnWebsocketDisconnect?.Invoke();

                    if (!dead)
                    {
                        CustomConsole.Warning("WebSocket connection lost. Attempting to reconnect...");
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
