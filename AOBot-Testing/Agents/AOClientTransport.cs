using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AOBot_Testing.Agents
{
    internal static class AOClientProtocolConstants
    {
        public const string ClientName = "AO2";
        public const string ClientVersion = "2.11.0";
        public const string UserAgent = "AttorneyOnline/4.0 (Desktop); OceanyaClient";
    }

    internal interface IAOClientTransport : IAsyncDisposable
    {
        bool IsConnected { get; }

        string TransportName { get; }

        Task ConnectAsync(Uri serverUri, CancellationToken cancellationToken);

        Task SendPacketAsync(string packet, CancellationToken cancellationToken);

        Task<string?> ReceivePacketAsync(CancellationToken cancellationToken);

        Task CloseAsync(CancellationToken cancellationToken);
    }

    internal static class AOClientTransportFactory
    {
        public static IAOClientTransport Create(Uri serverUri)
        {
            if (serverUri == null)
            {
                throw new ArgumentNullException(nameof(serverUri));
            }

            if (string.Equals(serverUri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase))
            {
                return new TcpAOClientTransport();
            }

            if (string.Equals(serverUri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(serverUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            {
                return new WebSocketAOClientTransport();
            }

            throw new NotSupportedException($"Unsupported server scheme '{serverUri.Scheme}'.");
        }
    }

    internal abstract class BufferedAOClientTransport : IAOClientTransport
    {
        private readonly Queue<string> pendingPackets = new Queue<string>();
        private readonly StringBuilder packetBuffer = new StringBuilder();

        public abstract bool IsConnected { get; }

        public abstract string TransportName { get; }

        public abstract Task ConnectAsync(Uri serverUri, CancellationToken cancellationToken);

        public abstract Task SendPacketAsync(string packet, CancellationToken cancellationToken);

        public abstract Task<string?> ReceivePacketAsync(CancellationToken cancellationToken);

        public abstract Task CloseAsync(CancellationToken cancellationToken);

        public abstract ValueTask DisposeAsync();

        protected string? DequeueBufferedPacket()
        {
            return pendingPackets.Count > 0
                ? pendingPackets.Dequeue()
                : null;
        }

        protected string? BufferAndTakePacket(string data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                packetBuffer.Append(data);
                ExtractPackets();
            }

            return DequeueBufferedPacket();
        }

        protected void ResetPacketBuffer()
        {
            pendingPackets.Clear();
            packetBuffer.Clear();
        }

        protected string GetBufferedData()
        {
            return packetBuffer.ToString();
        }

        private void ExtractPackets()
        {
            string current = packetBuffer.ToString();
            int packetStart = 0;

            while (packetStart < current.Length)
            {
                int packetEnd = current.IndexOf("#%", packetStart, StringComparison.Ordinal);
                if (packetEnd < 0)
                {
                    break;
                }

                int packetLength = (packetEnd + 2) - packetStart;
                pendingPackets.Enqueue(current.Substring(packetStart, packetLength));
                packetStart = packetEnd + 2;
            }

            if (packetStart > 0)
            {
                packetBuffer.Remove(0, packetStart);
            }
        }
    }

    internal sealed class WebSocketAOClientTransport : BufferedAOClientTransport
    {
        private ClientWebSocket? socket;

        public override bool IsConnected => socket != null && socket.State == WebSocketState.Open;

        public override string TransportName => "WebSocket";

        public override async Task ConnectAsync(Uri serverUri, CancellationToken cancellationToken)
        {
            ClientWebSocket clientWebSocket = new ClientWebSocket();
            clientWebSocket.Options.SetRequestHeader("User-Agent", AOClientProtocolConstants.UserAgent);

            socket = clientWebSocket;

            try
            {
                await clientWebSocket.ConnectAsync(serverUri, cancellationToken);
            }
            catch
            {
                socket = null;
                clientWebSocket.Dispose();
                throw;
            }
        }

        public override async Task SendPacketAsync(string packet, CancellationToken cancellationToken)
        {
            ClientWebSocket? clientWebSocket = socket;
            if (clientWebSocket == null || clientWebSocket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket transport is not connected.");
            }

            byte[] messageBytes = Encoding.UTF8.GetBytes(packet ?? string.Empty);
            await clientWebSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }

        public override async Task<string?> ReceivePacketAsync(CancellationToken cancellationToken)
        {
            string? bufferedPacket = DequeueBufferedPacket();
            if (!string.IsNullOrEmpty(bufferedPacket))
            {
                return bufferedPacket;
            }

            ClientWebSocket? clientWebSocket = socket;
            if (clientWebSocket == null)
            {
                return null;
            }

            byte[] buffer = new byte[4096];
            StringBuilder messageBuilder = new StringBuilder();

            while (clientWebSocket.State == WebSocketState.Open || clientWebSocket.State == WebSocketState.CloseReceived)
            {
                WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseAsync(CancellationToken.None);
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                return BufferAndTakePacket(messageBuilder.ToString());
            }

            return null;
        }

        public override async Task CloseAsync(CancellationToken cancellationToken)
        {
            ClientWebSocket? clientWebSocket = socket;
            socket = null;
            ResetPacketBuffer();

            if (clientWebSocket == null)
            {
                return;
            }

            try
            {
                if (clientWebSocket.State == WebSocketState.Open || clientWebSocket.State == WebSocketState.CloseReceived)
                {
                    await clientWebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Disconnecting",
                        cancellationToken);
                }
                else if (clientWebSocket.State != WebSocketState.Closed && clientWebSocket.State != WebSocketState.None)
                {
                    clientWebSocket.Abort();
                }
            }
            catch
            {
                clientWebSocket.Abort();
            }
            finally
            {
                clientWebSocket.Dispose();
            }
        }

        public override ValueTask DisposeAsync()
        {
            return new ValueTask(CloseAsync(CancellationToken.None));
        }
    }

    internal sealed class TcpAOClientTransport : BufferedAOClientTransport
    {
        private TcpClient? client;
        private NetworkStream? stream;

        public override bool IsConnected => client != null && client.Connected && stream != null;

        public override string TransportName => "TCP";

        public override async Task ConnectAsync(Uri serverUri, CancellationToken cancellationToken)
        {
            TcpClient tcpClient = new TcpClient();
            client = tcpClient;

            try
            {
                await tcpClient.ConnectAsync(serverUri.Host, serverUri.Port, cancellationToken);
                stream = tcpClient.GetStream();
            }
            catch
            {
                stream = null;
                client = null;
                tcpClient.Dispose();
                throw;
            }
        }

        public override async Task SendPacketAsync(string packet, CancellationToken cancellationToken)
        {
            NetworkStream? networkStream = stream;
            if (networkStream == null || client == null || !client.Connected)
            {
                throw new InvalidOperationException("TCP transport is not connected.");
            }

            byte[] messageBytes = Encoding.UTF8.GetBytes(packet ?? string.Empty);
            await networkStream.WriteAsync(messageBytes.AsMemory(0, messageBytes.Length), cancellationToken);
            await networkStream.FlushAsync(cancellationToken);
        }

        public override async Task<string?> ReceivePacketAsync(CancellationToken cancellationToken)
        {
            string? bufferedPacket = DequeueBufferedPacket();
            if (!string.IsNullOrEmpty(bufferedPacket))
            {
                return bufferedPacket;
            }

            NetworkStream? networkStream = stream;
            if (networkStream == null || client == null)
            {
                return null;
            }

            byte[] buffer = new byte[4096];

            while (client.Connected)
            {
                int bytesRead = await networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead <= 0)
                {
                    await CloseAsync(CancellationToken.None);
                    return null;
                }

                string? nextPacket = BufferAndTakePacket(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                if (!string.IsNullOrEmpty(nextPacket))
                {
                    return nextPacket;
                }

                string bufferedData = GetBufferedData();
                if (LooksLikeHttpResponse(bufferedData))
                {
                    throw new InvalidOperationException(
                        "Server returned an HTTP response on a raw TCP connection. " +
                        "This endpoint is likely a ws/wss port, not a legacy tcp port.");
                }
            }

            await CloseAsync(CancellationToken.None);
            return null;
        }

        public override Task CloseAsync(CancellationToken cancellationToken)
        {
            NetworkStream? networkStream = stream;
            TcpClient? tcpClient = client;

            stream = null;
            client = null;
            ResetPacketBuffer();

            networkStream?.Dispose();
            tcpClient?.Dispose();
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync()
        {
            return new ValueTask(CloseAsync(CancellationToken.None));
        }

        private static bool LooksLikeHttpResponse(string bufferedData)
        {
            if (string.IsNullOrWhiteSpace(bufferedData))
            {
                return false;
            }

            return bufferedData.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)
                || bufferedData.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
                || bufferedData.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }
    }
}
