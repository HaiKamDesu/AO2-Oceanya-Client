using System;
using System.Collections.Generic;
using System.Linq;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient.Features.Startup
{
    /// <summary>
    /// Writes the user's chosen launch settings and relevant saved state into the debug log.
    /// </summary>
    /// <remarks>
    /// A log collected from someone else is only actionable if it says what they were running. The session
    /// header covers the machine and the install; this covers the choices - what they launched, against
    /// which server, with which toggles, and the feature flags and viewport/audio options that change
    /// behaviour. Written once per launch, right after the configuration is saved.
    ///
    /// Deliberately excludes anything secret: no Google Drive tokens, no API keys, no credential paths.
    /// Only the settings that change how the client behaves.
    /// </remarks>
    public static class LaunchConfigurationReport
    {
        /// <summary>Logs the launch configuration as a block of <c>[LAUNCH-CONFIG]</c> lines.</summary>
        /// <param name="startupFunctionalityId">The functionality the user chose to launch.</param>
        /// <param name="serverName">Selected server display name.</param>
        /// <param name="serverEndpoint">Selected server endpoint.</param>
        public static void Log(string startupFunctionalityId, string serverName, string serverEndpoint)
        {
            foreach (string line in Build(startupFunctionalityId, serverName, serverEndpoint))
            {
                CustomConsole.Info("[LAUNCH-CONFIG] " + line, CustomConsole.LogCategory.System);
            }
        }

        /// <summary>Builds the report lines. Separated from logging so it can be tested.</summary>
        internal static List<string> Build(string startupFunctionalityId, string serverName, string serverEndpoint)
        {
            List<string> lines = new List<string>();

            try
            {
                SaveData data = SaveFile.Data;

                lines.Add($"Functionality: {startupFunctionalityId}");
                lines.Add($"Server: \"{serverName}\" <{serverEndpoint}>");
                lines.Add(
                    $"Client mode: {(data.UseSingleInternalClient ? "single internal client" : "multiple internal clients")}"
                    + $" | skipLoadingScreen={data.SkipLoadingScreen}");
                lines.Add(
                    $"OOC name: \"{data.OOCName}\" | sendCustomShowname={data.SendCustomShowname}"
                    + $" | switchPosOnIniSwap={data.SwitchPosOnIniSwap} | stickyEffect={data.StickyEffect}");
                lines.Add(
                    $"IC log: invert={data.InvertICLog} maxMessages={data.LogMaxMessages}");
                lines.Add(
                    $"UI scale: mode={data.UiScaleMode} factor={data.UiScaleFactor:0.##}");
                lines.Add(
                    $"Viewport: renderInPanel={data.GMViewportRenderInPanel}"
                    + $" chatboxOverlaps={data.GMViewportChatboxOverlapsViewport}"
                    + $" windowsPreview={data.GMViewportWindowPreviewPriority}"
                    + $" pictureInPicture={data.GMPictureInPictureViewport}");
                lines.Add(
                    $"Panels: areaListInPanel={data.GMAreaListRenderInPanel}"
                    + $" musicListInPanel={data.GMMusicListRenderInPanel}"
                    + $" areaMusicSlotShowsMusic={data.GMAreaMusicSlotShowsMusic}");
                lines.Add(
                    $"Audio: music={data.AudioMusicVolume:0.##} sfx={data.AudioSfxVolume:0.##} blip={data.AudioBlipVolume:0.##}"
                    + $" | musicEffects fadeOut={data.MusicEffectFadeOut} fadeIn={data.MusicEffectFadeIn} sync={data.MusicEffectSyncPos}");

                lines.Add(BuildSnapshotLine(data));
                lines.Add(BuildFeatureFlagLine(data));
                lines.Add(BuildAssetLine());
            }
            catch (Exception ex)
            {
                lines.Add("Launch configuration report failed: " + ex.GetType().Name + " " + ex.Message);
            }

            return lines;
        }

        private static string BuildSnapshotLine(SaveData data)
        {
            GmMultiClientSnapshot? snapshot = data.GMMultiClientSnapshot;
            if (snapshot?.Clients == null || snapshot.Clients.Count == 0)
            {
                return $"GM snapshot: none | presets={data.GMMultiClientSnapshotPresets?.Count ?? 0}";
            }

            IEnumerable<string> clientSummaries = snapshot.Clients.Select(client =>
                $"\"{client.ClientName}\"(puppet=\"{client.IniPuppetName}\")");
            return $"GM snapshot: {snapshot.Clients.Count} client(s) "
                + $"[{string.Join(", ", clientSummaries)}] selected=\"{snapshot.SelectedClientName}\""
                + $" savedMode={(snapshot.UseSingleInternalClient ? "single" : "multi")}"
                + $" server=\"{snapshot.ServerName}\""
                + $" | presets={data.GMMultiClientSnapshotPresets?.Count ?? 0}";
        }

        private static string BuildFeatureFlagLine(SaveData data)
        {
            try
            {
                List<string> enabled = (data.AdvancedFeatures?.EnabledFeatures ?? new Dictionary<string, bool>())
                    .Where(pair => pair.Value)
                    .Select(pair => pair.Key)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return enabled.Count == 0
                    ? "Advanced features: none enabled"
                    : $"Advanced features: {string.Join(", ", enabled)}";
            }
            catch (Exception ex)
            {
                return "Advanced features unavailable: " + ex.GetType().Name;
            }
        }

        private static string BuildAssetLine()
        {
            try
            {
                return $"Assets: indexedCharacters={CharacterFolder.CachedCharacterCount}"
                    + $" parsedCharacters={CharacterFolder.ParsedCharacterCount}";
            }
            catch (Exception ex)
            {
                return "Asset counts unavailable: " + ex.GetType().Name;
            }
        }
    }
}
