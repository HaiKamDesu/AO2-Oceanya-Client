using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AOBot_Testing.Structures;
using Common;
using Common.WebAssets;

namespace OceanyaClient.Features.WebAssets
{
    /// <summary>
    /// Makes a character that exists only on the server usable in the GM Multi-Client by mirroring its
    /// <c>char.ini</c> (and icon) into the local web mirror.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything downstream of <c>CharacterFolder.Create(configIniPath)</c> is path-based, so a
    /// mirrored <c>char.ini</c> is indistinguishable from a physical one: the character shows up in the
    /// selector, the emote grid, the pairing studio, and the viewport with no special cases.
    /// </para>
    /// <para>
    /// <b>Strictly on demand.</b> An earlier version mirrored the whole roster at connect. Measured on a
    /// server with 3564 characters the user did not have, that issued 3564 downloads through the
    /// prefetch lane, saturated the pipeline for over two minutes, and pushed average asset latency to
    /// 13 seconds with a 52 second peak - starving the sprites actually being drawn. Configs are now
    /// fetched only when a character is selected, or for the selector cards the user can actually see.
    /// </para>
    /// </remarks>
    public static class WebCharacterMirror
    {
        /// <summary>
        /// Materializes one character's <c>char.ini</c> and registers it, so a character picked from the
        /// selector becomes usable immediately.
        /// </summary>
        /// <returns><c>true</c> when the character is present locally afterwards.</returns>
        public static async Task<bool> EnsureCharacterAsync(string characterName)
        {
            WebAssetService? service = WebAssetService.Current;
            if (service == null || !WebAssetService.IsActive || string.IsNullOrWhiteSpace(characterName))
            {
                return false;
            }

            string trimmed = characterName.Trim();
            if (IsInstalledLocally(trimmed))
            {
                return true;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            string folderVPath = "characters/" + trimmed.Replace('\\', '/').Trim('/');
            string? configPath = await service
                .RequestAsync(folderVPath + "/char.ini", WebAssetKind.Config)
                .ConfigureAwait(false);
            if (configPath == null)
            {
                CustomConsole.Debug(
                    $"[WEB] Character \"{trimmed}\" has no char.ini on the server ({stopwatch.ElapsedMilliseconds}ms).",
                    CustomConsole.LogCategory.WebAssets);
                return false;
            }

            // The icon is what the selector draws; the default emote pair is what the viewport draws
            // first. Both ride the prefetch lane so neither delays this call.
            WebAssetService.PrefetchIfActive(folderVPath + "/char_icon", WebAssetKind.CharacterIcon);
            WebAssetService.PrefetchIfActive(folderVPath + "/(a)normal", WebAssetKind.CharacterSprite);
            WebAssetService.PrefetchIfActive(folderVPath + "/(b)normal", WebAssetKind.CharacterSprite);

            string directory = Path.GetDirectoryName(configPath) ?? string.Empty;
            if (directory.Length == 0)
            {
                return false;
            }

            if (CharacterFolder.TryUpsertCharacterFolderInCache(directory, null, out _, out string error))
            {
                CustomConsole.Debug(
                    $"[WEB] Mirrored character \"{trimmed}\" in {stopwatch.ElapsedMilliseconds}ms.",
                    CustomConsole.LogCategory.WebAssets);
                return true;
            }

            CustomConsole.Debug(
                $"[WEB] Could not register on-demand character \"{trimmed}\": {error}",
                CustomConsole.LogCategory.WebAssets);
            return false;
        }

        /// <summary>Reports whether the user already has this character physically installed.</summary>
        public static bool IsInstalledLocally(string characterName)
        {
            string trimmed = (characterName ?? string.Empty).Trim();
            return trimmed.Length > 0
                && CharacterFolder.FullList.Any(folder =>
                    string.Equals(folder.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns the names from <paramref name="publishedNames"/> that have no local character folder,
        /// comparing against the loaded character list rather than touching the filesystem per name.
        /// </summary>
        public static List<string> FindCharactersMissingLocally(IEnumerable<string> publishedNames)
        {
            HashSet<string> localNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CharacterFolder folder in CharacterFolder.FullList)
            {
                if (!string.IsNullOrWhiteSpace(folder.Name))
                {
                    localNames.Add(folder.Name.Trim());
                }
            }

            List<string> missing = new List<string>();
            foreach (string published in publishedNames)
            {
                string normalized = (published ?? string.Empty).Trim();
                if (normalized.Length > 0 && !localNames.Contains(normalized))
                {
                    missing.Add(normalized);
                }
            }

            return missing;
        }
    }
}
