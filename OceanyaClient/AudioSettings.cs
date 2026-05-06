using System;
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
    }
}
