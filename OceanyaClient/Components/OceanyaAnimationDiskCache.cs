using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Persists decoded animation frames to disk so they survive app restarts.
    /// Cache files are stored under %LocalAppData%/Oceanya/AnimCache/.
    /// The cache key is SHA256(lowercased path) + lastWriteUtc.Ticks + maxDimension,
    /// so stale entries are automatically replaced when a source file changes.
    /// </summary>
    internal static class OceanyaAnimationDiskCache
    {
        private static readonly byte[] Magic = { (byte)'O', (byte)'C', (byte)'A', (byte)'M' };
        private const byte FormatVersion = 1;
        private const string CacheExtension = ".oceanya_anim";
        private static readonly string CacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Oceanya", "AnimCache");

        /// <summary>
        /// Attempts to load animation frames from the disk cache.
        /// Returns true and populates <paramref name="frames"/> / <paramref name="frameDurations"/> on a hit.
        /// All returned BitmapSources are frozen and safe to use on any thread.
        /// </summary>
        internal static bool TryLoad(
            string path,
            DateTime lastWriteUtc,
            int maxDimension,
            out List<BitmapSource>? frames,
            out List<TimeSpan>? frameDurations)
        {
            frames = null;
            frameDurations = null;

            try
            {
                string cacheFile = BuildCacheFilePath(path, lastWriteUtc, maxDimension);
                if (!File.Exists(cacheFile))
                {
                    return false;
                }

                return TryReadCacheFile(cacheFile, out frames, out frameDurations);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Saves decoded animation frames to the disk cache.
        /// When called from a background thread (e.g. prefetch or asset refresh) the write is
        /// synchronous so that callers can depend on the file being present when they return.
        /// When called from the UI thread the write is dispatched to a background task to avoid
        /// any frame drop — this is the "first-time decode" path where the cache did not exist yet.
        /// </summary>
        internal static void SaveAsync(
            string path,
            DateTime lastWriteUtc,
            int maxDimension,
            IReadOnlyList<BitmapSource> frames,
            IReadOnlyList<TimeSpan> frameDurations)
        {
            if (frames.Count <= 1 || frames.Count != frameDurations.Count)
            {
                return;
            }

            // Capture raw pixel data synchronously. BitmapSources must already be frozen.
            var frameData = new List<(int Width, int Height, byte[] Pixels)>(frames.Count);
            var durationTicks = new List<long>(frameDurations.Count);

            foreach (BitmapSource frame in frames)
            {
                int w = frame.PixelWidth;
                int h = frame.PixelHeight;
                int stride = w * 4;
                byte[] pixels = new byte[stride * h];
                frame.CopyPixels(pixels, stride, 0);
                frameData.Add((w, h, pixels));
            }

            foreach (TimeSpan d in frameDurations)
            {
                durationTicks.Add(d.Ticks);
            }

            string cacheFile = BuildCacheFilePath(path, lastWriteUtc, maxDimension);
            string sha = ComputeSha256(path);

            // If we are already on a background thread write synchronously — callers like PrefetchBackground
            // and asset-refresh prebaking need the file present before they return.
            // On the UI thread use fire-and-forget to avoid blocking rendering.
            bool isOnUiThread = Application.Current?.Dispatcher.CheckAccess() ?? true;
            if (!isOnUiThread)
            {
                WriteToDisk(cacheFile, sha, lastWriteUtc, frameData, durationTicks);
            }
            else
            {
                Task.Run(() => WriteToDisk(cacheFile, sha, lastWriteUtc, frameData, durationTicks));
            }
        }

        private static void WriteToDisk(
            string cacheFile,
            string sha,
            DateTime lastWriteUtc,
            List<(int Width, int Height, byte[] Pixels)> frameData,
            List<long> durationTicks)
        {
            try
            {
                string dir = Path.GetDirectoryName(cacheFile)!;
                Directory.CreateDirectory(dir);
                DeleteStaleEntries(dir, sha, lastWriteUtc);

                string tempFile = cacheFile + ".tmp";
                using (FileStream fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(Magic);
                    writer.Write(FormatVersion);
                    writer.Write(frameData.Count);

                    for (int i = 0; i < frameData.Count; i++)
                    {
                        var (w, h, pixels) = frameData[i];
                        writer.Write(w);
                        writer.Write(h);
                        writer.Write(durationTicks[i]);
                        writer.Write(pixels);
                    }
                }

                File.Move(tempFile, cacheFile, overwrite: true);
            }
            catch
            {
                // Non-fatal: a failed save just means we re-decode on next open.
            }
        }

        private static bool TryReadCacheFile(
            string cacheFile,
            out List<BitmapSource>? frames,
            out List<TimeSpan>? frameDurations)
        {
            frames = null;
            frameDurations = null;

            using FileStream fs = new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new BinaryReader(fs);

            byte[] magic = reader.ReadBytes(4);
            if (magic.Length < 4
                || magic[0] != Magic[0] || magic[1] != Magic[1]
                || magic[2] != Magic[2] || magic[3] != Magic[3])
            {
                return false;
            }

            byte version = reader.ReadByte();
            if (version != FormatVersion)
            {
                return false;
            }

            int count = reader.ReadInt32();
            if (count <= 1)
            {
                return false;
            }

            var loadedFrames = new List<BitmapSource>(count);
            var loadedDurations = new List<TimeSpan>(count);

            for (int i = 0; i < count; i++)
            {
                int w = reader.ReadInt32();
                int h = reader.ReadInt32();
                long ticks = reader.ReadInt64();
                int pixelByteCount = w * h * 4;
                byte[] pixels = reader.ReadBytes(pixelByteCount);

                if (pixels.Length != pixelByteCount || w <= 0 || h <= 0)
                {
                    return false;
                }

                BitmapSource bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
                if (bmp.CanFreeze)
                {
                    bmp.Freeze();
                }

                loadedFrames.Add(bmp);
                loadedDurations.Add(TimeSpan.FromTicks(ticks));
            }

            frames = loadedFrames;
            frameDurations = loadedDurations;
            return true;
        }

        private static void DeleteStaleEntries(string dir, string sha, DateTime currentLastWriteUtc)
        {
            try
            {
                string expectedTicksStr = currentLastWriteUtc.Ticks.ToString();
                foreach (string file in Directory.GetFiles(dir, $"{sha}_*{CacheExtension}"))
                {
                    // filename: {sha}_{ticks}_{maxDim}.oceanya_anim
                    string name = Path.GetFileNameWithoutExtension(file);
                    string rest = name.Length > sha.Length ? name.Substring(sha.Length) : string.Empty;
                    if (!rest.StartsWith($"_{expectedTicksStr}_", StringComparison.Ordinal))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        private static string BuildCacheFilePath(string path, DateTime lastWriteUtc, int maxDimension)
        {
            string sha = ComputeSha256(path);
            string subDir = sha.Substring(0, 2);
            string fileName = $"{sha}_{lastWriteUtc.Ticks}_{maxDimension}{CacheExtension}";
            return Path.Combine(CacheRoot, subDir, fileName);
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
