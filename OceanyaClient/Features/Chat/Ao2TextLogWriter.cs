using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient.Features.Chat
{
    /// <summary>
    /// Writes AO2-compatible text logs under the selected AO installation's logs folder.
    /// </summary>
    /// <remarks>
    /// Session setup and the "Joined server" header are written synchronously so the log file exists as soon
    /// as a session starts. The hot per-message append path is offloaded to a single background consumer thread,
    /// so IC/OOC/action logging never blocks the UI thread with disk latency. The actual on-disk write is byte-for-byte
    /// the original open/seek/append/close per line with <see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/>,
    /// so external readers (Notepad, "Find in all logs", AO2) can open the log at any time exactly as before. Line
    /// content and ordering are identical to the previous synchronous writer: timestamps are captured on the calling
    /// thread before the text is queued, and a single FIFO consumer preserves order. See the "message freeze" map entry.
    /// </remarks>
    internal sealed class Ao2TextLogWriter : IDisposable
    {
        private static readonly Regex InvalidServerFolderChars = new Regex("[\\\\/:*?\"<>|']", RegexOptions.Compiled);
        private readonly object syncRoot = new object();
        private string logFilePath = string.Empty;

        // Background single-consumer write queue. Producers (UI thread hot path) enqueue formatted lines; the
        // consumer drains them in order. Flush requests carry a signal so callers can wait for the disk to catch up.
        private readonly BlockingCollection<LogWriteRequest> writeQueue = new BlockingCollection<LogWriteRequest>();
        private readonly Thread consumerThread;

        public Ao2TextLogWriter()
        {
            consumerThread = new Thread(ConsumeQueue)
            {
                IsBackground = true,
                Name = "Ao2TextLogWriter"
            };
            consumerThread.Start();
        }

        public void ResetSession()
        {
            // Drain everything already queued so no lines are lost, then clear the session.
            Flush();
            lock (syncRoot)
            {
                logFilePath = string.Empty;
            }
        }

        public void RefreshSession()
        {
            Dictionary<string, string> configValues = Ao2ConfigIniSettings.Load();
            bool textLoggingEnabled = Ao2ConfigIniSettings.GetBool(configValues, "automatic_logging_enabled", true);
            bool demoLoggingEnabled = Ao2ConfigIniSettings.GetBool(configValues, "demo_logging_enabled", true);
            if (!textLoggingEnabled && !demoLoggingEnabled)
            {
                ResetSession();
                return;
            }

            string baseDirectory = ResolveAoBaseDirectory();
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                ResetSession();
                return;
            }

            lock (syncRoot)
            {
                EnsureSessionUnlocked();
            }
        }

        public static string ResolveLogRootDirectory()
        {
            string baseDirectory = ResolveAoBaseDirectory();
            return string.IsNullOrWhiteSpace(baseDirectory)
                ? string.Empty
                : Path.Combine(baseDirectory, "logs");
        }

        public void AppendIcMessage(ICMessage? sourceMessage, string showName, string message)
        {
            AppendChatLogPiece(sourceMessage?.Character ?? showName, showName, message, string.Empty);
        }

        public void AppendIcAction(string showName, string action, string message)
        {
            AppendChatLogPiece(showName, showName, message, action);
        }

        public void AppendServerMessage(string showName, string message)
        {
            AppendLine("[OOC][" + FormatQtUtcTextDate(DateTime.UtcNow) + "] " + MaybeUnknown(showName) + ": " + MaybeUnknown(message));
        }

        /// <summary>
        /// Blocks until every line queued before this call has been written to disk. Used at shutdown and in tests.
        /// </summary>
        public void Flush()
        {
            if (writeQueue.IsAddingCompleted)
            {
                return;
            }

            using ManualResetEventSlim done = new ManualResetEventSlim(false);
            try
            {
                writeQueue.Add(new LogWriteRequest(null, done));
            }
            catch (InvalidOperationException)
            {
                // Queue was completed concurrently; nothing left to flush.
                return;
            }

            done.Wait(TimeSpan.FromSeconds(5));
        }

        public void Dispose()
        {
            writeQueue.CompleteAdding();
            try
            {
                consumerThread.Join(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Best-effort join on shutdown.
            }

            writeQueue.Dispose();
        }

        private void AppendChatLogPiece(string character, string characterName, string message, string action)
        {
            string details = "[" + FormatQtUtcTextDate(DateTime.UtcNow) + "] " + MaybeUnknown(characterName);
            if (!string.Equals(characterName, character, StringComparison.Ordinal))
            {
                details += " (" + MaybeUnknown(character) + ")";
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                details += " " + action.Trim();
            }

            details += ": " + MaybeUnknown(message);
            AppendLine(details);
        }

        private void AppendLine(string text)
        {
            // Cheap cached config check on the calling thread preserves the previous "drop when logging disabled"
            // behavior without re-parsing config.ini per message. The actual disk write is deferred to the consumer.
            Dictionary<string, string> configValues = Ao2ConfigIniSettings.Load();
            if (!Ao2ConfigIniSettings.GetBool(configValues, "automatic_logging_enabled", true))
            {
                return;
            }

            if (writeQueue.IsAddingCompleted)
            {
                return;
            }

            try
            {
                writeQueue.Add(new LogWriteRequest(text, null));
            }
            catch (InvalidOperationException)
            {
                // Queue completed during shutdown; drop the line.
            }
        }

        private void ConsumeQueue()
        {
            foreach (LogWriteRequest request in writeQueue.GetConsumingEnumerable())
            {
                if (request.FlushSignal != null)
                {
                    request.FlushSignal.Set();
                    continue;
                }

                if (request.Text == null)
                {
                    continue;
                }

                try
                {
                    WriteLineToDisk(request.Text);
                }
                catch (Exception ex)
                {
                    CustomConsole.Error("Failed to write AO2 text log line.", ex);
                }
            }
        }

        private void WriteLineToDisk(string text)
        {
            lock (syncRoot)
            {
                if (string.IsNullOrWhiteSpace(logFilePath) && !EnsureSessionUnlocked())
                {
                    return;
                }

                WriteLineUnlocked(text);
            }
        }

        /// <summary>
        /// Establishes the session log file (path + "Joined server" header) if not already established.
        /// Must be called under <see cref="syncRoot"/>. Returns true when a usable session exists afterward.
        /// </summary>
        private bool EnsureSessionUnlocked()
        {
            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                return true;
            }

            Dictionary<string, string> configValues = Ao2ConfigIniSettings.Load();
            bool textLoggingEnabled = Ao2ConfigIniSettings.GetBool(configValues, "automatic_logging_enabled", true);
            bool demoLoggingEnabled = Ao2ConfigIniSettings.GetBool(configValues, "demo_logging_enabled", true);
            if (!textLoggingEnabled && !demoLoggingEnabled)
            {
                return false;
            }

            string baseDirectory = ResolveAoBaseDirectory();
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return false;
            }

            string serverName = SaveFile.Data.SelectedServerName?.Trim() ?? string.Empty;
            string serverAddress = Globals.GetSelectedServerEndpoint()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverName))
            {
                serverName = string.IsNullOrWhiteSpace(serverAddress) ? "Direct Connect" : serverAddress;
            }

            string sanitizedServerName = SanitizeServerFolderName(serverName);
            string fileName = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ss 'UTC'.'log'", CultureInfo.InvariantCulture);
            logFilePath = Path.Combine(baseDirectory, "logs", sanitizedServerName, fileName);

            WriteLineUnlocked(
                "Joined server "
                + sanitizedServerName
                + " hosted on address "
                + serverAddress
                + " on "
                + FormatQtUtcTextDate(DateTime.UtcNow));
            return true;
        }

        /// <summary>
        /// Writes a single line to the session log using the original open/seek/append/close-per-line strategy so
        /// the file is never held open between writes and external readers keep the same access they had before.
        /// Must be called under <see cref="syncRoot"/> with a non-empty <see cref="logFilePath"/>.
        /// </summary>
        private void WriteLineUnlocked(string text)
        {
            string? directory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using FileStream stream = new FileStream(
                logFilePath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            bool fileExists = stream.Length > 0;
            stream.Seek(0, SeekOrigin.End);
            using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false));
            if (fileExists)
            {
                writer.Write("\r\n");
            }

            writer.Write(text);
        }

        private static string ResolveAoBaseDirectory()
        {
            string configPath = Ao2ConfigIniSettings.ConfigPath;
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                string? directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    string? directoryName = Path.GetFileName(
                        directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.Equals(directoryName, "base", StringComparison.OrdinalIgnoreCase))
                    {
                        string? parent = Path.GetDirectoryName(
                            directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (!string.IsNullOrWhiteSpace(parent))
                        {
                            return parent;
                        }
                    }

                    return directory;
                }
            }

            return AppContext.BaseDirectory;
        }

        private static string SanitizeServerFolderName(string value)
        {
            string sanitized = InvalidServerFolderChars.Replace(value.Trim(), string.Empty);
            return string.IsNullOrWhiteSpace(sanitized) ? "Direct Connect" : sanitized;
        }

        private static string MaybeUnknown(string? value)
        {
            return string.IsNullOrEmpty(value) ? "UNKNOWN" : value;
        }

        private static string FormatQtUtcTextDate(DateTime timestampUtc)
        {
            DateTime utc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
            return utc.ToString("ddd MMM d HH:mm:ss yyyy 'UTC'", CultureInfo.InvariantCulture);
        }

        private readonly struct LogWriteRequest
        {
            public LogWriteRequest(string? text, ManualResetEventSlim? flushSignal)
            {
                Text = text;
                FlushSignal = flushSignal;
            }

            public string? Text { get; }

            public ManualResetEventSlim? FlushSignal { get; }
        }
    }
}
