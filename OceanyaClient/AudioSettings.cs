using System;
using System.Collections.Generic;
using System.IO;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Shared audio volume accessors backed by persisted save data.
    /// </summary>
    public static class AudioSettings
    {
        /// <summary>
        /// Gets the effective music volume as a 0-1 scalar.
        /// </summary>
        public static double MusicVolume => ResolveVolume("default_music", SaveFile.Data.AudioMusicVolume);

        /// <summary>
        /// Gets the effective SFX volume as a 0-1 scalar.
        /// </summary>
        public static double SfxVolume => ResolveVolume("default_sfx", SaveFile.Data.AudioSfxVolume);

        /// <summary>
        /// Gets the effective blip volume as a 0-1 scalar.
        /// </summary>
        public static double BlipVolume => ResolveVolume("default_blip", SaveFile.Data.AudioBlipVolume);

        /// <summary>
        /// Resolves a volume the same way the Settings window does: the selected AO2
        /// <c>config.ini</c> value wins, with the savefile as fallback.
        /// </summary>
        /// <remarks>
        /// These two sources used to disagree. The Settings window read and wrote
        /// <c>config.ini</c> (<c>default_music</c> / <c>default_sfx</c> / <c>default_blip</c>) while
        /// playback read only the savefile, and nothing reconciled them at startup. Any change made
        /// outside our Settings window - by AO2 itself, or by editing config.ini - left the slider
        /// showing the real value while audio played at the stale savefile one, which is why a session
        /// started at the wrong volume and only corrected once the slider was touched (that write
        /// updated both). Sharing one precedence rule removes the divergence instead of papering over
        /// it with a startup sync.
        /// </remarks>
        private static double ResolveVolume(string configKey, double savefileScalar)
        {
            if (livePreviewVolumes != null && livePreviewVolumes.TryGetValue(configKey, out double preview))
            {
                return Math.Clamp(preview, 0.0, 1.0);
            }

            double fallback = Math.Clamp(savefileScalar, 0.0, 1.0);
            return Ao2ConfigIniSettings.TryGetCachedInt(configKey, out int percent)
                ? Math.Clamp(percent / 100.0, 0.0, 1.0)
                : fallback;
        }

        /// <summary>
        /// Unsaved slider values, active only while the Settings window is open.
        /// </summary>
        /// <remarks>
        /// Needed because <c>config.ini</c> now wins over the savefile: dragging a slider writes neither
        /// until Save, so without this the live preview would be silently ignored. Cleared on save and
        /// on cancel, after which the persisted values take over again.
        /// </remarks>
        private static volatile Dictionary<string, double>? livePreviewVolumes;

        /// <summary>Applies unsaved slider values (0-1 scalars) for live preview.</summary>
        public static void SetLivePreviewVolumes(double music, double sfx, double blip)
        {
            livePreviewVolumes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["default_music"] = music,
                ["default_sfx"] = sfx,
                ["default_blip"] = blip
            };
        }

        /// <summary>Drops the live preview so persisted values apply again.</summary>
        public static void ClearLivePreviewVolumes() => livePreviewVolumes = null;

        /// <summary>
        /// Gets a blip volume after applying saved extra audio rules.
        /// </summary>
        public static double ResolveBlipVolume(
            string? characterName,
            string? showname,
            string? playerName,
            string? blipToken)
        {
            return ResolveBlipVolume(characterName, showname, playerName, blipToken, SaveFile.Data.ExtraAudioRules);
        }

        /// <summary>
        /// Gets a blip volume after applying the supplied extra audio rules.
        /// </summary>
        internal static double ResolveBlipVolume(
            string? characterName,
            string? showname,
            string? playerName,
            string? blipToken,
            IEnumerable<ExtraAudioRule>? rules)
        {
            double volume = BlipVolume;
            foreach (ExtraAudioRule rule in rules ?? Array.Empty<ExtraAudioRule>())
            {
                if (!rule.IsEnabled || rule.Kind != ExtraAudioRuleKind.Blip || !RuleMatches(rule, characterName, showname, playerName, blipToken))
                {
                    continue;
                }

                volume = Math.Max(0, rule.VolumePercent) / 100.0;
            }

            return Math.Max(0.0, volume);
        }

        /// <summary>
        /// Gets an SFX volume after applying saved token-only extra audio rules.
        /// </summary>
        public static double ResolveSfxVolume(string? sfxToken)
        {
            return ResolveSfxVolume(null, null, null, sfxToken, SaveFile.Data.ExtraAudioRules);
        }

        /// <summary>
        /// Gets an SFX volume after applying saved extra audio rules for character, showname, player, or token matches.
        /// </summary>
        public static double ResolveSfxVolume(string? characterName, string? showname, string? playerName, string? sfxToken)
        {
            return ResolveSfxVolume(characterName, showname, playerName, sfxToken, SaveFile.Data.ExtraAudioRules);
        }

        /// <summary>
        /// Gets a music volume after applying saved music token extra audio rules.
        /// </summary>
        public static double ResolveMusicVolume(string? musicToken)
        {
            return ResolveTokenVolume(ExtraAudioRuleKind.Music, ExtraAudioRuleTarget.Any, musicToken, MusicVolume, SaveFile.Data.ExtraAudioRules);
        }

        /// <summary>
        /// Scales a base embedded-sound volume by the persisted SFX slider.
        /// </summary>
        public static float ScaleEmbeddedSfxVolume(float baseVolume)
        {
            return (float)Math.Clamp(baseVolume * SfxVolume, 0.0, 1.0);
        }

        /// <summary>
        /// Converts a 0-100 slider value to a 0-1 scalar.
        /// </summary>
        public static double PercentToScalar(double percent)
        {
            return Math.Clamp(percent / 100.0, 0.0, 1.0);
        }

        /// <summary>
        /// Converts a 0-1 scalar to a 0-100 slider value.
        /// </summary>
        public static double ScalarToPercent(double scalar)
        {
            return Math.Clamp(scalar, 0.0, 1.0) * 100.0;
        }

        private static bool RuleMatches(
            ExtraAudioRule rule,
            string? characterName,
            string? showname,
            string? playerName,
            string? blipToken)
        {
            string match = rule.Match?.Trim() ?? string.Empty;
            return rule.Target switch
            {
                ExtraAudioRuleTarget.Any => true,
                ExtraAudioRuleTarget.Character => Contains(characterName, match, rule.IsCaseSensitive),
                ExtraAudioRuleTarget.Showname => Contains(showname, match, rule.IsCaseSensitive),
                ExtraAudioRuleTarget.Player => Contains(playerName, match, rule.IsCaseSensitive),
                ExtraAudioRuleTarget.Blip => BlipMatches(blipToken, match, rule.IsCaseSensitive),
                ExtraAudioRuleTarget.Sfx => TokenMatches(blipToken, match, rule.IsCaseSensitive),
                _ => false
            };
        }

        internal static double ResolveSfxVolume(
            string? characterName,
            string? showname,
            string? playerName,
            string? sfxToken,
            IEnumerable<ExtraAudioRule>? rules)
        {
            double volume = SfxVolume;
            foreach (ExtraAudioRule rule in rules ?? Array.Empty<ExtraAudioRule>())
            {
                if (!rule.IsEnabled
                    || rule.Kind != ExtraAudioRuleKind.Sfx
                    || !RuleMatches(rule, characterName, showname, playerName, sfxToken))
                {
                    continue;
                }

                volume = Math.Max(0, rule.VolumePercent) / 100.0;
            }

            return Math.Max(0.0, volume);
        }

        private static double ResolveTokenVolume(
            ExtraAudioRuleKind kind,
            ExtraAudioRuleTarget target,
            string? token,
            double fallbackVolume,
            IEnumerable<ExtraAudioRule>? rules)
        {
            double volume = fallbackVolume;
            foreach (ExtraAudioRule rule in rules ?? Array.Empty<ExtraAudioRule>())
            {
                if (!rule.IsEnabled || rule.Kind != kind)
                {
                    continue;
                }

                bool matches = kind == ExtraAudioRuleKind.Music
                    ? TokenMatches(token, rule.Match, rule.IsCaseSensitive)
                    : rule.Target == target && TokenMatches(token, rule.Match, rule.IsCaseSensitive);
                if (matches)
                {
                    volume = Math.Max(0, rule.VolumePercent) / 100.0;
                }
            }

            return Math.Max(0.0, volume);
        }

        private static bool Contains(string? value, string match, bool caseSensitive)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(match)
                && value.IndexOf(match, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool BlipMatches(string? value, string match, bool caseSensitive)
        {
            return TokenMatches(value, match, caseSensitive);
        }

        private static bool TokenMatches(string? value, string match, bool caseSensitive)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(match))
            {
                return false;
            }

            string normalized = value.Trim().Replace('\\', '/');
            string fileName = Path.GetFileName(normalized);
            string withoutExtension = Path.GetFileNameWithoutExtension(normalized);
            StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return string.Equals(normalized, match, comparison)
                || string.Equals(fileName, match, comparison)
                || string.Equals(withoutExtension, match, comparison);
        }
    }
}
