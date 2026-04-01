using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.GoogleDriveSync;

namespace OceanyaClient
{
    /// <summary>
    /// Provides workflows for rebuilding client-side asset indexes.
    /// </summary>
    public static class ClientAssetRefreshService
    {
        private const int RefreshMarkerSchemaVersion = 1;

        /// <summary>
        /// Refreshes all supported client-side asset caches while showing progress in <see cref="WaitForm"/>.
        /// </summary>
        public static async Task RefreshCharactersAndBackgroundsAsync(Window owner)
        {
            await WaitForm.ShowFormAsync("Refreshing character and background info...", owner);

            try
            {
                RefreshAllAssets(subtitle => WaitForm.SetSubtitle(subtitle));
            }
            finally
            {
                await WaitForm.CloseFormAsync();
            }
        }

        /// <summary>
        /// Refreshes only the assets affected by the supplied local sync changes.
        /// </summary>
        public static void RefreshChangedAssets(
            string configIniPath,
            string localRootPath,
            GoogleDriveSyncLocalChangeSet localChanges,
            Action<string>? progress = null)
        {
            if (localChanges == null || !localChanges.HasAnyChanges)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(configIniPath))
            {
                throw new InvalidOperationException("A valid config.ini path is required before refreshing assets.");
            }

            if (string.IsNullOrWhiteSpace(localRootPath))
            {
                throw new InvalidOperationException("A valid local sync folder is required before refreshing assets.");
            }

            Globals.UpdateConfigINI(configIniPath);

            TargetedAssetRefreshPlan plan = BuildTargetedPlan(localChanges);
            bool performedWork = false;

            if (plan.RequiresFullCharacterRefresh)
            {
                RefreshAllCharacters(progress);
                performedWork = true;
            }
            else
            {
                foreach (string characterName in plan.CharacterNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    RefreshCharacter(characterName, progress);
                    performedWork = true;
                }
            }

            if (plan.RequiresFullBackgroundRefresh)
            {
                RefreshAllBackgrounds(progress);
                performedWork = true;
            }
            else
            {
                foreach (string backgroundName in plan.BackgroundNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    RefreshBackground(backgroundName, progress);
                    performedWork = true;
                }
            }

            if (plan.RefreshBlips)
            {
                progress?.Invoke("Indexing blip files...");
                _ = BlipCatalog.Refresh();
                performedWork = true;
            }

            if (plan.RefreshChats)
            {
                progress?.Invoke("Indexing chat profiles...");
                _ = ChatCatalog.Refresh();
                performedWork = true;
            }

            if (plan.RefreshEffects)
            {
                progress?.Invoke("Indexing effects folders...");
                _ = EffectsFolderCatalog.Refresh();
                performedWork = true;
            }

            if (performedWork)
            {
                PersistRefreshMarker();
            }
        }

        /// <summary>
        /// Returns true when character/background refresh should be forced for the current app/config state.
        /// </summary>
        public static bool RequiresRefreshForCurrentEnvironment()
        {
            return !string.IsNullOrWhiteSpace(GetRefreshRequirementReasonForCurrentEnvironment());
        }

        /// <summary>
        /// Returns a human-readable reason when a full refresh is required for the current app/config state.
        /// </summary>
        public static string GetRefreshRequirementReasonForCurrentEnvironment()
        {
            try
            {
                string markerPath = GetRefreshMarkerPath();
                if (!File.Exists(markerPath))
                {
                    return "No prior asset refresh marker was found for this app/config combination.";
                }

                string json = File.ReadAllText(markerPath);
                AssetRefreshMarker? marker = JsonSerializer.Deserialize<AssetRefreshMarker>(json);
                if (marker == null)
                {
                    return "The saved asset refresh marker could not be read.";
                }

                return EvaluateRefreshRequirementReason(
                    marker,
                    GetAppVersion(),
                    Globals.PathToConfigINI ?? string.Empty,
                    Globals.BaseFolders ?? new List<string>());
            }
            catch
            {
                return "Oceanya could not verify the previous asset refresh marker.";
            }
        }

        internal static string EvaluateRefreshRequirementReason(
            AssetRefreshMarker marker,
            string currentAppVersion,
            string currentConfigIniPath,
            IReadOnlyList<string> currentResolvedBaseFolders)
        {
            if (marker == null)
            {
                return "The saved asset refresh marker could not be read.";
            }

            if (marker.SchemaVersion != RefreshMarkerSchemaVersion)
            {
                return "The saved asset refresh marker uses an older schema.";
            }

            if (!string.Equals(
                    NormalizePathForComparison(marker.ConfigIniPath),
                    NormalizePathForComparison(currentConfigIniPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return "The configured AO config.ini path changed since the last full asset refresh.";
            }

            if (!string.Equals(marker.AppVersion, currentAppVersion, StringComparison.OrdinalIgnoreCase))
            {
                return "The Oceanya version changed since the last full asset refresh.";
            }

            List<string> currentConfiguredBaseFolders = BuildConfiguredBaseFolderSignature(currentConfigIniPath);
            if (IsEquivalentFolderSequence(marker.BaseFolders, currentConfiguredBaseFolders))
            {
                return string.Empty;
            }

            // Legacy compatibility: older markers stored the currently resolved directories.
            if (IsEquivalentFolderSequence(marker.BaseFolders, currentResolvedBaseFolders))
            {
                return string.Empty;
            }

            // Another legacy compatibility path: stale generated folders that no longer exist
            // should not force refresh forever after the mount configuration has otherwise stabilized.
            List<string> existingMarkerBaseFolders = (marker.BaseFolders ?? new List<string>())
                .Where(path =>
                {
                    try
                    {
                        return Directory.Exists(path);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();
            if (existingMarkerBaseFolders.Count > 0
                && IsEquivalentFolderSequence(existingMarkerBaseFolders, currentResolvedBaseFolders))
            {
                return string.Empty;
            }

            return "The AO mount/base-folder configuration changed since the last full asset refresh.";
        }

        internal static TargetedAssetRefreshPlan BuildTargetedPlan(GoogleDriveSyncLocalChangeSet localChanges)
        {
            TargetedAssetRefreshPlan plan = new TargetedAssetRefreshPlan();
            foreach (string rawPath in localChanges.GetAllAffectedPaths())
            {
                string normalizedPath = NormalizeRelativePath(rawPath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                string[] segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                {
                    continue;
                }

                string first = segments[0];
                if (string.Equals(first, "characters", StringComparison.OrdinalIgnoreCase))
                {
                    if (segments.Length < 2)
                    {
                        plan.RequiresFullCharacterRefresh = true;
                    }
                    else
                    {
                        plan.CharacterNames.Add(segments[1]);
                    }

                    continue;
                }

                if (string.Equals(first, "background", StringComparison.OrdinalIgnoreCase))
                {
                    if (segments.Length < 2)
                    {
                        plan.RequiresFullBackgroundRefresh = true;
                    }
                    else
                    {
                        plan.BackgroundNames.Add(segments[1]);
                    }

                    continue;
                }

                if (string.Equals(first, "sounds", StringComparison.OrdinalIgnoreCase)
                    && segments.Length >= 2
                    && string.Equals(segments[1], "blips", StringComparison.OrdinalIgnoreCase))
                {
                    plan.RefreshBlips = true;
                    continue;
                }

                if (string.Equals(first, "misc", StringComparison.OrdinalIgnoreCase))
                {
                    plan.RefreshChats = true;
                    plan.RefreshEffects = true;
                }
            }

            return plan;
        }

        private static void RefreshAllAssets(Action<string>? progress)
        {
            Globals.UpdateConfigINI(Globals.PathToConfigINI);
            RefreshAllCharacters(progress);
            RefreshAllBackgrounds(progress);

            progress?.Invoke("Indexing blip files...");
            _ = BlipCatalog.Refresh();
            progress?.Invoke("Indexing chat profiles...");
            _ = ChatCatalog.Refresh();
            progress?.Invoke("Indexing effects folders...");
            _ = EffectsFolderCatalog.Refresh();

            PersistRefreshMarker();
        }

        private static void RefreshAllCharacters(Action<string>? progress)
        {
            CharacterFolder.RefreshCharacterList(
                onParsedCharacter: character =>
                {
                    progress?.Invoke("Parsed Character: " + character.Name);
                },
                onChangedMountPath: path =>
                {
                    progress?.Invoke("Changed mount path: " + path);
                });

            foreach (CharacterFolder character in CharacterFolder.FullList)
            {
                progress?.Invoke("Integrity verify: " + character.Name);
                _ = CharacterIntegrityVerifier.RunAndPersist(character);
            }
        }

        private static void RefreshAllBackgrounds(Action<string>? progress)
        {
            Background.RefreshCache(
                onChangedMountPath: path =>
                {
                    progress?.Invoke("Indexed background mount path: " + path);
                });
        }

        private static void RefreshCharacter(string characterName, Action<string>? progress)
        {
            string? existingDirectory = CharacterFolder.FullList
                .FirstOrDefault(character => string.Equals(character.Name, characterName, StringComparison.OrdinalIgnoreCase))
                ?.DirectoryPath;

            string resolvedDirectory = ResolveEffectiveMountedDirectory("characters", characterName);
            if (!string.IsNullOrWhiteSpace(resolvedDirectory))
            {
                progress?.Invoke("Refreshing character: " + characterName);
                if (!CharacterFolder.TryUpsertCharacterFolderInCache(
                        resolvedDirectory,
                        existingDirectory,
                        out CharacterFolder? character,
                        out string errorMessage))
                {
                    throw new InvalidOperationException(
                        "Failed to refresh character '" + characterName + "': " + errorMessage);
                }

                if (character != null)
                {
                    progress?.Invoke("Integrity verify: " + character.Name);
                    _ = CharacterIntegrityVerifier.RunAndPersist(character);
                }

                return;
            }

            progress?.Invoke("Removing character: " + characterName);
            if (!CharacterFolder.TryRemoveCharacterFolderFromCache(existingDirectory, characterName, out _, out string removeError))
            {
                throw new InvalidOperationException(
                    "Failed to remove character '" + characterName + "' from cache: " + removeError);
            }

            if (!string.IsNullOrWhiteSpace(existingDirectory))
            {
                DeleteIntegrityReport(existingDirectory);
            }
        }

        private static void RefreshBackground(string backgroundName, Action<string>? progress)
        {
            Background? existingBackground = Background.FromBGPath(backgroundName);
            string? existingDirectory = existingBackground?.PathToFile;
            string resolvedDirectory = ResolveEffectiveMountedDirectory("background", backgroundName);
            if (!string.IsNullOrWhiteSpace(resolvedDirectory))
            {
                progress?.Invoke("Refreshing background: " + backgroundName);
                if (!Background.TryUpsertBackgroundInCache(
                        resolvedDirectory,
                        out _,
                        out string errorMessage))
                {
                    throw new InvalidOperationException(
                        "Failed to refresh background '" + backgroundName + "': " + errorMessage);
                }

                return;
            }

            progress?.Invoke("Removing background: " + backgroundName);
            if (!Background.TryRemoveBackgroundFromCache(existingDirectory, backgroundName, out _, out string removeError))
            {
                throw new InvalidOperationException(
                    "Failed to remove background '" + backgroundName + "' from cache: " + removeError);
            }
        }

        private static string ResolveEffectiveMountedDirectory(string categoryFolderName, string entryName)
        {
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string candidateDirectory = Path.Combine(baseFolder, categoryFolderName, entryName);
                if (!Directory.Exists(candidateDirectory))
                {
                    continue;
                }

                if (string.Equals(categoryFolderName, "characters", StringComparison.OrdinalIgnoreCase))
                {
                    string charIniPath = Path.Combine(candidateDirectory, "char.ini");
                    if (!File.Exists(charIniPath))
                    {
                        continue;
                    }
                }

                return candidateDirectory;
            }

            return string.Empty;
        }

        private static void DeleteIntegrityReport(string characterDirectory)
        {
            try
            {
                string reportPath = CharacterIntegrityVerifier.GetReportFilePath(characterDirectory);
                if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath))
                {
                    File.Delete(reportPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static void PersistRefreshMarker()
        {
            try
            {
                string markerPath = GetRefreshMarkerPath();
                string markerDirectory = Path.GetDirectoryName(markerPath) ?? string.Empty;
                Directory.CreateDirectory(markerDirectory);

                AssetRefreshMarker marker = new AssetRefreshMarker
                {
                    SchemaVersion = RefreshMarkerSchemaVersion,
                    AppVersion = GetAppVersion(),
                    ConfigIniPath = NormalizePathForComparison(Globals.PathToConfigINI),
                    BaseFolders = BuildConfiguredBaseFolderSignature(Globals.PathToConfigINI)
                };

                if (marker.BaseFolders.Count == 0)
                {
                    marker.BaseFolders = NormalizeFolderSequence(Globals.BaseFolders ?? new List<string>());
                }

                string json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(markerPath, json);
            }
            catch
            {
                // Marker persistence is best-effort; refresh still completed.
            }
        }

        private static string GetRefreshMarkerPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OceanyaClient",
                "cache",
                "asset_refresh_marker.json");
        }

        private static string GetAppVersion()
        {
            Version? version = Assembly.GetEntryAssembly()?.GetName().Version
                ?? Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "0.0.0.0";
        }

        private static string NormalizeRelativePath(string path)
        {
            return (path ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        }

        internal static List<string> BuildConfiguredBaseFolderSignature(string configIniPath)
        {
            string normalizedConfigIniPath = NormalizePathForComparison(configIniPath);
            if (string.IsNullOrWhiteSpace(normalizedConfigIniPath) || !File.Exists(normalizedConfigIniPath))
            {
                return new List<string>();
            }

            string configDirectory = NormalizePathForComparison(Path.GetDirectoryName(normalizedConfigIniPath));
            string configMountParentDirectory = NormalizePathForComparison(
                Path.GetDirectoryName(configDirectory));

            List<string> configuredEntries = new List<string> { configDirectory };
            configuredEntries.AddRange(ReadConfiguredMountPathEntries(normalizedConfigIniPath));
            configuredEntries.Reverse();

            List<string> signatures = new List<string>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string configuredEntry in configuredEntries)
            {
                string normalizedEntry = string.Equals(
                        NormalizePathForComparison(configuredEntry),
                        configDirectory,
                        StringComparison.OrdinalIgnoreCase)
                    ? configDirectory
                    : NormalizeConfiguredMountEntry(configuredEntry, configMountParentDirectory);
                if (string.IsNullOrWhiteSpace(normalizedEntry))
                {
                    continue;
                }

                if (seenPaths.Add(normalizedEntry))
                {
                    signatures.Add(normalizedEntry);
                }
            }

            return signatures;
        }

        private static List<string> ReadConfiguredMountPathEntries(string configIniPath)
        {
            foreach (string line in File.ReadLines(configIniPath))
            {
                if (!line.StartsWith("mount_paths=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string raw = line["mount_paths=".Length..].Trim();
                if (string.IsNullOrWhiteSpace(raw)
                    || string.Equals(raw, "@Invalid()", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string>();
                }

                return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
            }

            return new List<string>();
        }

        private static string NormalizeConfiguredMountEntry(string configuredEntry, string configMountParentDirectory)
        {
            string trimmedEntry = configuredEntry?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedEntry))
            {
                return string.Empty;
            }

            try
            {
                string candidate = trimmedEntry;
                if (!Path.IsPathRooted(candidate) && !string.IsNullOrWhiteSpace(configMountParentDirectory))
                {
                    candidate = Path.Combine(configMountParentDirectory, candidate);
                }

                return NormalizePathForComparison(candidate);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsEquivalentFolderSequence(IEnumerable<string>? first, IEnumerable<string>? second)
        {
            List<string> normalizedFirst = NormalizeFolderSequence(first);
            List<string> normalizedSecond = NormalizeFolderSequence(second);
            if (normalizedFirst.Count != normalizedSecond.Count)
            {
                return false;
            }

            for (int i = 0; i < normalizedFirst.Count; i++)
            {
                if (!string.Equals(normalizedFirst[i], normalizedSecond[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> NormalizeFolderSequence(IEnumerable<string>? paths)
        {
            List<string> normalizedPaths = new List<string>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths ?? Enumerable.Empty<string>())
            {
                string normalizedPath = NormalizePathForComparison(path);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                if (seenPaths.Add(normalizedPath))
                {
                    normalizedPaths.Add(normalizedPath);
                }
            }

            return normalizedPaths;
        }

        private static string NormalizePathForComparison(string? path)
        {
            string trimmedPath = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(trimmedPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return trimmedPath
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }

    internal sealed class TargetedAssetRefreshPlan
    {
        public HashSet<string> CharacterNames { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> BackgroundNames { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool RequiresFullCharacterRefresh { get; set; }
        public bool RequiresFullBackgroundRefresh { get; set; }
        public bool RefreshBlips { get; set; }
        public bool RefreshChats { get; set; }
        public bool RefreshEffects { get; set; }
    }

    internal sealed class AssetRefreshMarker
    {
        public int SchemaVersion { get; set; }
        public string AppVersion { get; set; } = string.Empty;
        public string ConfigIniPath { get; set; } = string.Empty;
        public List<string> BaseFolders { get; set; } = new List<string>();
    }
}
