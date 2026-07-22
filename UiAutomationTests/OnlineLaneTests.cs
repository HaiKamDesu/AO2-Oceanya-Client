using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace UiAutomationTests;

/// <summary>
/// Online integration lane: validates that real AO2 protocol packets travel through
/// the transport when the user interacts with the UI.
///
/// Unlike the Smoke suite (which stubs the transport), these tests spin up an
/// in-process TCP server that speaks the AO2 protocol and assert on the packets it
/// receives. UseSingleInternalClient is false in the fixture savefile, so the app
/// calls AOClient.Connect() against the local server.
///
/// Prerequisites: interactive Windows desktop session, OceanyaClient built in Debug.
/// Run with: dotnet test UiAutomationTests/UiAutomationTests.csproj --filter "Category=Online"
/// </summary>
[TestFixture]
[Category("Online")]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class OnlineLaneTests
{
    private FlaUiSmokeApp? app;

    [TearDown]
    public void TearDown()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != NUnit.Framework.Interfaces.TestStatus.Passed)
        {
            app?.CaptureFailureScreenshot(TestContext.CurrentContext.Test.Name);
        }

        // KillImmediately rather than Dispose: the loopback server is already closed
        // at this point, so a graceful WM_CLOSE would block for several seconds in
        // FlaUI's internal WaitForExit and emit "Application failed to exit" trace
        // noise before the process is killed anyway.
        app?.KillImmediately();
        app = null;
    }

    /// <summary>
    /// Validates the full AO2 client handshake sequence is sent when a client is
    /// added via the UI. Verifies HI → ID#AO2 → askchaa → RC → RM → RD arrive
    /// at the server in the correct order — a regression guard on the protocol layer.
    /// </summary>
    [Test]
    public async Task OnlineConnect_HandshakePacketSequence_IsAo2Compliant()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<(List<string> Packets, string? ServerError)> serverTask =
            Task.Run(() => RunServerAsync(listener, cts.Token));

        app = FlaUiSmokeApp.Launch(OnlineFixturePaths.BuildArguments(port));
        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        AddClientViaDialog(mainWindow, "HandshakeClient");

        // Messaging controls become enabled only after Connect() completes.
        WaitForElementEnabled(mainWindow, "Main.Ooc.Message", expectedEnabled: true);

        cts.Cancel();
        (List<string> received, string? serverError) = await serverTask;

        // Always log received packets so failures show exactly what the server saw.
        TestContext.WriteLine($"[Online] Server received {received.Count} packet(s):");
        foreach (string p in received)
        {
            TestContext.WriteLine("  " + p);
        }

        if (serverError != null)
        {
            TestContext.WriteLine("[Online] Server error: " + serverError);
        }

        int hiIndex       = received.FindIndex(p => p.StartsWith("HI#", StringComparison.Ordinal));
        int idIndex       = received.FindIndex(p => string.Equals(p, "ID#AO2#2.11.0#%", StringComparison.Ordinal));
        int askchaaIndex  = received.FindIndex(p => string.Equals(p, "askchaa#%", StringComparison.Ordinal));
        int rcIndex       = received.FindIndex(p => string.Equals(p, "RC#%", StringComparison.Ordinal));
        int rmIndex       = received.FindIndex(p => string.Equals(p, "RM#%", StringComparison.Ordinal));
        int rdIndex       = received.FindIndex(p => string.Equals(p, "RD#%", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(hiIndex,      Is.GreaterThanOrEqualTo(0),   "Expected HI# packet from client.");
            Assert.That(idIndex,      Is.GreaterThan(hiIndex),       "Expected ID#AO2 after HI.");
            Assert.That(askchaaIndex, Is.GreaterThan(idIndex),       "Expected askchaa after ID.");
            Assert.That(rcIndex,      Is.GreaterThan(askchaaIndex),  "Expected RC after askchaa.");
            Assert.That(rmIndex,      Is.GreaterThan(rcIndex),       "Expected RM after RC.");
            Assert.That(rdIndex,      Is.GreaterThan(rmIndex),       "Expected RD after RM.");
        });
    }

    /// <summary>
    /// Validates that sending an OOC message via the UI produces a real CT# packet
    /// on the transport — not just a UI clear. This is the one path the Smoke suite
    /// does not exercise (Smoke stubs the send when transport is disconnected).
    /// </summary>
    [Test]
    public async Task OnlineOocSend_MessageArrivesAtServer_AsCtPacket()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<(List<string> Packets, string? ServerError)> serverTask =
            Task.Run(() => RunServerAsync(listener, cts.Token));

        app = FlaUiSmokeApp.Launch(OnlineFixturePaths.BuildArguments(port));
        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        AddClientViaDialog(mainWindow, "OocTestClient");
        WaitForElementEnabled(mainWindow, "Main.Ooc.Message", expectedEnabled: true);

        SetText(mainWindow, "Main.Ooc.Showname", "OnlineTest");
        FlaUI.Core.AutomationElements.TextBox messageBox = SetText(mainWindow, "Main.Ooc.Message", "hello from online lane");
        PressEnter(messageBox);

        // Allow time for the CT# packet to travel through the loopback transport.
        // 5 s gives headroom for WPF-dispatch + SendKeys latency on loaded machines;
        // on a fast machine this completes in well under 1 s.
        await Task.Delay(TimeSpan.FromSeconds(5));
        cts.Cancel();
        (List<string> received, string? serverError) = await serverTask;

        // Always log received packets so failures show exactly what the server saw.
        TestContext.WriteLine($"[Online] Server received {received.Count} packet(s):");
        foreach (string p in received)
        {
            TestContext.WriteLine("  " + p);
        }

        if (serverError != null)
        {
            TestContext.WriteLine("[Online] Server error: " + serverError);
        }

        Assert.That(
            received,
            Has.Some.StartsWith("CT#OnlineTest#hello from online lane#"),
            "Expected CT# packet with showname and message to arrive at the server.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void AddClientViaDialog(Window mainWindow, string clientName)
    {
        app!.WaitForDescendantById(mainWindow, "Main.AddClient").AsButton().Invoke();
        Window selectorWindow = app.WaitForReadyWindow("CharacterSelector.Cancel");
        SetText(selectorWindow, "CharacterSelector.ClientName", clientName);
        app.WaitForDescendantById(selectorWindow, "CharacterSelector.Character.SmokePhoenix")?.Click();

        // Connect() takes ~2 s; WaitForReadyWindow has a 30 s ceiling.
        app.WaitForReadyWindow("Main.AddClient");
    }

    private static FlaUI.Core.AutomationElements.TextBox SetText(Window window, string automationId, string value)
    {
        FlaUI.Core.AutomationElements.TextBox textBox = window
            .FindFirstDescendant(cf => cf.ByAutomationId(automationId))
            ?.AsTextBox()
            ?? throw new InvalidOperationException("Text box not found: " + automationId);
        textBox.Text = value;
        return textBox;
    }

    private static void PressEnter(FlaUI.Core.AutomationElements.TextBox textBox)
    {
        textBox.Focus();
        System.Windows.Forms.SendKeys.SendWait("{ENTER}");
    }

    private static void WaitForElementEnabled(Window window, string automationId, bool expectedEnabled)
    {
        RetryResult<bool> result = Retry.WhileFalse(
            () =>
            {
                AutomationElement? el = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                return el != null && el.IsEnabled == expectedEnabled;
            },
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false,
            ignoreException: true);

        if (!result.Success)
        {
            throw new TimeoutException("Timed out waiting for enabled=" + expectedEnabled + " on " + automationId);
        }
    }

    // ── in-process AO2-compatible TCP server ─────────────────────────────────
    //
    // Mirrors the pattern in UnitTests/NetworkTests.cs and GmPacketLoopbackServer.
    //
    // IMPORTANT: this ACCEPTS CONNECTIONS IN A LOOP, one handler task per connection.
    // The InitialConfigurationWindow server-history combobox (added in commit 2083cae)
    // fire-and-forget probes the selected endpoint via ServerEndpointCatalog.ProbeEndpointAsync,
    // which opens its own short-lived connection to this loopback endpoint. A single-accept
    // server would hand that probe the one and only connection, starving the real AddClient
    // connection and leaving the client stuck on "Timed out waiting for handshake packet: ID".
    // Accepting in a loop lets the probe and the real client both connect.
    //
    // Packets from every connection are aggregated under a lock. The real client is the only
    // connection that sends the full HI→ID→askchaa→RC→RM→RD sequence and the CT# OOC send, so
    // the ordering / content assertions in the test bodies still hold.
    //
    // TCP-scheme clients send HI# immediately before the server speaks; each handler drains
    // that first before sending decryptor.

    private static async Task<(List<string> Packets, string? ServerError)> RunServerAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        List<string> receivedPackets = new List<string>();
        object packetsLock = new object();
        string? serverError = null;
        int nextConnectionId = 0;
        List<Task> connectionTasks = new List<Task>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                int connectionId = System.Threading.Interlocked.Increment(ref nextConnectionId);
                connectionTasks.Add(Task.Run(
                    () => HandleConnectionAsync(connectionId, client, receivedPackets, packetsLock, cancellationToken),
                    cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful test shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Listener stopped during shutdown.
        }
        catch (Exception ex)
        {
            serverError = ex.GetType().Name + ": " + ex.Message;
        }

        try
        {
            await Task.WhenAll(connectionTasks);
        }
        catch
        {
            // Per-connection errors are best-effort; assertions are the source of truth.
        }

        lock (packetsLock)
        {
            return (new List<string>(receivedPackets), serverError);
        }
    }

    private static async Task HandleConnectionAsync(
        int connectionId,
        TcpClient client,
        List<string> receivedPackets,
        object packetsLock,
        CancellationToken cancellationToken)
    {
        void Record(string packet)
        {
            lock (packetsLock)
            {
                receivedPackets.Add(packet);
            }
        }

        try
        {
            using (client)
            {
                using NetworkStream stream = client.GetStream();
                StringBuilder packetBuffer = new StringBuilder();
                int? selectedCharId = null;

                // The TCP transport sends HI# before the server speaks; drain it first.
                string? firstPacket = await ReadPacketAsync(stream, packetBuffer, cancellationToken);
                if (firstPacket != null)
                {
                    Record(firstPacket);
                }

                // Standard AO2 handshake server side.
                await SendToClientAsync(stream, "decryptor#NOENCRYPT#%", cancellationToken);
                await SendToClientAsync(stream, $"ID#{connectionId}#tsuserver#7#%", cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    string? packet = await ReadPacketAsync(stream, packetBuffer, cancellationToken);
                    if (packet == null)
                    {
                        break;
                    }

                    Record(packet);

                    if (string.Equals(packet, "ID#AO2#2.11.0#%", StringComparison.Ordinal))
                    {
                        await SendToClientAsync(stream, $"PN#{connectionId}#10#%", cancellationToken);
                        await SendToClientAsync(stream, "FL#noencryption#fastloading#%", cancellationToken);
                    }
                    else if (string.Equals(packet, "askchaa#%", StringComparison.Ordinal))
                    {
                        await SendToClientAsync(stream, "SI#1#0#0#%", cancellationToken);
                    }
                    else if (string.Equals(packet, "RC#%", StringComparison.Ordinal))
                    {
                        // Send a fixture-local character plus its availability so the current
                        // CharacterSelector flow can confirm a real INI puppet.
                        await SendToClientAsync(stream, "SC#SmokePhoenix#%", cancellationToken);
                        await SendToClientAsync(stream, "CharsCheck#0#%", cancellationToken);
                    }
                    else if (string.Equals(packet, "RM#%", StringComparison.Ordinal))
                    {
                        await SendToClientAsync(stream, "SM#Lobby#%", cancellationToken);
                    }
                    else if (string.Equals(packet, "RD#%", StringComparison.Ordinal))
                    {
                        await SendToClientAsync(stream, "DONE#%", cancellationToken);
                    }
                    else if (packet.StartsWith("CC#", StringComparison.Ordinal))
                    {
                        string[] fields = packet.Split('#', StringSplitOptions.None);
                        if (fields.Length >= 3 && int.TryParse(fields[2], out int charId))
                        {
                            selectedCharId = charId;
                        }

                        await SendToClientAsync(stream, "CharsCheck#1#%", cancellationToken);

                        // Confirm the INI puppet the same way a real AO2 server does; the
                        // client blocks IC sends until it sees PV#<player>#CID#<charId>#%.
                        if (selectedCharId.HasValue)
                        {
                            await SendToClientAsync(
                                stream,
                                $"PV#{connectionId}#CID#{selectedCharId.Value}#%",
                                cancellationToken);
                        }
                    }
                    // All other packets (post-handshake RM#, OOC sends, etc.) are collected
                    // but need no server-side response.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful test shutdown.
        }
        catch (Exception)
        {
            // Probe connections close abruptly; that is fine.
        }
    }

    private static async Task<string?> ReadPacketAsync(
        NetworkStream stream,
        StringBuilder packetBuffer,
        CancellationToken cancellationToken)
    {
        // Return a buffered packet if one is already complete.
        string current = packetBuffer.ToString();
        int end = current.IndexOf("#%", StringComparison.Ordinal);
        if (end >= 0)
        {
            string buffered = current.Substring(0, end + 2);
            packetBuffer.Remove(0, end + 2);
            return buffered;
        }

        byte[] buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead <= 0)
            {
                return null;
            }

            packetBuffer.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            current = packetBuffer.ToString();
            end = current.IndexOf("#%", StringComparison.Ordinal);
            if (end < 0)
            {
                continue;
            }

            string packet = current.Substring(0, end + 2);
            packetBuffer.Remove(0, end + 2);
            return packet;
        }

        return null;
    }

    private static async Task SendToClientAsync(
        NetworkStream stream,
        string packet,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(packet);
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
