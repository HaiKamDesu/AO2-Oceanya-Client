using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;
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
        event Action<int>? FrameIndexChanged;
        event Action? PlaybackFinished;
        ImageSource CurrentFrame { get; }
        int CurrentFrameIndex { get; }
        void SetLoop(bool shouldLoop);
        void Restart();
        void Stop();
    }

    public static class Ao2AnimationPreview
    {
        private const string FallbackPackUri =
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png";
        private const double DefaultFrameDelayMilliseconds = 100d;
        internal static readonly TimeSpan MinimumAnimationTickInterval = TimeSpan.FromMilliseconds(1);
        private const int StaticPreviewCacheEntryLimit = 256;
        internal const int MaxAnimatedPreviewDimension = 360;
        private const int MaxAnimatedPreviewFrames = 180;
        private const int AnimationFrameCacheEntryLimit = 96;

        private static readonly ImageSource FallbackImage = LoadEmbeddedFallback();
        private static readonly object StaticPreviewCacheLock = new object();
        private static readonly Dictionary<(string path, int width, int maxDim), WeakReference<ImageSource>> StaticPreviewCache =
            new Dictionary<(string path, int width, int maxDim), WeakReference<ImageSource>>();
        private static readonly object AnimationFrameCacheLock = new object();
        private static readonly Dictionary<(string path, DateTime lastWriteUtc, int maxDim), CachedDecodedAnimation> AnimationFrameCache =
            new Dictionary<(string path, DateTime lastWriteUtc, int maxDim), CachedDecodedAnimation>();
        private static readonly ConcurrentDictionary<string, bool> ApngDetectionCache =
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static bool IsPotentialAnimatedPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".gif" || extension == ".apng" || extension == ".webp")
            {
                return true;
            }

            if (extension == ".png" && File.Exists(path))
            {
                return IsApngFile(path);
            }

            return false;
        }

        private static bool IsApngFile(string path)
        {
            return ApngDetectionCache.GetOrAdd(path, static p =>
            {
                try
                {
                    using FileStream fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    byte[] header = new byte[512];
                    int bytesRead = fs.Read(header, 0, header.Length);

                    if (bytesRead < 8)
                    {
                        return false;
                    }

                    // Verify PNG signature: 89 50 4E 47 0D 0A 1A 0A
                    if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47 ||
                        header[4] != 0x0D || header[5] != 0x0A || header[6] != 0x1A || header[7] != 0x0A)
                    {
                        return false;
                    }

                    // Scan for acTL chunk type bytes (61 63 54 4C) which signals APNG
                    for (int i = 8; i <= bytesRead - 4; i++)
                    {
                        if (header[i] == 0x61 && header[i + 1] == 0x63 && header[i + 2] == 0x54 && header[i + 3] == 0x4C)
                        {
                            return true;
                        }
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            });
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

        public static ImageSource LoadStaticPreviewImage(string? path, int decodePixelWidth, ImageSource? fallback = null, int maxDimension = MaxAnimatedPreviewDimension)
        {
            ImageSource selectedFallback = fallback ?? FallbackImage;
            string? resolvedPath = ResolveAo2ImagePath(path);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                return selectedFallback;
            }

            int normalizedDecodeWidth = Math.Max(0, decodePixelWidth);
            if (TryGetCachedStaticPreview(resolvedPath, normalizedDecodeWidth, maxDimension, out ImageSource? cachedImage)
                && cachedImage != null)
            {
                return cachedImage;
            }

            ImageSource result;
            if (TryLoadFirstFrame(resolvedPath, out ImageSource? firstFrame, out _, maxDimension)
                && firstFrame != null
                && IsPotentialAnimatedPath(resolvedPath))
            {
                result = firstFrame is BitmapSource bitmapFirstFrame
                    ? ScaleBitmapForPreview(bitmapFirstFrame, decodePixelWidth)
                    : firstFrame;
                CacheStaticPreview(resolvedPath, normalizedDecodeWidth, maxDimension, result);
                return result;
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

                result = bitmapImage;
            }
            catch
            {
                result = selectedFallback;
            }

            CacheStaticPreview(resolvedPath, normalizedDecodeWidth, maxDimension, result);
            return result;
        }

        public static bool TryEstimateAnimationDuration(string? path, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            string? resolvedPath = ResolveAo2ImagePath(path);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                return false;
            }

            if (TryDecodeFirstAnimationFrame(resolvedPath, out _, out double estimatedDurationMs)
                && estimatedDurationMs > 0)
            {
                duration = TimeSpan.FromMilliseconds(estimatedDurationMs);
                return true;
            }

            if (TryDecodeAnimationFrames(resolvedPath, out List<BitmapSource>? frames, out List<TimeSpan>? durations)
                && frames != null
                && durations != null
                && frames.Count > 1
                && durations.Count == frames.Count)
            {
                TimeSpan total = TimeSpan.FromMilliseconds(durations.Sum(frameDuration => frameDuration.TotalMilliseconds));
                if (total > TimeSpan.Zero)
                {
                    duration = total;
                    return true;
                }
            }

            return false;
        }

        public static bool TryCreateAnimationPlayer(
            string? sourcePath,
            bool loop,
            out IAnimationPlayer? player,
            bool usePreviewLimits = true,
            int? maxDimensionOverride = null)
        {
            player = null;
            string? resolvedPath = ResolveAo2ImagePath(sourcePath);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                return false;
            }

            string extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
            bool isApng = IsApngExtensionOrContent(extension, resolvedPath);
            if (extension != ".gif" && extension != ".webp" && !isApng)
            {
                return false;
            }

            // APNG and WebP use the frame-decoder path.
            // GIF uses the legacy gif-specific player first for reliability.
            int maxDimension = maxDimensionOverride.HasValue
                ? Math.Max(1, maxDimensionOverride.Value)
                : MaxAnimatedPreviewDimension;
            if (extension == ".webp" || isApng)
            {
                if (BitmapFrameAnimationPlayer.TryCreate(resolvedPath, loop, out BitmapFrameAnimationPlayer? bitmapPlayer, maxDimension)
                    && bitmapPlayer != null)
                {
                    player = bitmapPlayer;
                    return true;
                }

                return false;
            }

            if (extension == ".gif")
            {
                try
                {
                    player = usePreviewLimits
                        ? new GifAnimationPlayer(resolvedPath, loop)
                        : GifAnimationPlayer.CreateFullFidelity(resolvedPath, loop, maxDimension);
                    return true;
                }
                catch
                {
                    // ignored
                }

                if (BitmapFrameAnimationPlayer.TryCreate(resolvedPath, loop, out BitmapFrameAnimationPlayer? bitmapPlayer, maxDimension)
                    && bitmapPlayer != null)
                {
                    player = bitmapPlayer;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when the animation frame cache already holds decoded frames for the given path
        /// and write time. A cache hit means <see cref="TryCreateAnimationPlayer"/> will return
        /// immediately without blocking on disk I/O or decode work.
        /// </summary>
        public static bool IsAnimationCached(
            string path,
            DateTime lastWriteUtc,
            int maxDimension = MaxAnimatedPreviewDimension,
            bool cacheDimensionIsTargetHeight = false)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            bool isApng = IsApngExtensionOrContent(extension, path);
            if (extension != ".gif" && extension != ".webp" && !isApng)
            {
                return false;
            }

            int dimensionKey = cacheDimensionIsTargetHeight
                ? BuildTargetHeightCacheKey(maxDimension)
                : Math.Max(1, maxDimension);
            var cacheKey = (path, lastWriteUtc, dimensionKey);
            lock (AnimationFrameCacheLock)
            {
                return AnimationFrameCache.ContainsKey(cacheKey);
            }
        }

        internal static bool TryCreateAnimationPlayerFromCachedTargetHeight(
            string path,
            bool loop,
            int targetHeight,
            out IAnimationPlayer? player)
        {
            player = null;
            DateTime lastWriteUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            var cacheKey = (path, lastWriteUtc, BuildTargetHeightCacheKey(targetHeight));
            if (!TryGetCachedAnimationFrames(cacheKey, out List<BitmapSource>? frames, out List<TimeSpan>? durations)
                || frames == null
                || durations == null
                || frames.Count <= 1
                || frames.Count != durations.Count)
            {
                return false;
            }

            player = BitmapFrameAnimationPlayer.CreateFromFrames(frames, durations, loop);
            return player != null;
        }

        public static bool TryLoadFirstFrame(string path, out ImageSource? initialFrame, out double estimatedDurationMs, int maxDimension = MaxAnimatedPreviewDimension)
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
                string extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
                if (IsPotentialAnimatedPath(resolvedPath)
                    && TryDecodeFirstAnimationFrame(resolvedPath, out BitmapSource? decodedFrame, out double decodedDurationMs, maxDimension)
                    && decodedFrame != null)
                {
                    initialFrame = decodedFrame;
                    estimatedDurationMs = decodedDurationMs;
                    return true;
                }

                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(resolvedPath, UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);

                BitmapFrame? frame = decoder.Frames.FirstOrDefault();
                if (frame == null)
                {
                    return false;
                }

                initialFrame = NormalizeBitmapForUi(frame);
                estimatedDurationMs = EstimateDecoderDurationMs(decoder, extension);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryDecodeFirstAnimationFrame(string path, out BitmapSource? frame, out double estimatedDurationMs, int maxDimension = MaxAnimatedPreviewDimension)
        {
            frame = null;
            estimatedDurationMs = 0;

            try
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();

                // APNG files are decoded by the dedicated ApngFrameDecoder
                if (IsApngExtensionOrContent(extension, path)
                    && ApngFrameDecoder.TryDecode(path, out List<BitmapSource>? apngFrames, out List<TimeSpan>? apngDurations)
                    && apngFrames != null && apngDurations != null && apngFrames.Count > 0)
                {
                    frame = apngFrames[0];
                    estimatedDurationMs = apngDurations.Sum(d => d.TotalMilliseconds);
                    return true;
                }

                if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase)
                    && TryDecodeWebPFirstFrameWithSkia(path, out BitmapSource? skiaFrame, out double skiaDurationMs)
                    && skiaFrame != null)
                {
                    frame = skiaFrame;
                    estimatedDurationMs = skiaDurationMs;
                    return true;
                }

                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(path, UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count <= 1)
                {
                    return false;
                }

                BitmapFrame firstFrame = decoder.Frames[0];
                int canvasWidth = Math.Max(1, firstFrame.PixelWidth);
                int canvasHeight = Math.Max(1, firstFrame.PixelHeight);
                int stride = canvasWidth * 4;
                byte[] canvas = new byte[stride * canvasHeight];

                BitmapSource normalizedFrame = ConvertToFormat(firstFrame, PixelFormats.Pbgra32);
                FrameLayout layout = ResolveFrameLayout(
                    firstFrame.Metadata as BitmapMetadata,
                    extension,
                    normalizedFrame.PixelWidth,
                    normalizedFrame.PixelHeight,
                    canvasWidth,
                    canvasHeight);
                BlendFrameIntoCanvas(canvas, stride, normalizedFrame, layout);

                BitmapSource compositedFrame = BitmapSource.Create(
                    canvasWidth,
                    canvasHeight,
                    96,
                    96,
                    PixelFormats.Pbgra32,
                    null,
                    canvas,
                    stride);
                if (compositedFrame.CanFreeze)
                {
                    compositedFrame.Freeze();
                }

                estimatedDurationMs = EstimateDecoderDurationMs(decoder, extension);
                frame = compositedFrame;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static double EstimateDecoderDurationMs(BitmapDecoder decoder, string extension)
        {
            if (decoder.Frames.Count <= 1)
            {
                return 0;
            }

            double total = 0;
            foreach (BitmapFrame frame in decoder.Frames)
            {
                total += ReadDelay(frame.Metadata, extension).TotalMilliseconds;
            }

            return total;
        }

        internal static BitmapSource NormalizeBitmapForUi(BitmapSource source)
        {
            if (source.Format == PixelFormats.Pbgra32)
            {
                if (source.CanFreeze)
                {
                    source.Freeze();
                }

                return source;
            }

            FormatConvertedBitmap converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = PixelFormats.Pbgra32;
            converted.EndInit();
            if (converted.CanFreeze)
            {
                converted.Freeze();
            }

            return converted;
        }

        internal static bool TryDecodeAnimationFrames(
            string path,
            out List<BitmapSource>? frames,
            out List<TimeSpan>? frameDurations,
            int maxDimension = MaxAnimatedPreviewDimension)
        {
            frames = null;
            frameDurations = null;

            try
            {
                DateTime lastWriteUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                var cacheKey = (path, lastWriteUtc, Math.Max(1, maxDimension));
                if (TryGetCachedAnimationFrames(cacheKey, out frames, out frameDurations))
                {
                    return true;
                }

                string extension = Path.GetExtension(path).ToLowerInvariant();

                // APNG files are decoded by the dedicated ApngFrameDecoder
                if (IsApngExtensionOrContent(extension, path)
                    && ApngFrameDecoder.TryDecode(path, out List<BitmapSource>? apngFrames, out List<TimeSpan>? apngDurations)
                    && apngFrames != null && apngDurations != null
                    && apngFrames.Count > 1 && apngFrames.Count == apngDurations.Count)
                {
                    frames = apngFrames;
                    frameDurations = apngDurations;
                    CacheAnimationFrames(cacheKey, frames, frameDurations);
                    return true;
                }

                if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryDecodeAnimationFramesWithSkia(path, out List<BitmapSource>? skiaFrames, out List<TimeSpan>? skiaDurations)
                        && skiaFrames != null && skiaDurations != null)
                    {
                        frames = skiaFrames;
                        frameDurations = skiaDurations;
                        CacheAnimationFrames(cacheKey, frames, frameDurations);
                        return true;
                    }

                    return false;
                }

                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(path, UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count <= 1)
                {
                    return false;
                }

                int canvasWidth = Math.Max(1, decoder.Frames[0].PixelWidth);
                int canvasHeight = Math.Max(1, decoder.Frames[0].PixelHeight);
                int stride = canvasWidth * 4;
                byte[] canvas = new byte[stride * canvasHeight];
                byte[]? previousCanvas = null;
                Int32Rect lastFrameRect = new Int32Rect(0, 0, canvasWidth, canvasHeight);
                FrameDisposal lastDisposal = FrameDisposal.None;

                List<BitmapSource> decodedFrames = new List<BitmapSource>(decoder.Frames.Count);
                List<TimeSpan> decodedDurations = new List<TimeSpan>(decoder.Frames.Count);

                for (int i = 0; i < decoder.Frames.Count; i++)
                {
                    ApplyFrameDisposal(canvas, previousCanvas, stride, lastFrameRect, lastDisposal);

                    BitmapFrame frame = decoder.Frames[i];
                    BitmapSource normalizedFrame = ConvertToFormat(frame, PixelFormats.Pbgra32);
                    FrameLayout layout = ResolveFrameLayout(
                        frame.Metadata as BitmapMetadata,
                        extension,
                        normalizedFrame.PixelWidth,
                        normalizedFrame.PixelHeight,
                        canvasWidth,
                        canvasHeight);

                    if (layout.Disposal == FrameDisposal.Previous)
                    {
                        previousCanvas = (byte[])canvas.Clone();
                    }
                    else
                    {
                        previousCanvas = null;
                    }

                    BlendFrameIntoCanvas(canvas, stride, normalizedFrame, layout);

                    BitmapSource compositedFrame = BitmapSource.Create(
                        canvasWidth,
                        canvasHeight,
                        96,
                        96,
                        PixelFormats.Pbgra32,
                        null,
                        (byte[])canvas.Clone(),
                        stride);
                    if (compositedFrame.CanFreeze)
                    {
                        compositedFrame.Freeze();
                    }

                    decodedFrames.Add(compositedFrame);
                    decodedDurations.Add(ReadDelay(frame.Metadata, extension));
                    lastFrameRect = layout.Rect;
                    lastDisposal = layout.Disposal;
                }

                frames = decodedFrames;
                frameDurations = decodedDurations;
                CacheAnimationFrames(cacheKey, frames, frameDurations);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetCachedAnimationFrames(
            (string path, DateTime lastWriteUtc, int maxDim) key,
            out List<BitmapSource>? frames,
            out List<TimeSpan>? frameDurations)
        {
            frames = null;
            frameDurations = null;
            lock (AnimationFrameCacheLock)
            {
                if (!AnimationFrameCache.TryGetValue(key, out CachedDecodedAnimation? cached))
                {
                    return false;
                }

                frames = cached.Frames.ToList();
                frameDurations = cached.FrameDurations.ToList();
                return frames.Count > 1 && frames.Count == frameDurations.Count;
            }
        }

        private static void CacheAnimationFrames(
            (string path, DateTime lastWriteUtc, int maxDim) key,
            IReadOnlyList<BitmapSource> frames,
            IReadOnlyList<TimeSpan> frameDurations)
        {
            if (frames.Count <= 1 || frames.Count != frameDurations.Count)
            {
                return;
            }

            lock (AnimationFrameCacheLock)
            {
                if (AnimationFrameCache.Count >= AnimationFrameCacheEntryLimit)
                {
                    AnimationFrameCache.Remove(AnimationFrameCache.Keys.First());
                }

                AnimationFrameCache[key] = new CachedDecodedAnimation(frames.ToList(), frameDurations.ToList());
            }
        }

        private sealed class CachedDecodedAnimation
        {
            public CachedDecodedAnimation(List<BitmapSource> frames, List<TimeSpan> frameDurations)
            {
                Frames = frames;
                FrameDurations = frameDurations;
            }

            public List<BitmapSource> Frames { get; }
            public List<TimeSpan> FrameDurations { get; }
        }

        /// <summary>Returns true when the path is an APNG file (by extension or content).</summary>
        private static bool IsApngExtensionOrContent(string extension, string path)
        {
            if (string.Equals(extension, ".apng", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                return IsApngFile(path);
            }

            return false;
        }

        /// <summary>
        /// Decodes the first frame of any WebP file (static or animated) using SkiaSharp.
        /// Used when only a thumbnail or the initial frame is needed without decoding all frames.
        /// </summary>
        private static bool TryDecodeWebPFirstFrameWithSkia(
            string path,
            out BitmapSource? frame,
            out double estimatedDurationMs)
        {
            frame = null;
            estimatedDurationMs = 0;
            try
            {
                using SKCodec? codec = SKCodec.Create(path);
                if (codec == null)
                {
                    return false;
                }

                int srcWidth = codec.Info.Width;
                int srcHeight = codec.Info.Height;
                if (srcWidth <= 0 || srcHeight <= 0)
                {
                    return false;
                }

                SKImageInfo decodeInfo = new SKImageInfo(srcWidth, srcHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                using SKBitmap bitmap = new SKBitmap(decodeInfo);

                // GetPixels without frame options decodes the first/only frame for both
                // static WebP (FrameCount = 0) and animated WebP (FrameCount > 0).
                SKCodecResult result = codec.GetPixels(decodeInfo, bitmap.GetPixels());
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    return false;
                }

                int stride = srcWidth * 4;
                byte[] pixels = new byte[stride * srcHeight];
                Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);

                BitmapSource bmpSource = BitmapSource.Create(
                    srcWidth, srcHeight, 96, 96,
                    PixelFormats.Pbgra32, null, pixels, stride);
                if (bmpSource.CanFreeze)
                {
                    bmpSource.Freeze();
                }

                frame = bmpSource;
                if (codec.FrameCount > 0)
                {
                    estimatedDurationMs = codec.FrameInfo.Sum(
                        fi => (double)Math.Max(fi.Duration, (int)DefaultFrameDelayMilliseconds));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecodeAnimationFramesWithSkia(
            string path,
            out List<BitmapSource>? frames,
            out List<TimeSpan>? frameDurations)
        {
            frames = null;
            frameDurations = null;

            var allBitmaps = new List<SKBitmap>();
            try
            {
                using SKCodec? codec = SKCodec.Create(path);
                if (codec == null || codec.FrameCount <= 1)
                {
                    return false;
                }

                SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
                int srcWidth = codec.Info.Width;
                int srcHeight = codec.Info.Height;
                int frameCount = codec.FrameCount;

                SKImageInfo decodeInfo = new SKImageInfo(srcWidth, srcHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

                List<BitmapSource> decodedFrames = new List<BitmapSource>(frameCount);
                List<TimeSpan> decodedDurations = new List<TimeSpan>(frameCount);

                for (int i = 0; i < frameCount; i++)
                {
                    int requiredFrame = frameInfo[i].RequiredFrame;
                    SKBitmap bitmap = new SKBitmap(decodeInfo);
                    allBitmaps.Add(bitmap);

                    if (requiredFrame >= 0 && requiredFrame < allBitmaps.Count - 1)
                    {
                        using SKCanvas canvas = new SKCanvas(bitmap);
                        canvas.DrawBitmap(allBitmaps[requiredFrame], 0, 0);
                    }

                    SKCodecResult result = codec.GetPixels(decodeInfo, bitmap.GetPixels(), new SKCodecOptions(i, requiredFrame));
                    if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                    {
                        return false;
                    }

                    int stride = srcWidth * 4;
                    byte[] pixels = new byte[stride * srcHeight];
                    Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);

                    BitmapSource bmpSource = BitmapSource.Create(
                        srcWidth, srcHeight, 96, 96,
                        PixelFormats.Pbgra32, null, pixels, stride);
                    if (bmpSource.CanFreeze)
                    {
                        bmpSource.Freeze();
                    }

                    decodedFrames.Add(bmpSource);

                    int durationMs = frameInfo[i].Duration;
                    if (durationMs <= 0)
                    {
                        durationMs = (int)DefaultFrameDelayMilliseconds;
                    }

                    decodedDurations.Add(TimeSpan.FromMilliseconds(durationMs));
                }

                frames = decodedFrames;
                frameDurations = decodedDurations;
                return decodedFrames.Count > 1 && decodedFrames.Count == decodedDurations.Count;
            }
            catch
            {
                return false;
            }
            finally
            {
                foreach (SKBitmap b in allBitmaps)
                {
                    b.Dispose();
                }
            }
        }

        /// <summary>
        /// Decodes an animated WebP frame-by-frame on the calling thread, invoking
        /// <paramref name="frameCallback"/> immediately after each frame is ready.
        /// Caches all decoded frames in <see cref="AnimationFrameCache"/> on success so that
        /// subsequent calls via <see cref="TryDecodeAnimationFrames"/> return instantly.
        /// Returns false for non-animated, single-frame, or decode-failed files.
        /// </summary>
        internal static bool TryStreamWebPFrames(
            string path,
            Action<BitmapSource, TimeSpan> frameCallback,
            CancellationToken cancellationToken = default,
            int targetHeight = 0)
        {
            var allBitmaps = new List<SKBitmap>();
            var allFrames = new List<BitmapSource>();
            var allDurations = new List<TimeSpan>();
            try
            {
                using SKCodec? codec = SKCodec.Create(path);
                if (codec == null || codec.FrameCount <= 1)
                {
                    return false;
                }

                SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
                int srcWidth = codec.Info.Width;
                int srcHeight = codec.Info.Height;
                int frameCount = codec.FrameCount;
                SKImageInfo decodeInfo = new SKImageInfo(srcWidth, srcHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

                for (int i = 0; i < frameCount; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    int requiredFrame = frameInfo[i].RequiredFrame;
                    SKBitmap bitmap = new SKBitmap(decodeInfo);
                    allBitmaps.Add(bitmap);

                    if (requiredFrame >= 0 && requiredFrame < allBitmaps.Count - 1)
                    {
                        using SKCanvas canvas = new SKCanvas(bitmap);
                        canvas.DrawBitmap(allBitmaps[requiredFrame], 0, 0);
                    }

                    SKCodecResult result = codec.GetPixels(decodeInfo, bitmap.GetPixels(), new SKCodecOptions(i, requiredFrame));
                    if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                    {
                        return false;
                    }

                    int stride = srcWidth * 4;
                    byte[] pixels = new byte[stride * srcHeight];
                    Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);

                    BitmapSource bmpSource = BitmapSource.Create(
                        srcWidth, srcHeight, 96, 96,
                        PixelFormats.Pbgra32, null, pixels, stride);
                    BitmapSource displayFrame = ScaleBitmapToTargetHeight(bmpSource, targetHeight);

                    int durationMs = frameInfo[i].Duration;
                    if (durationMs <= 0)
                    {
                        durationMs = (int)DefaultFrameDelayMilliseconds;
                    }

                    TimeSpan duration = TimeSpan.FromMilliseconds(durationMs);
                    allFrames.Add(displayFrame);
                    allDurations.Add(duration);
                    frameCallback(displayFrame, duration);
                }

                if (allFrames.Count > 1)
                {
                    DateTime lastWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                    int dimensionKey = targetHeight > 0
                        ? BuildTargetHeightCacheKey(targetHeight)
                        : Math.Max(1, MaxAnimatedPreviewDimension);
                    var cacheKey = (path, lastWrite, dimensionKey);
                    CacheAnimationFrames(cacheKey, allFrames, allDurations);
                }

                return allFrames.Count > 1;
            }
            catch
            {
                return false;
            }
            finally
            {
                foreach (SKBitmap b in allBitmaps)
                {
                    b.Dispose();
                }
            }
        }

        internal static bool TryStreamGifFrames(
            string path,
            Action<BitmapSource, TimeSpan> frameCallback,
            CancellationToken cancellationToken = default,
            int targetHeight = 0)
        {
            var allFrames = new List<BitmapSource>();
            var allDurations = new List<TimeSpan>();
            try
            {
                using DrawingImage gifImage = DrawingImage.FromFile(path);
                int frameCount = gifImage.GetFrameCount(DrawingFrameDimension.Time);
                if (frameCount <= 1)
                {
                    return false;
                }

                List<TimeSpan> frameDelays = ReadGifFrameDelays(gifImage, frameCount);
                for (int i = 0; i < frameCount; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    gifImage.SelectActiveFrame(DrawingFrameDimension.Time, i);
                    int sourceWidth = Math.Max(1, gifImage.Width);
                    int sourceHeight = Math.Max(1, gifImage.Height);
                    int targetFrameHeight = targetHeight > 0 ? targetHeight : sourceHeight;
                    int targetFrameWidth = Math.Max(1, (int)Math.Round(sourceWidth * (targetFrameHeight / (double)sourceHeight)));

                    using DrawingBitmap bitmap = new DrawingBitmap(targetFrameWidth, targetFrameHeight, DrawingPixelFormat.Format32bppArgb);
                    using (DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap))
                    {
                        graphics.Clear(System.Drawing.Color.Transparent);
                        ApplyAo2GifInterpolation(graphics, sourceHeight, targetFrameHeight);
                        graphics.DrawImage(gifImage, 0, 0, targetFrameWidth, targetFrameHeight);
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

                        TimeSpan duration = frameDelays[Math.Clamp(i, 0, frameDelays.Count - 1)];
                        allFrames.Add(source);
                        allDurations.Add(duration);
                        frameCallback(source, duration);
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }

                if (allFrames.Count > 1)
                {
                    DateTime lastWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                    int dimensionKey = targetHeight > 0
                        ? BuildTargetHeightCacheKey(targetHeight)
                        : Math.Max(1, MaxAnimatedPreviewDimension);
                    var cacheKey = (path, lastWrite, dimensionKey);
                    CacheAnimationFrames(cacheKey, allFrames, allDurations);
                }

                return allFrames.Count > 1;
            }
            catch
            {
                return false;
            }
        }

        private static int BuildTargetHeightCacheKey(int targetHeight)
        {
            return -Math.Max(1, targetHeight);
        }

        internal static void ApplyAo2GifInterpolation(DrawingGraphics graphics, int sourceHeight, int targetHeight)
        {
            if (sourceHeight > 0 && targetHeight > sourceHeight)
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                return;
            }

            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        }

        private static BitmapSource ScaleBitmapToTargetHeight(BitmapSource source, int targetHeight)
        {
            if (targetHeight <= 0 || source.PixelHeight <= 0 || source.PixelHeight == targetHeight)
            {
                return NormalizeBitmapForUi(source);
            }

            double scale = targetHeight / (double)source.PixelHeight;
            TransformedBitmap scaled = new TransformedBitmap();
            scaled.BeginInit();
            scaled.Source = source;
            scaled.Transform = new ScaleTransform(scale, scale);
            scaled.EndInit();
            return NormalizeBitmapForUi(scaled);
        }

        private static List<TimeSpan> ReadGifFrameDelays(DrawingImage image, int frameCount)
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
                        double milliseconds = Math.Max(0, delayUnits * 10d);
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
                delays.Add(TimeSpan.FromMilliseconds(DefaultFrameDelayMilliseconds));
            }

            return delays;
        }

        private static List<int> BuildSelectedFrameIndices(int sourceFrameCount, int maxFrames)
        {
            int safeFrameCount = Math.Max(0, sourceFrameCount);
            int safeMaxFrames = Math.Max(1, maxFrames);
            if (safeFrameCount <= safeMaxFrames)
            {
                return Enumerable.Range(0, safeFrameCount).ToList();
            }

            List<int> selected = new List<int>(safeMaxFrames);
            double stride = safeFrameCount / (double)safeMaxFrames;
            for (int i = 0; i < safeMaxFrames; i++)
            {
                int sourceIndex = Math.Min(safeFrameCount - 1, (int)Math.Round(i * stride));
                if (selected.Count == 0 || selected[selected.Count - 1] != sourceIndex)
                {
                    selected.Add(sourceIndex);
                }
            }

            if (selected.Count == 0 || selected[selected.Count - 1] != safeFrameCount - 1)
            {
                selected.Add(safeFrameCount - 1);
            }

            return selected;
        }

        private static (int width, int height) ComputePreviewDimensions(int sourceWidth, int sourceHeight, int maxDimension)
        {
            int safeWidth = Math.Max(1, sourceWidth);
            int safeHeight = Math.Max(1, sourceHeight);
            int largestSide = Math.Max(safeWidth, safeHeight);
            int safeMaxDimension = Math.Max(1, maxDimension);
            if (largestSide <= safeMaxDimension)
            {
                return (safeWidth, safeHeight);
            }

            double scale = safeMaxDimension / (double)largestSide;
            int width = Math.Max(1, (int)Math.Round(safeWidth * scale));
            int height = Math.Max(1, (int)Math.Round(safeHeight * scale));
            return (width, height);
        }

        private static BitmapSource ScaleBitmapForPreview(BitmapSource source, int decodePixelWidth)
        {
            if (decodePixelWidth <= 0 || source.PixelWidth <= decodePixelWidth)
            {
                return NormalizeBitmapForUi(source);
            }

            double scale = decodePixelWidth / (double)source.PixelWidth;
            int targetWidth = Math.Max(1, decodePixelWidth);
            int targetHeight = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
            double scaleX = targetWidth / (double)source.PixelWidth;
            double scaleY = targetHeight / (double)source.PixelHeight;

            TransformedBitmap scaled = new TransformedBitmap();
            scaled.BeginInit();
            scaled.Source = source;
            scaled.Transform = new ScaleTransform(scaleX, scaleY);
            scaled.EndInit();
            return NormalizeBitmapForUi(scaled);
        }

        private static bool TryGetCachedStaticPreview(string path, int decodePixelWidth, int maxDimension, out ImageSource? image)
        {
            image = null;
            (string path, int width, int maxDim) cacheKey = (path, decodePixelWidth, maxDimension);
            lock (StaticPreviewCacheLock)
            {
                if (!StaticPreviewCache.TryGetValue(cacheKey, out WeakReference<ImageSource>? reference))
                {
                    return false;
                }

                if (reference.TryGetTarget(out ImageSource? cachedImage) && cachedImage != null)
                {
                    image = cachedImage;
                    return true;
                }

                StaticPreviewCache.Remove(cacheKey);
                return false;
            }
        }

        private static void CacheStaticPreview(string path, int decodePixelWidth, int maxDimension, ImageSource image)
        {
            (string path, int width, int maxDim) cacheKey = (path, decodePixelWidth, maxDimension);
            lock (StaticPreviewCacheLock)
            {
                StaticPreviewCache[cacheKey] = new WeakReference<ImageSource>(image);
                if (StaticPreviewCache.Count <= StaticPreviewCacheEntryLimit)
                {
                    return;
                }

                List<(string path, int width, int maxDim)> keys = StaticPreviewCache.Keys.ToList();
                int removeCount = StaticPreviewCache.Count - StaticPreviewCacheEntryLimit;
                for (int i = 0; i < removeCount && i < keys.Count; i++)
                {
                    StaticPreviewCache.Remove(keys[i]);
                }
            }
        }

        public static void ClearStaticPreviewCache()
        {
            lock (StaticPreviewCacheLock)
            {
                StaticPreviewCache.Clear();
            }
        }

        internal static int GetStaticPreviewCacheEntryCountForTests()
        {
            lock (StaticPreviewCacheLock)
            {
                return StaticPreviewCache.Count;
            }
        }

        private static BitmapSource ConvertToFormat(BitmapSource source, PixelFormat format)
        {
            if (source.Format == format)
            {
                if (source.CanFreeze)
                {
                    source.Freeze();
                }

                return source;
            }

            FormatConvertedBitmap converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = format;
            converted.EndInit();
            if (converted.CanFreeze)
            {
                converted.Freeze();
            }

            return converted;
        }

        private static FrameLayout ResolveFrameLayout(
            BitmapMetadata? metadata,
            string extension,
            int sourceWidth,
            int sourceHeight,
            int canvasWidth,
            int canvasHeight)
        {
            int frameLeft = ReadMetadataInt(metadata, new[] { "/imgdesc/Left", "/ANMF/X", "/ANMF/Left", "/fcTL/x_offset" }, 0);
            int frameTop = ReadMetadataInt(metadata, new[] { "/imgdesc/Top", "/ANMF/Y", "/ANMF/Top", "/fcTL/y_offset" }, 0);
            int frameWidth = ReadMetadataInt(metadata, new[] { "/imgdesc/Width", "/ANMF/Width", "/fcTL/width" }, sourceWidth);
            int frameHeight = ReadMetadataInt(metadata, new[] { "/imgdesc/Height", "/ANMF/Height", "/fcTL/height" }, sourceHeight);
            int blendValue = ReadMetadataInt(metadata, new[] { "/imgdesc/Blend", "/ANMF/Blend", "/fcTL/blend_op" }, 1);
            int disposalValue = ReadMetadataInt(
                metadata,
                new[] { "/imgdesc/Disposal", "/ANMF/Disposal", "/fcTL/dispose_op", "/grctlext/Disposal" },
                0);

            if (sourceWidth == canvasWidth && sourceHeight == canvasHeight)
            {
                frameLeft = 0;
                frameTop = 0;
                frameWidth = canvasWidth;
                frameHeight = canvasHeight;
            }

            frameLeft = Math.Clamp(frameLeft, 0, Math.Max(0, canvasWidth - 1));
            frameTop = Math.Clamp(frameTop, 0, Math.Max(0, canvasHeight - 1));
            frameWidth = Math.Clamp(frameWidth, 1, canvasWidth - frameLeft);
            frameHeight = Math.Clamp(frameHeight, 1, canvasHeight - frameTop);

            FrameBlend blend = blendValue == 0 ? FrameBlend.Source : FrameBlend.Over;
            FrameDisposal disposal = ResolveFrameDisposal(disposalValue, extension);

            return new FrameLayout
            {
                Rect = new Int32Rect(frameLeft, frameTop, frameWidth, frameHeight),
                Blend = blend,
                Disposal = disposal
            };
        }

        private static int ReadMetadataInt(BitmapMetadata? metadata, IReadOnlyList<string> paths, int fallback)
        {
            if (metadata == null)
            {
                return fallback;
            }

            foreach (string queryPath in paths)
            {
                try
                {
                    if (!metadata.ContainsQuery(queryPath))
                    {
                        continue;
                    }

                    object queryValue = metadata.GetQuery(queryPath);
                    return queryValue switch
                    {
                        byte byteValue => byteValue,
                        ushort ushortValue => ushortValue,
                        uint uintValue => (int)uintValue,
                        int intValue => intValue,
                        long longValue => (int)longValue,
                        _ => fallback
                    };
                }
                catch
                {
                    // ignored
                }
            }

            return fallback;
        }

        private static void ApplyFrameDisposal(
            byte[] canvas,
            byte[]? previousCanvas,
            int canvasStride,
            Int32Rect frameRect,
            FrameDisposal disposal)
        {
            if (disposal == FrameDisposal.None)
            {
                return;
            }

            if (disposal == FrameDisposal.Previous)
            {
                if (previousCanvas != null && previousCanvas.Length == canvas.Length)
                {
                    Buffer.BlockCopy(previousCanvas, 0, canvas, 0, canvas.Length);
                }

                return;
            }

            int left = Math.Max(0, frameRect.X);
            int top = Math.Max(0, frameRect.Y);
            int width = Math.Max(0, frameRect.Width);
            int height = Math.Max(0, frameRect.Height);
            for (int row = 0; row < height; row++)
            {
                int rowStart = ((top + row) * canvasStride) + (left * 4);
                Array.Clear(canvas, rowStart, width * 4);
            }
        }

        private static void BlendFrameIntoCanvas(
            byte[] canvas,
            int canvasStride,
            BitmapSource sourceFrame,
            FrameLayout layout)
        {
            int sourceWidth = sourceFrame.PixelWidth;
            int sourceHeight = sourceFrame.PixelHeight;
            int sourceStride = sourceWidth * 4;
            byte[] sourcePixels = new byte[sourceStride * sourceHeight];
            sourceFrame.CopyPixels(sourcePixels, sourceStride, 0);

            int drawWidth = Math.Min(layout.Rect.Width, sourceWidth);
            int drawHeight = Math.Min(layout.Rect.Height, sourceHeight);
            int targetLeft = layout.Rect.X;
            int targetTop = layout.Rect.Y;

            for (int row = 0; row < drawHeight; row++)
            {
                int sourceRow = row * sourceStride;
                int targetRow = ((targetTop + row) * canvasStride) + (targetLeft * 4);
                for (int column = 0; column < drawWidth; column++)
                {
                    int sourceIndex = sourceRow + (column * 4);
                    int targetIndex = targetRow + (column * 4);

                    byte sourceBlue = sourcePixels[sourceIndex];
                    byte sourceGreen = sourcePixels[sourceIndex + 1];
                    byte sourceRed = sourcePixels[sourceIndex + 2];
                    byte sourceAlpha = sourcePixels[sourceIndex + 3];

                    if (layout.Blend == FrameBlend.Source)
                    {
                        canvas[targetIndex] = sourceBlue;
                        canvas[targetIndex + 1] = sourceGreen;
                        canvas[targetIndex + 2] = sourceRed;
                        canvas[targetIndex + 3] = sourceAlpha;
                        continue;
                    }

                    int inverseAlpha = 255 - sourceAlpha;
                    canvas[targetIndex] = (byte)Math.Clamp(sourceBlue + ((canvas[targetIndex] * inverseAlpha + 127) / 255), 0, 255);
                    canvas[targetIndex + 1] = (byte)Math.Clamp(sourceGreen + ((canvas[targetIndex + 1] * inverseAlpha + 127) / 255), 0, 255);
                    canvas[targetIndex + 2] = (byte)Math.Clamp(sourceRed + ((canvas[targetIndex + 2] * inverseAlpha + 127) / 255), 0, 255);
                    canvas[targetIndex + 3] = (byte)Math.Clamp(sourceAlpha + ((canvas[targetIndex + 3] * inverseAlpha + 127) / 255), 0, 255);
                }
            }
        }

        private static FrameDisposal ResolveFrameDisposal(int disposalValue, string extension)
        {
            if (string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase))
            {
                return disposalValue switch
                {
                    2 => FrameDisposal.Background,
                    3 => FrameDisposal.Previous,
                    _ => FrameDisposal.None
                };
            }

            return disposalValue switch
            {
                1 => FrameDisposal.Background,
                2 => FrameDisposal.Previous,
                _ => FrameDisposal.None
            };
        }

        internal static TimeSpan ReadDelay(ImageMetadata? metadata, string extension)
        {
            try
            {
                if (metadata is BitmapMetadata bitmapMetadata)
                {
                    if (bitmapMetadata.ContainsQuery("/grctlext/Delay"))
                    {
                        object query = bitmapMetadata.GetQuery("/grctlext/Delay");
                        if (query is ushort delayValue)
                        {
                            return TimeSpan.FromMilliseconds(delayValue * 10d);
                        }
                    }

                    if (bitmapMetadata.ContainsQuery("/fcTL/delay_num"))
                    {
                        double numerator = ReadMetadataInt(bitmapMetadata, new[] { "/fcTL/delay_num" }, 0);
                        double denominator = ReadMetadataInt(bitmapMetadata, new[] { "/fcTL/delay_den" }, 100);
                        if (denominator <= 0)
                        {
                            denominator = 100;
                        }

                        if (numerator > 0)
                        {
                            return TimeSpan.FromMilliseconds((numerator / denominator) * 1000d);
                        }
                    }

                    foreach (string queryPath in new[]
                    {
                        "/ANMF/FrameDuration",
                        "/ANMF/Duration",
                        "/imgdesc/Delay",
                        "/FrameDelay"
                    })
                    {
                        if (!bitmapMetadata.ContainsQuery(queryPath))
                        {
                            continue;
                        }

                        object? queryValue = bitmapMetadata.GetQuery(queryPath);
                        double delayMs = queryValue switch
                        {
                            byte byteValue => byteValue,
                            ushort ushortValue => ushortValue,
                            uint uintValue => uintValue,
                            int intValue => intValue,
                            long longValue => longValue,
                            _ => 0
                        };

                        if (delayMs > 0)
                        {
                            return TimeSpan.FromMilliseconds(delayMs);
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            return TimeSpan.FromMilliseconds(DefaultFrameDelayMilliseconds);
        }

        private enum FrameBlend
        {
            Source,
            Over
        }

        private enum FrameDisposal
        {
            None,
            Background,
            Previous
        }

        private readonly struct FrameLayout
        {
            public Int32Rect Rect { get; init; }
            public FrameBlend Blend { get; init; }
            public FrameDisposal Disposal { get; init; }
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

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }

    public sealed class BitmapFrameAnimationPlayer : IAnimationPlayer
    {
        private static readonly object SharedTimerLock = new object();
        private static readonly List<BitmapFrameAnimationPlayer> ActivePlayers = new List<BitmapFrameAnimationPlayer>();
        private static DispatcherTimer? sharedTimer;

        private readonly List<BitmapSource> frames = new List<BitmapSource>();
        private readonly List<TimeSpan> frameDurations = new List<TimeSpan>();
        private int frameIndex;
        private bool loop;
        private bool endedWithoutLoop;
        private bool isRunning;
        private DateTime nextFrameAtUtc;

        // Streaming-mode state — only active when created via CreateForStreaming.
        private bool streaming;
        private volatile bool streamingComplete;
        private ConcurrentQueue<(BitmapSource Frame, TimeSpan Duration)>? pendingStreamedFrames;

        public event Action<ImageSource>? FrameChanged;
        public event Action<int>? FrameIndexChanged;
        public event Action? PlaybackFinished;

        public ImageSource CurrentFrame => frames.Count > 0
            ? frames[Math.Clamp(frameIndex, 0, frames.Count - 1)]
            : new System.Windows.Media.DrawingImage();
        public int CurrentFrameIndex => frameIndex;

        private BitmapFrameAnimationPlayer(bool loop)
        {
            this.loop = loop;
        }

        public static bool TryCreate(string path, bool loop, out BitmapFrameAnimationPlayer? player, int maxDimension = Ao2AnimationPreview.MaxAnimatedPreviewDimension)
        {
            player = null;
            try
            {
                if (!Ao2AnimationPreview.TryDecodeAnimationFrames(path, out List<BitmapSource>? decodedFrames, out List<TimeSpan>? decodedDurations, maxDimension)
                    || decodedFrames == null
                    || decodedDurations == null
                    || decodedFrames.Count <= 1
                    || decodedFrames.Count != decodedDurations.Count)
                {
                    return false;
                }

                BitmapFrameAnimationPlayer candidate = new BitmapFrameAnimationPlayer(loop);
                for (int i = 0; i < decodedFrames.Count; i++)
                {
                    candidate.frames.Add(decodedFrames[i]);
                    candidate.frameDurations.Add(decodedDurations[i]);
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

        internal static BitmapFrameAnimationPlayer? CreateFromFrames(
            IReadOnlyList<BitmapSource> decodedFrames,
            IReadOnlyList<TimeSpan> decodedDurations,
            bool loop)
        {
            if (decodedFrames.Count <= 1 || decodedFrames.Count != decodedDurations.Count)
            {
                return null;
            }

            BitmapFrameAnimationPlayer candidate = new BitmapFrameAnimationPlayer(loop);
            for (int i = 0; i < decodedFrames.Count; i++)
            {
                candidate.frames.Add(decodedFrames[i]);
                candidate.frameDurations.Add(decodedDurations[i]);
            }

            candidate.frameIndex = 0;
            candidate.StartPlayback();
            return candidate;
        }

        /// <summary>
        /// Creates a player that accepts frames one-at-a-time via <see cref="EnqueueStreamedFrame"/>.
        /// Call <see cref="BeginStreamedPlayback"/> on the UI thread after frame 0 is enqueued,
        /// and <see cref="SignalStreamingComplete"/> from the decode thread when all frames are done.
        /// </summary>
        internal static BitmapFrameAnimationPlayer CreateForStreaming(bool loop)
        {
            BitmapFrameAnimationPlayer p = new BitmapFrameAnimationPlayer(loop);
            p.streaming = true;
            p.pendingStreamedFrames = new ConcurrentQueue<(BitmapSource, TimeSpan)>();
            return p;
        }

        /// <summary>Called from the background decode thread after each frame is ready.</summary>
        internal void EnqueueStreamedFrame(BitmapSource frame, TimeSpan duration)
        {
            pendingStreamedFrames?.Enqueue((frame, duration));
        }

        /// <summary>Called from the background decode thread after all frames have been enqueued.</summary>
        internal void SignalStreamingComplete()
        {
            streamingComplete = true;
        }

        /// <summary>
        /// Drains whatever frames have arrived so far and starts playback.
        /// Must be called on the UI thread after at least frame 0 has been enqueued.
        /// </summary>
        internal void BeginStreamedPlayback()
        {
            DrainPendingFrames();
            if (frames.Count == 0)
            {
                return;
            }

            frameIndex = 0;
            StartPlayback();
        }

        private void DrainPendingFrames()
        {
            if (pendingStreamedFrames == null)
            {
                return;
            }

            while (pendingStreamedFrames.TryDequeue(out (BitmapSource Frame, TimeSpan Duration) item))
            {
                frames.Add(item.Frame);
                frameDurations.Add(item.Duration);
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
            FrameIndexChanged = null;
            PlaybackFinished = null;
            frames.Clear();
            frameDurations.Clear();
            frameIndex = 0;
            // Prevent the background decoder from stalling if it's still running.
            streamingComplete = true;
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
            if (streaming)
            {
                DrainPendingFrames();
            }

            if (!isRunning || frames.Count == 0 || frameDurations.Count == 0 || nowUtc < nextFrameAtUtc)
            {
                return;
            }

            while (isRunning && nowUtc >= nextFrameAtUtc)
            {
                int nextIndex = frameIndex + 1;
                if (nextIndex >= frames.Count)
                {
                    if (streaming && !streamingComplete)
                    {
                        // Decoder hasn't produced the next frame yet — hold on the current frame
                        // and check again on the next tick (~16 ms).
                        nextFrameAtUtc = nowUtc + TimeSpan.FromMilliseconds(16);
                        return;
                    }

                    if (!loop)
                    {
                        endedWithoutLoop = true;
                        StopPlayback();
                        PlaybackFinished?.Invoke();
                        return;
                    }

                    nextIndex = 0;
                }

                frameIndex = nextIndex;
                RaiseFrameChanged();
                nextFrameAtUtc += frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            }
        }

        private void RaiseFrameChanged()
        {
            FrameChanged?.Invoke(frames[frameIndex]);
            FrameIndexChanged?.Invoke(frameIndex);
        }

        private static void RegisterActivePlayer(BitmapFrameAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                if (!ActivePlayers.Contains(player))
                {
                    ActivePlayers.Add(player);
                }

                ScheduleNextSharedTick(DateTime.UtcNow);
            }
        }

        private static void UnregisterActivePlayer(BitmapFrameAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                ActivePlayers.Remove(player);

                ScheduleNextSharedTick(DateTime.UtcNow);
            }
        }

        private static void EnsureSharedTimer()
        {
            if (sharedTimer != null)
            {
                return;
            }

            Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            sharedTimer = new DispatcherTimer(DispatcherPriority.Render, dispatcher)
            {
                Interval = Ao2AnimationPreview.MinimumAnimationTickInterval
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

            lock (SharedTimerLock)
            {
                ScheduleNextSharedTick(nowUtc);
            }
        }

        private static void ScheduleNextSharedTick(DateTime nowUtc)
        {
            EnsureSharedTimer();
            if (sharedTimer == null)
            {
                return;
            }

            if (ActivePlayers.Count == 0)
            {
                sharedTimer.Stop();
                return;
            }

            TimeSpan nextDelay = ActivePlayers
                .Where(player => player.isRunning && player.frames.Count > 0 && player.frameDurations.Count > 0)
                .Select(player => player.nextFrameAtUtc - nowUtc)
                .DefaultIfEmpty(Ao2AnimationPreview.MinimumAnimationTickInterval)
                .Min();

            if (nextDelay < Ao2AnimationPreview.MinimumAnimationTickInterval)
            {
                nextDelay = Ao2AnimationPreview.MinimumAnimationTickInterval;
            }

            sharedTimer.Stop();
            sharedTimer.Interval = nextDelay;
            sharedTimer.Start();
        }

    }

    public sealed class GifAnimationPlayer : IAnimationPlayer
    {
        private static readonly object SharedTimerLock = new object();
        private static readonly List<GifAnimationPlayer> ActivePlayers = new List<GifAnimationPlayer>();
        private static DispatcherTimer? sharedTimer;
        // Preview players do not need full-resolution/full-frame GIF decoding; cap both to prevent UI stalls.
        private const int MaxGifPreviewDimension = 360;
        private const int MaxGifPreviewFrames = 180;

        private readonly DrawingImage gifImage;
        private readonly List<BitmapSource> frames = new List<BitmapSource>();
        private readonly List<TimeSpan> frameDurations = new List<TimeSpan>();
        private readonly int maxDimension;
        private readonly int maxFrames;
        private int frameCount;
        private int frameIndex;
        private bool loop;
        private bool endedWithoutLoop;
        private bool isRunning;
        private DateTime nextFrameAtUtc;

        public event Action<ImageSource>? FrameChanged;
        public event Action<int>? FrameIndexChanged;
        public event Action? PlaybackFinished;

        public ImageSource CurrentFrame => frameCount > 0
            ? frames[Math.Clamp(frameIndex, 0, frameCount - 1)]
            : new System.Windows.Media.DrawingImage();
        public int CurrentFrameIndex => frameIndex;

        public GifAnimationPlayer(string gifPath, bool loop)
            : this(gifPath, loop, MaxGifPreviewDimension, MaxGifPreviewFrames)
        {
        }

        private GifAnimationPlayer(string gifPath, bool loop, int maxDimension, int maxFrames)
        {
            this.loop = loop;
            this.maxDimension = maxDimension;
            this.maxFrames = maxFrames;
            gifImage = DrawingImage.FromFile(gifPath);
            int sourceFrameCount = gifImage.GetFrameCount(DrawingFrameDimension.Time);

            if (sourceFrameCount <= 0)
            {
                throw new InvalidOperationException("Gif has no decodable frames.");
            }

            List<int> selectedFrameIndices = BuildSelectedFrameIndices(sourceFrameCount);
            List<TimeSpan> sourceDelays = ReadFrameDelays(gifImage, sourceFrameCount);
            frameDurations.AddRange(SampleFrameDurations(sourceDelays, selectedFrameIndices));
            CacheFrames(selectedFrameIndices);
            frameCount = frames.Count;
            if (frameCount == 0)
            {
                throw new InvalidOperationException("Gif produced no preview frames.");
            }

            if (frameDurations.Count != frameCount)
            {
                frameDurations.Clear();
                for (int i = 0; i < frameCount; i++)
                {
                    frameDurations.Add(TimeSpan.FromMilliseconds(100));
                }
            }

            frameIndex = 0;
            StartPlayback();
        }

        public static GifAnimationPlayer CreateFullFidelity(string gifPath, bool loop, int maxDimension = 0)
        {
            return new GifAnimationPlayer(gifPath, loop, maxDimension: maxDimension, maxFrames: 0);
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
            FrameIndexChanged = null;
            PlaybackFinished = null;
            frames.Clear();
            frameDurations.Clear();
            frameIndex = 0;
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
                        PlaybackFinished?.Invoke();
                        return;
                    }

                    nextIndex = 0;
                }

                frameIndex = nextIndex;
                RaiseFrameChanged();
                nextFrameAtUtc += frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            }
        }

        private void RaiseFrameChanged()
        {
            FrameChanged?.Invoke(frames[Math.Clamp(frameIndex, 0, frameCount - 1)]);
            FrameIndexChanged?.Invoke(Math.Clamp(frameIndex, 0, frameCount - 1));
        }

        private void CacheFrames(IReadOnlyList<int> selectedFrameIndices)
        {
            (int targetWidth, int targetHeight) = ComputePreviewDimensions(gifImage.Width, gifImage.Height, maxDimension);

            for (int i = 0; i < selectedFrameIndices.Count; i++)
            {
                gifImage.SelectActiveFrame(DrawingFrameDimension.Time, selectedFrameIndices[i]);
                using DrawingBitmap bitmap = new DrawingBitmap(targetWidth, targetHeight, DrawingPixelFormat.Format32bppArgb);
                using (DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap))
                {
                    graphics.Clear(System.Drawing.Color.Transparent);
                    Ao2AnimationPreview.ApplyAo2GifInterpolation(graphics, gifImage.Height, targetHeight);
                    graphics.DrawImage(gifImage, 0, 0, targetWidth, targetHeight);
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

        private static (int width, int height) ComputePreviewDimensions(int sourceWidth, int sourceHeight, int maxDimension)
        {
            int safeWidth = Math.Max(1, sourceWidth);
            int safeHeight = Math.Max(1, sourceHeight);
            if (maxDimension <= 0)
            {
                return (safeWidth, safeHeight);
            }

            int largestSide = Math.Max(safeWidth, safeHeight);
            if (largestSide <= maxDimension)
            {
                return (safeWidth, safeHeight);
            }

            double scale = maxDimension / (double)largestSide;
            int width = Math.Max(1, (int)Math.Round(safeWidth * scale));
            int height = Math.Max(1, (int)Math.Round(safeHeight * scale));
            return (width, height);
        }

        private List<int> BuildSelectedFrameIndices(int sourceFrameCount)
        {
            if (maxFrames <= 0 || sourceFrameCount <= maxFrames)
            {
                return Enumerable.Range(0, sourceFrameCount).ToList();
            }

            List<int> selected = new List<int>(maxFrames);
            double stride = sourceFrameCount / (double)maxFrames;
            for (int i = 0; i < maxFrames; i++)
            {
                int sourceIndex = Math.Min(sourceFrameCount - 1, (int)Math.Round(i * stride));
                if (selected.Count == 0 || selected[selected.Count - 1] != sourceIndex)
                {
                    selected.Add(sourceIndex);
                }
            }

            if (selected.Count == 0 || selected[selected.Count - 1] != sourceFrameCount - 1)
            {
                selected.Add(sourceFrameCount - 1);
            }

            return selected;
        }

        private static List<TimeSpan> SampleFrameDurations(
            IReadOnlyList<TimeSpan> sourceDelays,
            IReadOnlyList<int> selectedFrameIndices)
        {
            List<TimeSpan> sampled = new List<TimeSpan>(selectedFrameIndices.Count);
            if (selectedFrameIndices.Count == 0)
            {
                return sampled;
            }

            for (int i = 0; i < selectedFrameIndices.Count; i++)
            {
                int start = Math.Clamp(selectedFrameIndices[i], 0, sourceDelays.Count - 1);
                int endExclusive = i + 1 < selectedFrameIndices.Count
                    ? Math.Clamp(selectedFrameIndices[i + 1], start + 1, sourceDelays.Count)
                    : sourceDelays.Count;

                TimeSpan total = TimeSpan.Zero;
                for (int sourceIndex = start; sourceIndex < endExclusive; sourceIndex++)
                {
                    total += sourceDelays[sourceIndex];
                }

                if (total <= TimeSpan.Zero)
                {
                    total = TimeSpan.FromMilliseconds(100);
                }

                sampled.Add(total);
            }

            return sampled;
        }

        private static void RegisterActivePlayer(GifAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                if (!ActivePlayers.Contains(player))
                {
                    ActivePlayers.Add(player);
                }

                ScheduleNextSharedTick(DateTime.UtcNow);
            }
        }

        private static void UnregisterActivePlayer(GifAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                ActivePlayers.Remove(player);

                ScheduleNextSharedTick(DateTime.UtcNow);
            }
        }

        private static void EnsureSharedTimer()
        {
            if (sharedTimer != null)
            {
                return;
            }

            Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            sharedTimer = new DispatcherTimer(DispatcherPriority.Render, dispatcher)
            {
                Interval = Ao2AnimationPreview.MinimumAnimationTickInterval
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

            lock (SharedTimerLock)
            {
                ScheduleNextSharedTick(nowUtc);
            }
        }

        private static void ScheduleNextSharedTick(DateTime nowUtc)
        {
            EnsureSharedTimer();
            if (sharedTimer == null)
            {
                return;
            }

            if (ActivePlayers.Count == 0)
            {
                sharedTimer.Stop();
                return;
            }

            TimeSpan nextDelay = ActivePlayers
                .Where(player => player.isRunning && player.frameCount > 0 && player.frameDurations.Count > 0)
                .Select(player => player.nextFrameAtUtc - nowUtc)
                .DefaultIfEmpty(Ao2AnimationPreview.MinimumAnimationTickInterval)
                .Min();

            if (nextDelay < Ao2AnimationPreview.MinimumAnimationTickInterval)
            {
                nextDelay = Ao2AnimationPreview.MinimumAnimationTickInterval;
            }

            sharedTimer.Stop();
            sharedTimer.Interval = nextDelay;
            sharedTimer.Start();
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
                        double milliseconds = Math.Max(0, delayUnits * 10d);
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
                delays.Add(TimeSpan.FromMilliseconds(100));
            }

            return delays;
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
