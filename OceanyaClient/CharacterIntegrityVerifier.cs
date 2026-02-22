using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient
{
    public enum CharacterIntegrityFixActionType
    {
        None,
        SetBlankEmotesDefinition
    }

    public enum CharacterIntegrityViewActionType
    {
        None,
        OpenInExplorer,
        OpenInExplorerSelect,
        OpenPath,
        OpenPathAndCharIni
    }

    public sealed class CharacterIntegrityIssue
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string TestName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? EmoteId { get; set; }
        public CharacterIntegrityFixActionType FixActionType { get; set; }
        public int? FixValue { get; set; }
        public string FixLabel { get; set; } = string.Empty;
        public CharacterIntegrityViewActionType ViewActionType { get; set; }
        public string ViewPath { get; set; } = string.Empty;
        public string SecondaryViewPath { get; set; } = string.Empty;
        public int? LineNumberHint { get; set; }

        public bool CanAutoFix => !Passed && FixActionType != CharacterIntegrityFixActionType.None;
        public bool CanViewError => !Passed && ViewActionType != CharacterIntegrityViewActionType.None;
    }

    public sealed class CharacterIntegrityReport
    {
        public string CharacterDirectory { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public string CharIniPath { get; set; } = string.Empty;
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        public List<CharacterIntegrityIssue> Results { get; set; } = new List<CharacterIntegrityIssue>();

        public bool HasFailures => Results.Any(result => !result.Passed);
        public int FailureCount => Results.Count(result => !result.Passed);
    }

    public static class CharacterIntegrityVerifier
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static string GetReportFilePath(string directoryPath)
        {
            string safeDirectory = directoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeDirectory))
            {
                return string.Empty;
            }

            string normalizedDirectory = NormalizeDirectoryPathForCacheKey(safeDirectory);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDirectory))).ToLowerInvariant();
            string cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OceanyaClient",
                "cache",
                "integrity_verifier");
            return Path.Combine(cacheRoot, $"integrity_{hash}.json");
        }

        public static CharacterIntegrityReport RunAndPersist(CharacterFolder folder)
        {
            if (folder == null)
            {
                throw new ArgumentNullException(nameof(folder));
            }

            CharacterIntegrityReport report = Run(folder.DirectoryPath, folder.PathToConfigIni, folder.Name);
            PersistReport(report);
            return report;
        }

        public static CharacterIntegrityReport RunAndPersist(string directoryPath, string? charIniPath = null, string? characterName = null)
        {
            CharacterIntegrityReport report = Run(directoryPath, charIniPath, characterName);
            PersistReport(report);
            return report;
        }

        public static CharacterIntegrityReport Run(string directoryPath, string? charIniPath = null, string? characterName = null)
        {
            CharacterIntegrityReport report = new CharacterIntegrityReport
            {
                CharacterDirectory = directoryPath?.Trim() ?? string.Empty,
                CharacterName = characterName?.Trim() ?? string.Empty,
                CharIniPath = charIniPath?.Trim() ?? string.Empty,
                GeneratedAtUtc = DateTime.UtcNow
            };

            try
            {
                ExecuteVerifier(report);
            }
            catch (Exception ex)
            {
                report.Results.Add(CreateFailure(
                    "Verifier Runtime",
                    "The verifier encountered an unexpected runtime error.",
                    "Verifier failed unexpectedly: " + ex.Message));
            }

            return report;
        }

        public static bool TryLoadPersistedReport(string directoryPath, out CharacterIntegrityReport? report)
        {
            report = null;
            string reportPath = GetReportFilePath(directoryPath);
            if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(reportPath);
                CharacterIntegrityReport? loaded = JsonSerializer.Deserialize<CharacterIntegrityReport>(json, JsonOptions);
                if (loaded == null)
                {
                    return false;
                }

                NormalizeLoadedReport(loaded, directoryPath);
                report = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryApplyFix(CharacterIntegrityReport report, CharacterIntegrityIssue issue, out string resultMessage)
        {
            resultMessage = string.Empty;
            if (report == null)
            {
                resultMessage = "Integrity report was not provided.";
                return false;
            }

            if (issue == null)
            {
                resultMessage = "Integrity issue was not provided.";
                return false;
            }

            try
            {
                return issue.FixActionType switch
                {
                    CharacterIntegrityFixActionType.SetBlankEmotesDefinition =>
                        ApplyBlankEmotesFix(report.CharIniPath, issue.FixValue, out resultMessage),
                    _ => NotSupportedFix(out resultMessage)
                };
            }
            catch (Exception ex)
            {
                resultMessage = "Auto-fix failed: " + ex.Message;
                return false;
            }
        }

        public static CharacterIntegrityReport RerunSingleTest(CharacterIntegrityReport baseReport, CharacterIntegrityIssue issueToRerun)
        {
            if (baseReport == null)
            {
                throw new ArgumentNullException(nameof(baseReport));
            }

            if (issueToRerun == null)
            {
                throw new ArgumentNullException(nameof(issueToRerun));
            }

            CharacterIntegrityReport freshReport = Run(
                baseReport.CharacterDirectory,
                baseReport.CharIniPath,
                baseReport.CharacterName);

            List<CharacterIntegrityIssue> remaining = baseReport.Results
                .Where(issue => !IsSameLogicalTest(issue, issueToRerun))
                .ToList();

            IEnumerable<CharacterIntegrityIssue> refreshed = freshReport.Results
                .Where(issue => IsSameLogicalTest(issue, issueToRerun));

            remaining.AddRange(refreshed);
            baseReport.Results = remaining;
            baseReport.GeneratedAtUtc = DateTime.UtcNow;
            PersistReport(baseReport);
            return baseReport;
        }

        private static bool NotSupportedFix(out string message)
        {
            message = "No supported auto-fix is associated with this result.";
            return false;
        }

        private static bool IsSameLogicalTest(CharacterIntegrityIssue left, CharacterIntegrityIssue right)
        {
            if (!string.Equals(left.TestName, right.TestName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return left.EmoteId == right.EmoteId;
        }

        private static void ExecuteVerifier(CharacterIntegrityReport report)
        {
            if (string.IsNullOrWhiteSpace(report.CharacterDirectory) || !Directory.Exists(report.CharacterDirectory))
            {
                CharacterIntegrityIssue issue = CreateFailure(
                    "Character Folder Exists",
                    "Verify the character folder can be found on disk.",
                    "Character folder was not found on disk.");
                issue.ViewActionType = CharacterIntegrityViewActionType.OpenInExplorer;
                issue.ViewPath = ResolveCharactersRootFromCharacterDirectory(report.CharacterDirectory);
                report.Results.Add(issue);
                return;
            }

            report.Results.Add(CreatePass(
                "Character Folder Exists",
                "Verify the character folder can be found on disk.",
                "Character folder exists."));

            List<string> charIniFiles = EnumerateCharIniFiles(report.CharacterDirectory);
            if (charIniFiles.Count == 0)
            {
                CharacterIntegrityIssue issue = CreateFailure(
                    "char.ini Present",
                    "Verify at least one char.ini exists in the character folder.",
                    "No char.ini was found in this character folder.");
                issue.ViewActionType = CharacterIntegrityViewActionType.OpenInExplorer;
                issue.ViewPath = report.CharacterDirectory;
                report.Results.Add(issue);
                return;
            }

            report.Results.Add(CreatePass(
                "char.ini Present",
                "Verify at least one char.ini exists in the character folder.",
                "char.ini was found."));

            if (charIniFiles.Count > 1)
            {
                string duplicateList = string.Join(", ", charIniFiles.Select(path => Path.GetRelativePath(report.CharacterDirectory, path)).Take(5));
                CharacterIntegrityIssue issue = CreateFailure(
                    "Multiple char.ini Files",
                    "Verify only one char.ini exists under the character folder.",
                    "Multiple char.ini files were found: " + duplicateList);
                issue.ViewActionType = CharacterIntegrityViewActionType.OpenInExplorerSelect;
                issue.ViewPath = charIniFiles.Skip(1).FirstOrDefault() ?? charIniFiles[0];
                report.Results.Add(issue);
            }
            else
            {
                report.Results.Add(CreatePass(
                    "Multiple char.ini Files",
                    "Verify only one char.ini exists under the character folder.",
                    "Only one char.ini file was found."));
            }

            string selectedCharIniPath = SelectCharIniPath(report.CharIniPath, charIniFiles, report.CharacterDirectory);
            report.CharIniPath = selectedCharIniPath;

            CharacterFolder? parsedCharacter = TryParseCharacterConfig(report, selectedCharIniPath);
            int inferredEmoteCount = InferEmoteCount(selectedCharIniPath, parsedCharacter?.configINI);
            RunBlankEmotesDefinitionTest(report, selectedCharIniPath, inferredEmoteCount);

            if (parsedCharacter == null || parsedCharacter.configINI == null)
            {
                return;
            }

            RunAssetPresenceTests(report, parsedCharacter);
        }

        private static List<string> EnumerateCharIniFiles(string directoryPath)
        {
            try
            {
                return Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                    .Where(path => string.Equals(Path.GetFileName(path), "char.ini", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                CustomConsole.Warning($"Unable to enumerate char.ini files for '{directoryPath}'.", ex);
                return new List<string>();
            }
        }

        private static string SelectCharIniPath(string existingPath, IReadOnlyList<string> discoveredPaths, string characterDirectory)
        {
            if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
            {
                return existingPath;
            }

            string topLevelPath = Path.Combine(characterDirectory, "char.ini");
            if (File.Exists(topLevelPath))
            {
                return topLevelPath;
            }

            return discoveredPaths.First();
        }

        private static CharacterFolder? TryParseCharacterConfig(CharacterIntegrityReport report, string charIniPath)
        {
            try
            {
                CharacterFolder folder = CharacterFolder.Create(charIniPath);
                if (string.IsNullOrWhiteSpace(report.CharacterName))
                {
                    report.CharacterName = folder.Name;
                }

                report.Results.Add(CreatePass(
                    "char.ini Parsing",
                    "Verify char.ini can be parsed by the client parser.",
                    "char.ini parsed successfully."));
                return folder;
            }
            catch (Exception ex)
            {
                CharacterIntegrityIssue issue = CreateFailure(
                    "char.ini Parsing",
                    "Verify char.ini can be parsed by the client parser.",
                    "char.ini could not be parsed: " + ex.Message);
                issue.ViewActionType = CharacterIntegrityViewActionType.OpenPath;
                issue.ViewPath = charIniPath;
                report.Results.Add(issue);
                return null;
            }
        }

        private static void RunBlankEmotesDefinitionTest(CharacterIntegrityReport report, string charIniPath, int inferredEmoteCount)
        {
            try
            {
                string[] lines = File.ReadAllLines(charIniPath);
                bool hasBlankEmotes = false;

                foreach (string rawLine in lines)
                {
                    if (!TrySplitIniLine(rawLine, out string key, out string value))
                    {
                        continue;
                    }

                    if (!string.Equals(key, "emotes", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IsBlankValue(value))
                    {
                        hasBlankEmotes = true;
                        break;
                    }
                }

                if (!hasBlankEmotes)
                {
                    report.Results.Add(CreatePass(
                        "Blank emotes Definition",
                        "Verify char.ini does not include blank emotes= definitions.",
                        "No blank emotes= definition was found."));
                    return;
                }

                CharacterIntegrityIssue issue = CreateFailure(
                    "Blank emotes Definition",
                    "Verify char.ini does not include blank emotes= definitions.",
                    "The char.ini file within this folder has a blank emotes definition! (emotes=\"\")");
                issue.ViewActionType = CharacterIntegrityViewActionType.OpenPath;
                issue.ViewPath = charIniPath;
                issue.LineNumberHint = FindIniKeyLineNumber(charIniPath, "emotes");

                if (inferredEmoteCount > 0)
                {
                    issue.FixActionType = CharacterIntegrityFixActionType.SetBlankEmotesDefinition;
                    issue.FixValue = inferredEmoteCount;
                    issue.FixLabel = "Set emotes=" + inferredEmoteCount;
                }

                report.Results.Add(issue);
            }
            catch (Exception ex)
            {
                report.Results.Add(CreateFailure(
                    "Blank emotes Definition",
                    "Verify char.ini does not include blank emotes= definitions.",
                    "Unable to validate emotes= definition: " + ex.Message));
            }
        }

        private static int InferEmoteCount(string charIniPath, CharacterConfigINI? parsedConfig)
        {
            if (parsedConfig != null && parsedConfig.EmotionsCount > 0)
            {
                return parsedConfig.EmotionsCount;
            }

            int maxEmoteId = 0;
            string currentSection = string.Empty;
            try
            {
                foreach (string rawLine in File.ReadLines(charIniPath))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                    {
                        currentSection = line.Trim('[', ']').Trim();
                        continue;
                    }

                    if (!string.Equals(currentSection, "Emotions", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!TrySplitIniLine(line, out string key, out _))
                    {
                        continue;
                    }

                    if (int.TryParse(key, out int emoteId) && emoteId > maxEmoteId)
                    {
                        maxEmoteId = emoteId;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return maxEmoteId;
        }

        private static void RunAssetPresenceTests(CharacterIntegrityReport report, CharacterFolder parsedCharacter)
        {
            CharacterConfigINI config = parsedCharacter.configINI;
            string directoryPath = parsedCharacter.DirectoryPath ?? string.Empty;
            List<CharacterIntegrityIssue> missingAssetIssues = new List<CharacterIntegrityIssue>();

            foreach (KeyValuePair<int, Emote> pair in config.Emotions.OrderBy(pair => pair.Key))
            {
                int emoteId = pair.Key;
                Emote emote = pair.Value;

                string preAnimationName = emote.PreAnimation?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(preAnimationName) && preAnimationName != "-")
                {
                    string preAnimationPath = CharacterAssetPathResolver.ResolveCharacterAnimationPath(
                        directoryPath,
                        preAnimationName,
                        includePlaceholder: false);

                    if (string.IsNullOrWhiteSpace(preAnimationPath))
                    {
                        missingAssetIssues.Add(CreateFailure(
                            "Emote Assets Exist",
                            "Verify every defined pre-animation asset exists on disk.",
                            $"Preanim in emote {emoteId} ('{emote.Name}') is defined as '{preAnimationName}' but asset was not found on disk.",
                            emoteId));
                        CharacterIntegrityIssue issue = missingAssetIssues[missingAssetIssues.Count - 1];
                        issue.ViewActionType = CharacterIntegrityViewActionType.OpenPathAndCharIni;
                        issue.ViewPath = BuildMissingAssetHintPath(directoryPath, preAnimationName);
                        issue.SecondaryViewPath = report.CharIniPath;
                        issue.LineNumberHint = FindEmoteLineNumber(report.CharIniPath, emoteId);
                    }
                }

                string animationName = emote.Animation?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(animationName) || animationName == "-")
                {
                    missingAssetIssues.Add(CreateFailure(
                        "Emote Assets Exist",
                        "Verify every emote defines a final animation that exists on disk.",
                        $"Final animation for emote {emoteId} ('{emote.Name}') is blank or '-'.",
                        emoteId));
                    CharacterIntegrityIssue issue = missingAssetIssues[missingAssetIssues.Count - 1];
                    issue.ViewActionType = CharacterIntegrityViewActionType.OpenPathAndCharIni;
                    issue.ViewPath = directoryPath;
                    issue.SecondaryViewPath = report.CharIniPath;
                    issue.LineNumberHint = FindEmoteLineNumber(report.CharIniPath, emoteId);
                    continue;
                }

                string animationPath = CharacterAssetPathResolver.ResolveCharacterAnimationPath(
                    directoryPath,
                    animationName,
                    includePlaceholder: false);

                if (string.IsNullOrWhiteSpace(animationPath))
                {
                    missingAssetIssues.Add(CreateFailure(
                        "Emote Assets Exist",
                        "Verify every emote defines a final animation that exists on disk.",
                        $"Animation in emote {emoteId} ('{emote.Name}') is defined as '{animationName}' but asset was not found on disk.",
                        emoteId));
                    CharacterIntegrityIssue issue = missingAssetIssues[missingAssetIssues.Count - 1];
                    issue.ViewActionType = CharacterIntegrityViewActionType.OpenPathAndCharIni;
                    issue.ViewPath = BuildMissingAssetHintPath(directoryPath, animationName);
                    issue.SecondaryViewPath = report.CharIniPath;
                    issue.LineNumberHint = FindEmoteLineNumber(report.CharIniPath, emoteId);
                }
            }

            if (missingAssetIssues.Count == 0)
            {
                report.Results.Add(CreatePass(
                    "Emote Assets Exist",
                    "Verify all emote assets referenced by char.ini are present on disk.",
                    "All checked emote assets were found on disk."));
            }
            else
            {
                report.Results.AddRange(missingAssetIssues);
            }
        }

        private static CharacterIntegrityIssue CreatePass(string testName, string description, string message)
        {
            return new CharacterIntegrityIssue
            {
                TestName = testName,
                Description = description,
                Passed = true,
                Message = message
            };
        }

        private static CharacterIntegrityIssue CreateFailure(string testName, string description, string message, int? emoteId = null)
        {
            return new CharacterIntegrityIssue
            {
                TestName = testName,
                Description = description,
                Passed = false,
                Message = message,
                EmoteId = emoteId
            };
        }

        private static bool TrySplitIniLine(string rawLine, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return false;
            }

            int equalsIndex = rawLine.IndexOf('=');
            if (equalsIndex <= 0)
            {
                return false;
            }

            key = rawLine.Substring(0, equalsIndex).Trim();
            value = rawLine.Substring(equalsIndex + 1).Trim();
            return !string.IsNullOrWhiteSpace(key);
        }

        private static bool IsBlankValue(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length >= 2)
            {
                bool doubleQuoted = normalized.StartsWith("\"", StringComparison.Ordinal)
                    && normalized.EndsWith("\"", StringComparison.Ordinal);
                bool singleQuoted = normalized.StartsWith("'", StringComparison.Ordinal)
                    && normalized.EndsWith("'", StringComparison.Ordinal);

                if (doubleQuoted || singleQuoted)
                {
                    normalized = normalized.Substring(1, normalized.Length - 2).Trim();
                }
            }

            return string.IsNullOrWhiteSpace(normalized);
        }

        private static string ResolveCharactersRootFromCharacterDirectory(string characterDirectory)
        {
            string safePath = characterDirectory?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(safePath))
            {
                try
                {
                    string? parent = Path.GetDirectoryName(safePath);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        return parent;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            return CharacterFolder.CharacterFolders.FirstOrDefault() ?? safePath;
        }

        private static string BuildMissingAssetHintPath(string characterDirectory, string assetToken)
        {
            string token = assetToken?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                return characterDirectory ?? string.Empty;
            }

            string normalizedToken = token
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(characterDirectory ?? string.Empty, normalizedToken);
        }

        private static int? FindIniKeyLineNumber(string iniPath, string keyName)
        {
            if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
            {
                return null;
            }

            try
            {
                string[] lines = File.ReadAllLines(iniPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!TrySplitIniLine(lines[i], out string key, out _))
                    {
                        continue;
                    }

                    if (string.Equals(key, keyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return i + 1;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return null;
        }

        private static int? FindEmoteLineNumber(string iniPath, int emoteId)
        {
            if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
            {
                return null;
            }

            string section = string.Empty;
            try
            {
                string[] lines = File.ReadAllLines(iniPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                    {
                        section = line.Trim('[', ']').Trim();
                        continue;
                    }

                    if (!string.Equals(section, "Emotions", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!TrySplitIniLine(line, out string key, out _))
                    {
                        continue;
                    }

                    if (int.TryParse(key, out int lineEmoteId) && lineEmoteId == emoteId)
                    {
                        return i + 1;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return null;
        }

        private static bool ApplyBlankEmotesFix(string charIniPath, int? count, out string resultMessage)
        {
            resultMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(charIniPath) || !File.Exists(charIniPath))
            {
                resultMessage = "char.ini was not found.";
                return false;
            }

            int emoteCount = Math.Max(0, count ?? 0);
            if (emoteCount <= 0)
            {
                resultMessage = "Unable to infer a valid emote count for auto-fix.";
                return false;
            }

            string[] lines = File.ReadAllLines(charIniPath);
            bool changed = false;
            Regex pattern = new Regex(@"^(\s*)([^=]+?)(\s*)=(.*)$", RegexOptions.Compiled);

            for (int i = 0; i < lines.Length; i++)
            {
                string current = lines[i];
                Match match = pattern.Match(current);
                if (!match.Success)
                {
                    continue;
                }

                string key = match.Groups[2].Value.Trim();
                if (!string.Equals(key, "emotes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string currentValue = match.Groups[4].Value;
                if (!IsBlankValue(currentValue))
                {
                    continue;
                }

                lines[i] = match.Groups[1].Value + match.Groups[2].Value + "=" + emoteCount;
                changed = true;
            }

            if (!changed)
            {
                resultMessage = "No blank emotes= definition was found to fix.";
                return false;
            }

            File.WriteAllLines(charIniPath, lines);
            resultMessage = "char.ini was updated with emotes=" + emoteCount + ".";
            return true;
        }

        private static void PersistReport(CharacterIntegrityReport report)
        {
            try
            {
                string reportPath = GetReportFilePath(report.CharacterDirectory);
                if (string.IsNullOrWhiteSpace(reportPath))
                {
                    return;
                }

                string parentDirectory = Path.GetDirectoryName(reportPath) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(parentDirectory))
                {
                    return;
                }

                Directory.CreateDirectory(parentDirectory);
                string json = JsonSerializer.Serialize(report, JsonOptions);
                File.WriteAllText(reportPath, json);
            }
            catch (Exception ex)
            {
                CustomConsole.Warning($"Unable to persist integrity report for '{report.CharacterDirectory}'.", ex);
            }
        }

        private static void NormalizeLoadedReport(CharacterIntegrityReport report, string fallbackDirectory)
        {
            report.CharacterDirectory = string.IsNullOrWhiteSpace(report.CharacterDirectory)
                ? fallbackDirectory
                : report.CharacterDirectory.Trim();
            report.CharacterName = report.CharacterName?.Trim() ?? string.Empty;
            report.CharIniPath = report.CharIniPath?.Trim() ?? string.Empty;
            report.Results ??= new List<CharacterIntegrityIssue>();

            for (int i = 0; i < report.Results.Count; i++)
            {
                CharacterIntegrityIssue? result = report.Results[i];
                if (result == null)
                {
                    report.Results[i] = new CharacterIntegrityIssue
                    {
                        Passed = false,
                        TestName = "Unknown Test",
                        Description = "The result entry was invalid.",
                        Message = "Verifier output contained an invalid entry."
                    };
                    continue;
                }

                result.Id = string.IsNullOrWhiteSpace(result.Id) ? Guid.NewGuid().ToString("N") : result.Id.Trim();
                result.TestName = result.TestName?.Trim() ?? string.Empty;
                result.Description = result.Description?.Trim() ?? string.Empty;
                result.Message = result.Message?.Trim() ?? string.Empty;
                result.FixLabel = result.FixLabel?.Trim() ?? string.Empty;
                result.ViewPath = result.ViewPath?.Trim() ?? string.Empty;
                result.SecondaryViewPath = result.SecondaryViewPath?.Trim() ?? string.Empty;
            }
        }

        private static string NormalizeDirectoryPathForCacheKey(string directoryPath)
        {
            try
            {
                return Path.GetFullPath(directoryPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();
            }
            catch
            {
                return directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();
            }
        }
    }
}
