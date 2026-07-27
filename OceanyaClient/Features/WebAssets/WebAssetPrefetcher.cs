using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AOBot_Testing.Structures;
using Common;
using Common.WebAssets;

namespace OceanyaClient.Features.WebAssets
{
    /// <summary>
    /// Warms the web mirror ahead of the moment an asset is actually drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modeled on webAO's <c>viewport/utils/preloadMessageAssets.ts</c>, which resolves and preloads
    /// every asset an IC message references before its animation timeline starts. Oceanya has the same
    /// head start available: the FIFO chat queue paces messages by <c>stay_time</c>, shouts run their
    /// full bubble animation first, and pre-animations buy hundreds of milliseconds more.
    /// </para>
    /// <para>
    /// Everything here is fire-and-forget and rides the prefetch concurrency lane, so a speculative
    /// download can never delay an asset the currently displayed message is waiting on.
    /// </para>
    /// </remarks>
    public static class WebAssetPrefetcher
    {
        /// <summary>Emote prefixes AO2 uses for the idle and talking halves of one emote.</summary>
        private static readonly string[] SpritePrefixes = { "(a)", "(b)" };

        /// <summary>Shout bubble stems, indexed to match <see cref="ICMessage.ShoutModifiers"/>.</summary>
        private static readonly Dictionary<ICMessage.ShoutModifiers, string> ShoutBubbleStems = new()
        {
            [ICMessage.ShoutModifiers.HoldIt] = "holdit_bubble",
            [ICMessage.ShoutModifiers.Objection] = "objection_bubble",
            [ICMessage.ShoutModifiers.TakeThat] = "takethat_bubble"
        };

        /// <summary>
        /// Queues every asset an incoming IC message will need. Cheap and safe to call for every
        /// message: already-present assets short-circuit in the mirror and cost no request.
        /// </summary>
        public static void PrefetchMessageAssets(ICMessage? message, string? backgroundName)
        {
            if (message == null || !WebAssetService.IsActive)
            {
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            int queued = 0;

            queued += PrefetchCharacterEmote(message.Character, message.Emote);

            if (!string.IsNullOrWhiteSpace(message.PreAnim) && message.PreAnim.Trim() != "-")
            {
                queued += Request(
                    BuildCharacterStem(message.Character, message.PreAnim),
                    WebAssetKind.CharacterSprite);
            }

            if (!string.IsNullOrWhiteSpace(message.OtherName))
            {
                queued += PrefetchCharacterEmote(message.OtherName, message.OtherEmote);
            }

            if (ShoutBubbleStems.TryGetValue(message.ShoutModifier, out string? shoutStem))
            {
                queued += Request(
                    BuildCharacterStem(message.Character, shoutStem),
                    WebAssetKind.CharacterSprite);
                queued += Request(
                    BuildCharacterStem(message.Character, shoutStem.Replace("_bubble", string.Empty)),
                    WebAssetKind.Sound);
            }

            if (!string.IsNullOrWhiteSpace(message.SfxName)
                && !string.Equals(message.SfxName, "0", StringComparison.Ordinal)
                && !string.Equals(message.SfxName, "1", StringComparison.Ordinal))
            {
                queued += Request("sounds/general/" + message.SfxName.Trim(), WebAssetKind.Sound);
            }

            queued += PrefetchBackground(backgroundName, message.Side);

            if (queued > 0)
            {
                CustomConsole.Debug(
                    $"[WEB-PREFETCH] message char=\"{message.Character}\" emote=\"{message.Emote}\" "
                    + $"queued={queued} elapsedMs={stopwatch.Elapsed.TotalMilliseconds:0.0}",
                    CustomConsole.LogCategory.WebAssets);
            }
        }

        /// <summary>
        /// Warms a character that just appeared in the area roster, so they are already local by the
        /// time they speak. Deliberately small: the config, the icon, and the default emote pair.
        /// </summary>
        public static void PrefetchRosterCharacter(string? characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName) || !WebAssetService.IsActive)
            {
                return;
            }

            string folder = BuildCharacterFolder(characterName);
            if (folder.Length == 0)
            {
                return;
            }

            Request(folder + "/char.ini", WebAssetKind.Config);
            Request(folder + "/char_icon", WebAssetKind.CharacterIcon);
            PrefetchCharacterEmote(characterName, "normal");

            CustomConsole.Debug(
                $"[WEB-PREFETCH] roster character=\"{characterName}\"",
                CustomConsole.LogCategory.WebAssets);
        }

        /// <summary>Warms the position images of a background that just became current.</summary>
        public static int PrefetchBackground(string? backgroundName, string? position)
        {
            if (string.IsNullOrWhiteSpace(backgroundName) || !WebAssetService.IsActive)
            {
                return 0;
            }

            string folder = "background/" + backgroundName.Trim().Replace('\\', '/').Trim('/');
            int queued = Request(folder + "/design.ini", WebAssetKind.Config);
            queued += Request(folder + "/wit", WebAssetKind.Background);

            string normalizedPosition = (position ?? string.Empty).Trim();
            int separator = normalizedPosition.IndexOf(':');
            if (separator > 0)
            {
                normalizedPosition = normalizedPosition[..separator];
            }

            if (AO2BackgroundStems.TryGetValue(normalizedPosition, out (string Background, string Desk) stems))
            {
                queued += Request(folder + "/" + stems.Background, WebAssetKind.Background);
                queued += Request(folder + "/" + stems.Desk, WebAssetKind.Background);
            }

            return queued;
        }

        /// <summary>AO2 position token to (background image, desk image) stem pairs.</summary>
        private static readonly Dictionary<string, (string Background, string Desk)> AO2BackgroundStems =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["def"] = ("defenseempty", "defensedesk"),
                ["pro"] = ("prosecutorempty", "prosecutiondesk"),
                ["wit"] = ("witnessempty", "stand"),
                ["jud"] = ("judgestand", "judgedesk"),
                ["hld"] = ("helperstand", "helperdesk"),
                ["hlp"] = ("prohelperstand", "prohelperdesk"),
                ["jur"] = ("jurystand", "jurydesk"),
                ["sea"] = ("seancestand", "seancedesk")
            };

        private static int PrefetchCharacterEmote(string? characterName, string? emoteName)
        {
            if (string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(emoteName))
            {
                return 0;
            }

            string folder = BuildCharacterFolder(characterName);
            if (folder.Length == 0)
            {
                return 0;
            }

            int queued = 0;
            foreach (string prefix in SpritePrefixes)
            {
                queued += Request(folder + "/" + prefix + emoteName.Trim(), WebAssetKind.CharacterSprite);
            }

            return queued;
        }

        private static string BuildCharacterFolder(string? characterName)
        {
            string normalized = (characterName ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            return normalized.Length == 0 ? string.Empty : "characters/" + normalized;
        }

        private static string BuildCharacterStem(string? characterName, string? token)
        {
            string folder = BuildCharacterFolder(characterName);
            string normalizedToken = (token ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            return folder.Length == 0 || normalizedToken.Length == 0
                ? string.Empty
                : folder + "/" + normalizedToken;
        }

        private static int Request(string stem, WebAssetKind kind)
        {
            if (string.IsNullOrWhiteSpace(stem))
            {
                return 0;
            }

            WebAssetService.PrefetchIfActive(stem, kind);
            return 1;
        }
    }
}
