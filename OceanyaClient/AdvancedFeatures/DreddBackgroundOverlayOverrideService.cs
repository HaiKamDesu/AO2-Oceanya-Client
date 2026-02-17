using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OceanyaClient.AdvancedFeatures
{
    public static class DreddBackgroundOverlayOverrideService
    {
        public sealed class DreddOverlayChangePreview
        {
            public string DesignIniPath { get; set; } = string.Empty;
            public string PositionKey { get; set; } = string.Empty;
            public string OriginalValue { get; set; } = string.Empty;
            public string CurrentValue { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }

        private sealed class RecordState
        {
            public bool EntryExists { get; set; }
            public string EntryValue { get; set; } = string.Empty;
            public string Status { get; set; } = "Modified";
        }

        public static bool TryApplyOverlay(AOClient client, DreddOverlayEntry overlay, out string error)
        {
            error = string.Empty;

            if (client == null)
            {
                error = "No client is currently selected.";
                return false;
            }

            if (overlay == null || string.IsNullOrWhiteSpace(overlay.FilePath))
            {
                error = "No valid overlay was selected.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(client.curBG))
            {
                error = "Current background is unknown.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(client.curPos))
            {
                error = "Current position is unknown.";
                return false;
            }

            if (!TryResolveBackgroundDirectory(client.curBG, out string backgroundDirectory))
            {
                error = $"Could not resolve background path for '{client.curBG}'.";
                return false;
            }

            string designIniPath = Path.Combine(backgroundDirectory, "design.ini");
            string positionKey = client.curPos.Trim();

            try
            {
                ApplyOverlayToDesignIni(designIniPath, positionKey, overlay.FilePath, backgroundDirectory);
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to apply Dredd overlay override.", ex);
                error = ex.Message;
                return false;
            }
        }

        public static bool TryGetOverlayContext(
            AOClient client,
            out string designIniPath,
            out string positionKey,
            out string backgroundDirectory,
            out string error)
        {
            designIniPath = string.Empty;
            positionKey = string.Empty;
            backgroundDirectory = string.Empty;
            error = string.Empty;

            if (client == null)
            {
                error = "No client is currently selected.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(client.curBG))
            {
                error = "Current background is unknown.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(client.curPos))
            {
                error = "Current position is unknown.";
                return false;
            }

            if (!TryResolveBackgroundDirectory(client.curBG, out backgroundDirectory))
            {
                error = $"Could not resolve background path for '{client.curBG}'.";
                return false;
            }

            designIniPath = Path.Combine(backgroundDirectory, "design.ini");
            positionKey = client.curPos.Trim();
            return true;
        }

        public static bool TryGetCurrentOverlayValue(
            AOClient client,
            out bool hasEntry,
            out string overlayValue,
            out string designIniPath,
            out string positionKey,
            out string backgroundDirectory,
            out string error)
        {
            hasEntry = false;
            overlayValue = string.Empty;
            designIniPath = string.Empty;
            positionKey = string.Empty;
            backgroundDirectory = string.Empty;
            error = string.Empty;

            if (!TryGetOverlayContext(client, out designIniPath, out positionKey, out backgroundDirectory, out error))
            {
                return false;
            }

            if (!TryGetOverlayValueFromDesignIni(designIniPath, positionKey, out overlayValue))
            {
                hasEntry = false;
                overlayValue = string.Empty;
                return true;
            }

            hasEntry = true;
            return true;
        }

        public static bool TryGetOriginalOverlayValue(
            string designIniPath,
            string positionKey,
            bool currentHasEntry,
            string currentValue,
            out bool hasOriginalEntry,
            out string originalValue)
        {
            DreddOverlayMutationRecord? record = SaveFile.Data.DreddBackgroundOverlayOverride.MutationCache.FirstOrDefault(item =>
                string.Equals(item.DesignIniPath, designIniPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.PositionKey, positionKey, StringComparison.OrdinalIgnoreCase));

            if (record == null)
            {
                hasOriginalEntry = currentHasEntry;
                originalValue = currentValue;
                return true;
            }

            hasOriginalEntry = record.EntryExisted;
            originalValue = record.OriginalValue ?? string.Empty;
            return true;
        }

        public static bool OverlayReferencesMatch(
            string designIniPath,
            string backgroundDirectory,
            string leftReference,
            string rightReference)
        {
            string left = leftReference?.Trim() ?? string.Empty;
            string right = rightReference?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool leftResolved = TryResolveOverlayPath(left, designIniPath, backgroundDirectory, out string leftAbsolute);
            bool rightResolved = TryResolveOverlayPath(right, designIniPath, backgroundDirectory, out string rightAbsolute);
            if (leftResolved && rightResolved)
            {
                return string.Equals(leftAbsolute, rightAbsolute, StringComparison.OrdinalIgnoreCase);
            }

            string normalizedLeft = NormalizeOverlayPathForDesignIni(left, designIniPath, backgroundDirectory);
            string normalizedRight = NormalizeOverlayPathForDesignIni(right, designIniPath, backgroundDirectory);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryResolveOverlayPathToAbsolute(
            string overlayReference,
            string designIniPath,
            string backgroundDirectory,
            out string absolutePath)
        {
            return TryResolveOverlayPath(overlayReference, designIniPath, backgroundDirectory, out absolutePath);
        }

        public static bool TryClearOverlay(AOClient client, out string error)
        {
            error = string.Empty;

            if (client == null)
            {
                error = "No client is currently selected.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(client.curBG))
            {
                error = "Current background is unknown.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(client.curPos))
            {
                error = "Current position is unknown.";
                return false;
            }

            if (!TryResolveBackgroundDirectory(client.curBG, out string backgroundDirectory))
            {
                error = $"Could not resolve background path for '{client.curBG}'.";
                return false;
            }

            string designIniPath = Path.Combine(backgroundDirectory, "design.ini");
            string positionKey = client.curPos.Trim();

            try
            {
                RemoveOverlayEntryFromDesignIni(designIniPath, positionKey);
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to clear Dredd overlay override.", ex);
                error = ex.Message;
                return false;
            }
        }

        public static bool TryDiscardAllChanges(out string message)
        {
            message = string.Empty;
            DreddBackgroundOverlayOverrideConfig config = SaveFile.Data.DreddBackgroundOverlayOverride;
            PruneNoOpMutations(config);
            if (config.MutationCache.Count == 0)
            {
                message = "No modified backgrounds were found in cache.";
                return true;
            }

            List<string> errors = new List<string>();
            List<DreddOverlayMutationRecord> records = config.MutationCache.ToList();

            foreach (DreddOverlayMutationRecord record in records)
            {
                try
                {
                    RestoreMutation(record);
                }
                catch (Exception ex)
                {
                    errors.Add($"{record.DesignIniPath} ({record.PositionKey}): {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                message = string.Join(Environment.NewLine, errors);
                return false;
            }

            config.MutationCache.Clear();
            SaveFile.Save();
            message = "All Dredd overlay changes were discarded.";
            return true;
        }

        public static List<DreddOverlayChangePreview> GetCachedChangesPreview()
        {
            DreddBackgroundOverlayOverrideConfig config = SaveFile.Data.DreddBackgroundOverlayOverride;
            bool pruned = PruneNoOpMutations(config);

            List<DreddOverlayChangePreview> previews = new List<DreddOverlayChangePreview>();
            foreach (DreddOverlayMutationRecord record in config.MutationCache)
            {
                RecordState state = ReadRecordState(record);

                previews.Add(new DreddOverlayChangePreview
                {
                    DesignIniPath = record.DesignIniPath,
                    PositionKey = record.PositionKey,
                    OriginalValue = record.EntryExisted ? record.OriginalValue : "<missing>",
                    CurrentValue = state.EntryExists
                        ? (string.IsNullOrWhiteSpace(state.EntryValue) ? "<empty>" : state.EntryValue)
                        : "<missing>",
                    Status = state.Status
                });
            }

            if (pruned)
            {
                SaveFile.Save();
            }

            return previews;
        }

        public static bool TryDiscardSingleChange(string designIniPath, string positionKey, out string message)
        {
            message = string.Empty;
            DreddBackgroundOverlayOverrideConfig config = SaveFile.Data.DreddBackgroundOverlayOverride;
            DreddOverlayMutationRecord? record = config.MutationCache.FirstOrDefault(item =>
                string.Equals(item.DesignIniPath, designIniPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.PositionKey, positionKey, StringComparison.OrdinalIgnoreCase));

            if (record == null)
            {
                message = "The selected change is no longer pending.";
                return true;
            }

            try
            {
                RestoreMutation(record);
                config.MutationCache.Remove(record);
                SaveFile.Save();
                message = "Change discarded.";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static bool TryKeepSingleChange(string designIniPath, string positionKey, out string message)
        {
            message = string.Empty;
            DreddBackgroundOverlayOverrideConfig config = SaveFile.Data.DreddBackgroundOverlayOverride;
            DreddOverlayMutationRecord? record = config.MutationCache.FirstOrDefault(item =>
                string.Equals(item.DesignIniPath, designIniPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.PositionKey, positionKey, StringComparison.OrdinalIgnoreCase));

            if (record == null)
            {
                message = "The selected change is no longer pending.";
                return true;
            }

            config.MutationCache.Remove(record);
            SaveFile.Save();
            message = "Change kept as-is and removed from pending changes.";
            return true;
        }

        public static bool TryKeepAllChanges(out string message)
        {
            message = string.Empty;
            DreddBackgroundOverlayOverrideConfig config = SaveFile.Data.DreddBackgroundOverlayOverride;
            PruneNoOpMutations(config);
            if (config.MutationCache.Count == 0)
            {
                message = "No pending changes to apply.";
                return true;
            }

            config.MutationCache.Clear();
            SaveFile.Save();
            message = "All pending changes were applied and are now the new baseline.";
            return true;
        }

        private static void ApplyOverlayToDesignIni(
            string designIniPath,
            string positionKey,
            string overlayInputPath,
            string backgroundDirectory)
        {
            string fullDesignIniPath = Path.GetFullPath(designIniPath);
            bool fileExisted = File.Exists(fullDesignIniPath);
            List<string> lines = fileExisted ? File.ReadAllLines(fullDesignIniPath).ToList() : new List<string>();

            bool hadOverlaysSection = TryFindSection(lines, "Overlays", out int sectionHeaderIndex, out int sectionEndExclusive);
            string originalValue = string.Empty;
            bool hadPositionEntry = hadOverlaysSection && TryFindKeyInSection(lines, sectionHeaderIndex, sectionEndExclusive, positionKey, out _, out originalValue);

            CacheOriginalState(fullDesignIniPath, positionKey, fileExisted, hadOverlaysSection, hadPositionEntry, originalValue);

            string overlayDesignValue = ResolveOverlayValueForDesignIni(overlayInputPath, fullDesignIniPath, backgroundDirectory);
            SetKeyInSection(lines, "Overlays", positionKey, overlayDesignValue);

            Directory.CreateDirectory(Path.GetDirectoryName(fullDesignIniPath) ?? string.Empty);
            File.WriteAllLines(fullDesignIniPath, lines);
        }

        private static void RemoveOverlayEntryFromDesignIni(string designIniPath, string positionKey)
        {
            string fullDesignIniPath = Path.GetFullPath(designIniPath);
            bool fileExisted = File.Exists(fullDesignIniPath);
            List<string> lines = fileExisted ? File.ReadAllLines(fullDesignIniPath).ToList() : new List<string>();

            bool hadOverlaysSection = TryFindSection(lines, "Overlays", out int sectionHeaderIndex, out int sectionEndExclusive);
            string originalValue = string.Empty;
            bool hadPositionEntry = hadOverlaysSection && TryFindKeyInSection(lines, sectionHeaderIndex, sectionEndExclusive, positionKey, out _, out originalValue);

            CacheOriginalState(fullDesignIniPath, positionKey, fileExisted, hadOverlaysSection, hadPositionEntry, originalValue);

            if (!hadOverlaysSection)
            {
                return;
            }

            RemoveKeyInSection(lines, sectionHeaderIndex, sectionEndExclusive, positionKey);
            if (TryFindSection(lines, "Overlays", out int refreshedHeader, out int refreshedEnd)
                && !SectionContainsAnyKeys(lines, refreshedHeader, refreshedEnd))
            {
                RemoveSection(lines, refreshedHeader, refreshedEnd);
            }

            if (!fileExisted && IsEffectivelyEmpty(lines))
            {
                if (File.Exists(fullDesignIniPath))
                {
                    File.Delete(fullDesignIniPath);
                }

                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullDesignIniPath) ?? string.Empty);
            File.WriteAllLines(fullDesignIniPath, lines);
        }

        private static void RestoreMutation(DreddOverlayMutationRecord record)
        {
            string fullDesignIniPath = Path.GetFullPath(record.DesignIniPath);
            bool fileExistsNow = File.Exists(fullDesignIniPath);

            if (!fileExistsNow)
            {
                if (record.FileExisted)
                {
                    throw new FileNotFoundException("Expected design.ini file is missing.", fullDesignIniPath);
                }

                return;
            }

            List<string> lines = File.ReadAllLines(fullDesignIniPath).ToList();
            if (!TryFindSection(lines, "Overlays", out int sectionHeaderIndex, out int sectionEndExclusive))
            {
                if (record.EntryExisted)
                {
                    throw new InvalidOperationException("[Overlays] section is missing while restoring original value.");
                }

                if (!record.FileExisted)
                {
                    File.Delete(fullDesignIniPath);
                }

                return;
            }

            if (record.EntryExisted)
            {
                SetKeyInSection(lines, "Overlays", record.PositionKey, record.OriginalValue);
            }
            else
            {
                RemoveKeyInSection(lines, sectionHeaderIndex, sectionEndExclusive, record.PositionKey);
                if (!record.OverlaysSectionExisted)
                {
                    TryFindSection(lines, "Overlays", out int refreshedHeader, out int refreshedEnd);
                    if (refreshedHeader >= 0 && !SectionContainsAnyKeys(lines, refreshedHeader, refreshedEnd))
                    {
                        RemoveSection(lines, refreshedHeader, refreshedEnd);
                    }
                }
            }

            if (!record.FileExisted && IsEffectivelyEmpty(lines))
            {
                File.Delete(fullDesignIniPath);
                return;
            }

            File.WriteAllLines(fullDesignIniPath, lines);
        }

        private static bool IsEffectivelyEmpty(List<string> lines)
        {
            return lines.All(line => string.IsNullOrWhiteSpace(line));
        }

        private static void CacheOriginalState(
            string designIniPath,
            string positionKey,
            bool fileExisted,
            bool overlaysSectionExisted,
            bool entryExisted,
            string originalValue)
        {
            DreddBackgroundOverlayOverrideConfig config = SaveFile.Data.DreddBackgroundOverlayOverride;
            DreddOverlayMutationRecord? existingRecord = config.MutationCache.FirstOrDefault(record =>
                string.Equals(record.DesignIniPath, designIniPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.PositionKey, positionKey, StringComparison.OrdinalIgnoreCase));

            if (existingRecord != null)
            {
                return;
            }

            config.MutationCache.Add(new DreddOverlayMutationRecord
            {
                DesignIniPath = designIniPath,
                PositionKey = positionKey,
                FileExisted = fileExisted,
                OverlaysSectionExisted = overlaysSectionExisted,
                EntryExisted = entryExisted,
                OriginalValue = originalValue
            });

            SaveFile.Save();
        }

        private static bool PruneNoOpMutations(DreddBackgroundOverlayOverrideConfig config)
        {
            bool changed = false;
            List<DreddOverlayMutationRecord> copy = config.MutationCache.ToList();
            foreach (DreddOverlayMutationRecord record in copy)
            {
                RecordState state = ReadRecordState(record);
                bool isNoOp = record.EntryExisted
                    ? (state.EntryExists && string.Equals(state.EntryValue, record.OriginalValue, StringComparison.Ordinal))
                    : !state.EntryExists;

                if (!isNoOp)
                {
                    continue;
                }

                config.MutationCache.Remove(record);
                changed = true;
            }

            return changed;
        }

        private static RecordState ReadRecordState(DreddOverlayMutationRecord record)
        {
            RecordState state = new RecordState();
            try
            {
                if (!File.Exists(record.DesignIniPath))
                {
                    state.Status = "Missing design.ini";
                    return state;
                }

                List<string> lines = File.ReadAllLines(record.DesignIniPath).ToList();
                if (!TryFindSection(lines, "Overlays", out int sectionHeaderIndex, out int sectionEndExclusive))
                {
                    state.Status = "Missing [Overlays]";
                    return state;
                }

                if (TryFindKeyInSection(lines, sectionHeaderIndex, sectionEndExclusive, record.PositionKey, out _, out string value))
                {
                    state.EntryExists = true;
                    state.EntryValue = value;
                    state.Status = "Modified";
                    return state;
                }

                state.Status = "Entry removed";
                return state;
            }
            catch (Exception ex)
            {
                state.Status = $"Read error: {ex.Message}";
                return state;
            }
        }

        private static string GetRelativeOverlayPath(string designIniPath, string overlayAbsolutePath)
        {
            string? designDirectory = Path.GetDirectoryName(designIniPath);
            if (string.IsNullOrWhiteSpace(designDirectory))
            {
                return overlayAbsolutePath.Replace('\\', '/');
            }

            string relativePath = Path.GetRelativePath(designDirectory, overlayAbsolutePath);
            return relativePath.Replace('\\', '/');
        }

        public static string ResolveOverlayValueForDesignIni(string overlayInputPath, string designIniPath, string backgroundDirectory)
        {
            string raw = overlayInputPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("Selected overlay path is empty.");
            }

            if (TryResolveOverlayPath(raw, designIniPath, backgroundDirectory, out string resolvedAbsolutePath))
            {
                return GetRelativeOverlayPath(designIniPath, resolvedAbsolutePath);
            }

            // Preserve non-resolvable virtual paths (AO-style), but normalize slashes and dot segments
            // against the current background folder so values like custom/../overlayTestBG stay valid.
            return NormalizeOverlayPathForDesignIni(raw, designIniPath, backgroundDirectory);
        }

        private static bool TryResolveOverlayPath(
            string overlayInputPath,
            string designIniPath,
            string backgroundDirectory,
            out string resolvedAbsolutePath)
        {
            string raw = overlayInputPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                resolvedAbsolutePath = string.Empty;
                return false;
            }

            string normalizedRaw = raw.Replace('/', Path.DirectorySeparatorChar);
            List<string> candidates = new List<string>();
            if (Path.IsPathRooted(normalizedRaw))
            {
                candidates.Add(normalizedRaw);
            }
            else
            {
                string? designDir = Path.GetDirectoryName(designIniPath);
                if (!string.IsNullOrWhiteSpace(designDir))
                {
                    candidates.Add(Path.Combine(designDir, normalizedRaw));
                }

                if (!string.IsNullOrWhiteSpace(backgroundDirectory))
                {
                    candidates.Add(Path.Combine(backgroundDirectory, normalizedRaw));
                }

                foreach (string baseFolder in Globals.BaseFolders)
                {
                    candidates.Add(Path.Combine(baseFolder, normalizedRaw));
                }
            }

            foreach (string candidate in candidates)
            {
                string fullCandidate;
                try
                {
                    fullCandidate = Path.GetFullPath(candidate);
                }
                catch
                {
                    continue;
                }

                if (TryResolveWithOptionalExtension(fullCandidate, out string resolved))
                {
                    resolvedAbsolutePath = resolved;
                    return true;
                }
            }

            resolvedAbsolutePath = string.Empty;
            return false;
        }

        private static string NormalizeOverlayPathForDesignIni(string overlayInputPath, string designIniPath, string backgroundDirectory)
        {
            string raw = overlayInputPath.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(raw))
            {
                return raw;
            }

            string baseDirectory = backgroundDirectory;
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = Path.GetDirectoryName(designIniPath) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return raw;
            }

            try
            {
                string combined = Path.Combine(baseDirectory, raw.Replace('/', Path.DirectorySeparatorChar));
                string full = Path.GetFullPath(combined);
                string relative = Path.GetRelativePath(baseDirectory, full);
                return relative.Replace('\\', '/');
            }
            catch
            {
                return raw;
            }
        }

        private static bool TryResolveWithOptionalExtension(string candidatePath, out string resolvedPath)
        {
            if (File.Exists(candidatePath))
            {
                resolvedPath = candidatePath;
                return true;
            }

            if (Directory.Exists(candidatePath))
            {
                resolvedPath = candidatePath;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Path.GetExtension(candidatePath)))
            {
                resolvedPath = string.Empty;
                return false;
            }

            foreach (string extension in Globals.AllowedImageExtensions)
            {
                string withExtension = $"{candidatePath}.{extension}";
                if (File.Exists(withExtension))
                {
                    resolvedPath = withExtension;
                    return true;
                }
            }

            resolvedPath = string.Empty;
            return false;
        }

        private static bool TryResolveBackgroundDirectory(string currentBackgroundValue, out string backgroundDirectory)
        {
            backgroundDirectory = string.Empty;
            string raw = currentBackgroundValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            // Keep compatibility with previous cache-based lookup when possible.
            Background? cachedBackground = Background.FromBGPath(raw);
            if (cachedBackground != null && !string.IsNullOrWhiteSpace(cachedBackground.PathToFile))
            {
                backgroundDirectory = cachedBackground.PathToFile;
                return true;
            }

            string normalizedRaw = raw.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string backgroundRelative = normalizedRaw;
            string backgroundPrefix = $"background{Path.DirectorySeparatorChar}";
            if (backgroundRelative.StartsWith(backgroundPrefix, StringComparison.OrdinalIgnoreCase))
            {
                backgroundRelative = backgroundRelative.Substring(backgroundPrefix.Length);
            }

            List<string> candidates = new List<string>();
            if (Path.IsPathRooted(backgroundRelative))
            {
                candidates.Add(backgroundRelative);
            }
            else
            {
                foreach (string baseFolder in Globals.BaseFolders)
                {
                    candidates.Add(Path.Combine(baseFolder, "background", backgroundRelative));
                }
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    string fullCandidate = Path.GetFullPath(candidate);
                    if (Directory.Exists(fullCandidate))
                    {
                        backgroundDirectory = fullCandidate;
                        return true;
                    }
                }
                catch
                {
                    // Ignore malformed candidate and continue.
                }
            }

            return false;
        }

        private static bool TryGetOverlayValueFromDesignIni(string designIniPath, string positionKey, out string overlayValue)
        {
            overlayValue = string.Empty;
            if (!File.Exists(designIniPath))
            {
                return false;
            }

            List<string> lines = File.ReadAllLines(designIniPath).ToList();
            if (!TryFindSection(lines, "Overlays", out int sectionHeaderIndex, out int sectionEndExclusive))
            {
                return false;
            }

            if (!TryFindKeyInSection(lines, sectionHeaderIndex, sectionEndExclusive, positionKey, out _, out string value))
            {
                return false;
            }

            overlayValue = value;
            return true;
        }

        private static void SetKeyInSection(List<string> lines, string sectionName, string key, string value)
        {
            if (!TryFindSection(lines, sectionName, out int sectionHeaderIndex, out int sectionEndExclusive))
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add($"[{sectionName}]");
                lines.Add($"{key}={value}");
                return;
            }

            if (TryFindKeyInSection(lines, sectionHeaderIndex, sectionEndExclusive, key, out int keyLineIndex, out _))
            {
                lines[keyLineIndex] = $"{key}={value}";
                return;
            }

            int insertIndex = sectionEndExclusive;
            lines.Insert(insertIndex, $"{key}={value}");
        }

        private static void RemoveKeyInSection(List<string> lines, int sectionHeaderIndex, int sectionEndExclusive, string key)
        {
            if (!TryFindKeyInSection(lines, sectionHeaderIndex, sectionEndExclusive, key, out int keyLineIndex, out _))
            {
                return;
            }

            lines.RemoveAt(keyLineIndex);
        }

        private static bool SectionContainsAnyKeys(List<string> lines, int sectionHeaderIndex, int sectionEndExclusive)
        {
            for (int i = sectionHeaderIndex + 1; i < sectionEndExclusive; i++)
            {
                if (TryParseKeyValue(lines[i], out _, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveSection(List<string> lines, int sectionHeaderIndex, int sectionEndExclusive)
        {
            int removeStart = sectionHeaderIndex;
            int removeCount = sectionEndExclusive - sectionHeaderIndex;

            while (removeStart > 0 && string.IsNullOrWhiteSpace(lines[removeStart - 1]))
            {
                removeStart--;
                removeCount++;
            }

            lines.RemoveRange(removeStart, removeCount);
        }

        private static bool TryFindSection(List<string> lines, string sectionName, out int sectionHeaderIndex, out int sectionEndExclusive)
        {
            sectionHeaderIndex = -1;
            sectionEndExclusive = lines.Count;

            string expectedHeader = $"[{sectionName}]";
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (!string.Equals(line, expectedHeader, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sectionHeaderIndex = i;
                for (int j = i + 1; j < lines.Count; j++)
                {
                    string nextLine = lines[j].Trim();
                    if (nextLine.StartsWith("[", StringComparison.Ordinal) && nextLine.EndsWith("]", StringComparison.Ordinal))
                    {
                        sectionEndExclusive = j;
                        break;
                    }
                }

                return true;
            }

            return false;
        }

        private static bool TryFindKeyInSection(
            List<string> lines,
            int sectionHeaderIndex,
            int sectionEndExclusive,
            string key,
            out int keyLineIndex,
            out string value)
        {
            keyLineIndex = -1;
            value = string.Empty;

            for (int i = sectionHeaderIndex + 1; i < sectionEndExclusive; i++)
            {
                if (!TryParseKeyValue(lines[i], out string parsedKey, out string parsedValue))
                {
                    continue;
                }

                if (!string.Equals(parsedKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                keyLineIndex = i;
                value = parsedValue;
                return true;
            }

            return false;
        }

        private static bool TryParseKeyValue(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string trimmed = line.Trim();
            if (trimmed.StartsWith(";", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                return false;
            }

            int separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                return false;
            }

            key = trimmed.Substring(0, separator).Trim();
            value = trimmed.Substring(separator + 1).Trim();
            return !string.IsNullOrWhiteSpace(key);
        }
    }
}
