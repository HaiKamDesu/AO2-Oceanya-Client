using System;
using System.Collections.Generic;
using AOBot_Testing.Structures;
using Common;
using Common.WebAssets;

namespace OceanyaClient.Features.WebAssets
{
    /// <summary>
    /// Fills in character icons and emote button art for characters served from the web.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CharacterFolder"/> resolves <c>CharIconPath</c> and each emote's button paths exactly
    /// once, when the folder is parsed, and those results are persisted in the character cache. A
    /// character registered from the web mirror the moment its <c>char.ini</c> lands therefore records
    /// EMPTY paths for art that had not downloaded yet - and, because the value is cached, it stays
    /// empty forever. That is why a streamed character showed its icon in the selector (which resolves
    /// live) but had no icon in the clients panel and no emote button art.
    /// </para>
    /// <para>
    /// Rather than make the cache mutable, display code asks here: baked path first, then the mirror,
    /// and a request is fired for anything still absent so the next lookup succeeds.
    /// </para>
    /// </remarks>
    public static class WebCharacterIconResolver
    {
        /// <summary>
        /// Returns the best available character icon path, falling back to the web mirror when the
        /// cached path is empty. Returns the input unchanged when web fallback is off.
        /// </summary>
        public static string ResolveCharacterIcon(string? characterName, string? bakedIconPath)
        {
            if (!string.IsNullOrWhiteSpace(bakedIconPath))
            {
                return bakedIconPath;
            }

            if (!WebAssetService.IsActive || string.IsNullOrWhiteSpace(characterName))
            {
                return bakedIconPath ?? string.Empty;
            }

            string stem = "characters/" + WebAssetSource.NormalizeVPath(characterName) + "/char_icon";
            string? mirrored = WebAssetService.FindInMirrorIfActive(stem, WebAssetKind.CharacterIcon);
            if (mirrored != null)
            {
                return mirrored;
            }

            WebAssetService.PrefetchIfActive(stem, WebAssetKind.CharacterIcon);
            return string.Empty;
        }

        /// <summary>
        /// Returns the best available emote button path for the given state, falling back to the mirror.
        /// </summary>
        /// <param name="on">
        /// <c>true</c> for the pressed (<c>_on</c>) art, <c>false</c> for the released (<c>_off</c>) art.
        /// </param>
        public static string ResolveEmoteButton(string? characterName, Emote? emote, bool on, string? bakedPath)
        {
            if (!string.IsNullOrWhiteSpace(bakedPath))
            {
                return bakedPath;
            }

            if (!WebAssetService.IsActive || emote == null || string.IsNullOrWhiteSpace(characterName))
            {
                return bakedPath ?? string.Empty;
            }

            string stem = BuildButtonStem(characterName, emote, on);
            string? mirrored = WebAssetService.FindInMirrorIfActive(stem, WebAssetKind.CharacterIcon);
            if (mirrored != null)
            {
                return mirrored;
            }

            WebAssetService.PrefetchIfActive(stem, WebAssetKind.CharacterIcon);
            return string.Empty;
        }

        /// <summary>
        /// Warms button art for a character's emotes so the grid fills in rather than showing blanks.
        /// </summary>
        public static void PrefetchEmoteButtons(string? characterName, IEnumerable<Emote>? emotes)
        {
            if (!WebAssetService.IsActive || string.IsNullOrWhiteSpace(characterName) || emotes == null)
            {
                return;
            }

            int queued = 0;
            foreach (Emote emote in emotes)
            {
                if (emote == null || emote.ID <= 0)
                {
                    continue;
                }

                WebAssetService.PrefetchIfActive(
                    BuildButtonStem(characterName, emote, on: false),
                    WebAssetKind.CharacterIcon);
                WebAssetService.PrefetchIfActive(
                    BuildButtonStem(characterName, emote, on: true),
                    WebAssetKind.CharacterIcon);
                queued += 2;
            }

            if (queued > 0)
            {
                CustomConsole.Debug(
                    $"[WEB-PREFETCH] emote buttons character=\"{characterName}\" queued={queued}",
                    CustomConsole.LogCategory.WebAssets);
            }
        }

        /// <summary>
        /// Builds the AO2 emote button VFS stem: <c>characters/&lt;char&gt;/emotions/button&lt;id&gt;_on|_off</c>.
        /// </summary>
        /// <remarks>
        /// Keyed by the emote's numeric ID, matching <see cref="CharacterFolder"/>'s own button lookup.
        /// The emote NAME is not part of the filename.
        /// </remarks>
        private static string BuildButtonStem(string characterName, Emote emote, bool on)
        {
            return "characters/" + WebAssetSource.NormalizeVPath(characterName)
                + "/emotions/button" + emote.ID.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (on ? "_on" : "_off");
        }
    }
}
