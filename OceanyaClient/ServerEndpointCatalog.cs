using Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OceanyaClient
{
    internal enum ServerEndpointSource
    {
        Defaults,
        AoServerPoll,
        Favorites
    }

    internal sealed class ServerEndpointDefinition
    {
        public int DisplayId { get; set; }
        public required string Name { get; init; }
        public required string Endpoint { get; init; }
        public required string Description { get; init; }
        public required ServerEndpointSource Source { get; init; }
        public required bool IsLegacy { get; init; }
        public bool IsAoClientCompatible { get; set; } = true;
        public bool IsOnline { get; set; }
        public int? OnlinePlayers { get; set; }
        public int? MaxPlayers { get; set; }

        public bool IsSelectable => IsOnline
            && OnlinePlayers.HasValue
            && MaxPlayers.HasValue
            && !IsLegacy
            && IsAoClientCompatible
            && InitialConfigurationWindow.IsValidServerEndpoint(Endpoint);

        public string SourceDisplayName => Source switch
        {
            ServerEndpointSource.Defaults => "Defaults",
            ServerEndpointSource.AoServerPoll => "AO Server Poll",
            ServerEndpointSource.Favorites => "Favorites",
            _ => "Unknown"
        };

        public string AvailabilityText
        {
            get
            {
                if (!IsOnline)
                {
                    return "Offline";
                }

                if (OnlinePlayers.HasValue && MaxPlayers.HasValue)
                {
                    return $"Online: {OnlinePlayers.Value}/{MaxPlayers.Value}";
                }

                return "Online";
            }
        }

        public string PlayersText
        {
            get
            {
                if (!IsOnline || !OnlinePlayers.HasValue || !MaxPlayers.HasValue)
                {
                    return string.Empty;
                }

                return $"{OnlinePlayers.Value}/{MaxPlayers.Value}";
            }
        }
    }

    internal sealed class FavoriteServerEntry
    {
        public required string Name { get; init; }
        public required string Address { get; init; }
        public required int Port { get; init; }
        public required string Description { get; init; }
        public required bool Legacy { get; init; }

        public string Endpoint
        {
            get
            {
                string scheme = Legacy ? "tcp" : "ws";
                return $"{scheme}://{Address}:{Port}";
            }
        }
    }

    internal static class ServerEndpointCatalog
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string DefaultMasterServerUrl = "http://servers.aceattorneyonline.com";
        private const int AoProbeConcurrency = 6;
        private const int AoProbeTimeoutMs = 6000;
        private const string AoProbeClientName = "AO2";
        private const string AoProbeClientVersion = "2.11.0";

        public static async Task<List<ServerEndpointDefinition>> LoadAsync(string configIniPath, CancellationToken cancellationToken)
        {
            List<ServerEndpointDefinition> pollServers = await LoadAoServerPollAsync(configIniPath, cancellationToken);
            await PopulateAoPlayerCountsAsync(
                pollServers.Where(server => !server.IsLegacy),
                cancellationToken);
            List<ServerEndpointDefinition> defaultServers = LoadDefaultServers();
            List<ServerEndpointDefinition> favoriteServers = LoadFavorites(configIniPath);

            Dictionary<string, ServerEndpointDefinition> pollByEndpointKey = pollServers
                .Select(server => (Key: GetEndpointKey(server.Endpoint), Server: server))
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Server, StringComparer.OrdinalIgnoreCase);

            ApplyKnownStatus(defaultServers, pollByEndpointKey);
            ApplyKnownStatus(favoriteServers, pollByEndpointKey);

            List<ServerEndpointDefinition> unresolvedServers = defaultServers
                .Concat(favoriteServers)
                .Where(server => !server.IsOnline && server.IsSelectable)
                .ToList();
            await PopulateReachabilityAsync(unresolvedServers, cancellationToken);

            List<ServerEndpointDefinition> servers = new List<ServerEndpointDefinition>();
            servers.AddRange(defaultServers);
            servers.AddRange(pollServers);
            servers.AddRange(favoriteServers);

            return servers
                .OrderBy(server => server.Source)
                .ThenBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<ServerEndpointDefinition> LoadDefaultServers()
        {
            List<ServerEndpointDefinition> defaults = new List<ServerEndpointDefinition>();
            foreach (KeyValuePair<Globals.Servers, string> entry in Globals.IPs.OrderBy(item => item.Key))
            {
                string endpoint = (entry.Value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    continue;
                }

                defaults.Add(new ServerEndpointDefinition
                {
                    Name = GetPresetDisplayName(entry.Key),
                    Endpoint = endpoint,
                    Description = "Known-good default endpoint.",
                    Source = ServerEndpointSource.Defaults,
                    IsLegacy = false
                });
            }

            return defaults;
        }

        public static List<ServerEndpointDefinition> LoadFavorites(string configIniPath)
        {
            string favoritesPath = GetFavoritesPath(configIniPath);
            List<FavoriteServerEntry> favorites = FavoriteServerStore.LoadFavorites(favoritesPath);

            return favorites
                .Select(favorite => new ServerEndpointDefinition
                {
                    Name = favorite.Name,
                    Endpoint = favorite.Endpoint,
                    Description = favorite.Description,
                    Source = ServerEndpointSource.Favorites,
                    IsLegacy = favorite.Legacy
                })
                .ToList();
        }

        public static void AddFavorite(string configIniPath, FavoriteServerEntry entry)
        {
            string favoritesPath = GetFavoritesPath(configIniPath);
            FavoriteServerStore.AddFavorite(favoritesPath, entry);
        }

        public static void UpdateFavorite(string configIniPath, int index, FavoriteServerEntry entry)
        {
            string favoritesPath = GetFavoritesPath(configIniPath);
            FavoriteServerStore.UpdateFavorite(favoritesPath, index, entry);
        }

        public static void RemoveFavorite(string configIniPath, int index)
        {
            string favoritesPath = GetFavoritesPath(configIniPath);
            FavoriteServerStore.RemoveFavorite(favoritesPath, index);
        }

        public static int FindFavoriteIndexByEndpoint(string configIniPath, string endpoint)
        {
            string favoritesPath = GetFavoritesPath(configIniPath);
            List<FavoriteServerEntry> favorites = FavoriteServerStore.LoadFavorites(favoritesPath);
            return favorites.FindIndex(favorite => string.Equals(favorite.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
        }

        public static string ResolveMasterServerUrl(string configIniPath)
        {
            if (!File.Exists(configIniPath))
            {
                return DefaultMasterServerUrl;
            }

            foreach (string line in File.ReadLines(configIniPath))
            {
                if (!line.StartsWith("master=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string configuredMasterServer = line.Substring("master=".Length).Trim();
                if (!Uri.TryCreate(configuredMasterServer, UriKind.Absolute, out Uri? uri) || uri == null)
                {
                    continue;
                }

                if (!uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return configuredMasterServer.TrimEnd('/');
            }

            return DefaultMasterServerUrl;
        }

        private static async Task<List<ServerEndpointDefinition>> LoadAoServerPollAsync(string configIniPath, CancellationToken cancellationToken)
        {
            List<ServerEndpointDefinition> result = new List<ServerEndpointDefinition>();
            string masterServerUrl = ResolveMasterServerUrl(configIniPath);
            string requestUrl = masterServerUrl + "/servers";

            try
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.TryAddWithoutValidation("User-Agent", "AttorneyOnline/OceanyaClient (Desktop)");

                using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                string content = await response.Content.ReadAsStringAsync(cancellationToken);
                using JsonDocument json = JsonDocument.Parse(content);

                if (json.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return result;
                }

                foreach (JsonElement entry in json.RootElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string name = entry.TryGetProperty("name", out JsonElement nameElement)
                        ? nameElement.GetString() ?? "Unnamed Server"
                        : "Unnamed Server";
                    string ip = entry.TryGetProperty("ip", out JsonElement ipElement)
                        ? ipElement.GetString() ?? string.Empty
                        : string.Empty;
                    string description = entry.TryGetProperty("description", out JsonElement descriptionElement)
                        ? descriptionElement.GetString() ?? "No description provided."
                        : "No description provided.";

                    if (string.IsNullOrWhiteSpace(ip))
                    {
                        continue;
                    }

                    bool hasWebsocketPort = entry.TryGetProperty("ws_port", out JsonElement websocketPortElement);
                    bool hasLegacyPort = entry.TryGetProperty("port", out JsonElement legacyPortElement);

                    int port = 0;
                    bool legacy = false;

                    if (hasWebsocketPort)
                    {
                        port = websocketPortElement.GetInt32();
                    }
                    else if (hasLegacyPort)
                    {
                        port = legacyPortElement.GetInt32();
                        legacy = true;
                    }

                    if (port <= 0)
                    {
                        continue;
                    }

                    string scheme = legacy ? "tcp" : "ws";
                    string endpoint = $"{scheme}://{ip}:{port}";

                    (int? onlinePlayers, int? maxPlayers) = ExtractPlayersAndCapacity(entry);

                    result.Add(new ServerEndpointDefinition
                    {
                        Name = name,
                        Endpoint = endpoint,
                        Description = description,
                        Source = ServerEndpointSource.AoServerPoll,
                        IsLegacy = legacy,
                        IsOnline = true,
                        OnlinePlayers = onlinePlayers,
                        MaxPlayers = maxPlayers
                    });
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Failed to poll AO master server list.", ex);
            }

            return result;
        }

        private static void ApplyKnownStatus(
            IEnumerable<ServerEndpointDefinition> servers,
            IReadOnlyDictionary<string, ServerEndpointDefinition> knownStatuses)
        {
            foreach (ServerEndpointDefinition server in servers)
            {
                string key = GetEndpointKey(server.Endpoint);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!knownStatuses.TryGetValue(key, out ServerEndpointDefinition? knownStatus))
                {
                    continue;
                }

                server.IsOnline = knownStatus.IsOnline;
                server.OnlinePlayers = knownStatus.OnlinePlayers;
                server.MaxPlayers = knownStatus.MaxPlayers;
                server.IsAoClientCompatible = knownStatus.IsAoClientCompatible;
            }
        }

        private static async Task PopulateReachabilityAsync(IEnumerable<ServerEndpointDefinition> servers, CancellationToken cancellationToken)
        {
            List<Task> tasks = new List<Task>();
            using SemaphoreSlim concurrency = new SemaphoreSlim(8);

            foreach (ServerEndpointDefinition server in servers)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await concurrency.WaitAsync(cancellationToken);
                    try
                    {
                        server.IsOnline = await IsEndpointReachableAsync(server.Endpoint, cancellationToken);
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private static async Task PopulateAoPlayerCountsAsync(
            IEnumerable<ServerEndpointDefinition> servers,
            CancellationToken cancellationToken)
        {
            List<ServerEndpointDefinition> targets = servers.ToList();
            if (targets.Count == 0)
            {
                return;
            }

            List<Task> tasks = new List<Task>();
            using SemaphoreSlim concurrency = new SemaphoreSlim(AoProbeConcurrency);

            foreach (ServerEndpointDefinition server in targets)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await concurrency.WaitAsync(cancellationToken);
                    try
                    {
                        (bool success, int? players, int? maxPlayers, bool incompatibleClient) =
                            await ProbeAoPlayerCountAsync(server.Endpoint, cancellationToken);

                        if (incompatibleClient)
                        {
                            server.IsOnline = true;
                            server.IsAoClientCompatible = false;
                            return;
                        }

                        if (success)
                        {
                            server.IsOnline = true;
                            server.IsAoClientCompatible = true;
                            server.OnlinePlayers = players;
                            server.MaxPlayers = maxPlayers;
                            return;
                        }

                        // Fallback status check if PN probe fails.
                        server.IsOnline = await IsEndpointReachableAsync(server.Endpoint, cancellationToken);
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private static async Task<bool> IsEndpointReachableAsync(string endpoint, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return false;
            }

            if (uri.Port <= 0 || string.IsNullOrWhiteSpace(uri.Host))
            {
                return false;
            }

            try
            {
                using TcpClient client = new TcpClient();
                Task connectTask = client.ConnectAsync(uri.Host, uri.Port);
                Task timeoutTask = Task.Delay(1200, cancellationToken);
                Task completed = await Task.WhenAny(connectTask, timeoutTask);
                if (completed != connectTask)
                {
                    return false;
                }

                await connectTask;
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<(bool Success, int? Players, int? MaxPlayers, bool IncompatibleClient)> ProbeAoPlayerCountAsync(
            string endpoint,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return (false, null, null, false);
            }

            bool validScheme = string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
            if (!validScheme)
            {
                return (false, null, null, false);
            }

            try
            {
                using ClientWebSocket webSocket = new ClientWebSocket();
                webSocket.Options.SetRequestHeader(
                    "User-Agent",
                    $"AttorneyOnline/{AoProbeClientVersion} (Desktop)");

                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(AoProbeTimeoutMs);
                CancellationToken timeoutToken = timeoutCts.Token;

                await webSocket.ConnectAsync(uri, timeoutToken);

                string hdid = Guid.NewGuid().ToString();
                StringBuilder packetBuffer = new StringBuilder();
                ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[8192]);

                while (webSocket.State == WebSocketState.Open && !timeoutToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(buffer, timeoutToken);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (receiveResult.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    string text = Encoding.UTF8.GetString(buffer.Array!, 0, receiveResult.Count);
                    packetBuffer.Append(text);

                    bool keepParsing = true;
                    while (keepParsing)
                    {
                        string current = packetBuffer.ToString();
                        int packetEnd = current.IndexOf("#%", StringComparison.Ordinal);
                        if (packetEnd < 0)
                        {
                            keepParsing = false;
                            continue;
                        }

                        string packet = current.Substring(0, packetEnd + 1); // include trailing '#'
                        packetBuffer.Remove(0, packetEnd + 2);

                        List<string> outgoingPackets = GetProbeFollowUpPackets(packet, hdid);
                        foreach (string outgoingPacket in outgoingPackets)
                        {
                            await SendWsPacketAsync(webSocket, outgoingPacket, timeoutToken);
                        }

                        if (TryParseProbePlayerCountPacket(packet, out int players, out int maxPlayers))
                        {
                            if (webSocket.State == WebSocketState.Open)
                            {
                                await webSocket.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    "Probe complete",
                                    CancellationToken.None);
                            }

                            return (true, players, maxPlayers, false);
                        }

                        if (packet.StartsWith("BD#", StringComparison.OrdinalIgnoreCase))
                        {
                            if (webSocket.State == WebSocketState.Open)
                            {
                                await webSocket.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    "Probe rejected by server",
                                    CancellationToken.None);
                            }

                            return (false, null, null, true);
                        }
                    }
                }
            }
            catch
            {
                return (false, null, null, false);
            }

            return (false, null, null, false);
        }

        internal static List<string> GetProbeFollowUpPackets(string packet, string hdid)
        {
            List<string> outgoingPackets = new List<string>();
            if (packet.StartsWith("decryptor#", StringComparison.OrdinalIgnoreCase))
            {
                outgoingPackets.Add($"HI#{hdid}#%");
                return outgoingPackets;
            }

            if (!packet.StartsWith("ID#", StringComparison.OrdinalIgnoreCase))
            {
                return outgoingPackets;
            }

            // Match AO2 identity packet flow, then request a join transition.
            // Some servers emit PN only after askchaa.
            outgoingPackets.Add($"ID#{AoProbeClientName}#{AoProbeClientVersion}#%");
            outgoingPackets.Add("askchaa#%");
            return outgoingPackets;
        }

        internal static bool TryParseProbePlayerCountPacket(string packet, out int players, out int maxPlayers)
        {
            players = default;
            maxPlayers = default;

            if (!packet.StartsWith("PN#", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalizedPacket = packet.EndsWith("#", StringComparison.Ordinal)
                ? packet.Substring(0, packet.Length - 1)
                : packet;
            string[] parts = normalizedPacket.Split('#');
            if (parts.Length < 3)
            {
                return false;
            }

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out players))
            {
                players = 0;
            }

            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out maxPlayers))
            {
                maxPlayers = 0;
            }

            return true;
        }

        private static async Task SendWsPacketAsync(ClientWebSocket webSocket, string packet, CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(packet);
            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }

        private static int? GetCountValue(JsonElement source, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!source.TryGetProperty(key, out JsonElement valueElement))
                {
                    continue;
                }

                if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetInt32(out int intValue))
                {
                    return intValue;
                }

                if (valueElement.ValueKind == JsonValueKind.String
                    && int.TryParse(valueElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
                {
                    return parsedValue;
                }
            }

            return null;
        }

        private static (int? OnlinePlayers, int? MaxPlayers) ExtractPlayersAndCapacity(JsonElement source)
        {
            int? online = GetCountValue(
                source,
                "players",
                "player_count",
                "playercount",
                "online",
                "clients",
                "users",
                "current_players");

            int? max = GetCountValue(
                source,
                "maxplayers",
                "max_players",
                "maxplayer",
                "capacity",
                "max",
                "max_clients",
                "slots");

            foreach (JsonProperty property in source.EnumerateObject())
            {
                string keyLower = property.Name.ToLowerInvariant();
                if (!LooksLikePlayerCountKey(keyLower))
                {
                    continue;
                }

                JsonElement value = property.Value;
                if (value.ValueKind == JsonValueKind.String)
                {
                    TryExtractPlayerPairFromText(value.GetString() ?? string.Empty, ref online, ref max);
                    continue;
                }

                if (value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                int? nestedOnline = GetCountValue(
                    value,
                    "players",
                    "player_count",
                    "online",
                    "current",
                    "count",
                    "users");
                int? nestedMax = GetCountValue(
                    value,
                    "max",
                    "maxplayers",
                    "max_players",
                    "capacity",
                    "slots");

                online ??= nestedOnline;
                max ??= nestedMax;

                foreach (JsonProperty nestedProperty in value.EnumerateObject())
                {
                    if (nestedProperty.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string nestedKeyLower = nestedProperty.Name.ToLowerInvariant();
                    if (!LooksLikePlayerCountKey(nestedKeyLower))
                    {
                        continue;
                    }

                    TryExtractPlayerPairFromText(nestedProperty.Value.GetString() ?? string.Empty, ref online, ref max);
                }
            }

            return (online, max);
        }

        private static bool LooksLikePlayerCountKey(string keyLower)
        {
            return keyLower.Contains("player", StringComparison.Ordinal)
                || keyLower.Contains("online", StringComparison.Ordinal)
                || keyLower.Contains("count", StringComparison.Ordinal)
                || keyLower.Contains("slot", StringComparison.Ordinal)
                || keyLower.Contains("capacity", StringComparison.Ordinal)
                || keyLower.Contains("status", StringComparison.Ordinal)
                || keyLower.Contains("user", StringComparison.Ordinal);
        }

        private static void TryExtractPlayerPairFromText(string text, ref int? online, ref int? max)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Match slashPattern = Regex.Match(text, @"(?<online>\d+)\s*/\s*(?<max>\d+)", RegexOptions.IgnoreCase);
            if (slashPattern.Success)
            {
                if (!online.HasValue
                    && int.TryParse(slashPattern.Groups["online"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedOnline))
                {
                    online = parsedOnline;
                }

                if (!max.HasValue
                    && int.TryParse(slashPattern.Groups["max"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax))
                {
                    max = parsedMax;
                }

                return;
            }

            Match onlinePattern = Regex.Match(text, @"online[^0-9]*(?<online>\d+)[^0-9]+(?<max>\d+)", RegexOptions.IgnoreCase);
            if (!onlinePattern.Success)
            {
                return;
            }

            if (!online.HasValue
                && int.TryParse(onlinePattern.Groups["online"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int altOnline))
            {
                online = altOnline;
            }

            if (!max.HasValue
                && int.TryParse(onlinePattern.Groups["max"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int altMax))
            {
                max = altMax;
            }
        }

        private static string GetEndpointKey(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0)
            {
                return string.Empty;
            }

            return $"{uri.Host}:{uri.Port}";
        }

        private static string GetFavoritesPath(string configIniPath)
        {
            string configDirectory = Path.GetDirectoryName(configIniPath) ?? string.Empty;
            return Path.Combine(configDirectory, "favorite_servers.ini");
        }

        private static string GetPresetDisplayName(Globals.Servers server)
        {
            return server switch
            {
                Globals.Servers.ChillAndDices => "Chill and Dices",
                Globals.Servers.CaseCafe => "Case Cafe",
                Globals.Servers.Vanilla => "Vanilla",
                _ => server.ToString()
            };
        }
    }

    internal static class FavoriteServerStore
    {
        public static List<FavoriteServerEntry> LoadFavorites(string favoritesPath)
        {
            List<FavoriteServerEntry> favorites = new List<FavoriteServerEntry>();
            if (!File.Exists(favoritesPath))
            {
                return favorites;
            }

            Dictionary<int, Dictionary<string, string>> sections = ParseIniSections(favoritesPath);
            foreach (KeyValuePair<int, Dictionary<string, string>> section in sections.OrderBy(entry => entry.Key))
            {
                Dictionary<string, string> values = section.Value;
                string name = UnescapeIniValue(GetValue(values, "name", "Missing Name"));
                string address = UnescapeIniValue(GetValue(values, "address", "127.0.0.1"));
                string portText = GetValue(values, "port", "27016");
                string description = UnescapeIniValue(GetValue(values, "desc", "No description"));
                bool legacy = ParseLegacyValue(values);

                if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port <= 0)
                {
                    port = 27016;
                }

                favorites.Add(new FavoriteServerEntry
                {
                    Name = name,
                    Address = address,
                    Port = port,
                    Description = description,
                    Legacy = legacy
                });
            }

            return favorites;
        }

        public static void SaveFavorites(string favoritesPath, List<FavoriteServerEntry> favorites)
        {
            string? directory = Path.GetDirectoryName(favoritesPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            StringBuilder builder = new StringBuilder();
            for (int index = 0; index < favorites.Count; index++)
            {
                FavoriteServerEntry server = favorites[index];
                builder.AppendLine($"[{index}]");
                builder.AppendLine($"name={EscapeIniValue(server.Name)}");
                builder.AppendLine($"address={EscapeIniValue(server.Address)}");
                builder.AppendLine($"port={server.Port.ToString(CultureInfo.InvariantCulture)}");
                builder.AppendLine($"desc={EscapeIniValue(server.Description)}");
                builder.AppendLine($"legacy={(server.Legacy ? "true" : "false")}");
                builder.AppendLine();
            }

            File.WriteAllText(favoritesPath, builder.ToString());
        }

        public static void AddFavorite(string favoritesPath, FavoriteServerEntry entry)
        {
            List<FavoriteServerEntry> favorites = LoadFavorites(favoritesPath);
            favorites.Add(entry);
            SaveFavorites(favoritesPath, favorites);
        }

        public static void UpdateFavorite(string favoritesPath, int index, FavoriteServerEntry entry)
        {
            List<FavoriteServerEntry> favorites = LoadFavorites(favoritesPath);
            if (index < 0 || index >= favorites.Count)
            {
                return;
            }

            favorites[index] = entry;
            SaveFavorites(favoritesPath, favorites);
        }

        public static void RemoveFavorite(string favoritesPath, int index)
        {
            List<FavoriteServerEntry> favorites = LoadFavorites(favoritesPath);
            if (index < 0 || index >= favorites.Count)
            {
                return;
            }

            favorites.RemoveAt(index);
            SaveFavorites(favoritesPath, favorites);
        }

        private static Dictionary<int, Dictionary<string, string>> ParseIniSections(string path)
        {
            Dictionary<int, Dictionary<string, string>> sections = new Dictionary<int, Dictionary<string, string>>();
            int? currentSection = null;

            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                {
                    continue;
                }

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string sectionName = line.Substring(1, line.Length - 2);
                    if (int.TryParse(sectionName, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sectionNumber)
                        && sectionNumber >= 0)
                    {
                        currentSection = sectionNumber;
                        if (!sections.ContainsKey(sectionNumber))
                        {
                            sections[sectionNumber] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        }
                    }
                    else
                    {
                        currentSection = null;
                    }

                    continue;
                }

                if (currentSection == null)
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                sections[currentSection.Value][key] = value;
            }

            return sections;
        }

        private static bool ParseLegacyValue(Dictionary<string, string> values)
        {
            if (values.TryGetValue("protocol", out string? protocol))
            {
                return string.Equals(protocol, "tcp", StringComparison.OrdinalIgnoreCase);
            }

            if (values.TryGetValue("legacy", out string? legacyValue))
            {
                return legacyValue.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || legacyValue.Equals("1", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static string GetValue(Dictionary<string, string> values, string key, string fallback)
        {
            if (!values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value;
        }

        private static string EscapeIniValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\r\n", "\\n", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
        }

        private static string UnescapeIniValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }
    }
}
