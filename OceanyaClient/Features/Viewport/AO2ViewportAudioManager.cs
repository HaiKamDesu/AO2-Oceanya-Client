using System;
using System.Collections.Generic;
using Common;
using ManagedBass;
using OceanyaClient;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Owns viewport-local music, SFX, and blip playback.
    /// </summary>
    internal sealed class AO2ViewportAudioManager : IDisposable
    {
        private const int FadeOutDurationMs = 4000;
        private const int FadeInDurationMs = 1000;

        private readonly AO2BlipPreviewPlayer musicPlayer = new AO2BlipPreviewPlayer(streamCount: 1);
        private readonly AO2BlipPreviewPlayer sfxPlayer = new AO2BlipPreviewPlayer();
        private readonly AO2BlipPreviewPlayer effectSfxPlayer = new AO2BlipPreviewPlayer();
        private readonly AO2BlipPreviewPlayer shoutSfxPlayer = new AO2BlipPreviewPlayer();
        private readonly AO2BlipPreviewPlayer blipPlayer = new AO2BlipPreviewPlayer();
        private readonly Dictionary<int, AO2BlipPreviewPlayer> ambientPlayers = new Dictionary<int, AO2BlipPreviewPlayer>();
        private readonly Dictionary<int, string> ambientCurrentTokens = new Dictionary<int, string>();
        private string currentMusicPath = string.Empty;
        private bool currentMusicLoop = true;
        private string currentSfxPath = string.Empty;
        private string currentEffectSfxPath = string.Empty;
        private string currentShoutSfxPath = string.Empty;
        private string currentBlipPath = string.Empty;
        private string currentBlipCharacterName = string.Empty;
        private string currentBlipShowname = string.Empty;
        private int fadingMusicStream;
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
            foreach (KeyValuePair<int, AO2BlipPreviewPlayer> entry in ambientPlayers)
            {
                string? token = ambientCurrentTokens.TryGetValue(entry.Key, out string? t) ? t : null;
                entry.Value.Volume = (float)AudioSettings.ResolveMusicVolume(token);
            }
        }

        /// <summary>
        /// Stops all active viewport audio including music, cancelling any in-progress fade.
        /// </summary>
        public void StopAll()
        {
            musicPlayer.Stop();
            StopAndFreeFadingMusicStream();
            currentMusicPath = string.Empty;
            currentMusicLoop = true;
            sfxPlayer.Stop();
            effectSfxPlayer.Stop();
            shoutSfxPlayer.Stop();
            blipPlayer.Stop();
            StopAllAmbient();
        }

        /// <summary>
        /// Stops SFX, effect, shout, and blip players but leaves music playing.
        /// </summary>
        public void StopSfxAndBlips()
        {
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
        /// Plays an AO2-style music token, applying the effect flags from the MC# packet.
        /// effectFlags bits: FADE_IN=1, FADE_OUT=2, SYNC_POS=4.
        /// </summary>
        public void PlayMusic(string? token, bool loop = true, int effectFlags = 0, string? serverAssetUrl = null)
        {
            string? path = AO2ViewportAudioResolver.ResolveMusicPath(token, serverAssetUrl);
            bool fadeOut = (effectFlags & 2) != 0;
            bool fadeIn = (effectFlags & 1) != 0;
            bool synchronize = (effectFlags & 4) != 0;

            if (!string.IsNullOrWhiteSpace(token))
            {
                if (AO2ViewportAudioResolver.IsStreamingUrl(token))
                    CustomConsole.Info($"[AUDIO] URL stream: {token}", Common.CustomConsole.LogCategory.MusicList);
                else if (!string.IsNullOrWhiteSpace(path))
                    CustomConsole.Info(AO2ViewportAudioResolver.IsStreamingUrl(path)
                        ? $"[AUDIO] Server asset stream: token={token} -> {path}"
                        : $"[AUDIO] Local file resolved: token={token} -> {path}", Common.CustomConsole.LogCategory.MusicList);
                else
                    CustomConsole.Warning($"[AUDIO] Token not found locally: {token}", category: Common.CustomConsole.LogCategory.MusicList);
            }

            long synchronizedPosition = synchronize ? musicPlayer.GetFirstStreamPosition() : 0;

            // Free any previously fading stream that has already stopped naturally.
            CleanupFadingMusicStreamIfStopped();

            if (fadeOut)
            {
                // Detach current stream and let it fade out independently.
                // BASS slides the volume to 0 then stops the channel automatically.
                StopAndFreeFadingMusicStream();
                int oldStream = musicPlayer.TakeFirstActiveStream();
                if (oldStream != 0)
                {
                    _ = Bass.ChannelSlideAttribute(oldStream, ChannelAttribute.Volume, -1f, FadeOutDurationMs);
                    fadingMusicStream = oldStream;
                }
            }
            else
            {
                musicPlayer.Stop();
                StopAndFreeFadingMusicStream();
            }

            currentMusicPath = string.Empty;
            currentMusicLoop = loop;

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!musicPlayer.TrySetBlip(path, loop: loop))
            {
                return;
            }

            // Apply loop region from sidecar .txt if present (AO2 parity: song.mp3.txt).
            var loopSidecar = AO2ViewportAudioResolver.ParseMusicLoopSidecar(path);
            if (loopSidecar.HasValue)
            {
                long startBytes = musicPlayer.ConvertLoopValueToBytes(loopSidecar.Value.IsSeconds, loopSidecar.Value.LoopStart);
                long endBytes = musicPlayer.ConvertLoopValueToBytes(loopSidecar.Value.IsSeconds, loopSidecar.Value.LoopEnd);
                if (startBytes >= 0 && endBytes > startBytes)
                {
                    musicPlayer.ApplyLoopRegion(startBytes, endBytes);
                }
            }

            if (synchronizedPosition > 0)
            {
                musicPlayer.SetStreamPosition(synchronizedPosition);
            }

            currentMusicPath = path;

            float targetVolume = (float)AudioSettings.ResolveMusicVolume(token);
            if (fadeIn)
            {
                musicPlayer.FadeInPlay(targetVolume, FadeInDurationMs);
            }
            else
            {
                musicPlayer.Volume = targetVolume;
                _ = musicPlayer.PlayBlip();
            }
        }

        /// <summary>
        /// Stops music playback, optionally fading out over 4 seconds.
        /// effectFlags bits: FADE_OUT=2.
        /// </summary>
        public void StopMusic(int effectFlags = 0)
        {
            bool fadeOut = (effectFlags & 2) != 0;
            CleanupFadingMusicStreamIfStopped();

            if (fadeOut)
            {
                StopAndFreeFadingMusicStream();
                int oldStream = musicPlayer.TakeFirstActiveStream();
                if (oldStream != 0)
                {
                    _ = Bass.ChannelSlideAttribute(oldStream, ChannelAttribute.Volume, -1f, FadeOutDurationMs);
                    fadingMusicStream = oldStream;
                }
            }
            else
            {
                musicPlayer.Stop();
                StopAndFreeFadingMusicStream();
            }

            currentMusicPath = string.Empty;
            currentMusicLoop = true;
        }

        /// <summary>
        /// Plays an AO2 courtroom SFX identified by its courtroom_sounds.ini key (e.g. "testimony1").
        /// Falls back to resolving the key directly as an SFX token when no ini entry is found.
        /// </summary>
        public void PlayCourtSfx(string key)
        {
            string? path = AO2ViewportAudioResolver.ResolveCourtSfxPath(key);
            PlayResolvedSfx(path, key, null, null, null);
        }

        /// <summary>
        /// Plays ambient music on the given channel (1+).
        /// Passing a null or empty song path stops the channel.
        /// AO2 parity: MC# channel field &gt; 0 routes to ambient layers, independent of channel 0.
        /// </summary>
        public void PlayAmbientMusic(int channel, string? songPath, bool loop, string? serverAssetUrl = null)
        {
            if (channel <= 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(songPath))
            {
                StopAmbientChannel(channel);
                return;
            }

            string? path = AO2ViewportAudioResolver.ResolveMusicPath(songPath, serverAssetUrl);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!ambientPlayers.TryGetValue(channel, out AO2BlipPreviewPlayer? player))
            {
                player = new AO2BlipPreviewPlayer(streamCount: 1);
                ambientPlayers[channel] = player;
            }

            player.Stop();
            if (!player.TrySetBlip(path, loop: loop))
            {
                return;
            }

            ambientCurrentTokens[channel] = songPath;
            player.Volume = (float)AudioSettings.ResolveMusicVolume(songPath);
            _ = player.PlayBlip();
        }

        /// <summary>
        /// Stops playback on the given ambient channel.
        /// </summary>
        public void StopAmbientChannel(int channel)
        {
            if (ambientPlayers.TryGetValue(channel, out AO2BlipPreviewPlayer? player))
            {
                player.Stop();
            }

            ambientCurrentTokens.Remove(channel);
        }

        /// <summary>
        /// Stops all ambient channels.
        /// </summary>
        public void StopAllAmbient()
        {
            foreach (AO2BlipPreviewPlayer player in ambientPlayers.Values)
            {
                player.Stop();
            }

            ambientCurrentTokens.Clear();
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
            foreach (AO2BlipPreviewPlayer player in ambientPlayers.Values)
            {
                player.Dispose();
            }

            ambientPlayers.Clear();
        }

        private void CleanupFadingMusicStreamIfStopped()
        {
            if (fadingMusicStream == 0)
            {
                return;
            }

            if (Bass.ChannelIsActive(fadingMusicStream) == PlaybackState.Stopped)
            {
                Bass.StreamFree(fadingMusicStream);
                fadingMusicStream = 0;
            }
        }

        private void StopAndFreeFadingMusicStream()
        {
            if (fadingMusicStream == 0)
            {
                return;
            }

            Bass.ChannelStop(fadingMusicStream);
            Bass.StreamFree(fadingMusicStream);
            fadingMusicStream = 0;
        }
    }
}
