using System;
using OceanyaClient;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Owns viewport-local music, SFX, and blip playback.
    /// </summary>
    internal sealed class AO2ViewportAudioManager : IDisposable
    {
        private readonly AO2BlipPreviewPlayer musicPlayer = new AO2BlipPreviewPlayer();
        private readonly AO2BlipPreviewPlayer sfxPlayer = new AO2BlipPreviewPlayer();
        private readonly AO2BlipPreviewPlayer blipPlayer = new AO2BlipPreviewPlayer();
        private string currentMusicPath = string.Empty;
        private string currentSfxPath = string.Empty;
        private string currentBlipPath = string.Empty;
        private bool disposed;

        /// <summary>
        /// Applies the current saved volume settings to the active players.
        /// </summary>
        public void RefreshVolumes()
        {
            musicPlayer.Volume = (float)AudioSettings.MusicVolume;
            sfxPlayer.Volume = (float)AudioSettings.SfxVolume;
            blipPlayer.Volume = (float)AudioSettings.BlipVolume;
        }

        /// <summary>
        /// Stops any active viewport audio.
        /// </summary>
        public void StopAll()
        {
            musicPlayer.Stop();
            sfxPlayer.Stop();
            blipPlayer.Stop();
        }

        /// <summary>
        /// Preloads a blip sound for the next text reveal.
        /// </summary>
        public void PrepareBlip(string? token)
        {
            string? path = AO2ViewportAudioResolver.ResolveBlipPath(token);
            if (string.IsNullOrWhiteSpace(path))
            {
                currentBlipPath = string.Empty;
                return;
            }

            if (string.Equals(currentBlipPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (blipPlayer.TrySetBlip(path))
            {
                currentBlipPath = path;
                blipPlayer.Volume = (float)AudioSettings.BlipVolume;
                return;
            }

            currentBlipPath = string.Empty;
        }

        /// <summary>
        /// Plays the prepared blip sound once.
        /// </summary>
        public void PlayBlip()
        {
            if (string.IsNullOrWhiteSpace(currentBlipPath))
            {
                return;
            }

            blipPlayer.Volume = (float)AudioSettings.BlipVolume;
            _ = blipPlayer.PlayBlip();
        }

        /// <summary>
        /// Plays an AO2-style SFX token immediately.
        /// </summary>
        public void PlaySfx(string? token)
        {
            string? path = AO2ViewportAudioResolver.ResolveSfxPath(token);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!string.Equals(currentSfxPath, path, StringComparison.OrdinalIgnoreCase))
            {
                currentSfxPath = path;
                if (!sfxPlayer.TrySetBlip(path))
                {
                    return;
                }
            }

            sfxPlayer.Volume = (float)AudioSettings.SfxVolume;
            _ = sfxPlayer.PlayBlip();
        }

        /// <summary>
        /// Plays an AO2-style music token.
        /// </summary>
        public void PlayMusic(string? token)
        {
            string? path = AO2ViewportAudioResolver.ResolveMusicPath(token);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!string.Equals(currentMusicPath, path, StringComparison.OrdinalIgnoreCase))
            {
                musicPlayer.Stop();
                currentMusicPath = path;
                if (!musicPlayer.TrySetBlip(path))
                {
                    return;
                }
            }

            musicPlayer.Volume = (float)AudioSettings.MusicVolume;
            _ = musicPlayer.PlayBlip();
        }

        /// <summary>
        /// Stops music playback only.
        /// </summary>
        public void StopMusic()
        {
            musicPlayer.Stop();
            currentMusicPath = string.Empty;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopAll();
            musicPlayer.Dispose();
            sfxPlayer.Dispose();
            blipPlayer.Dispose();
        }
    }
}
