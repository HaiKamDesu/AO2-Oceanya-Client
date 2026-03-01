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
using ImageMagick;
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
        private const double DefaultFrameDelayMilliseconds = 100d;
        private const int StaticPreviewCacheEntryLimit = 256;

        private static readonly ImageSource FallbackImage = LoadEmbeddedFallback();
        private static readonly object StaticPreviewCacheLock = new object();
        private static readonly Dictionary<(string path, int width), WeakReference<ImageSource>> StaticPreviewCache =
            new Dictionary<(string path, int width), WeakReference<ImageSource>>();

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

            int normalizedDecodeWidth = Math.Max(0, decodePixelWidth);
            if (TryGetCachedStaticPreview(resolvedPath, normalizedDecodeWidth, out ImageSource? cachedImage)
                && cachedImage != null)
            {
                return cachedImage;
            }

            ImageSource result;
            if (TryLoadFirstFrame(resolvedPath, out ImageSource? firstFrame, out _)
                && firstFrame != null
                && IsPotentialAnimatedPath(resolvedPath))
            {
                result = firstFrame is BitmapSource bitmapFirstFrame
                    ? ScaleBitmapForPreview(bitmapFirstFrame, decodePixelWidth)
                    : firstFrame;
                CacheStaticPreview(resolvedPath, normalizedDecodeWidth, result);
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

            CacheStaticPreview(resolvedPath, normalizedDecodeWidth, result);
            return result;
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
            if (extension != ".gif" && extension != ".webp" && extension != ".apng")
            {
                return false;
            }

            if (BitmapFrameAnimationPlayer.TryCreate(resolvedPath, loop, out BitmapFrameAnimationPlayer? bitmapPlayer)
                && bitmapPlayer != null)
            {
                player = bitmapPlayer;
                return true;
            }

            if (extension == ".gif")
            {
                try
                {
                    player = new GifAnimationPlayer(resolvedPath, loop);
                    return true;
                }
                catch
                {
                    // ignored
                }
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
                string extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
                if (IsPotentialAnimatedPath(resolvedPath)
                    && TryDecodeFirstAnimationFrame(resolvedPath, out BitmapSource? decodedFrame, out double decodedDurationMs)
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

        internal static bool TryDecodeFirstAnimationFrame(string path, out BitmapSource? frame, out double estimatedDurationMs)
        {
            frame = null;
            estimatedDurationMs = 0;

            try
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (ShouldPreferMagickDecoder(extension)
                    && TryDecodeAnimationFramesWithMagick(path, out List<BitmapSource>? magickFrames, out List<TimeSpan>? magickDurations)
                    && magickFrames != null
                    && magickDurations != null
                    && magickFrames.Count > 0)
                {
                    frame = magickFrames[0];
                    estimatedDurationMs = magickDurations.Sum(duration => duration.TotalMilliseconds);
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
            out List<TimeSpan>? frameDurations)
        {
            frames = null;
            frameDurations = null;

            try
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (ShouldPreferMagickDecoder(extension)
                    && TryDecodeAnimationFramesWithMagick(path, out List<BitmapSource>? magickFrames, out List<TimeSpan>? magickDurations)
                    && magickFrames != null
                    && magickDurations != null
                    && magickFrames.Count > 1
                    && magickFrames.Count == magickDurations.Count)
                {
                    frames = magickFrames;
                    frameDurations = magickDurations;
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
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldPreferMagickDecoder(string extension)
        {
            return string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".apng", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDecodeAnimationFramesWithMagick(
            string path,
            out List<BitmapSource>? frames,
            out List<TimeSpan>? frameDurations)
        {
            frames = null;
            frameDurations = null;

            try
            {
                using MagickImageCollection source = new MagickImageCollection(path);
                if (source.Count == 0)
                {
                    return false;
                }

                source.Coalesce();
                if (source.Count == 0)
                {
                    return false;
                }

                List<BitmapSource> decodedFrames = new List<BitmapSource>(source.Count);
                List<TimeSpan> decodedDurations = new List<TimeSpan>(source.Count);
                foreach (MagickImage image in source)
                {
                    int width = Math.Max(1, (int)image.Width);
                    int height = Math.Max(1, (int)image.Height);
                    int stride = width * 4;
                    byte[]? pixels = image.GetPixels().ToByteArray(PixelMapping.BGRA);
                    if (pixels == null || pixels.Length == 0)
                    {
                        continue;
                    }

                    BitmapSource frame = BitmapSource.Create(
                        width,
                        height,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null,
                        pixels,
                        stride);
                    decodedFrames.Add(NormalizeBitmapForUi(frame));
                    decodedDurations.Add(ReadMagickDelay(image));
                }

                frames = decodedFrames;
                frameDurations = decodedDurations;
                return decodedFrames.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static TimeSpan ReadMagickDelay(MagickImage image)
        {
            int ticksPerSecond = (int)image.AnimationTicksPerSecond;
            if (ticksPerSecond <= 0)
            {
                ticksPerSecond = 100;
            }

            int delayTicks = (int)image.AnimationDelay;
            if (delayTicks <= 0)
            {
                return TimeSpan.FromMilliseconds(DefaultFrameDelayMilliseconds);
            }

            double milliseconds = (delayTicks * 1000d) / ticksPerSecond;
            if (milliseconds <= 0)
            {
                milliseconds = DefaultFrameDelayMilliseconds;
            }

            return TimeSpan.FromMilliseconds(milliseconds);
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

        private static bool TryGetCachedStaticPreview(string path, int decodePixelWidth, out ImageSource? image)
        {
            image = null;
            (string path, int width) cacheKey = (path, decodePixelWidth);
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

        private static void CacheStaticPreview(string path, int decodePixelWidth, ImageSource image)
        {
            (string path, int width) cacheKey = (path, decodePixelWidth);
            lock (StaticPreviewCacheLock)
            {
                StaticPreviewCache[cacheKey] = new WeakReference<ImageSource>(image);
                if (StaticPreviewCache.Count <= StaticPreviewCacheEntryLimit)
                {
                    return;
                }

                List<(string path, int width)> keys = StaticPreviewCache.Keys.ToList();
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
                if (!Ao2AnimationPreview.TryDecodeAnimationFrames(path, out List<BitmapSource>? decodedFrames, out List<TimeSpan>? decodedDurations)
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
