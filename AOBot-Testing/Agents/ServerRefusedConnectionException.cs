using System;

namespace AOBot_Testing.Agents
{
    /// <summary>
    /// Thrown when the server answers the handshake with a <c>BD#</c> disconnect instead of completing it.
    /// </summary>
    /// <remarks>
    /// AO servers reject a connection outright for rate limiting ("Please wait before connecting another
    /// client"), bans, or a full server, and they say so immediately. Treating that as a generic handshake
    /// timeout meant waiting out the full timeout for a packet that was never coming - measured at 4.1
    /// seconds of a launch - and then discarding the reason the server had already given. This carries the
    /// reason so the caller can retry deliberately and tell the user what happened.
    /// </remarks>
    public sealed class ServerRefusedConnectionException : Exception
    {
        public ServerRefusedConnectionException(string reason)
            : base(reason)
        {
            Reason = reason;
        }

        /// <summary>The server's own explanation, decoded from the <c>BD#</c> packet.</summary>
        public string Reason { get; }
    }
}
