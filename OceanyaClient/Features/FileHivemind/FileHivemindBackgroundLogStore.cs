using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OceanyaClient.Features.FileHivemind
{
    public sealed class FileHivemindBackgroundLogEntry
    {
        public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Level { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public string ConnectionName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class FileHivemindBackgroundLogReadResult
    {
        public long NextPosition { get; set; }
        public List<FileHivemindBackgroundLogEntry> Entries { get; set; } = new List<FileHivemindBackgroundLogEntry>();
    }

    public sealed class FileHivemindBackgroundLogStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        private readonly string filePath;

        public FileHivemindBackgroundLogStore(string? filePath = null)
        {
            this.filePath = string.IsNullOrWhiteSpace(filePath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OceanyaClient",
                    "file_hivemind",
                    "background-agent.log")
                : filePath;
        }

        public void Append(FileHivemindBackgroundLogEntry entry)
        {
            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            Directory.CreateDirectory(directory);

            FileHivemindBackgroundLogEntry normalized = Normalize(entry);
            string line = JsonSerializer.Serialize(normalized, JsonOptions) + Environment.NewLine;

            using FileStream stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using StreamWriter writer = new StreamWriter(stream);
            writer.Write(line);
        }

        public FileHivemindBackgroundLogReadResult ReadFrom(long position)
        {
            if (!File.Exists(filePath))
            {
                return new FileHivemindBackgroundLogReadResult
                {
                    NextPosition = 0
                };
            }

            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long safePosition = Math.Clamp(position, 0, stream.Length);
            stream.Seek(safePosition, SeekOrigin.Begin);
            using StreamReader reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            long nextPosition = stream.Length;

            return new FileHivemindBackgroundLogReadResult
            {
                NextPosition = nextPosition,
                Entries = ParseLines(text)
            };
        }

        public IReadOnlyList<FileHivemindBackgroundLogEntry> ReadRecent(int maxEntries)
        {
            if (!File.Exists(filePath))
            {
                return Array.Empty<FileHivemindBackgroundLogEntry>();
            }

            string text = File.ReadAllText(filePath);
            return ParseLines(text)
                .TakeLast(Math.Max(1, maxEntries))
                .ToList();
        }

        private static List<FileHivemindBackgroundLogEntry> ParseLines(string? text)
        {
            List<FileHivemindBackgroundLogEntry> entries = new List<FileHivemindBackgroundLogEntry>();
            foreach (string line in (text ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    FileHivemindBackgroundLogEntry? parsed =
                        JsonSerializer.Deserialize<FileHivemindBackgroundLogEntry>(line, JsonOptions);
                    if (parsed != null)
                    {
                        entries.Add(Normalize(parsed));
                    }
                }
                catch
                {
                    // Ignore malformed log lines so the UI can still read the rest.
                }
            }

            return entries;
        }

        private static FileHivemindBackgroundLogEntry Normalize(FileHivemindBackgroundLogEntry? entry)
        {
            FileHivemindBackgroundLogEntry normalized = entry ?? new FileHivemindBackgroundLogEntry();
            normalized.Level = normalized.Level?.Trim() ?? string.Empty;
            normalized.ConnectionId = normalized.ConnectionId?.Trim() ?? string.Empty;
            normalized.ConnectionName = normalized.ConnectionName?.Trim() ?? string.Empty;
            normalized.Message = normalized.Message?.Trim() ?? string.Empty;
            return normalized;
        }
    }
}
