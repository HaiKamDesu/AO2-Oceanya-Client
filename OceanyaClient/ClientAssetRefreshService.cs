using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Provides a single workflow for rebuilding client-side character and background indexes.
    /// </summary>
    public static class ClientAssetRefreshService
    {
        private const int RefreshMarkerSchemaVersion = 1;

        /// <summary>
        /// Refreshes character and background caches while showing progress in <see cref="WaitForm"/>.
        /// </summary>
        /// <param name="owner">Owning window for the progress dialog.</param>
        public static async Task RefreshCharactersAndBackgroundsAsync(Window owner)
        {
            await WaitForm.ShowFormAsync("Refreshing character and background info...", owner);

            try
            {
                Globals.UpdateConfigINI(Globals.PathToConfigINI);

                CharacterFolder.RefreshCharacterList(
                    onParsedCharacter: (CharacterFolder ini) =>
                    {
                        WaitForm.SetSubtitle("Parsed Character: " + ini.Name);
                    },
                    onChangedMountPath: (string path) =>
                    {
                        WaitForm.SetSubtitle("Changed mount path: " + path);
                    });

                foreach (CharacterFolder character in CharacterFolder.FullList)
                {
                    WaitForm.SetSubtitle("Integrity verify: " + character.Name);
                    _ = CharacterIntegrityVerifier.RunAndPersist(character);
                }

                Background.RefreshCache(
                    onChangedMountPath: (string path) =>
                    {
                        WaitForm.SetSubtitle("Indexed background mount path: " + path);
                    });

                WaitForm.SetSubtitle("Indexing blip files...");
                _ = BlipCatalog.Refresh();
                WaitForm.SetSubtitle("Indexing chat profiles...");
                _ = ChatCatalog.Refresh();
                WaitForm.SetSubtitle("Indexing effects folders...");
                _ = EffectsFolderCatalog.Refresh();

                PersistRefreshMarker();
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        /// <summary>
        /// Returns true when character/background refresh should be forced for the current app/config state.
        /// </summary>
        public static bool RequiresRefreshForCurrentEnvironment()
        {
            try
            {
                string markerPath = GetRefreshMarkerPath();
                if (!File.Exists(markerPath))
                {
                    return true;
                }

                string json = File.ReadAllText(markerPath);
                AssetRefreshMarker? marker = JsonSerializer.Deserialize<AssetRefreshMarker>(json);
                if (marker == null)
                {
                    return true;
                }

                if (marker.SchemaVersion != RefreshMarkerSchemaVersion)
                {
                    return true;
                }

                if (!string.Equals(marker.AppVersion, GetAppVersion(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.Equals(
                        marker.ConfigIniPath ?? string.Empty,
                        Globals.PathToConfigINI ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                List<string> markerBaseFolders = marker.BaseFolders ?? new List<string>();
                if (markerBaseFolders.Count != Globals.BaseFolders.Count)
                {
                    return true;
                }

                for (int i = 0; i < markerBaseFolders.Count; i++)
                {
                    if (!string.Equals(markerBaseFolders[i], Globals.BaseFolders[i], StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return true;
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
                    ConfigIniPath = Globals.PathToConfigINI ?? string.Empty,
                    BaseFolders = new List<string>(Globals.BaseFolders)
                };

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
    }

    internal sealed class AssetRefreshMarker
    {
        public int SchemaVersion { get; set; }
        public string AppVersion { get; set; } = string.Empty;
        public string ConfigIniPath { get; set; } = string.Empty;
        public List<string> BaseFolders { get; set; } = new List<string>();
    }
}
