using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AOBot_Testing.Agents;
using NUnit.Framework;

namespace UnitTests;

[TestFixture]
[Category("RequiresConnection")]
[Explicit("Runs against a live AO2 server. Execute manually when validating production connectivity.")]
[NonParallelizable]
public class LiveServerConnectionTests
{
    private static readonly object LogSync = new object();
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(),
        $"ao2_live_connection_{DateTime.UtcNow:yyyyMMdd}.log");

    [Test]
    public async Task LiveServer_DefaultServer_AllowsSingleClientConnectAndOocSend()
    {
        string serverUrl = GetDefaultServerUrl();
        string probeMessage = $"[LiveTest:{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}] single-client connectivity probe";

        AOClient client = new AOClient(serverUrl, Globals.ConnectionString)
        {
            clientName = "LiveTestPrimary",
            OOCShowname = "LiveTestPrimary"
        };

        try
        {
            Log($"START SingleClient url={serverUrl} location={Globals.ConnectionString}");
            int attempts = await ConnectWithCooldownRetryAsync(client, "SingleClient");

            Assert.Multiple(() =>
            {
                Assert.That(client.aliveTime.IsRunning, Is.True, "Client stopwatch should be running after connect.");
                Assert.That(client.playerID, Is.GreaterThanOrEqualTo(0), "Player ID should be assigned by server.");
            });

            await client.SendOOCMessage(probeMessage);
            Log($"SEND_OOC SingleClient message={probeMessage}");

            await Task.Delay(1200);
            Log($"PASS SingleClient attempts={attempts}");
        }
        catch (Exception ex)
        {
            Log($"FAIL SingleClient exception={ex}");
            throw;
        }
        finally
        {
            await SafeDisconnectAsync(client, "SingleClient");
        }
    }

    [Test]
    public async Task LiveServer_DefaultServer_SecondClientConnectHandlesServerCooldown()
    {
        string serverUrl = GetDefaultServerUrl();
        string sharedLocation = Globals.ConnectionString;

        AOClient firstClient = new AOClient(serverUrl, sharedLocation)
        {
            clientName = "LiveTestFirst",
            OOCShowname = "LiveTestFirst"
        };

        AOClient secondClient = new AOClient(serverUrl, sharedLocation)
        {
            clientName = "LiveTestSecond",
            OOCShowname = "LiveTestSecond"
        };

        try
        {
            Log($"START MultiClient url={serverUrl} location={sharedLocation}");

            int firstAttempts = await ConnectWithCooldownRetryAsync(firstClient, "MultiClientFirst");
            int secondAttempts = await ConnectWithCooldownRetryAsync(secondClient, "MultiClientSecond");

            Assert.Multiple(() =>
            {
                Assert.That(firstClient.aliveTime.IsRunning, Is.True, "First client should be connected.");
                Assert.That(secondClient.aliveTime.IsRunning, Is.True, "Second client should be connected.");
                Assert.That(firstClient.playerID, Is.GreaterThanOrEqualTo(0));
                Assert.That(secondClient.playerID, Is.GreaterThanOrEqualTo(0));
                Assert.That(firstClient.playerID, Is.Not.EqualTo(secondClient.playerID),
                    "Live server should assign distinct player IDs after cooldown rules are satisfied.");
            });

            string firstMessage = $"[LiveTest:{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}] first-client message";
            string secondMessage = $"[LiveTest:{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}] second-client message";

            await firstClient.SendOOCMessage(firstMessage);
            await secondClient.SendOOCMessage(secondMessage);

            Log($"SEND_OOC MultiClient first={firstMessage}");
            Log($"SEND_OOC MultiClient second={secondMessage}");

            await Task.Delay(1200);
            Log($"PASS MultiClient firstAttempts={firstAttempts} secondAttempts={secondAttempts}");
        }
        catch (Exception ex)
        {
            Log($"FAIL MultiClient exception={ex}");
            throw;
        }
        finally
        {
            await SafeDisconnectAsync(secondClient, "MultiClientSecond");
            await SafeDisconnectAsync(firstClient, "MultiClientFirst");
        }
    }

    private static async Task SafeDisconnectAsync(AOClient client, string context)
    {
        try
        {
            await client.Disconnect();
            Log($"DISCONNECT {context}");
        }
        catch (Exception ex)
        {
            Log($"DISCONNECT_FAIL {context} exception={ex}");
        }
    }

    private static async Task<int> ConnectWithCooldownRetryAsync(
        AOClient client,
        string context,
        int maxAttempts = 4,
        int retryDelayMs = 6000)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Log($"CONNECT_ATTEMPT {context} attempt={attempt}");
                await client.Connect(0, 0, 250, 250);
                Log($"CONNECT_SUCCESS {context} attempt={attempt}");
                return attempt;
            }
            catch (TimeoutException ex) when (attempt < maxAttempts)
            {
                Log($"CONNECT_TIMEOUT {context} attempt={attempt} message={ex.Message}");
                await Task.Delay(retryDelayMs);
            }
        }

        throw new TimeoutException($"Failed to connect '{context}' after {maxAttempts} attempts.");
    }

    private static void Log(string message)
    {
        lock (LogSync)
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] {message}";
            File.AppendAllText(LogPath, line + Environment.NewLine);
            TestContext.Progress.WriteLine(line);
        }
    }

    private static string GetDefaultServerUrl()
    {
        if (Globals.IPs.TryGetValue(Globals.Servers.ChillAndDices, out string? fromGlobals) &&
            !string.IsNullOrWhiteSpace(fromGlobals))
        {
            return fromGlobals;
        }

        string? repoRoot = FindRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("Could not locate repository root to load default server configuration.");
        }

        string serverConfigPath = Path.Combine(repoRoot, "OceanyaClient", "server.json");
        if (!File.Exists(serverConfigPath))
        {
            throw new FileNotFoundException("Default server configuration not found.", serverConfigPath);
        }

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(serverConfigPath));
        if (!json.RootElement.TryGetProperty("ChillAndDices", out JsonElement endpoint))
        {
            throw new InvalidOperationException("ChillAndDices endpoint is missing from server.json.");
        }

        string? serverUrl = endpoint.GetString();
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new InvalidOperationException("ChillAndDices endpoint in server.json is empty.");
        }

        return serverUrl;
    }

    private static string? FindRepositoryRoot()
    {
        string current = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string solutionPath = Path.Combine(current, "AOBot-Testing.sln");
            if (File.Exists(solutionPath))
            {
                return current;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }
}
