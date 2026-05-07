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
        private readonly AO2BlipPreviewPlayer effectSfxPlayer = new AO2BlipPreviewPlayer();
        private readonly AO2BlipPreviewPlayer shoutSfxPlayer = new AO2BlipPreviewPlayer();
        private readonly AO2BlipPreviewPlayer blipPlayer = new AO2BlipPreviewPlayer();
        private string currentMusicPath = string.Empty;
        private string currentSfxPath = string.Empty;
        private string currentEffectSfxPath = string.Empty;
        private string currentShoutSfxPath = string.Empty;
        private string currentBlipPath = string.Empty;
        private string currentBlipCharacterName = string.Empty;
        private string currentBlipShowname = string.Empty;
        private bool disposed;

        /// <summary>
        /// Applies the current saved volume settings to the active players.
        /// </summary>
        public void RefreshVolumes()
        {
            musicPlayer.Volume = (float)AudioSettings.MusicVolume;
            sfxPlayer.Volume = (float)AudioSettings.SfxVolume;
            effectSfxPlayer.Volume = (float)AudioSettings.SfxVolume;
            shoutSfxPlayer.Volume = (float)AudioSettings.SfxVolume;
            blipPlayer.Volume = (float)AudioSettings.BlipVolume;
        }

        /// <summary>
        /// Stops any active viewport audio.
        /// </summary>
        public void StopAll()
        {
            musicPlayer.Stop();
            sfxPlayer.Stop();
            effectSfxPlayer.Stop();
            shoutSfxPlayer.Stop();
            blipPlayer.Stop();
        }

        /// <summary>
        /// Preloads a blip sound for the next text reveal.
        /// </summary>
        public void PrepareBlip(string? token, string? characterName = null, string? showname = null)
        {
            string? path = AO2ViewportAudioResolver.ResolveBlipPath(token);
            if (string.IsNullOrWhiteSpace(path))
            {
                currentBlipPath = string.Empty;
                currentBlipCharacterName = string.Empty;
                currentBlipShowname = string.Empty;
                return;
            }

            currentBlipCharacterName = characterName?.Trim() ?? string.Empty;
            currentBlipShowname = showname?.Trim() ?? string.Empty;
            if (string.Equals(currentBlipPath, path, StringComparison.OrdinalIgnoreCase))
            {
                blipPlayer.Volume = (float)AudioSettings.ResolveBlipVolume(
                    currentBlipCharacterName,
                    currentBlipShowname,
                    null,
                    token);
                return;
            }

            if (blipPlayer.TrySetBlip(path))
            {
                currentBlipPath = path;
                blipPlayer.Volume = (float)AudioSettings.ResolveBlipVolume(
                    currentBlipCharacterName,
                    currentBlipShowname,
                    null,
                    token);
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

            _ = blipPlayer.PlayBlip();
        }

        /// <summary>
        /// Plays an AO2-style SFX token immediately.
        /// </summary>
        public void PlaySfx(string? token, string? characterName = null, string? showname = null, string? playerName = null)
        {
            string? path = AO2ViewportAudioResolver.ResolveSfxPath(token);
            PlayResolvedSfx(path, token, characterName, showname, playerName);
        }

        /// <summary>
        /// Plays an AO2 character shout SFX token immediately.
        /// </summary>
        public void PlayShoutSfx(string? token, string? characterName, string? miscName, string? showname = null, string? playerName = null)
        {
            string? path = AO2ViewportAudioResolver.ResolveCharacterShoutPath(token, characterName, miscName);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!string.Equals(currentShoutSfxPath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (!shoutSfxPlayer.TrySetBlip(path))
                {
                    return;
                }

                currentShoutSfxPath = path;
            }

            shoutSfxPlayer.Volume = (float)AudioSettings.ResolveSfxVolume(characterName, showname, playerName, token);
            _ = shoutSfxPlayer.PlayBlip();
        }

        private void PlayResolvedSfx(string? path, string? token, string? characterName, string? showname, string? playerName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!string.Equals(currentSfxPath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (!sfxPlayer.TrySetBlip(path))
                {
                    return;
                }

                currentSfxPath = path;
            }

            sfxPlayer.Volume = (float)AudioSettings.ResolveSfxVolume(characterName, showname, playerName, token);
            _ = sfxPlayer.PlayBlip();
        }

        /// <summary>
        /// Plays an AO2-style effect SFX token on a dedicated player that won't be
        /// interrupted by regular emote SFX playback.
        /// </summary>
        public void PlayEffectSfx(string? token, string? characterName = null, string? showname = null, string? playerName = null)
        {
            string? path = AO2ViewportAudioResolver.ResolveSfxPath(token);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!string.Equals(currentEffectSfxPath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (!effectSfxPlayer.TrySetBlip(path))
                {
                    return;
                }

                currentEffectSfxPath = path;
            }

            effectSfxPlayer.Volume = (float)AudioSettings.ResolveSfxVolume(characterName, showname, playerName, token);
            _ = effectSfxPlayer.PlayBlip();
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

            musicPlayer.Volume = (float)AudioSettings.ResolveMusicVolume(token);
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
            effectSfxPlayer.Dispose();
            shoutSfxPlayer.Dispose();
            blipPlayer.Dispose();
        }
    }
}
