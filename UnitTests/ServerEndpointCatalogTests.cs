using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        Assert.That(packets, Is.EqualTo(new[] { "ID#AO2#2.11.0#%" }));
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

    [Test]
    public void TryParseProbeRejectionPacket_BdPacket_ExtractsReason()
    {
        bool success = ServerEndpointCatalog.TryParseProbeRejectionPacket(
            "BD#Please wait before connecting another client. Try again!#",
            out string reason);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(reason, Is.EqualTo("Please wait before connecting another client. Try again!"));
        });
    }

    [Test]
    public void LooksLikeIncompatibleClientRejection_ThrottleMessage_ReturnsFalse()
    {
        bool incompatible = ServerEndpointCatalog.LooksLikeIncompatibleClientRejection(
            "Please wait before connecting another client. Try again!");

        Assert.That(incompatible, Is.False);
    }

    [Test]
    public void LooksLikeIncompatibleClientRejection_Ao2CompatibilityMessage_ReturnsTrue()
    {
        bool incompatible = ServerEndpointCatalog.LooksLikeIncompatibleClientRejection(
            "Server rejected AO2-compatible client probe.");

        Assert.That(incompatible, Is.True);
    }

    [Test]
    public void ServerEndpointDefinition_IsSelectable_WhenOnlineWithoutPlayerCounts()
    {
        ServerEndpointDefinition server = new ServerEndpointDefinition
        {
            Name = "Chill and Dices",
            Endpoint = "ws://82.165.1.79:50001",
            Description = "Direct-connect favorite",
            Source = ServerEndpointSource.Favorites,
            IsLegacy = false,
            IsOnline = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(server.SupportsDirectConnection, Is.True);
            Assert.That(server.IsSelectable, Is.True);
        });
    }

    [Test]
    public void ServerEndpointDefinition_SupportsDirectConnection_WhenOfflineFavoriteNeedsFallbackProbe()
    {
        ServerEndpointDefinition server = new ServerEndpointDefinition
        {
            Name = "Chill and Dices",
            Endpoint = "ws://82.165.1.79:50001",
            Description = "Direct-connect favorite",
            Source = ServerEndpointSource.Favorites,
            IsLegacy = false,
            IsOnline = false
        };

        Assert.Multiple(() =>
        {
            Assert.That(server.SupportsDirectConnection, Is.True);
            Assert.That(server.IsSelectable, Is.False);
        });
    }

    [Test]
    public void ServerEndpointDefinition_IsSelectable_WhenProbeWasRejectedButServerIsOnline()
    {
        ServerEndpointDefinition server = new ServerEndpointDefinition
        {
            Name = "Chill and Dices",
            Endpoint = "ws://82.165.1.79:50001",
            Description = "Direct-connect favorite",
            Source = ServerEndpointSource.Favorites,
            IsLegacy = false,
            IsOnline = true,
            IsAoClientCompatible = false
        };

        Assert.That(server.IsSelectable, Is.True);
    }

    [Test]
    public void ServerEndpointDefinition_PlayersText_UsesUnknownPlaceholder_WhenOnlineWithoutCounts()
    {
        ServerEndpointDefinition server = new ServerEndpointDefinition
        {
            Name = "Chill and Dices",
            Endpoint = "ws://82.165.1.79:50001",
            Description = "Direct-connect favorite",
            Source = ServerEndpointSource.Favorites,
            IsLegacy = false,
            IsOnline = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(server.PlayersText, Is.EqualTo("???/???"));
            Assert.That(server.AvailabilityText, Is.EqualTo("Online: ???/???"));
        });
    }

    [Test]
    public void NeedsSupplementalAoProbe_ReturnsTrueForDirectServerWithoutPlayerCounts()
    {
        ServerEndpointDefinition server = new ServerEndpointDefinition
        {
            Name = "Chill and Dices",
            Endpoint = "ws://82.165.1.79:50001",
            Description = "Direct-connect favorite",
            Source = ServerEndpointSource.Favorites,
            IsLegacy = false,
            IsOnline = true
        };

        bool needsProbe = ServerEndpointCatalog.NeedsSupplementalAoProbe(server);

        Assert.That(needsProbe, Is.True);
    }

    [Test]
    public void NeedsSupplementalAoProbe_ReturnsFalseWhenDirectServerAlreadyHasCounts()
    {
        ServerEndpointDefinition server = new ServerEndpointDefinition
        {
            Name = "Chill and Dices",
            Endpoint = "ws://82.165.1.79:50001",
            Description = "Direct-connect favorite",
            Source = ServerEndpointSource.Favorites,
            IsLegacy = false,
            IsOnline = true,
            OnlinePlayers = 4,
            MaxPlayers = 100
        };

        bool needsProbe = ServerEndpointCatalog.NeedsSupplementalAoProbe(server);

        Assert.That(needsProbe, Is.False);
    }

    [Test]
    public void NeedsSupplementalReachabilityProbe_ReturnsTrueForLegacyOfflineFavorite()
    {
        ServerEndpointDefinition server = new ServerEndpointDefinition
        {
            Name = "Legacy Favorite",
            Endpoint = "tcp://127.0.0.1:27016",
            Description = "Legacy favorite",
            Source = ServerEndpointSource.Favorites,
            IsLegacy = true,
            IsOnline = false
        };

        bool needsProbe = ServerEndpointCatalog.NeedsSupplementalReachabilityProbe(server);

        Assert.That(needsProbe, Is.True);
    }

    [Test]
    public void LoadFavorites_AssignsStableFavoriteIndices()
    {
        string configIniPath = CreateTempConfigIniPath();

        try
        {
            ServerEndpointCatalog.AddFavorite(configIniPath, new FavoriteServerEntry
            {
                Name = "One",
                Address = "127.0.0.1",
                Port = 27016,
                Description = "First",
                Legacy = false,
                Secure = false
            });
            ServerEndpointCatalog.AddFavorite(configIniPath, new FavoriteServerEntry
            {
                Name = "Two",
                Address = "example.com",
                Port = 443,
                Description = "Second",
                Legacy = false,
                Secure = true
            });

            List<ServerEndpointDefinition> favorites = ServerEndpointCatalog.LoadFavorites(configIniPath);

            Assert.That(
                favorites.Select(favorite => favorite.FavoriteStoreIndex).ToArray(),
                Is.EqualTo(new int?[] { 0, 1 }));
        }
        finally
        {
            DeleteTempConfigDirectory(configIniPath);
        }
    }

    [Test]
    public void LoadFavorites_PreservesSecureWebSocketFavorites()
    {
        string configIniPath = CreateTempConfigIniPath();

        try
        {
            ServerEndpointCatalog.AddFavorite(configIniPath, new FavoriteServerEntry
            {
                Name = "Secure Favorite",
                Address = "secure.example.com",
                Port = 443,
                Description = "Secure endpoint",
                Legacy = false,
                Secure = true
            });

            ServerEndpointDefinition favorite = ServerEndpointCatalog.LoadFavorites(configIniPath).Single();

            Assert.That(favorite.Endpoint, Is.EqualTo("wss://secure.example.com:443"));
        }
        finally
        {
            DeleteTempConfigDirectory(configIniPath);
        }
    }

    [Test]
    public async Task PopulateAoPlayerCountsAsyncForTesting_DeduplicatesDuplicateEndpointsAcrossEntries()
    {
        List<ServerEndpointDefinition> servers = new List<ServerEndpointDefinition>
        {
            new ServerEndpointDefinition
            {
                Name = "Default Chill",
                Endpoint = "ws://82.165.1.79:50001",
                Description = "Default",
                Source = ServerEndpointSource.Defaults,
                IsLegacy = false
            },
            new ServerEndpointDefinition
            {
                Name = "Favorite Chill",
                Endpoint = "ws://82.165.1.79:50001",
                Description = "Favorite",
                Source = ServerEndpointSource.Favorites,
                IsLegacy = false
            }
        };

        int probeCalls = 0;
        int reachabilityCalls = 0;

        await ServerEndpointCatalog.PopulateAoPlayerCountsAsyncForTesting(
            servers,
            async (endpoint, cancellationToken) =>
            {
                probeCalls++;
                await Task.CompletedTask;
                return (true, 7, 100, false);
            },
            async (endpoint, cancellationToken) =>
            {
                reachabilityCalls++;
                await Task.CompletedTask;
                return true;
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(probeCalls, Is.EqualTo(1));
            Assert.That(reachabilityCalls, Is.EqualTo(0));
            Assert.That(servers.All(server => server.IsOnline), Is.True);
            Assert.That(servers.All(server => server.OnlinePlayers == 7), Is.True);
            Assert.That(servers.All(server => server.MaxPlayers == 100), Is.True);
        });
    }

    [Test]
    public async Task PopulateReachabilityAsyncForTesting_DeduplicatesDuplicateEndpointsAcrossEntries()
    {
        List<ServerEndpointDefinition> servers = new List<ServerEndpointDefinition>
        {
            new ServerEndpointDefinition
            {
                Name = "Default Chill",
                Endpoint = "ws://82.165.1.79:50001",
                Description = "Default",
                Source = ServerEndpointSource.Defaults,
                IsLegacy = false
            },
            new ServerEndpointDefinition
            {
                Name = "Favorite Chill",
                Endpoint = "ws://82.165.1.79:50001",
                Description = "Favorite",
                Source = ServerEndpointSource.Favorites,
                IsLegacy = false
            }
        };

        int reachabilityCalls = 0;

        await ServerEndpointCatalog.PopulateReachabilityAsyncForTesting(
            servers,
            async (endpoint, cancellationToken) =>
            {
                reachabilityCalls++;
                await Task.CompletedTask;
                return true;
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(reachabilityCalls, Is.EqualTo(1));
            Assert.That(servers.All(server => server.IsOnline), Is.True);
        });
    }

    private static string CreateTempConfigIniPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "OceanyaClientTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string configIniPath = Path.Combine(directory, "config.ini");
        File.WriteAllText(configIniPath, string.Empty);
        return configIniPath;
    }

    private static void DeleteTempConfigDirectory(string configIniPath)
    {
        string? directory = Path.GetDirectoryName(configIniPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
