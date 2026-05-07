using System;
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
        /// Gets the persisted music volume as a 0-1 scalar.
        /// </summary>
        public static double MusicVolume => Math.Clamp(SaveFile.Data.AudioMusicVolume, 0.0, 1.0);

        /// <summary>
        /// Gets the persisted SFX volume as a 0-1 scalar.
        /// </summary>
        public static double SfxVolume => Math.Clamp(SaveFile.Data.AudioSfxVolume, 0.0, 1.0);

        /// <summary>
        /// Gets the persisted blip volume as a 0-1 scalar.
        /// </summary>
        public static double BlipVolume => Math.Clamp(SaveFile.Data.AudioBlipVolume, 0.0, 1.0);

        /// <summary>
        /// Gets a blip volume after applying saved extra audio rules.
        /// </summary>
        public static double ResolveBlipVolume(
            string? characterName,
            string? showname,
            string? playerName,
            string? blipToken)
        {
            double volume = BlipVolume;
            foreach (ExtraAudioRule rule in SaveFile.Data.ExtraAudioRules)
            {
                if (!rule.IsEnabled || rule.Kind != ExtraAudioRuleKind.Blip || !RuleMatches(rule, characterName, showname, playerName, blipToken))
                {
                    continue;
                }

                volume *= Math.Clamp(rule.VolumePercent, 0, 200) / 100.0;
            }

            return Math.Clamp(volume, 0.0, 1.0);
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
                ExtraAudioRuleTarget.Character => Contains(characterName, match),
                ExtraAudioRuleTarget.Showname => Contains(showname, match),
                ExtraAudioRuleTarget.Player => Contains(playerName, match),
                ExtraAudioRuleTarget.Blip => Contains(blipToken, match),
                _ => false
            };
        }

        private static bool Contains(string? value, string match)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(match)
                && value.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
