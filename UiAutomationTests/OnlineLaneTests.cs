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

        app?.Dispose();
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
        Task<List<string>> serverTask = Task.Run(() => RunServerAsync(listener, cts.Token));

        app = FlaUiSmokeApp.Launch(OnlineFixturePaths.BuildArguments(port));
        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        AddClientViaDialog(mainWindow, "HandshakeClient");

        // Messaging controls become enabled only after Connect() completes.
        WaitForElementEnabled(mainWindow, "Main.Ooc.Message", expectedEnabled: true);

        cts.Cancel();
        List<string> received = await serverTask;

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
        Task<List<string>> serverTask = Task.Run(() => RunServerAsync(listener, cts.Token));

        app = FlaUiSmokeApp.Launch(OnlineFixturePaths.BuildArguments(port));
        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        AddClientViaDialog(mainWindow, "OocTestClient");
        WaitForElementEnabled(mainWindow, "Main.Ooc.Message", expectedEnabled: true);

        SetText(mainWindow, "Main.Ooc.Showname", "OnlineTest");
        FlaUI.Core.AutomationElements.TextBox messageBox = SetText(mainWindow, "Main.Ooc.Message", "hello from online lane");
        PressEnter(messageBox);

        // Allow time for the CT# packet to travel through the loopback transport.
        await Task.Delay(TimeSpan.FromSeconds(3));
        cts.Cancel();
        List<string> received = await serverTask;

        Assert.That(
            received,
            Has.Some.StartsWith("CT#OnlineTest#hello from online lane#"),
            "Expected CT# packet with showname and message to arrive at the server.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void AddClientViaDialog(Window mainWindow, string clientName)
    {
        app!.WaitForDescendantById(mainWindow, "Main.AddClient").AsButton().Invoke();
        Window inputDialog = app.WaitForReadyWindow("InputDialog.Ok");
        SetText(inputDialog, "InputDialog.Input", clientName);
        app.WaitForDescendantById(inputDialog, "InputDialog.Ok").AsButton().Invoke();

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
    // Mirrors the pattern in UnitTests/NetworkTests.cs.
    // Accepts one connection, performs the full AO2 handshake, then collects every
    // packet the client sends until the CancellationToken fires or the socket closes.
    //
    // TCP-scheme clients send HI# immediately before the server speaks; the server
    // drains that first before sending decryptor.

    private static async Task<List<string>> RunServerAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        List<string> receivedPackets = new List<string>();

        try
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            using NetworkStream stream = client.GetStream();
            StringBuilder packetBuffer = new StringBuilder();

            // The TCP transport sends HI# before the server speaks; drain it first.
            string? firstPacket = await ReadPacketAsync(stream, packetBuffer, cancellationToken);
            if (firstPacket != null)
            {
                receivedPackets.Add(firstPacket);
            }

            // Standard AO2 handshake server side.
            await SendToClientAsync(stream, "decryptor#NOENCRYPT#%", cancellationToken);
            await SendToClientAsync(stream, "ID#1#tsuserver#7#%", cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                string? packet = await ReadPacketAsync(stream, packetBuffer, cancellationToken);
                if (packet == null)
                {
                    break;
                }

                receivedPackets.Add(packet);

                if (string.Equals(packet, "ID#AO2#2.11.0#%", StringComparison.Ordinal))
                {
                    await SendToClientAsync(stream, "PN#1#10#%", cancellationToken);
                    await SendToClientAsync(stream, "FL#noencryption#fastloading#%", cancellationToken);
                }
                else if (string.Equals(packet, "askchaa#%", StringComparison.Ordinal))
                {
                    await SendToClientAsync(stream, "SI#1#0#0#%", cancellationToken);
                }
                else if (string.Equals(packet, "RC#%", StringComparison.Ordinal))
                {
                    // Send a single character entry (no local match — INI puppet
                    // selection silently skips, which is fine for OOC tests).
                    await SendToClientAsync(stream, "SC#OnlineLaneCharacter#%", cancellationToken);
                }
                else if (string.Equals(packet, "RM#%", StringComparison.Ordinal))
                {
                    await SendToClientAsync(stream, "SM#Lobby#%", cancellationToken);
                }
                else if (string.Equals(packet, "RD#%", StringComparison.Ordinal))
                {
                    await SendToClientAsync(stream, "DONE#%", cancellationToken);
                }
                // All other packets (CT# bootstrap, post-handshake RM#, OOC sends, etc.)
                // are silently collected but need no server-side response.
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful test shutdown.
        }
        catch (Exception)
        {
            // Swallow transport teardown noise; test assertions drive the result.
        }

        return receivedPackets;
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
