using System;
using System.Collections.Generic;
using System.Windows.Media;
using Common;
using OceanyaClient.Components;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Plays a theme's animated button artwork, the way AO2 does.
    /// </summary>
    /// <remarks>
    /// AO2 loads every piece of button art through a <c>QMovie</c>, not a still pixmap:
    /// <c>AOButton::setImage</c> starts one whenever <c>animated_theme</c> is on, and repaints the button on
    /// each <c>frameChanged</c>. Themes rely on it - AAI ships <c>holdit.gif</c>, <c>objection.gif</c> and
    /// <c>takethat.gif</c> alongside static PNGs, and because AO2 probes <c>.webp/.apng/.gif/.png</c> in that
    /// order the animated file is the one it picks. Loading only the first frame, as we did, quietly turned
    /// every animated shout in every theme into a still.
    ///
    /// Frames are pushed through a caller-supplied setter so this works for a plain image face, a shout
    /// panel's dependency properties, or anything else a panel uses to show its art.
    /// </remarks>
    internal sealed class Ao2ThemeArtAnimation
    {
        private readonly IAnimationPlayer player;
        private readonly Action<ImageSource> apply;

        private Ao2ThemeArtAnimation(IAnimationPlayer player, Action<ImageSource> apply)
        {
            this.player = player;
            this.apply = apply;
            player.FrameChanged += OnFrameChanged;
        }

        /// <summary>
        /// Starts playing <paramref name="path"/> into <paramref name="apply"/>, if it is animated at all.
        /// </summary>
        /// <param name="path">Resolved artwork path.</param>
        /// <param name="apply">Receives each frame, starting with the first.</param>
        /// <returns>The running animation, or <c>null</c> for a still image.</returns>
        public static Ao2ThemeArtAnimation? TryStart(string path, Action<ImageSource> apply)
        {
            if (string.IsNullOrWhiteSpace(path) || apply == null)
            {
                return null;
            }

            // A still image is the common case and costs nothing to rule out first.
            if (!Ao2AnimationPreview.IsPotentialAnimatedPath(path))
            {
                return null;
            }

            try
            {
                // Button art is small and on screen the whole session, so it is decoded at its own size
                // rather than the preview cap - a shout button is nowhere near the 360px preview limit.
                if (!Ao2AnimationPreview.TryCreateAnimationPlayer(path, loop: true, out IAnimationPlayer? player)
                    || player == null)
                {
                    return null;
                }

                Ao2ThemeArtAnimation animation = new Ao2ThemeArtAnimation(player, apply);
                apply(player.CurrentFrame);
                return animation;
            }
            catch (Exception exception)
            {
                CustomConsole.Warning($"Animated theme art could not be started: {path}", exception);
                return null;
            }
        }

        /// <summary>
        /// Stops playback and releases the player's frames.
        /// </summary>
        public void Stop()
        {
            player.FrameChanged -= OnFrameChanged;
            try
            {
                player.Stop();
            }
            catch (Exception exception)
            {
                CustomConsole.Warning("Animated theme art could not be stopped.", exception);
            }
        }

        private void OnFrameChanged(ImageSource frame)
        {
            if (frame != null)
            {
                apply(frame);
            }
        }
    }

    /// <summary>
    /// Owns every animation a theme's artwork started, so re-applying a layout never leaks one.
    /// </summary>
    /// <remarks>
    /// A live animation holds its decoded frames for as long as it runs, and panel art is re-applied on
    /// every layout pass, theme import and reset. Without one place to stop the previous set, each pass
    /// would leave the old players ticking and retaining frames - the leak the viewport already learned
    /// about the hard way (see the memory notes in AGENTS.md).
    /// </remarks>
    internal static class Ao2ThemeArtAnimations
    {
        private static readonly List<Ao2ThemeArtAnimation> Running = new List<Ao2ThemeArtAnimation>();

        /// <summary>Registers a started animation so it can be stopped with the rest.</summary>
        public static void Track(Ao2ThemeArtAnimation? animation)
        {
            if (animation != null)
            {
                Running.Add(animation);
            }
        }

        /// <summary>Stops every running theme animation.</summary>
        public static void StopAll()
        {
            foreach (Ao2ThemeArtAnimation animation in Running)
            {
                animation.Stop();
            }

            Running.Clear();
        }
    }
}
