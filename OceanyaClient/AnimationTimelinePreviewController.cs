using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OceanyaClient.Utilities
{
    /// <summary>
    /// Frame-accurate animation playback controller used by <see cref="AssetImageViewerDialog"/>
    /// and the character creator's image asset viewer to preview animated sprites on a timeline.
    /// </summary>
    internal sealed class AnimationTimelinePreviewController : IDisposable
    {
        private readonly List<ImageSource> frames;
        private readonly List<double> frameStartMs;
        private readonly List<double> frameDurationMs;
        private readonly DispatcherTimer timer;
        private bool disposed;
        private bool loop = true;
        private bool isPlaying;
        private double currentPositionMs;
        private double? cutDurationMs;
        private DateTime playStartedUtc;

        /// <summary>Raised each tick with the current frame and position in milliseconds.</summary>
        public event Action<ImageSource, double>? PositionChanged;

        /// <summary>Raised when playback starts or stops.</summary>
        public event Action<bool>? PlaybackStateChanged;

        /// <summary>Gets the frame that should be displayed at the current playback position.</summary>
        public ImageSource CurrentFrame => frames[Math.Clamp(GetFrameIndexFromPosition(currentPositionMs), 0, frames.Count - 1)];

        /// <summary>Gets whether the controller is currently playing.</summary>
        public bool IsPlaying => isPlaying;

        /// <summary>Gets whether this animation has more than one frame.</summary>
        public bool HasTimeline => frames.Count > 1;

        /// <summary>Gets the current playback position in milliseconds.</summary>
        public double CurrentPositionMs => currentPositionMs;

        /// <summary>Gets the effective duration in milliseconds, respecting any cut point.</summary>
        public double EffectiveDurationMs => cutDurationMs.HasValue
            ? Math.Min(cutDurationMs.Value, TotalDurationMs)
            : TotalDurationMs;

        private double TotalDurationMs => frameDurationMs.Sum();

        private AnimationTimelinePreviewController(List<ImageSource> frames, List<double> frameDurationMs)
        {
            this.frames = frames;
            this.frameDurationMs = frameDurationMs;
            frameStartMs = new List<double>(frameDurationMs.Count);
            double cumulative = 0;
            foreach (double frameLength in frameDurationMs)
            {
                frameStartMs.Add(cumulative);
                cumulative += frameLength;
            }

            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };
            timer.Tick += Timer_Tick;
            RaisePositionChanged();
        }

        /// <summary>Tries to create a controller for the given animation file path.</summary>
        public static bool TryCreate(string? path, [NotNullWhen(true)] out AnimationTimelinePreviewController? controller)
        {
            controller = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase)
                    && TryCreateGifTimeline(path, out AnimationTimelinePreviewController? gifController))
                {
                    controller = gifController;
                    return true;
                }

                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(path, UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return false;
                }

                List<ImageSource> decodedFrames = new List<ImageSource>(decoder.Frames.Count);
                List<double> decodedDurations = new List<double>(decoder.Frames.Count);
                foreach (BitmapFrame frame in decoder.Frames)
                {
                    BitmapSource source = frame;
                    if (source.CanFreeze)
                    {
                        source.Freeze();
                    }

                    decodedFrames.Add(source);
                    decodedDurations.Add(ReadDelay(frame.Metadata));
                }

                controller = new AnimationTimelinePreviewController(decodedFrames, decodedDurations);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCreateGifTimeline(string gifPath, [NotNullWhen(true)] out AnimationTimelinePreviewController? controller)
        {
            controller = null;
            try
            {
                using System.Drawing.Image gifImage = System.Drawing.Image.FromFile(gifPath);
                int frameCount = gifImage.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
                if (frameCount <= 0)
                {
                    return false;
                }

                List<double> durations = ReadGifFrameDelays(gifImage, frameCount);
                List<ImageSource> decodedFrames = new List<ImageSource>(frameCount);
                for (int i = 0; i < frameCount; i++)
                {
                    gifImage.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time, i);
                    using System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(
                        gifImage.Width,
                        gifImage.Height,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(System.Drawing.Color.Transparent);
                        graphics.DrawImage(gifImage, 0, 0, gifImage.Width, gifImage.Height);
                    }

                    IntPtr hBitmap = bitmap.GetHbitmap();
                    try
                    {
                        BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        if (source.CanFreeze)
                        {
                            source.Freeze();
                        }

                        decodedFrames.Add(source);
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }

                controller = new AnimationTimelinePreviewController(decodedFrames, durations);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<double> ReadGifFrameDelays(System.Drawing.Image image, int frameCount)
        {
            List<double> delays = new List<double>(frameCount);
            const int PropertyTagFrameDelay = 0x5100;
            try
            {
                System.Drawing.Imaging.PropertyItem? property = image.PropertyItems.FirstOrDefault(item => item.Id == PropertyTagFrameDelay);
                if (property?.Value != null && property.Value.Length >= frameCount * 4)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        int delayUnits = BitConverter.ToInt32(property.Value, i * 4);
                        double milliseconds = Math.Max(20, delayUnits * 10d);
                        delays.Add(milliseconds);
                    }
                }
            }
            catch
            {
                // ignored
            }

            while (delays.Count < frameCount)
            {
                delays.Add(90);
            }

            return delays;
        }

        /// <summary>Starts playback from the current position.</summary>
        public void Play()
        {
            if (disposed || !HasTimeline)
            {
                return;
            }

            if (currentPositionMs >= EffectiveDurationMs)
            {
                currentPositionMs = 0;
                RaisePositionChanged();
            }

            playStartedUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Math.Max(0, currentPositionMs));
            isPlaying = true;
            timer.Start();
            PlaybackStateChanged?.Invoke(true);
        }

        /// <summary>Pauses playback without resetting position.</summary>
        public void Pause()
        {
            if (disposed || !isPlaying)
            {
                return;
            }

            isPlaying = false;
            timer.Stop();
            PlaybackStateChanged?.Invoke(false);
        }

        /// <summary>Seeks to the specified position in milliseconds.</summary>
        public void Seek(double positionMs)
        {
            if (disposed)
            {
                return;
            }

            currentPositionMs = Math.Clamp(positionMs, 0, Math.Max(0, EffectiveDurationMs));
            if (isPlaying)
            {
                playStartedUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(currentPositionMs);
            }

            RaisePositionChanged();
        }

        /// <summary>Steps forward or backward by the given number of frames.</summary>
        public void StepFrame(int frameDelta)
        {
            if (disposed || !HasTimeline)
            {
                return;
            }

            int index = Math.Clamp(GetFrameIndexFromPosition(currentPositionMs) + frameDelta, 0, frames.Count - 1);
            Seek(frameStartMs[Math.Clamp(index, 0, frameStartMs.Count - 1)]);
        }

        /// <summary>Sets whether playback loops when it reaches the end.</summary>
        public void SetLoop(bool shouldLoop)
        {
            loop = shouldLoop;
        }

        /// <summary>Sets an optional cut duration that caps the effective animation length.</summary>
        public void SetCutoffDurationMs(int? durationMs)
        {
            cutDurationMs = durationMs.HasValue && durationMs.Value > 0
                ? durationMs.Value
                : null;
            if (currentPositionMs > EffectiveDurationMs)
            {
                currentPositionMs = EffectiveDurationMs;
            }

            RaisePositionChanged();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (disposed || !isPlaying)
            {
                return;
            }

            double elapsedMs = (DateTime.UtcNow - playStartedUtc).TotalMilliseconds;
            double durationMs = Math.Max(1, EffectiveDurationMs);
            if (loop && durationMs > 0)
            {
                currentPositionMs = elapsedMs % durationMs;
                RaisePositionChanged();
                return;
            }

            currentPositionMs = Math.Clamp(elapsedMs, 0, durationMs);
            RaisePositionChanged();
            if (elapsedMs >= durationMs)
            {
                Pause();
            }
        }

        private void RaisePositionChanged()
        {
            PositionChanged?.Invoke(CurrentFrame, currentPositionMs);
        }

        private int GetFrameIndexFromPosition(double positionMs)
        {
            double clamped = Math.Clamp(positionMs, 0, Math.Max(0, EffectiveDurationMs));
            for (int i = frameStartMs.Count - 1; i >= 0; i--)
            {
                if (clamped >= frameStartMs[i])
                {
                    return i;
                }
            }

            return 0;
        }

        private static double ReadDelay(ImageMetadata? metadata)
        {
            try
            {
                if (metadata is BitmapMetadata bitmapMetadata && bitmapMetadata.ContainsQuery("/grctlext/Delay"))
                {
                    object query = bitmapMetadata.GetQuery("/grctlext/Delay");
                    if (query is ushort delayValue)
                    {
                        return Math.Max(20, delayValue * 10d);
                    }
                }
            }
            catch
            {
                // ignored
            }

            return 90;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            timer.Stop();
            timer.Tick -= Timer_Tick;
            PositionChanged = null;
            PlaybackStateChanged = null;
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
