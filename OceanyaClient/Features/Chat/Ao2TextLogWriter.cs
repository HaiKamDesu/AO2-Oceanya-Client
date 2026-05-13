using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient.Features.Chat
{
    /// <summary>
    /// Writes AO2-compatible text logs under the selected AO installation's logs folder.
    /// </summary>
    internal sealed class Ao2TextLogWriter
    {
        private static readonly Regex InvalidServerFolderChars = new Regex("[\\\\/:*?\"<>|']", RegexOptions.Compiled);
        private readonly object syncRoot = new object();
        private string logFilePath = string.Empty;

        public void ResetSession()
        {
            lock (syncRoot)
            {
                logFilePath = string.Empty;
            }
        }

        public void RefreshSession()
        {
            Dictionary<string, string> configValues = Ao2ConfigIniSettings.Load();
            if (!Ao2ConfigIniSettings.GetBool(configValues, "demo_logging_enabled", true))
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
                if (!string.IsNullOrWhiteSpace(logFilePath))
                {
                    return;
                }
            }

            string serverName = SaveFile.Data.SelectedServerName?.Trim() ?? string.Empty;
            string serverAddress = Globals.GetSelectedServerEndpoint()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverName))
            {
                serverName = string.IsNullOrWhiteSpace(serverAddress) ? "Direct Connect" : serverAddress;
            }

            string sanitizedServerName = SanitizeServerFolderName(serverName);
            string fileName = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ss 'UTC'.'log'", CultureInfo.InvariantCulture);
            string path = Path.Combine(baseDirectory, "logs", sanitizedServerName, fileName);

            lock (syncRoot)
            {
                if (string.Equals(logFilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                logFilePath = path;
                WriteLineUnlocked(
                    "Joined server "
                    + sanitizedServerName
                    + " hosted on address "
                    + serverAddress
                    + " on "
                    + FormatQtUtcTextDate(DateTime.UtcNow));
            }
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
            AppendLine(MaybeUnknown(showName) + ": " + MaybeUnknown(message));
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
            Dictionary<string, string> configValues = Ao2ConfigIniSettings.Load();
            if (!Ao2ConfigIniSettings.GetBool(configValues, "automatic_logging_enabled", true))
            {
                return;
            }

            lock (syncRoot)
            {
                if (string.IsNullOrWhiteSpace(logFilePath))
                {
                    RefreshSession();
                }

                if (string.IsNullOrWhiteSpace(logFilePath))
                {
                    return;
                }

                WriteLineUnlocked(text);
            }
        }

        private void WriteLineUnlocked(string text)
        {
            string? directory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(logFilePath))
            {
                File.AppendAllText(logFilePath, "\r\n" + text, new UTF8Encoding(false));
            }
            else
            {
                File.WriteAllText(logFilePath, text, new UTF8Encoding(false));
            }
        }

        private static string ResolveAoBaseDirectory()
        {
            string configPath = Ao2ConfigIniSettings.ConfigPath;
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                string? directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
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
    }
}
