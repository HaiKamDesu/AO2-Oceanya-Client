using System;
using System.Collections.Generic;
using System.Linq;

namespace Common.WebAssets
{
    /// <summary>
    /// Classifies a web asset so the fetch pipeline can pick the right extension probe order and
    /// keep a separate learned-format entry per kind (AsyncAO ADR-0001: one perfectly-aimed request
    /// per asset once the working format for a kind is known).
    /// </summary>
    public enum WebAssetKind
    {
        /// <summary>Character selector / emote grid icon (<c>char_icon</c>).</summary>
        CharacterIcon,
        /// <summary>Character emote sprite, pre-animation, or shout bubble under the character folder.</summary>
        CharacterSprite,
        /// <summary>Background position image or desk overlay.</summary>
        Background,
        /// <summary>Theme, misc, chatbox, WTCE, effect, and speedline art.</summary>
        Misc,
        /// <summary>Evidence icon / presentation image.</summary>
        Evidence,
        /// <summary>General SFX under <c>sounds/general</c>.</summary>
        Sound,
        /// <summary>Blip under <c>sounds/blips</c>.</summary>
        Blip,
        /// <summary>Track under <c>sounds/music</c>.</summary>
        Music,
        /// <summary>A file whose extension is already known exactly (<c>char.ini</c>, <c>design.ini</c>, manifests).</summary>
        Config
    }

    /// <summary>
    /// Default extension probe orders per <see cref="WebAssetKind"/>.
    /// </summary>
    /// <remarks>
    /// Image orders intentionally mirror <c>AO2ViewportAssetResolver.ImageExtensions</c> so a web asset
    /// resolves to the same format the local resolver would have picked. A server may override these
    /// through its <c>extensions.json</c> manifest (webAO <c>client/fetchLists.ts</c>).
    /// </remarks>
    public static class WebAssetExtensions
    {
        /// <summary>Image probe order shared by sprites, backgrounds, misc art, and evidence.</summary>
        public static readonly IReadOnlyList<string> Image =
            new[] { ".webp", ".apng", ".gif", ".png", ".jpg", ".jpeg" };

        /// <summary>Icon probe order. Servers ship PNG icons, so PNG leads (AsyncAO ADR-0001).</summary>
        public static readonly IReadOnlyList<string> Icon = new[] { ".png", ".webp" };

        /// <summary>Audio probe order shared by SFX, blips, and music.</summary>
        public static readonly IReadOnlyList<string> Audio = new[] { ".opus", ".ogg", ".wav", ".mp3" };

        /// <summary>
        /// webAO's synthetic marker for "the .webp file, with the (a)/(b) emote prefix removed".
        /// </summary>
        /// <remarks>
        /// See <c>webAO/webAO/client/setEmote.ts</c>: this entry builds
        /// <c>characters/&lt;char&gt;/&lt;emote&gt;.webp</c> without the prefix, which is how
        /// single-sprite characters (one file for both idle and talking) are served.
        /// </remarks>
        public const string StaticWebPMarker = ".webp.static";

        /// <summary>Every extension the pipeline recognises, used to tell a stem from a full filename.</summary>
        public static readonly IReadOnlyCollection<string> All =
            new HashSet<string>(
                Image.Concat(Icon).Concat(Audio).Concat(new[] { ".ini", ".json", ".txt" }),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns the default probe order for <paramref name="kind"/>.</summary>
        public static IReadOnlyList<string> DefaultFor(WebAssetKind kind) => kind switch
        {
            WebAssetKind.CharacterIcon => Icon,
            WebAssetKind.Sound or WebAssetKind.Blip or WebAssetKind.Music => Audio,
            WebAssetKind.Config => Array.Empty<string>(),
            _ => Image
        };

        /// <summary>
        /// Maps a manifest key from <c>extensions.json</c> to the kinds it configures.
        /// </summary>
        public static IReadOnlyList<WebAssetKind> KindsForManifestKey(string manifestKey) => manifestKey switch
        {
            "charicon_extensions" => new[] { WebAssetKind.CharacterIcon },
            "emote_extensions" => new[] { WebAssetKind.CharacterSprite },
            "emotions_extensions" => new[] { WebAssetKind.CharacterIcon },
            "background_extensions" => new[] { WebAssetKind.Background },
            _ => Array.Empty<WebAssetKind>()
        };
    }
}
