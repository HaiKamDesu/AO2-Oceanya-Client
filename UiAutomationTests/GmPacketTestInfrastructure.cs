using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AOBot_Testing.Structures;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace UiAutomationTests;

internal sealed class GmPacketLoopbackServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cts;
    private readonly Task acceptLoopTask;
    private readonly ConcurrentQueue<CapturedPacket> packets = new();
    private readonly ConcurrentDictionary<int, int?> selectedCharacterByConnection = new();
    private readonly object characterLock = new();
    private readonly string[] characters;
    private readonly string[] features;
    private readonly bool useWebSocket;
    private int nextConnectionId;
    private bool disposed;

    public GmPacketLoopbackServer(
        IEnumerable<string>? characters = null,
        IEnumerable<string>? features = null,
        bool useWebSocket = false)
    {
        this.characters = (characters ?? new[] { "SmokePhoenix", "SmokeEdgeworth" }).ToArray();
        this.features = (features ?? DefaultFeatures).ToArray();
        this.useWebSocket = useWebSocket;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        acceptLoopTask = Task.Run(() => AcceptLoopAsync(cts.Token));
    }

    public static IReadOnlyList<string> DefaultFeatures { get; } = new[]
    {
        "noencryption",
        "fastloading",
        "CCCC_IC_SUPPORT",
        "LOOPING_SFX",
        "ADDITIVE",
        "EFFECTS",
        "CUSTOM_BLIPS",
        "Y_OFFSET",
        "FLIPPING",
        "PREZOOM",
        "DESKMOD",
        "EXPANDED_DESK_MODS",
        "CUSTOMOBJECTIONS"
    };

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public string Endpoint => (useWebSocket ? "ws://127.0.0.1:" : "tcp://127.0.0.1:") + Port;

    public IReadOnlyCollection<CapturedPacket> Packets => packets.ToArray();

    public async Task<CapturedPacket> WaitForPacketAsync(
        Func<CapturedPacket, bool> predicate,
        TimeSpan? timeout = null)
    {
        DateTime timeoutAt = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < timeoutAt)
        {
            foreach (CapturedPacket packet in packets)
            {
                if (predicate(packet))
                {
                    return packet;
                }
            }

            await Task.Delay(100, CancellationToken.None);
        }

        throw new TimeoutException("Timed out waiting for expected packet.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cts.Cancel();
        listener.Stop();

        try
        {
            acceptLoopTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown only.
        }

        cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                int connectionId = Interlocked.Increment(ref nextConnectionId);
                _ = Task.Run(() => HandleConnectionAsync(connectionId, client, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during test shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Listener was stopped during shutdown.
        }
    }

    private async Task HandleConnectionAsync(int connectionId, TcpClient client, CancellationToken cancellationToken)
    {
        selectedCharacterByConnection[connectionId] = null;

        try
        {
            using (client)
            {
                using NetworkStream stream = client.GetStream();
                StringBuilder buffer = new StringBuilder();

                if (useWebSocket)
                {
                    await CompleteWebSocketHandshakeAsync(stream, cancellationToken);
                    await SendToClientAsync(stream, $"ID#{connectionId}#tsuserver#7#%", cancellationToken, useWebSocket);
                }
                else
                {
                    string? firstPacket = await ReadPacketAsync(stream, buffer, cancellationToken, useWebSocket);
                    if (firstPacket != null)
                    {
                        RecordPacket(connectionId, firstPacket);
                    }

                    await SendToClientAsync(stream, "decryptor#NOENCRYPT#%", cancellationToken, useWebSocket);
                    await SendToClientAsync(stream, $"ID#{connectionId}#tsuserver#7#%", cancellationToken, useWebSocket);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    string? packet = await ReadPacketAsync(stream, buffer, cancellationToken, useWebSocket);
                    if (packet == null)
                    {
                        break;
                    }

                    RecordPacket(connectionId, packet);

                    if (string.Equals(packet, "ID#AO2#2.11.0#%", StringComparison.Ordinal))
                    {
                        await SendToClientAsync(stream, $"PN#{connectionId}#10#%", cancellationToken, useWebSocket);
                        await SendToClientAsync(stream, "FL#" + string.Join("#", features) + "#%", cancellationToken, useWebSocket);
                    }
                    else if (string.Equals(packet, "askchaa#%", StringComparison.Ordinal))
                    {
                        await SendToClientAsync(stream, "SI#1#0#0#%", cancellationToken, useWebSocket);
                    }
                    else if (string.Equals(packet, "RC#%", StringComparison.Ordinal))
                    {
                        await SendToClientAsync(stream, "SC#" + string.Join("#", characters) + "#%", cancellationToken, useWebSocket);
                        await SendToClientAsync(stream, BuildCharsCheckPacket(), cancellationToken, useWebSocket);
                    }
                    else if (string.Equals(packet, "RM#%", StringComparison.Ordinal))
                    {
                        await SendToClientAsync(stream, "SM#Lobby#%", cancellationToken, useWebSocket);
                    }
                    else if (string.Equals(packet, "RD#%", StringComparison.Ordinal))
                    {
                        await SendToClientAsync(stream, "DONE#%", cancellationToken, useWebSocket);
                    }
                    else if (packet.StartsWith("CC#", StringComparison.Ordinal))
                    {
                        HandleCharacterSelect(connectionId, packet);
                        await SendToClientAsync(stream, BuildCharsCheckPacket(), cancellationToken, useWebSocket);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        finally
        {
            if (selectedCharacterByConnection.TryRemove(connectionId, out _))
            {
                // Selection availability naturally resets for new connections.
            }
        }
    }

    private void HandleCharacterSelect(int connectionId, string packet)
    {
        string[] fields = packet.Split('#', StringSplitOptions.None);
        if (fields.Length < 3 || !int.TryParse(fields[2], out int characterId))
        {
            return;
        }

        lock (characterLock)
        {
            selectedCharacterByConnection[connectionId] = characterId;
        }
    }

    private string BuildCharsCheckPacket()
    {
        lock (characterLock)
        {
            string[] availability = new string[characters.Length];
            for (int index = 0; index < characters.Length; index++)
            {
                bool taken = selectedCharacterByConnection.Values.Any(value => value == index);
                availability[index] = taken ? "1" : "0";
            }

            return "CharsCheck#" + string.Join("#", availability) + "#%";
        }
    }

    private void RecordPacket(int connectionId, string packet)
    {
        packets.Enqueue(new CapturedPacket(connectionId, packet, DateTime.UtcNow));
    }

    private static async Task SendToClientAsync(
        NetworkStream stream,
        string packet,
        CancellationToken cancellationToken,
        bool useWebSocket)
    {
        if (useWebSocket)
        {
            await SendWebSocketTextMessageAsync(stream, packet, cancellationToken);
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(packet);
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string?> ReadPacketAsync(
        NetworkStream stream,
        StringBuilder packetBuffer,
        CancellationToken cancellationToken,
        bool useWebSocket)
    {
        string? bufferedPacket = TakeBufferedPacket(packetBuffer);
        if (bufferedPacket != null)
        {
            return bufferedPacket;
        }

        if (useWebSocket)
        {
            string? message = await ReadWebSocketTextMessageAsync(stream, cancellationToken);
            if (message == null)
            {
                return null;
            }

            packetBuffer.Append(message);
            return TakeBufferedPacket(packetBuffer);
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
            bufferedPacket = TakeBufferedPacket(packetBuffer);
            if (bufferedPacket == null)
            {
                continue;
            }

            return bufferedPacket;
        }

        return null;
    }

    private static string? TakeBufferedPacket(StringBuilder packetBuffer)
    {
        string current = packetBuffer.ToString();
        int end = current.IndexOf("#%", StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        string packet = current.Substring(0, end + 2);
        packetBuffer.Remove(0, end + 2);
        return packet;
    }

    private static async Task CompleteWebSocketHandshakeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        StringBuilder request = new StringBuilder();
        byte[] oneByte = new byte[1];
        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            int bytesRead = await stream.ReadAsync(oneByte.AsMemory(0, 1), cancellationToken);
            if (bytesRead <= 0)
            {
                throw new IOException("WebSocket handshake ended before headers completed.");
            }

            request.Append((char)oneByte[0]);
        }

        string key = request.ToString()
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .Where(parts => string.Equals(parts[0].Trim(), "Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim())
            .FirstOrDefault()
            ?? throw new InvalidOperationException("WebSocket handshake did not include Sec-WebSocket-Key.");

        string accept = Convert.ToBase64String(SHA1.HashData(
            Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        string response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";

        byte[] responseBytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(responseBytes.AsMemory(0, responseBytes.Length), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task SendWebSocketTextMessageAsync(
        NetworkStream stream,
        string message,
        CancellationToken cancellationToken)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message);
        List<byte> frame = new List<byte> { 0x81 };
        if (payload.Length < 126)
        {
            frame.Add((byte)payload.Length);
        }
        else
        {
            frame.Add(126);
            frame.Add((byte)(payload.Length >> 8));
            frame.Add((byte)payload.Length);
        }

        frame.AddRange(payload);
        byte[] bytes = frame.ToArray();
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string?> ReadWebSocketTextMessageAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] header = await ReadExactAsync(stream, 2, cancellationToken);
            byte opcode = (byte)(header[0] & 0x0F);
            bool masked = (header[1] & 0x80) != 0;
            ulong length = (ulong)(header[1] & 0x7F);
            if (length == 126)
            {
                byte[] extended = await ReadExactAsync(stream, 2, cancellationToken);
                length = (ulong)((extended[0] << 8) | extended[1]);
            }
            else if (length == 127)
            {
                byte[] extended = await ReadExactAsync(stream, 8, cancellationToken);
                length = 0;
                foreach (byte value in extended)
                {
                    length = (length << 8) | value;
                }
            }

            byte[] mask = masked ? await ReadExactAsync(stream, 4, cancellationToken) : Array.Empty<byte>();
            byte[] payload = await ReadExactAsync(stream, checked((int)length), cancellationToken);
            if (masked)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(payload[i] ^ mask[i % 4]);
                }
            }

            if (opcode == 0x8)
            {
                return null;
            }

            if (opcode == 0x1)
            {
                return Encoding.UTF8.GetString(payload);
            }
        }

        return null;
    }

    private static async Task<byte[]> ReadExactAsync(
        NetworkStream stream,
        int length,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (bytesRead <= 0)
            {
                throw new IOException("Connection closed before expected bytes arrived.");
            }

            offset += bytesRead;
        }

        return buffer;
    }
}

internal sealed record CapturedPacket(int ConnectionId, string Packet, DateTime TimestampUtc)
{
    public ICMessage? TryParseIcMessage()
    {
        return Packet.StartsWith("MS#", StringComparison.Ordinal)
            ? ICMessage.FromConsoleLine(Packet)
            : null;
    }

    public IReadOnlyList<string> ExtractFields()
    {
        string[] parts = Packet.Split('#', StringSplitOptions.None);
        if (parts.Length <= 1)
        {
            return Array.Empty<string>();
        }

        int endIndex = parts.Length;
        if (string.Equals(parts[^1], "%", StringComparison.Ordinal))
        {
            endIndex--;
        }

        int fieldCount = Math.Max(0, endIndex - 1);
        string[] fields = new string[fieldCount];
        Array.Copy(parts, 1, fields, 0, fieldCount);
        return fields;
    }

    public string GetField(int index)
    {
        IReadOnlyList<string> fields = ExtractFields();
        if (index < 0 || index >= fields.Count)
        {
            return string.Empty;
        }

        return DecodeAoSymbols(fields[index] ?? string.Empty);
    }

    private static string DecodeAoSymbols(string value)
    {
        return value
            .Replace("<percent>", "%", StringComparison.Ordinal)
            .Replace("<dollar>", "$", StringComparison.Ordinal)
            .Replace("<num>", "#", StringComparison.Ordinal)
            .Replace("<and>", "&", StringComparison.Ordinal);
    }
}

internal static class GmPacketUiDriver
{
    public static void AddClient(
        FlaUiSmokeApp app,
        Window mainWindow,
        string clientName,
        string? selectedCharacterName = null)
    {
        app.WaitForDescendantById(mainWindow, "Main.AddClient").AsButton().Invoke();
        Window selectorWindow = app.WaitForReadyWindow("CharacterSelector.Cancel");
        SetText(selectorWindow, "CharacterSelector.ClientName", clientName);
        string selectorAutomationId = string.IsNullOrWhiteSpace(selectedCharacterName)
            ? "CharacterSelector.FirstSelectableCard"
            : "CharacterSelector.Character." + SanitizeAutomationSegment(selectedCharacterName);

        app.WaitForDescendantById(selectorWindow, selectorAutomationId)?.Click();
        app.WaitForReadyWindow("Main.AddClient");
    }

    public static FlaUI.Core.AutomationElements.TextBox SetText(Window window, string automationId, string value)
    {
        FlaUI.Core.AutomationElements.TextBox textBox = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
            ?.AsTextBox()
            ?? throw new InvalidOperationException("Text box was not found: " + automationId);
        textBox.Text = value;
        return textBox;
    }

    public static void PressEnter(FlaUI.Core.AutomationElements.TextBox textBox)
    {
        textBox.Focus();
        System.Windows.Forms.SendKeys.SendWait("{ENTER}");
    }

    public static void WaitForElementEnabled(Window window, string automationId, bool expectedEnabled)
    {
        RetryAction(
            () =>
            {
                AutomationElement element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                    ?? throw new InvalidOperationException("Element not found: " + automationId);
                if (element.IsEnabled != expectedEnabled)
                {
                    throw new InvalidOperationException("Enabled state did not match yet.");
                }
            },
            "wait for enabled state on " + automationId);
    }

    public static void Toggle(Window window, string automationId, ToggleState expectedState)
    {
        RetryAction(
            () =>
            {
                FlaUI.Core.AutomationElements.ToggleButton toggleButton = window
                    .FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                    ?.AsToggleButton()
                    ?? throw new InvalidOperationException("Toggle button was not found: " + automationId);
                while (toggleButton.ToggleState != expectedState)
                {
                    toggleButton.Toggle();
                }
            },
            "toggle " + automationId);
    }

    public static void SelectComboBoxItem(Window window, string automationId, string itemName)
    {
        FlaUI.Core.AutomationElements.ComboBox comboBox = window.FindFirstDescendant(
                cf => cf.ByAutomationId(automationId + ".ComboBox"))
            ?.AsComboBox()
            ?? throw new InvalidOperationException("Combo box was not found: " + automationId);

        RetryAction(
            () =>
            {
                comboBox.Expand();

                ComboBoxItem? selectedItem = comboBox.Select(itemName);
                if (selectedItem != null && IsComboSelectionConfirmed(window, automationId, itemName))
                {
                    comboBox.Collapse();
                    return;
                }

                AutomationElement? popupItem = FindPopupSelectionTarget(window, automationId, itemName);
                if (popupItem != null)
                {
                    if (popupItem.Patterns.SelectionItem.IsSupported)
                    {
                        popupItem.Patterns.SelectionItem.Pattern.Select();
                    }
                    else if (popupItem.Patterns.Invoke.IsSupported)
                    {
                        popupItem.Patterns.Invoke.Pattern.Invoke();
                    }
                    else
                    {
                        popupItem.Click();
                    }
                }
                else
                {
                    if (!TryConfirmComboBoxViaTextEntry(window, automationId, comboBox, itemName))
                    {
                        comboBox.Collapse();
                        throw new InvalidOperationException("Combo box item was not found: " + itemName);
                    }
                }

                comboBox.Collapse();

                if (!IsComboSelectionConfirmed(window, automationId, itemName))
                {
                    if (!TryConfirmComboBoxViaTextEntry(window, automationId, comboBox, itemName))
                    {
                        throw new InvalidOperationException("Combo box selection did not stick: " + itemName);
                    }
                }
            },
            "select combo box item " + itemName + " for " + automationId);
    }

    public static void Click(Window window, string automationId)
    {
        RetryAction(
            () =>
            {
                AutomationElement element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                    ?? throw new InvalidOperationException("Element not found: " + automationId);
                if (element.Patterns.Invoke.IsSupported)
                {
                    element.Patterns.Invoke.Pattern.Invoke();
                    return;
                }

                if (element.Patterns.Toggle.IsSupported)
                {
                    while (element.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                    {
                        element.Patterns.Toggle.Pattern.Toggle();
                    }

                    return;
                }

                if (element.Patterns.SelectionItem.IsSupported)
                {
                    element.Patterns.SelectionItem.Pattern.Select();
                    return;
                }

                element.Click();
            },
            "click " + automationId);
    }

    public static async Task<CapturedPacket> SendIcAndCapturePacketAsync(
        Window mainWindow,
        string message,
        GmPacketLoopbackServer server,
        Func<CapturedPacket, bool>? extraPredicate = null)
    {
        FlaUI.Core.AutomationElements.TextBox messageBox = SetText(mainWindow, "Main.Ic.Message", message);
        PressEnter(messageBox);

        return await server.WaitForPacketAsync(packet =>
            packet.Packet.StartsWith("MS#", StringComparison.Ordinal)
            && (packet.Packet.Contains("#" + message + "#", StringComparison.Ordinal)
                || packet.Packet.Contains("#~" + message + "~#", StringComparison.Ordinal))
            && (extraPredicate == null || extraPredicate(packet)));
    }

    public static void RetryAction(Action action, string operation)
    {
        Exception? lastException = null;
        RetryResult<bool> result = Retry.WhileFalse(
            () =>
            {
                try
                {
                    action();
                    return true;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    return false;
                }
            },
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(150),
            throwOnTimeout: false,
            ignoreException: true);

        if (result.Success)
        {
            return;
        }

        throw new InvalidOperationException("Failed to " + operation + ".", lastException);
    }

    private static string SanitizeAutomationSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Empty";
        }

        StringBuilder builder = new StringBuilder(value.Length);
        foreach (char c in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString();
    }

    private static AutomationElement? FindPopupSelectionTarget(Window window, string automationId, string itemName)
    {
        string itemAutomationId = automationId + ".Item." + SanitizeAutomationSegment(itemName);
        AutomationElement desktop = window.Automation.GetDesktop();

        AutomationElement? directTarget = desktop.FindFirstDescendant(cf => cf.ByAutomationId(itemAutomationId));
        if (directTarget != null)
        {
            return directTarget;
        }

        directTarget = desktop.FindFirstDescendant(
            cf => cf.ByName(itemName).And(
                cf.ByControlType(ControlType.ListItem).Or(cf.ByControlType(ControlType.DataItem))));
        if (directTarget != null)
        {
            return directTarget;
        }

        AutomationElement? namedDescendant = desktop.FindFirstDescendant(cf => cf.ByName(itemName));
        return FindSelectableAncestor(namedDescendant);
    }

    private static AutomationElement? FindSelectableAncestor(AutomationElement? element)
    {
        AutomationElement? current = element;
        while (current != null)
        {
            if (current.ControlType == ControlType.ListItem
                || current.ControlType == ControlType.DataItem
                || current.Patterns.SelectionItem.IsSupported
                || current.Patterns.Invoke.IsSupported)
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool TryConfirmComboBoxViaTextEntry(
        Window window,
        string automationId,
        FlaUI.Core.AutomationElements.ComboBox comboBox,
        string itemName)
    {
        FlaUI.Core.AutomationElements.TextBox? input = window.FindFirstDescendant(
                cf => cf.ByAutomationId(automationId + ".Input"))
            ?.AsTextBox();

        if (input == null)
        {
            return false;
        }

        try
        {
            input.Text = itemName;
            comboBox.Focus();
            System.Windows.Forms.SendKeys.SendWait("{ENTER}");
            WaitUntilComboSelection(window, automationId, itemName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WaitUntilComboSelection(Window window, string automationId, string itemName)
    {
        RetryAction(
            () =>
            {
                if (!IsComboSelectionConfirmed(window, automationId, itemName))
                {
                    throw new InvalidOperationException("Selection not confirmed yet.");
                }
            },
            "wait for combo box selection " + itemName + " for " + automationId);
    }

    private static bool IsComboSelectionConfirmed(Window window, string automationId, string itemName)
    {
        FlaUI.Core.AutomationElements.TextBox? input = window.FindFirstDescendant(
                cf => cf.ByAutomationId(automationId + ".Input"))
            ?.AsTextBox();
        string currentText = string.Empty;

        if (input != null)
        {
            currentText = input.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentText))
            {
                currentText = input.Name?.Trim() ?? string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(currentText))
        {
            FlaUI.Core.AutomationElements.ComboBox? combo = window.FindFirstDescendant(
                    cf => cf.ByAutomationId(automationId + ".ComboBox"))
                ?.AsComboBox();
            currentText = combo?.Name?.Trim() ?? string.Empty;
        }

        return string.Equals(currentText, itemName, StringComparison.OrdinalIgnoreCase);
    }

}
