using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    /// <summary>
    /// Enhanced console logging class that handles console output while providing additional error tracking features
    /// </summary>
    public static class CustomConsole
    {
        /// <summary>
        /// Log levels for console output
        /// </summary>
        public enum LogLevel
        {
            Info,
            Warning,
            Error,
            Debug
        }

        /// <summary>
        /// Logical category of a log entry, used for filtering in the debug console
        /// </summary>
        public enum LogCategory
        {
            /// <summary>Internal app state, startup, connection, server features</summary>
            System,
            /// <summary>Raw AO2 network packets (send/receive)</summary>
            Network,
            /// <summary>IC (in-character) chat messages</summary>
            IC,
            /// <summary>OOC (out-of-character) chat messages</summary>
            OOC,
            /// <summary>AO2 viewport rendering and playback diagnostics</summary>
            Viewport,
            /// <summary>Music list packet, tree build, and playback diagnostics</summary>
            MusicList,
            /// <summary>Area navigator list build and refresh diagnostics</summary>
            AreaVisualizer,
            /// <summary>AO2 pairing studio candidate discovery and /getarea diagnostics</summary>
            PairingStudio,
            /// <summary>SFX/blip/shout packet diagnostics</summary>
            SFX,
            /// <summary>webAO-style asset fallback: probes, downloads, manifests, and latency</summary>
            WebAssets
        }

        /// <summary>
        /// A single structured log entry
        /// </summary>
        public record LogEntry(string Text, LogLevel Level, LogCategory Category, DateTime Timestamp);

        /// <summary>
        /// How many entries are kept in memory for the debug console window.
        /// </summary>
        /// <remarks>
        /// This used to be 1,000,000 entries held in TWO parallel lists (a formatted string AND a
        /// <see cref="LogEntry"/> record per line), which on a long session with packet logging on is
        /// hundreds of megabytes of pure log text that is never released. The full stream now goes to
        /// <see cref="DebugFileLogger"/> on disk, so memory only has to hold what the console window can
        /// realistically show; anything older is read from <c>DEBUG.txt</c>.
        /// </remarks>
        public const int MaxStoredEntries = 25_000;
        private static readonly object syncRoot = new object();

        /// <summary>
        /// Structured log entries kept for the debug console, oldest first, capped at
        /// <see cref="MaxStoredEntries"/>.
        /// </summary>
        /// <remarks>
        /// A queue, not a list: the previous implementation trimmed with <c>RemoveRange(0, n)</c>, which
        /// copies the entire remaining buffer down on every trim.
        /// </remarks>
        private static readonly Queue<LogEntry> logEntries = new Queue<LogEntry>();

        /// <summary>Number of entries currently held in memory.</summary>
        public static int StoredEntryCount
        {
            get
            {
                lock (syncRoot)
                {
                    return logEntries.Count;
                }
            }
        }

        /// <summary>
        /// Fires with the formatted string whenever a line is written (backward-compat)
        /// </summary>
        public static Action<string>? OnWriteLine;

        /// <summary>
        /// Fires with the full structured entry whenever a line is written
        /// </summary>
        public static Action<LogEntry>? OnLogEntry;

        public static List<LogEntry> GetLogEntriesSnapshot()
        {
            lock (syncRoot)
            {
                return new List<LogEntry>(logEntries);
            }
        }

        /// <summary>Replaces the in-memory entries (test helper).</summary>
        public static void ReplaceStoredEntriesForTests(IEnumerable<LogEntry> entries)
        {
            lock (syncRoot)
            {
                logEntries.Clear();
                foreach (LogEntry entry in entries)
                {
                    logEntries.Enqueue(entry);
                }
            }
        }

        /// <summary>Clears the in-memory entries (test helper).</summary>
        public static void ClearStoredEntriesForTests()
        {
            lock (syncRoot)
            {
                logEntries.Clear();
            }
        }

        /// <summary>
        /// Base method for all logging - writes a message to the console with timestamp
        /// </summary>
        public static void WriteLine(string message)
        {
            string timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(timestampedMessage);
            System.Diagnostics.Debug.WriteLine(timestampedMessage);

            OnWriteLine?.Invoke(timestampedMessage);
        }

        /// <summary>
        /// Writes a log message with the specified log level and optional exception details
        /// </summary>
        public static void Log(string message, LogLevel level = LogLevel.Info, LogCategory category = LogCategory.System,
            Exception? exception = null,
            [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
        {
            var levelPrefix = level switch
            {
                LogLevel.Info => "ℹ️",
                LogLevel.Warning => "⚠️",
                LogLevel.Error => "❌",
                LogLevel.Debug => "🔍",
                _ => ""
            };

            string fileName = Path.GetFileName(sourceFile);
            string formattedMessage = $"{levelPrefix} {message}";

            // Add source location for non-info messages
            if (level != LogLevel.Info)
            {
                formattedMessage += $" [{fileName}:{sourceLine}]";
            }

            var entry = new LogEntry(formattedMessage, level, category, DateTime.Now);
            AddStoredEntry(entry);
            OnLogEntry?.Invoke(entry);

            string timestampedMessage = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {formattedMessage}";
            Console.WriteLine(timestampedMessage);
            System.Diagnostics.Debug.WriteLine(timestampedMessage);
            OnWriteLine?.Invoke(timestampedMessage);

            if (exception != null)
            {
                var stackTrace = new StackTrace(exception, true);
                var frame = stackTrace.GetFrame(0);
                var exLineNumber = frame?.GetFileLineNumber() ?? 0;
                var exFileName = Path.GetFileName(frame?.GetFileName() ?? "unknown");

                LogExceptionLine($"   Exception: {exception.GetType().Name}", level, category, entry.Timestamp);
                LogExceptionLine($"   Message: {exception.Message}", level, category, entry.Timestamp);
                LogExceptionLine($"   Location: {exFileName}:{exLineNumber}", level, category, entry.Timestamp);

                var firstStackLine = exception.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(firstStackLine))
                {
                    LogExceptionLine($"   Stack: {firstStackLine}", level, category, entry.Timestamp);
                }

                if (exception.InnerException != null)
                {
                    LogExceptionLine($"   Inner Exception: {exception.InnerException.Message}", level, category, entry.Timestamp);
                }
            }
        }

        private static void LogExceptionLine(string text, LogLevel level, LogCategory category, DateTime timestamp)
        {
            var entry = new LogEntry(text, level, category, timestamp);
            AddStoredEntry(entry);
            OnLogEntry?.Invoke(entry);

            string timestampedMessage = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] {text}";
            Console.WriteLine(timestampedMessage);
            System.Diagnostics.Debug.WriteLine(timestampedMessage);
            OnWriteLine?.Invoke(timestampedMessage);
        }

        private static void AddStoredEntry(LogEntry entry)
        {
            lock (syncRoot)
            {
                logEntries.Enqueue(entry);
                while (logEntries.Count > MaxStoredEntries)
                {
                    logEntries.Dequeue();
                }
            }
        }

        /// <summary>
        /// Logs an informational message
        /// </summary>
        public static void Info(string message, LogCategory category = LogCategory.System,
            [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
            => Log(message, LogLevel.Info, category, null, sourceFile, sourceLine);

        /// <summary>
        /// Logs a warning message with optional exception details
        /// </summary>
        public static void Warning(string message, Exception? ex = null, LogCategory category = LogCategory.System,
            [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
            => Log(message, LogLevel.Warning, category, ex, sourceFile, sourceLine);

        /// <summary>
        /// Logs an error message with optional exception details
        /// </summary>
        public static void Error(string message, Exception? ex = null, LogCategory category = LogCategory.System,
            [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
            => Log(message, LogLevel.Error, category, ex, sourceFile, sourceLine);

        /// <summary>
        /// Logs a debug message (only in debug builds)
        /// </summary>
        /// <summary>
        /// Whether <see cref="Debug"/> lines are recorded.
        /// </summary>
        /// <remarks>
        /// These used to be compiled out with <c>#if DEBUG</c>, which meant a log collected from a user on
        /// a release build was missing ~30 diagnostic sites - exactly the ones worth having when something
        /// only reproduces on someone else's machine. They are now a runtime switch so a release build can
        /// produce a complete DEBUG.txt.
        /// </remarks>
        public static bool IsDebugLoggingEnabled { get; set; } = true;

        public static void Debug(string message, LogCategory category = LogCategory.System,
            [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
        {
            if (!IsDebugLoggingEnabled)
            {
                return;
            }

            Log(message, LogLevel.Debug, category, null, sourceFile, sourceLine);
        }
    }
}
