using System;
using System.IO;
using Common;

namespace OceanyaClient.Features.Assets
{
    /// <summary>
    /// Releases every in-memory hold the client has on a character folder, so it can be edited in place,
    /// and reloads it afterwards.
    /// </summary>
    /// <remarks>
    /// Editing a character the client is actively using has two separate failure modes, and both have to be
    /// handled for the edit to feel seamless:
    ///
    /// 1. **Locked files.** Anything holding an OS file handle blocks the editor's folder replacement with
    ///    "access to the path is denied". Loaders are individually responsible for not doing this (see
    ///    <c>BitmapFileLoader</c>'s <c>OnLoad</c> and <c>GifAnimationPlayer</c>'s in-memory decode), but a
    ///    live animation player also has to let go of the frames it decoded from those files.
    /// 2. **Stale art.** Several caches are keyed by path ALONE, with no write timestamp -
    ///    <c>Ao2AnimationPreview</c>'s static preview cache and its APNG detection cache - so after an edit
    ///    they keep serving the old image. Caches that do validate by timestamp self-heal, but are purged
    ///    here too so the edit is visible immediately rather than on the next natural refresh.
    ///
    /// The order is release, apply, reload. Call <see cref="Release"/> immediately before replacing the
    /// folder and <see cref="Reload"/> immediately after.
    /// </remarks>
    public static class AssetHandleReleaser
    {
        /// <summary>
        /// Drops every cached decode and live animation under <paramref name="characterDirectoryPath"/>.
        /// </summary>
        /// <param name="characterDirectoryPath">Character folder about to be replaced or edited.</param>
        public static void Release(string? characterDirectoryPath)
        {
            string directory = Normalize(characterDirectoryPath);
            if (directory.Length == 0)
            {
                return;
            }

            CustomConsole.Info(
                $"[ASSET-EDIT] Release starting for \"{directory}\".",
                CustomConsole.LogCategory.System);

            RunGuarded("viewport animations", () => Features.Viewport.AO2ViewportControl.ReleaseLiveAnimations());
            RunGuarded("decoded image caches", () => Ao2AnimationPreview.ReleaseCachedAssetsUnder(directory));
            RunGuarded("emote button bitmaps", () => Components.ICMessageSettings.ReleaseEmoteButtonImagesUnder(directory));
            RunGuarded("character icons", () => CharacterSelectorWindow.ReleaseIconsUnder(directory));
            RunGuarded("viewport resolver caches", () => Features.Viewport.AO2ViewportAssetResolver.ReleaseCachesUnder(directory));

            CustomConsole.Info(
                $"[ASSET-RELEASE] Released in-memory holds under \"{directory}\".",
                CustomConsole.LogCategory.System);
        }

        /// <summary>
        /// Re-reads a character folder after it was edited and refreshes everything showing it.
        /// </summary>
        /// <param name="characterDirectoryPath">The folder as it exists now.</param>
        /// <param name="previousDirectoryPath">Its path before the edit, when the folder was renamed.</param>
        public static void Reload(string? characterDirectoryPath, string? previousDirectoryPath = null)
        {
            string directory = Normalize(characterDirectoryPath);
            if (directory.Length == 0)
            {
                return;
            }

            CustomConsole.Info(
                $"[ASSET-EDIT] Reload starting for \"{directory}\" (was \"{Normalize(previousDirectoryPath)}\").",
                CustomConsole.LogCategory.System);

            string reindexedCharacterName = string.Empty;
            RunGuarded("character index", () =>
            {
                if (!AOBot_Testing.Structures.CharacterFolder.TryUpsertCharacterFolderInCache(
                        directory,
                        previousDirectoryPath,
                        out AOBot_Testing.Structures.CharacterFolder? reindexed,
                        out string error))
                {
                    CustomConsole.Warning($"[ASSET-EDIT] Could not re-index \"{directory}\": {error}");
                    return;
                }

                reindexedCharacterName = reindexed?.Name ?? string.Empty;
                CustomConsole.Info(
                    $"[ASSET-EDIT] Re-indexed \"{reindexedCharacterName}\" with "
                    + $"{reindexed?.configINI?.Emotions?.Count ?? 0} emote(s).",
                    CustomConsole.LogCategory.System);
            });

            // Visual-only refresh: the current message keeps typing, its SFX do not replay and the chat
            // queue is untouched. Only the art is re-resolved.
            RunGuarded("viewport refresh", () => Features.Viewport.AO2ViewportControl.RequestVisualRefreshForAll());

            // Dropping the cached bitmaps is not enough for anything holding the character MODEL: an edit
            // can repoint an emote at a different file, and the emote buttons, character icon and dropdowns
            // were all built from the pre-edit parse. This asks the UI to rebind to the re-indexed one.
            RunGuarded(
                "client rebind",
                () => ClientAssetRefreshService.NotifyCharacterFolderChanged(reindexedCharacterName));

            CustomConsole.Info(
                $"[ASSET-EDIT] Reload finished for \"{directory}\".",
                CustomConsole.LogCategory.System);

            CustomConsole.Info(
                $"[ASSET-RELEASE] Reloaded \"{directory}\".",
                CustomConsole.LogCategory.System);
        }

        /// <summary>Whether <paramref name="assetPath"/> lives under <paramref name="directoryPath"/>.</summary>
        public static bool IsUnder(string? assetPath, string directoryPath)
        {
            string asset = Normalize(assetPath);
            if (asset.Length == 0 || directoryPath.Length == 0)
            {
                return false;
            }

            return asset.StartsWith(directoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(asset, directoryPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Normalizes a path for prefix comparison, tolerating anything unparseable.</summary>
        public static string Normalize(string? path)
        {
            string value = (path ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        /// <summary>One failing surface must never stop the rest from releasing.</summary>
        private static void RunGuarded(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                CustomConsole.Warning($"[ASSET-RELEASE] Releasing {what} failed.", ex);
            }
        }
    }
}
