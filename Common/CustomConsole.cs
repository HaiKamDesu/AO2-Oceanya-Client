using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// Collection of logged lines
        /// </summary>
        public static List<string> lines = new List<string>();
        
        /// <summary>
        /// Event that fires when a new line is written
        /// </summary>
        public static Action<string>? OnWriteLine;

        /// <summary>
        /// Base method for all logging - writes a message to the console with timestamp
        /// </summary>
        public static void WriteLine(string message)
        {
            string timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(timestampedMessage);
            System.Diagnostics.Debug.WriteLine(timestampedMessage);

            lines.Add(timestampedMessage);
            OnWriteLine?.Invoke(timestampedMessage);
        }

        /// <summary>
        /// Writes a log message with the specified log level and optional exception details
        /// </summary>
        public static void Log(string message, LogLevel level = LogLevel.Info, Exception? exception = null, 
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

            WriteLine(formattedMessage);

            // Log exception details if present
            if (exception != null)
            {
                // Get file name and line number from stack trace if available
                var stackTrace = new StackTrace(exception, true);
                var frame = stackTrace.GetFrame(0);
                var exLineNumber = frame?.GetFileLineNumber() ?? 0;
                var exFileName = Path.GetFileName(frame?.GetFileName() ?? "unknown");

                WriteLine($"   Exception: {exception.GetType().Name}");
                WriteLine($"   Message: {exception.Message}");
                WriteLine($"   Location: {exFileName}:{exLineNumber}");
                
                // Get the first line of the stack trace for brevity
                var firstStackLine = exception.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(firstStackLine))
                {
                    WriteLine($"   Stack: {firstStackLine}");
                }

                // Log inner exception if present
                if (exception.InnerException != null)
                {
                    WriteLine($"   Inner Exception: {exception.InnerException.Message}");
                }
            }
        }

        /// <summary>
        /// Logs an informational message
        /// </summary>
        public static void Info(string message, 
            [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0) 
            => Log(message, LogLevel.Info, null, sourceFile, sourceLine);

        /// <summary>
        /// Logs a warning message with optional exception details
        /// </summary>
        public static void Warning(string message, Exception? ex = null, 
            [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0) 
            => Log(message, LogLevel.Warning, ex, sourceFile, sourceLine);

        /// <summary>
        /// Logs an error message with optional exception details
        /// </summary>
        public static void Error(string message, Exception? ex = null, 
            [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0) 
            => Log(message, LogLevel.Error, ex, sourceFile, sourceLine);

        /// <summary>
        /// Logs a debug message (only in debug builds)
        /// </summary>
        public static void Debug(string message, 
            [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
        {
            #if DEBUG
            Log(message, LogLevel.Debug, null, sourceFile, sourceLine);
            #endif
        }
    }
}
