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
[Category("RequiresConnection")]
[Explicit("Runs live AO polling and count probes. Execute manually when validating polling behavior.")]
[NonParallelizable]
public class LiveServerPollingTests
{
    [Test]
    [CancelAfter(180000)]
    public async Task LivePolling_AoServerPoll_ReturnsServerEntries()
    {
        string configIniPath = GetConfiguredIniPathOrIgnore();

        List<ServerEndpointDefinition> servers = await ServerEndpointCatalog.LoadAsync(configIniPath, CancellationToken.None);
        List<ServerEndpointDefinition> pollEntries = servers
            .Where(server => server.Source == ServerEndpointSource.AoServerPoll)
            .ToList();

        Assert.That(pollEntries.Count, Is.GreaterThan(0),
            "Expected AO poll to return at least one server entry.");
    }

    [Test]
    [CancelAfter(300000)]
    public async Task LivePolling_SelectableServers_ExposePlayerCounts()
    {
        string configIniPath = GetConfiguredIniPathOrIgnore();

        List<ServerEndpointDefinition> servers = await ServerEndpointCatalog.LoadAsync(configIniPath, CancellationToken.None);
        List<ServerEndpointDefinition> selectableServers = servers
            .Where(server => server.IsSelectable)
            .ToList();

        Assert.That(selectableServers.Count, Is.GreaterThan(0),
            "Expected at least one selectable server from live polling.");

        List<ServerEndpointDefinition> missingCounts = selectableServers
            .Where(server => !server.OnlinePlayers.HasValue || !server.MaxPlayers.HasValue)
            .ToList();

        if (missingCounts.Count > 0)
        {
            string details = string.Join(Environment.NewLine, missingCounts.Select(server =>
                $"- {server.SourceDisplayName} | {server.Name} | {server.Endpoint} | players={server.OnlinePlayers?.ToString() ?? "null"}/{server.MaxPlayers?.ToString() ?? "null"}"));

            Assert.Fail(
                "Every selectable server must expose player counts (AO style). Missing counts:" +
                Environment.NewLine +
                details);
        }

        int nonSelectableCount = servers.Count - selectableServers.Count;
        TestContext.Progress.WriteLine(
            $"Selectable servers: {selectableServers.Count}; Non-selectable servers: {nonSelectableCount}");
    }

    [Test]
    [CancelAfter(300000)]
    public async Task LivePolling_AoPollServers_NotSelectableSolelyDueToMissingPlayerCounts_IsEmpty()
    {
        string configIniPath = GetConfiguredIniPathOrIgnore();

        List<ServerEndpointDefinition> servers = await ServerEndpointCatalog.LoadAsync(configIniPath, CancellationToken.None);
        List<ServerEndpointDefinition> missingCountOnly = servers
            .Where(server => server.Source == ServerEndpointSource.AoServerPoll)
            .Where(server => server.IsOnline)
            .Where(server => !server.IsLegacy)
            .Where(server => server.IsAoClientCompatible)
            .Where(server => InitialConfigurationWindow.IsValidServerEndpoint(server.Endpoint))
            .Where(server => !server.OnlinePlayers.HasValue || !server.MaxPlayers.HasValue)
            .OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingCountOnly.Count > 0)
        {
            string details = string.Join(Environment.NewLine, missingCountOnly.Select(server =>
                $"- {server.Name} | {server.Endpoint} | online={server.IsOnline} legacy={server.IsLegacy} players={server.OnlinePlayers?.ToString() ?? "null"}/{server.MaxPlayers?.ToString() ?? "null"}"));

            Assert.Fail(
                "AO poll servers whose sole non-selectable reason is missing player counts:" +
                Environment.NewLine +
                details);
        }
    }

    [Test]
    [CancelAfter(180000)]
    public async Task LivePolling_MoltoPotente2_MaxPlayersMatchesAoExpectation()
    {
        string configIniPath = GetConfiguredIniPathOrIgnore();

        List<ServerEndpointDefinition> servers = await ServerEndpointCatalog.LoadAsync(configIniPath, CancellationToken.None);
        ServerEndpointDefinition? targetServer = servers
            .Where(server => server.Source == ServerEndpointSource.AoServerPoll)
            .FirstOrDefault(server => string.Equals(
                server.Name?.Trim(),
                "server molto potente 2 [ITA]",
                StringComparison.OrdinalIgnoreCase));

        if (targetServer == null)
        {
            Assert.Ignore("Target server 'server molto potente 2 [ITA]' is not present in current AO poll.");
        }

        TestContext.Progress.WriteLine(
            $"Molto probe: selectable={targetServer.IsSelectable} online={targetServer.IsOnline} players={targetServer.OnlinePlayers?.ToString() ?? "null"}/{targetServer.MaxPlayers?.ToString() ?? "null"} endpoint={targetServer.Endpoint}");

        Assert.That(targetServer.MaxPlayers, Is.EqualTo(100),
            "Expected max players of 100 for 'server molto potente 2 [ITA]' to match AO behavior.");
    }

    [Test]
    [CancelAfter(180000)]
    public async Task LivePolling_AoOfficialVanilla_MaxPlayersMatchesAoExpectation()
    {
        string configIniPath = GetConfiguredIniPathOrIgnore();

        List<ServerEndpointDefinition> servers = await ServerEndpointCatalog.LoadAsync(configIniPath, CancellationToken.None);
        ServerEndpointDefinition? targetServer = servers
            .Where(server => server.Source == ServerEndpointSource.AoServerPoll)
            .FirstOrDefault(server => string.Equals(
                server.Name?.Trim(),
                "AO official server (Vanilla)",
                StringComparison.OrdinalIgnoreCase));

        if (targetServer == null)
        {
            Assert.Ignore("Target server 'AO official server (Vanilla)' is not present in current AO poll.");
        }

        TestContext.Progress.WriteLine(
            $"AO official probe: selectable={targetServer.IsSelectable} online={targetServer.IsOnline} players={targetServer.OnlinePlayers?.ToString() ?? "null"}/{targetServer.MaxPlayers?.ToString() ?? "null"} endpoint={targetServer.Endpoint}");

        Assert.That(targetServer.MaxPlayers, Is.EqualTo(100),
            "Expected max players of 100 for 'AO official server (Vanilla)' to match AO behavior.");
    }

    private static string GetConfiguredIniPathOrIgnore()
    {
        string configuredPath = SaveFile.Data.ConfigIniPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configuredPath) || !File.Exists(configuredPath))
        {
            Assert.Ignore("Live polling tests require a valid configured config.ini path in savefile.");
        }

        return configuredPath;
    }
}
