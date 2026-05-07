using System;
using System.IO;
using Common;
using OceanyaClient.Features.Viewport;

namespace OceanyaClient
{
    /// <summary>
    /// Plays saved callword notification rules for incoming chat messages.
    /// </summary>
    internal sealed class CallwordAudioNotifier : IDisposable
    {
        private readonly AO2BlipPreviewPlayer player = new AO2BlipPreviewPlayer();
        private string currentPath = string.Empty;

        public void TryNotify(string message)
        {
            string text = message ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            foreach (CallwordRule rule in SaveFile.Data.CallwordRules)
            {
                if (!rule.IsEnabled
                    || string.IsNullOrWhiteSpace(rule.Word)
                    || text.IndexOf(rule.Word.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                PlayRule(rule);
                return;
            }
        }

        private void PlayRule(CallwordRule rule)
        {
            string? path = ResolveRulePath(rule);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (!player.TrySetBlip(path))
                {
                    return;
                }

                currentPath = path;
            }

            player.Volume = (float)AudioSettings.SfxVolume;
            _ = player.PlayBlip();
        }

        private static string? ResolveRulePath(CallwordRule rule)
        {
            string customPath = rule.SoundPath?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                return customPath;
            }

            return AO2ViewportAudioResolver.ResolveSfxPath("word_call")
                ?? AO2ViewportAudioResolver.ResolveSfxPath("sfx-word_call")
                ?? AO2ViewportAudioResolver.ResolveSfxPath("modcall");
        }

        public void Dispose()
        {
            player.Dispose();
        }
    }
}
