using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImage = System.Drawing.Image;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingFrameDimension = System.Drawing.Imaging.FrameDimension;

namespace OceanyaClient
{
    public interface IAnimationPlayer
    {
        event Action<ImageSource>? FrameChanged;
        ImageSource CurrentFrame { get; }
        void SetLoop(bool shouldLoop);
        void Restart();
        void Stop();
    }

    public static class Ao2AnimationPreview
    {
        private const string FallbackPackUri =
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png";

        private static readonly ImageSource FallbackImage = LoadEmbeddedFallback();

        public static bool IsPotentialAnimatedPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".gif" || extension == ".apng" || extension == ".webp";
        }

        public static string? ResolveAo2ImagePath(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return null;
            }

            string candidate = sourcePath.Trim();
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (!string.IsNullOrWhiteSpace(Path.GetExtension(candidate)))
            {
                return null;
            }

            string[] suffixOrder = { ".webp", ".apng", ".gif", ".png" };
            foreach (string suffix in suffixOrder)
            {
                string withSuffix = candidate + suffix;
                if (File.Exists(withSuffix))
                {
                    return withSuffix;
                }
            }

            return null;
        }

        public static ImageSource LoadStaticPreviewImage(string? path, int decodePixelWidth, ImageSource? fallback = null)
        {
            ImageSource selectedFallback = fallback ?? FallbackImage;
            string? resolvedPath = ResolveAo2ImagePath(path);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                return selectedFallback;
            }

            if (TryLoadFirstFrame(resolvedPath, out ImageSource? firstFrame, out _)
                && firstFrame != null
                && IsPotentialAnimatedPath(resolvedPath))
            {
                return firstFrame;
            }

            try
            {
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(resolvedPath, UriKind.Absolute);
                if (decodePixelWidth > 0)
                {
                    bitmapImage.DecodePixelWidth = decodePixelWidth;
                }

                bitmapImage.EndInit();
                if (bitmapImage.CanFreeze)
                {
                    bitmapImage.Freeze();
                }

                return bitmapImage;
            }
            catch
            {
                return selectedFallback;
            }
        }

        public static bool TryCreateAnimationPlayer(string? sourcePath, bool loop, out IAnimationPlayer? player)
        {
            player = null;
            string? resolvedPath = ResolveAo2ImagePath(sourcePath);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                return false;
            }

            string extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
            if (extension == ".gif")
            {
                try
                {
                    player = new GifAnimationPlayer(resolvedPath, loop);
                    return true;
                }
                catch
                {
                    // Fall back to decoder player below.
                }
            }

            if (BitmapFrameAnimationPlayer.TryCreate(resolvedPath, loop, out BitmapFrameAnimationPlayer? bitmapPlayer)
                && bitmapPlayer != null)
            {
                player = bitmapPlayer;
                return true;
            }

            return false;
        }

        public static bool TryLoadFirstFrame(string path, out ImageSource? initialFrame, out double estimatedDurationMs)
        {
            initialFrame = null;
            estimatedDurationMs = 0;
            string? resolvedPath = ResolveAo2ImagePath(path);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                return false;
            }

            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(resolvedPath, UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);

                BitmapFrame? frame = decoder.Frames.FirstOrDefault();
                if (frame == null)
                {
                    return false;
                }

                BitmapSource source = frame;
                if (source.CanFreeze)
                {
                    source.Freeze();
                }

                initialFrame = source;
                estimatedDurationMs = EstimateDecoderDurationMs(decoder);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static double EstimateDecoderDurationMs(BitmapDecoder decoder)
        {
            if (decoder.Frames.Count <= 1)
            {
                return 0;
            }

            double total = 0;
            foreach (BitmapFrame frame in decoder.Frames)
            {
                double delayMs = 90;
                try
                {
                    if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("/grctlext/Delay"))
                    {
                        object delayQuery = meta.GetQuery("/grctlext/Delay");
                        if (delayQuery is ushort delay)
                        {
                            delayMs = Math.Max(20, delay * 10d);
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                total += delayMs;
            }

            return total;
        }

        private static ImageSource LoadEmbeddedFallback()
        {
            try
            {
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(FallbackPackUri, UriKind.Absolute);
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
            catch
            {
                return new System.Windows.Media.DrawingImage();
            }
        }
    }

    public sealed class BitmapFrameAnimationPlayer : IAnimationPlayer
    {
        private static readonly object SharedTimerLock = new object();
        private static readonly List<BitmapFrameAnimationPlayer> ActivePlayers = new List<BitmapFrameAnimationPlayer>();
        private static DispatcherTimer? sharedTimer;
        private static readonly TimeSpan SharedTickInterval = TimeSpan.FromMilliseconds(15);

        private readonly List<BitmapSource> frames = new List<BitmapSource>();
        private readonly List<TimeSpan> frameDurations = new List<TimeSpan>();
        private int frameIndex;
        private bool loop;
        private bool endedWithoutLoop;
        private bool isRunning;
        private DateTime nextFrameAtUtc;

        public event Action<ImageSource>? FrameChanged;

        public ImageSource CurrentFrame => frames.Count > 0
            ? frames[Math.Clamp(frameIndex, 0, frames.Count - 1)]
            : new System.Windows.Media.DrawingImage();

        private BitmapFrameAnimationPlayer(bool loop)
        {
            this.loop = loop;
        }

        public static bool TryCreate(string path, bool loop, out BitmapFrameAnimationPlayer? player)
        {
            player = null;
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(path, UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count <= 1)
                {
                    return false;
                }

                BitmapFrameAnimationPlayer candidate = new BitmapFrameAnimationPlayer(loop);
                foreach (BitmapFrame frame in decoder.Frames)
                {
                    BitmapSource source = frame;
                    if (source.CanFreeze)
                    {
                        source.Freeze();
                    }

                    candidate.frames.Add(source);
                    candidate.frameDurations.Add(ReadDelay(frame.Metadata));
                }

                candidate.frameIndex = 0;
                candidate.StartPlayback();
                player = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SetLoop(bool shouldLoop)
        {
            loop = shouldLoop;
            if (loop && endedWithoutLoop)
            {
                endedWithoutLoop = false;
                frameIndex = 0;
                RaiseFrameChanged();
                StartPlayback();
            }
        }

        public void Restart()
        {
            if (frames.Count == 0)
            {
                return;
            }

            endedWithoutLoop = false;
            frameIndex = 0;
            RaiseFrameChanged();
            StartPlayback();
        }

        public void Stop()
        {
            StopPlayback();
            FrameChanged = null;
        }

        private void StartPlayback()
        {
            if (frames.Count == 0 || frameDurations.Count == 0)
            {
                return;
            }

            endedWithoutLoop = false;
            isRunning = true;
            nextFrameAtUtc = DateTime.UtcNow + frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            RegisterActivePlayer(this);
        }

        private void StopPlayback()
        {
            isRunning = false;
            UnregisterActivePlayer(this);
        }

        private void Tick(DateTime nowUtc)
        {
            if (!isRunning || frames.Count == 0 || frameDurations.Count == 0 || nowUtc < nextFrameAtUtc)
            {
                return;
            }

            while (isRunning && nowUtc >= nextFrameAtUtc)
            {
                int nextIndex = frameIndex + 1;
                if (nextIndex >= frames.Count)
                {
                    if (!loop)
                    {
                        endedWithoutLoop = true;
                        StopPlayback();
                        return;
                    }

                    nextIndex = 0;
                }

                frameIndex = nextIndex;
                RaiseFrameChanged();
                nextFrameAtUtc = nowUtc + frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            }
        }

        private void RaiseFrameChanged()
        {
            FrameChanged?.Invoke(frames[frameIndex]);
        }

        private static void RegisterActivePlayer(BitmapFrameAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                if (!ActivePlayers.Contains(player))
                {
                    ActivePlayers.Add(player);
                }

                EnsureSharedTimer();
                sharedTimer?.Start();
            }
        }

        private static void UnregisterActivePlayer(BitmapFrameAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                ActivePlayers.Remove(player);
                if (ActivePlayers.Count == 0)
                {
                    sharedTimer?.Stop();
                }
            }
        }

        private static void EnsureSharedTimer()
        {
            if (sharedTimer != null)
            {
                return;
            }

            sharedTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = SharedTickInterval
            };
            sharedTimer.Tick += SharedTimer_Tick;
        }

        private static void SharedTimer_Tick(object? sender, EventArgs e)
        {
            BitmapFrameAnimationPlayer[] snapshot;
            lock (SharedTimerLock)
            {
                if (ActivePlayers.Count == 0)
                {
                    sharedTimer?.Stop();
                    return;
                }

                snapshot = ActivePlayers.ToArray();
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (BitmapFrameAnimationPlayer player in snapshot)
            {
                player.Tick(nowUtc);
            }
        }

        private static TimeSpan ReadDelay(ImageMetadata? metadata)
        {
            try
            {
                if (metadata is BitmapMetadata bitmapMetadata && bitmapMetadata.ContainsQuery("/grctlext/Delay"))
                {
                    object query = bitmapMetadata.GetQuery("/grctlext/Delay");
                    if (query is ushort delayValue)
                    {
                        double milliseconds = Math.Max(20, delayValue * 10d);
                        return TimeSpan.FromMilliseconds(milliseconds);
                    }
                }
            }
            catch
            {
                // ignored
            }

            return TimeSpan.FromMilliseconds(90);
        }
    }

    public sealed class GifAnimationPlayer : IAnimationPlayer
    {
        private static readonly object SharedTimerLock = new object();
        private static readonly List<GifAnimationPlayer> ActivePlayers = new List<GifAnimationPlayer>();
        private static DispatcherTimer? sharedTimer;
        private static readonly TimeSpan SharedTickInterval = TimeSpan.FromMilliseconds(15);

        private readonly DrawingImage gifImage;
        private readonly List<BitmapSource> frames = new List<BitmapSource>();
        private readonly List<TimeSpan> frameDurations = new List<TimeSpan>();
        private readonly int frameCount;
        private int frameIndex;
        private bool loop;
        private bool endedWithoutLoop;
        private bool isRunning;
        private DateTime nextFrameAtUtc;

        public event Action<ImageSource>? FrameChanged;

        public ImageSource CurrentFrame => frameCount > 0
            ? frames[Math.Clamp(frameIndex, 0, frameCount - 1)]
            : new System.Windows.Media.DrawingImage();

        public GifAnimationPlayer(string gifPath, bool loop)
        {
            this.loop = loop;
            gifImage = DrawingImage.FromFile(gifPath);
            frameCount = gifImage.GetFrameCount(DrawingFrameDimension.Time);

            if (frameCount <= 0)
            {
                throw new InvalidOperationException("Gif has no decodable frames.");
            }

            frameDurations.AddRange(ReadFrameDelays(gifImage, frameCount));
            CacheFrames();
            frameIndex = 0;
            StartPlayback();
        }

        public void SetLoop(bool shouldLoop)
        {
            loop = shouldLoop;
            if (loop && endedWithoutLoop)
            {
                endedWithoutLoop = false;
                frameIndex = 0;
                RaiseFrameChanged();
                StartPlayback();
            }
        }

        public void Restart()
        {
            if (frameCount == 0)
            {
                return;
            }

            endedWithoutLoop = false;
            frameIndex = 0;
            RaiseFrameChanged();
            StartPlayback();
        }

        public void Stop()
        {
            StopPlayback();
            gifImage.Dispose();
            FrameChanged = null;
        }

        private void StartPlayback()
        {
            if (frameCount == 0 || frameDurations.Count == 0)
            {
                return;
            }

            endedWithoutLoop = false;
            isRunning = true;
            nextFrameAtUtc = DateTime.UtcNow + frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            RegisterActivePlayer(this);
        }

        private void StopPlayback()
        {
            isRunning = false;
            UnregisterActivePlayer(this);
        }

        private void Tick(DateTime nowUtc)
        {
            if (!isRunning || frameCount == 0 || frameDurations.Count == 0 || nowUtc < nextFrameAtUtc)
            {
                return;
            }

            while (isRunning && nowUtc >= nextFrameAtUtc)
            {
                int nextIndex = frameIndex + 1;
                if (nextIndex >= frameCount)
                {
                    if (!loop)
                    {
                        endedWithoutLoop = true;
                        StopPlayback();
                        return;
                    }

                    nextIndex = 0;
                }

                frameIndex = nextIndex;
                RaiseFrameChanged();
                nextFrameAtUtc = nowUtc + frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            }
        }

        private void RaiseFrameChanged()
        {
            FrameChanged?.Invoke(frames[Math.Clamp(frameIndex, 0, frameCount - 1)]);
        }

        private void CacheFrames()
        {
            for (int i = 0; i < frameCount; i++)
            {
                gifImage.SelectActiveFrame(DrawingFrameDimension.Time, i);
                using DrawingBitmap bitmap = new DrawingBitmap(gifImage.Width, gifImage.Height, DrawingPixelFormat.Format32bppArgb);
                using (DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap))
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
                    source.Freeze();
                    frames.Add(source);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
        }

        private static void RegisterActivePlayer(GifAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                if (!ActivePlayers.Contains(player))
                {
                    ActivePlayers.Add(player);
                }

                EnsureSharedTimer();
                sharedTimer?.Start();
            }
        }

        private static void UnregisterActivePlayer(GifAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                ActivePlayers.Remove(player);
                if (ActivePlayers.Count == 0)
                {
                    sharedTimer?.Stop();
                }
            }
        }

        private static void EnsureSharedTimer()
        {
            if (sharedTimer != null)
            {
                return;
            }

            sharedTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = SharedTickInterval
            };
            sharedTimer.Tick += SharedTimer_Tick;
        }

        private static void SharedTimer_Tick(object? sender, EventArgs e)
        {
            GifAnimationPlayer[] snapshot;
            lock (SharedTimerLock)
            {
                if (ActivePlayers.Count == 0)
                {
                    sharedTimer?.Stop();
                    return;
                }

                snapshot = ActivePlayers.ToArray();
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (GifAnimationPlayer player in snapshot)
            {
                player.Tick(nowUtc);
            }
        }

        private static List<TimeSpan> ReadFrameDelays(DrawingImage image, int frameCount)
        {
            List<TimeSpan> delays = new List<TimeSpan>(frameCount);
            const int PropertyTagFrameDelay = 0x5100;
            try
            {
                System.Drawing.Imaging.PropertyItem? property = null;
                foreach (System.Drawing.Imaging.PropertyItem candidate in image.PropertyItems)
                {
                    if (candidate.Id == PropertyTagFrameDelay)
                    {
                        property = candidate;
                        break;
                    }
                }

                if (property?.Value != null && property.Value.Length >= frameCount * 4)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        int delayUnits = BitConverter.ToInt32(property.Value, i * 4);
                        double milliseconds = Math.Max(20, delayUnits * 10d);
                        delays.Add(TimeSpan.FromMilliseconds(milliseconds));
                    }
                }
            }
            catch
            {
                // ignored
            }

            while (delays.Count < frameCount)
            {
                delays.Add(TimeSpan.FromMilliseconds(90));
            }

            return delays;
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
