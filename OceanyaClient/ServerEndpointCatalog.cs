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
        public int? FavoriteStoreIndex { get; init; }
        public bool IsAoClientCompatible { get; set; } = true;
        public bool IsOnline { get; set; }
        public int? OnlinePlayers { get; set; }
        public int? MaxPlayers { get; set; }

        public bool HasKnownPlayerCounts => OnlinePlayers.HasValue && MaxPlayers.HasValue;

        public bool SupportsDirectConnection => InitialConfigurationWindow.IsValidServerEndpoint(Endpoint);

        public bool IsSelectable => IsOnline && SupportsDirectConnection;

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

                int? onlinePlayers = OnlinePlayers;
                int? maxPlayers = MaxPlayers;
                if (onlinePlayers.HasValue && maxPlayers.HasValue)
                {
                    return $"Online: {onlinePlayers.Value}/{maxPlayers.Value}";
                }

                return "Online: ???/???";
            }
        }

        public string PlayersText
        {
            get
            {
                if (!IsOnline)
                {
                    return string.Empty;
                }

                if (!HasKnownPlayerCounts)
                {
                    return "???/???";
                }

                int? onlinePlayers = OnlinePlayers;
                int? maxPlayers = MaxPlayers;
                return $"{onlinePlayers.GetValueOrDefault()}/{maxPlayers.GetValueOrDefault()}";
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
        public required bool Secure { get; init; }

        public string Endpoint
        {
            get
            {
                string scheme = Legacy ? "tcp" : Secure ? "wss" : "ws";
                return $"{scheme}://{Address}:{Port}";
            }
        }
    }

    internal sealed class AoPlayerCountProbeResult
    {
        public bool Success { get; set; }
        public int? Players { get; set; }
        public int? MaxPlayers { get; set; }
        public bool IncompatibleClient { get; set; }
        public string RejectionReason { get; set; } = string.Empty;
        public List<string> SentPackets { get; } = new List<string>();
        public List<string> ReceivedPackets { get; } = new List<string>();
    }

    internal sealed class EndpointProbeBatch
    {
        public EndpointProbeBatch(string endpoint, List<ServerEndpointDefinition> servers)
        {
            Endpoint = endpoint;
            Servers = servers;
        }

        public string Endpoint { get; }

        public List<ServerEndpointDefinition> Servers { get; }
    }

    internal static class ServerEndpointCatalog
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string DefaultMasterServerUrl = "http://servers.aceattorneyonline.com";
        private const int AoProbeConcurrency = 12;
        private const int ReachabilityProbeConcurrency = 16;
        private const int AoProbeTimeoutMs = 6000;
        private const string AoProbeClientName = "AO2";
        private const string AoProbeClientVersion = "2.11.0";

        public static async Task<List<ServerEndpointDefinition>> LoadAsync(string configIniPath, CancellationToken cancellationToken)
        {
            List<ServerEndpointDefinition> pollServers = await LoadAoServerPollAsync(configIniPath, cancellationToken);
            await PopulateAoPlayerCountsAsync(
                pollServers,
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

            List<ServerEndpointDefinition> supplementalServers = defaultServers
                .Concat(favoriteServers)
                .ToList();
            await PopulateAoPlayerCountsAsync(
                supplementalServers.Where(NeedsSupplementalAoProbe),
                cancellationToken);
            await PopulateReachabilityAsync(
                supplementalServers.Where(NeedsSupplementalReachabilityProbe),
                cancellationToken);

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
                .Select((favorite, index) => new ServerEndpointDefinition
                {
                    Name = favorite.Name,
                    Endpoint = favorite.Endpoint,
                    Description = favorite.Description,
                    Source = ServerEndpointSource.Favorites,
                    IsLegacy = favorite.Legacy,
                    FavoriteStoreIndex = index
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

        internal static bool TryGetFavoriteIndex(ServerEndpointDefinition? server, out int favoriteIndex)
        {
            favoriteIndex = -1;
            if (server?.Source != ServerEndpointSource.Favorites || !server.FavoriteStoreIndex.HasValue)
            {
                return false;
            }

            favoriteIndex = server.FavoriteStoreIndex.Value;
            return favoriteIndex >= 0;
        }

        public static int FindFavoriteIndexByEndpoint(string configIniPath, string endpoint)
        {
            string favoritesPath = GetFavoritesPath(configIniPath);
            List<FavoriteServerEntry> favorites = FavoriteServerStore.LoadFavorites(favoritesPath);
            return favorites.FindIndex(favorite => string.Equals(favorite.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool TryParseDirectEndpoint(
            string endpoint,
            out string address,
            out int port,
            out bool legacy,
            out bool secure)
        {
            address = string.Empty;
            port = 0;
            legacy = false;
            secure = false;

            if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return false;
            }

            bool validScheme = string.Equals(uri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
            if (!validScheme)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0)
            {
                return false;
            }

            address = uri.Host;
            port = uri.Port;
            legacy = string.Equals(uri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase);
            secure = string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        internal static bool IsLegacyEndpoint(string endpoint)
        {
            return TryParseDirectEndpoint(endpoint, out _, out _, out bool legacy, out _) && legacy;
        }

        internal static string GetNotSelectableReason(ServerEndpointDefinition server)
        {
            List<string> reasons = new List<string>();

            if (!server.IsOnline)
            {
                reasons.Add("Server appears offline.");
            }

            if (!InitialConfigurationWindow.IsValidServerEndpoint(server.Endpoint))
            {
                reasons.Add("Invalid server endpoint.");
            }

            if (reasons.Count == 0)
            {
                reasons.Add("Unavailable for connection.");
            }

            return "Reason: " + string.Join(" ", reasons);
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
            await PopulateReachabilityAsyncForTesting(
                servers,
                IsEndpointReachableAsync,
                cancellationToken);
        }

        internal static async Task PopulateReachabilityAsyncForTesting(
            IEnumerable<ServerEndpointDefinition> servers,
            Func<string, CancellationToken, Task<bool>> reachabilityProbeAsync,
            CancellationToken cancellationToken)
        {
            List<EndpointProbeBatch> batches = CreateEndpointProbeBatches(servers);
            if (batches.Count == 0)
            {
                return;
            }

            List<Task> tasks = new List<Task>();
            using SemaphoreSlim concurrency = new SemaphoreSlim(ReachabilityProbeConcurrency);

            foreach (EndpointProbeBatch batch in batches)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await concurrency.WaitAsync(cancellationToken);
                    try
                    {
                        bool isOnline = await reachabilityProbeAsync(batch.Endpoint, cancellationToken);
                        foreach (ServerEndpointDefinition server in batch.Servers)
                        {
                            server.IsOnline = isOnline;
                        }
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
            await PopulateAoPlayerCountsAsyncForTesting(
                servers,
                ProbeAoPlayerCountAsync,
                IsEndpointReachableAsync,
                cancellationToken);
        }

        internal static async Task PopulateAoPlayerCountsAsyncForTesting(
            IEnumerable<ServerEndpointDefinition> servers,
            Func<string, CancellationToken, Task<(bool Success, int? Players, int? MaxPlayers, bool IncompatibleClient)>> aoProbeAsync,
            Func<string, CancellationToken, Task<bool>> reachabilityProbeAsync,
            CancellationToken cancellationToken)
        {
            List<EndpointProbeBatch> batches = CreateEndpointProbeBatches(servers);
            if (batches.Count == 0)
            {
                return;
            }

            List<Task> tasks = new List<Task>();
            using SemaphoreSlim concurrency = new SemaphoreSlim(AoProbeConcurrency);

            foreach (EndpointProbeBatch batch in batches)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await concurrency.WaitAsync(cancellationToken);
                    try
                    {
                        (bool success, int? players, int? maxPlayers, bool incompatibleClient) =
                            await aoProbeAsync(batch.Endpoint, cancellationToken);

                        if (incompatibleClient)
                        {
                            foreach (ServerEndpointDefinition server in batch.Servers)
                            {
                                server.IsOnline = true;
                                server.IsAoClientCompatible = false;
                            }
                            return;
                        }

                        if (success)
                        {
                            foreach (ServerEndpointDefinition server in batch.Servers)
                            {
                                server.IsOnline = true;
                                server.IsAoClientCompatible = true;
                                server.OnlinePlayers = players;
                                server.MaxPlayers = maxPlayers;
                            }
                            return;
                        }

                        // Fallback status check if PN probe fails.
                        bool isOnline = await reachabilityProbeAsync(batch.Endpoint, cancellationToken);
                        foreach (ServerEndpointDefinition server in batch.Servers)
                        {
                            server.IsOnline = isOnline;
                            server.IsAoClientCompatible = true;
                        }
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private static List<EndpointProbeBatch> CreateEndpointProbeBatches(IEnumerable<ServerEndpointDefinition> servers)
        {
            return servers
                .Where(server => server != null)
                .GroupBy(server => GetEndpointProbeBatchKey(server.Endpoint), StringComparer.OrdinalIgnoreCase)
                .Select(group => new EndpointProbeBatch(group.First().Endpoint, group.ToList()))
                .ToList();
        }

        private static string GetEndpointProbeBatchKey(string endpoint)
        {
            string endpointKey = GetEndpointKey(endpoint);
            if (!string.IsNullOrWhiteSpace(endpointKey))
            {
                return endpointKey;
            }

            return endpoint?.Trim() ?? string.Empty;
        }

        internal static async Task PopulateSupplementalStatusAsync(
            IEnumerable<ServerEndpointDefinition> servers,
            CancellationToken cancellationToken)
        {
            List<ServerEndpointDefinition> targets = servers.ToList();
            if (targets.Count == 0)
            {
                return;
            }

            await PopulateAoPlayerCountsAsync(
                targets.Where(NeedsSupplementalAoProbe),
                cancellationToken);
            await PopulateReachabilityAsync(
                targets.Where(NeedsSupplementalReachabilityProbe),
                cancellationToken);
        }

        internal static bool NeedsSupplementalAoProbe(ServerEndpointDefinition? server)
        {
            if (server == null)
            {
                return false;
            }

            return server.SupportsDirectConnection
                && (!server.IsOnline || !server.OnlinePlayers.HasValue || !server.MaxPlayers.HasValue);
        }

        internal static bool NeedsSupplementalReachabilityProbe(ServerEndpointDefinition? server)
        {
            if (server == null)
            {
                return false;
            }

            return !NeedsSupplementalAoProbe(server)
                && !server.IsOnline
                && CanAttemptReachabilityProbe(server.Endpoint);
        }

        private static async Task<bool> IsEndpointReachableAsync(string endpoint, CancellationToken cancellationToken)
        {
            if (!TryParseReachabilityUri(endpoint, out Uri? uri))
            {
                return false;
            }

            try
            {
                Uri resolvedUri = uri ?? throw new InvalidOperationException("Reachability URI was not parsed.");
                using TcpClient client = new TcpClient();
                Task connectTask = client.ConnectAsync(resolvedUri.Host, resolvedUri.Port);
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

        private static bool CanAttemptReachabilityProbe(string endpoint)
        {
            return TryParseReachabilityUri(endpoint, out _);
        }

        private static bool TryParseReachabilityUri(string endpoint, out Uri? uri)
        {
            uri = null;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsedUri) || parsedUri == null)
            {
                return false;
            }

            if (parsedUri.Port <= 0 || string.IsNullOrWhiteSpace(parsedUri.Host))
            {
                return false;
            }

            uri = parsedUri;
            return true;
        }

        private static async Task<(bool Success, int? Players, int? MaxPlayers, bool IncompatibleClient)> ProbeAoPlayerCountAsync(
            string endpoint,
            CancellationToken cancellationToken)
        {
            AoPlayerCountProbeResult result = await ProbeAoPlayerCountDetailedAsync(
                endpoint,
                cancellationToken,
                captureTranscript: false);
            return (result.Success, result.Players, result.MaxPlayers, result.IncompatibleClient);
        }

        internal static Task<AoPlayerCountProbeResult> ProbeAoPlayerCountDetailedForTestingAsync(
            string endpoint,
            CancellationToken cancellationToken)
        {
            return ProbeAoPlayerCountDetailedAsync(endpoint, cancellationToken, captureTranscript: true);
        }

        private static async Task<AoPlayerCountProbeResult> ProbeAoPlayerCountDetailedAsync(
            string endpoint,
            CancellationToken cancellationToken,
            bool captureTranscript)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return new AoPlayerCountProbeResult();
            }

            bool isWebSocket = string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
            bool isTcp = string.Equals(uri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase);
            if (!isWebSocket && !isTcp)
            {
                return new AoPlayerCountProbeResult();
            }

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AoProbeTimeoutMs);
            CancellationToken timeoutToken = timeoutCts.Token;

            if (isWebSocket)
            {
                return await ProbeAoPlayerCountOverWebSocketAsync(uri, timeoutToken, captureTranscript);
            }

            return await ProbeAoPlayerCountOverTcpAsync(uri, timeoutToken, captureTranscript);
        }

        private static async Task<AoPlayerCountProbeResult> ProbeAoPlayerCountOverWebSocketAsync(
            Uri uri,
            CancellationToken cancellationToken,
            bool captureTranscript)
        {
            AoPlayerCountProbeResult result = new AoPlayerCountProbeResult();
            try
            {
                using ClientWebSocket webSocket = new ClientWebSocket();
                webSocket.Options.SetRequestHeader(
                    "User-Agent",
                    $"AttorneyOnline/{AoProbeClientVersion} (Desktop)");

                await webSocket.ConnectAsync(uri, cancellationToken);

                string hdid = Guid.NewGuid().ToString();
                StringBuilder packetBuffer = new StringBuilder();
                ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[8192]);

                while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(buffer, cancellationToken);
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

                    while (TryTakeBufferedProbePacket(packetBuffer, out string packet))
                    {
                        if (captureTranscript)
                        {
                            result.ReceivedPackets.Add(packet);
                        }

                        List<string> outgoingPackets = GetProbeFollowUpPackets(packet, hdid);
                        foreach (string outgoingPacket in outgoingPackets)
                        {
                            if (captureTranscript)
                            {
                                result.SentPackets.Add(outgoingPacket);
                            }

                            await SendWsPacketAsync(webSocket, outgoingPacket, cancellationToken);
                        }

                        if (TryParseProbePlayerCountPacket(packet, out int players, out int maxPlayers))
                        {
                            result.Success = true;
                            result.Players = players;
                            result.MaxPlayers = maxPlayers;
                            return result;
                        }

                        if (packet.StartsWith("BD#", StringComparison.OrdinalIgnoreCase))
                        {
                            TryParseProbeRejectionPacket(packet, out string rejectionReason);
                            result.RejectionReason = rejectionReason;
                            result.IncompatibleClient = LooksLikeIncompatibleClientRejection(rejectionReason);
                            return result;
                        }
                    }
                }
            }
            catch
            {
                return result;
            }

            return result;
        }

        private static async Task<AoPlayerCountProbeResult> ProbeAoPlayerCountOverTcpAsync(
            Uri uri,
            CancellationToken cancellationToken,
            bool captureTranscript)
        {
            AoPlayerCountProbeResult result = new AoPlayerCountProbeResult();
            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(uri.Host, uri.Port, cancellationToken);
                using NetworkStream stream = client.GetStream();

                string hdid = Guid.NewGuid().ToString();
                StringBuilder packetBuffer = new StringBuilder();
                byte[] buffer = new byte[8192];

                while (client.Connected && !cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    packetBuffer.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    while (TryTakeBufferedProbePacket(packetBuffer, out string packet))
                    {
                        if (captureTranscript)
                        {
                            result.ReceivedPackets.Add(packet);
                        }

                        List<string> outgoingPackets = GetProbeFollowUpPackets(packet, hdid);
                        foreach (string outgoingPacket in outgoingPackets)
                        {
                            if (captureTranscript)
                            {
                                result.SentPackets.Add(outgoingPacket);
                            }

                            await SendTcpPacketAsync(stream, outgoingPacket, cancellationToken);
                        }

                        if (TryParseProbePlayerCountPacket(packet, out int players, out int maxPlayers))
                        {
                            result.Success = true;
                            result.Players = players;
                            result.MaxPlayers = maxPlayers;
                            return result;
                        }

                        if (packet.StartsWith("BD#", StringComparison.OrdinalIgnoreCase))
                        {
                            TryParseProbeRejectionPacket(packet, out string rejectionReason);
                            result.RejectionReason = rejectionReason;
                            result.IncompatibleClient = LooksLikeIncompatibleClientRejection(rejectionReason);
                            return result;
                        }
                    }
                }
            }
            catch
            {
                return result;
            }

            return result;
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

            // Match AO2's initial identity response only.
            // The actual join transition happens later when the user selects a server,
            // and sending askchaa/RC during the probe can trigger false rejections.
            outgoingPackets.Add($"ID#{AoProbeClientName}#{AoProbeClientVersion}#%");
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

        internal static bool TryParseProbeRejectionPacket(string packet, out string reason)
        {
            reason = string.Empty;
            if (!packet.StartsWith("BD#", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalizedPacket = packet.EndsWith("#", StringComparison.Ordinal)
                ? packet.Substring(0, packet.Length - 1)
                : packet;
            string[] parts = normalizedPacket.Split('#');
            if (parts.Length < 2)
            {
                return false;
            }

            reason = parts[1]?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(reason);
        }

        internal static bool LooksLikeIncompatibleClientRejection(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            string normalizedReason = reason.Trim();
            return normalizedReason.Contains("incompatible", StringComparison.OrdinalIgnoreCase)
                || normalizedReason.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
                || normalizedReason.Contains("outdated", StringComparison.OrdinalIgnoreCase)
                || normalizedReason.Contains("version", StringComparison.OrdinalIgnoreCase)
                || normalizedReason.Contains("ao2-compatible", StringComparison.OrdinalIgnoreCase)
                || normalizedReason.Contains("ao2 client", StringComparison.OrdinalIgnoreCase)
                || normalizedReason.Contains("client version", StringComparison.OrdinalIgnoreCase)
                || normalizedReason.Contains("ao2", StringComparison.OrdinalIgnoreCase)
                || normalizedReason.Contains("attorneyonline", StringComparison.OrdinalIgnoreCase);
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

        private static async Task SendTcpPacketAsync(NetworkStream stream, string packet, CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(packet);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private static bool TryTakeBufferedProbePacket(StringBuilder packetBuffer, out string packet)
        {
            packet = string.Empty;

            string current = packetBuffer.ToString();
            int packetEnd = current.IndexOf("#%", StringComparison.Ordinal);
            if (packetEnd < 0)
            {
                return false;
            }

            packet = current.Substring(0, packetEnd + 1);
            packetBuffer.Remove(0, packetEnd + 2);
            return true;
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
                (bool legacy, bool secure) = ParseTransportFlags(values);

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
                    Legacy = legacy,
                    Secure = secure
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
                builder.AppendLine($"protocol={GetProtocolValue(server)}");
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

        private static (bool Legacy, bool Secure) ParseTransportFlags(Dictionary<string, string> values)
        {
            if (values.TryGetValue("protocol", out string? protocol))
            {
                if (string.Equals(protocol, "tcp", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, false);
                }

                if (string.Equals(protocol, "wss", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, true);
                }
            }

            if (values.TryGetValue("legacy", out string? legacyValue))
            {
                bool isLegacy = legacyValue.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || legacyValue.Equals("1", StringComparison.OrdinalIgnoreCase);
                return (isLegacy, false);
            }

            return (false, false);
        }

        private static string GetProtocolValue(FavoriteServerEntry entry)
        {
            if (entry.Legacy)
            {
                return "tcp";
            }

            return entry.Secure ? "wss" : "ws";
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
