using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OceanyaClient
{
    /// <summary>
    /// Represents a single AO2 sound list entry with separate transmitted and displayed values.
    /// </summary>
    public sealed class AO2SoundListEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AO2SoundListEntry"/> class.
        /// </summary>
        public AO2SoundListEntry(string value, string displayText, string sourceLine)
        {
            Value = value ?? string.Empty;
            DisplayText = displayText ?? string.Empty;
            SourceLine = sourceLine ?? string.Empty;
        }

        /// <summary>
        /// Gets the token AO2 sends in the packet.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Gets the label AO2 shows in the dropdown.
        /// </summary>
        public string DisplayText { get; }

        /// <summary>
        /// Gets the original line as read from disk.
        /// </summary>
        public string SourceLine { get; }
    }

    /// <summary>
    /// Provides AO2-compatible sound list parsing and lookup helpers.
    /// </summary>
    public static class AO2SoundList
    {
        /// <summary>
        /// Parses one AO2 sound list line into its transmitted token and visible label.
        /// </summary>
        public static AO2SoundListEntry ParseLine(string line)
        {
            string sourceLine = line ?? string.Empty;
            string[] unpacked = sourceLine.Split('=');
            string value = unpacked.Length > 0 ? unpacked[0].Trim() : string.Empty;
            string displayText = value;
            if (unpacked.Length > 1)
            {
                displayText = unpacked[1].Trim();
            }

            return new AO2SoundListEntry(value, displayText, sourceLine);
        }

        /// <summary>
        /// Loads the AO2 sound list entries for a character, including AO2 fallback files.
        /// </summary>
        public static IReadOnlyList<AO2SoundListEntry> LoadEntries(string characterDirectoryPath)
        {
            return LoadEntries(characterDirectoryPath, Globals.BaseFolders);
        }

        /// <summary>
        /// Loads the AO2 sound list entries for a character, including AO2 fallback files.
        /// </summary>
        public static IReadOnlyList<AO2SoundListEntry> LoadEntries(
            string characterDirectoryPath,
            IEnumerable<string>? baseFolders)
        {
            List<AO2SoundListEntry> entries = new List<AO2SoundListEntry>();

            string characterSoundListPath = ResolveCharacterSoundListPath(characterDirectoryPath);
            if (!string.IsNullOrWhiteSpace(characterSoundListPath))
            {
                AppendEntries(entries, characterSoundListPath);
            }

            string baseSoundListPath = ResolveBaseSoundListPath(baseFolders);
            if (!string.IsNullOrWhiteSpace(baseSoundListPath))
            {
                AppendEntries(entries, baseSoundListPath);
            }

            return entries;
        }

        private static void AppendEntries(List<AO2SoundListEntry> entries, string filePath)
        {
            if (entries == null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            foreach (string line in File.ReadLines(filePath))
            {
                entries.Add(ParseLine(line));
            }
        }

        private static string ResolveCharacterSoundListPath(string characterDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(characterDirectoryPath) || !Directory.Exists(characterDirectoryPath))
            {
                return string.Empty;
            }

            string soundListPath = Path.Combine(characterDirectoryPath, "soundlist.ini");
            if (File.Exists(soundListPath))
            {
                return soundListPath;
            }

            string legacySoundListPath = Path.Combine(characterDirectoryPath, "sounds.ini");
            return File.Exists(legacySoundListPath) ? legacySoundListPath : string.Empty;
        }

        private static string ResolveBaseSoundListPath(IEnumerable<string>? baseFolders)
        {
            IEnumerable<string> folders = baseFolders ?? Enumerable.Empty<string>();
            foreach (string baseFolder in folders)
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string candidate = Path.Combine(baseFolder, "soundlist.ini");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }
    }
}
