using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class AkechiWebpDiagnosticsTests
    {
        private const int Iterations = 50;

        [Test]
        [Explicit("Temporary diagnostics for Akechi WebP/APNG transparency behavior.")]
        public void Diagnose_Akechi_Normal_And_Happy_Webp_RenderingConsistency()
        {
            string? normalPath = ResolveAkechiAssetPath("Normal.webp");
            string? happyPath = ResolveAkechiAssetPath("Happy.webp");
            if (normalPath == null || happyPath == null)
            {
                Assert.Ignore("Akechi Normal.webp/Happy.webp were not found on this machine.");
                return;
            }

            string outputRoot = Path.Combine(
                Path.GetTempPath(),
                "oceanya_akechi_diag_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(outputRoot);

            string normalReport = RunDiagnosticsForAsset(normalPath, outputRoot);
            string happyReport = RunDiagnosticsForAsset(happyPath, outputRoot);

            string reportPath = Path.Combine(outputRoot, "akechi_webp_diagnostics.txt");
            File.WriteAllText(reportPath, normalReport + Environment.NewLine + happyReport, Encoding.UTF8);
            TestContext.WriteLine($"Akechi diagnostics report: {reportPath}");

            Assert.Pass($"Diagnostics generated at: {reportPath}");
        }

        private static string RunDiagnosticsForAsset(string assetPath, string outputRoot)
        {
            string assetName = Path.GetFileNameWithoutExtension(assetPath);
            string safeName = MakeSafeFileName(assetName);
            string assetOutputDir = Path.Combine(outputRoot, safeName);
            Directory.CreateDirectory(assetOutputDir);

            List<string> staticHashes = new List<string>();
            List<string> firstFrameHashes = new List<string>();
            List<string> playerFrameHashes = new List<string>();
            List<string> wicNoneHashes = new List<string>();
            List<string> wicPreserveHashes = new List<string>();
            List<string> iterationSummaries = new List<string>();

            for (int i = 0; i < Iterations; i++)
            {
                BitmapStats staticStats = CaptureStaticPreview(assetPath, out string staticHash, out BitmapSource? staticFrame);
                BitmapStats firstFrameStats = CaptureFirstFrame(assetPath, out string firstFrameHash, out BitmapSource? firstFrame);
                BitmapStats playerStats = CapturePlayerCurrentFrame(assetPath, out string playerHash, out BitmapSource? playerFrame);
                BitmapStats wicNoneStats = CaptureDirectWicFrame(
                    assetPath,
                    BitmapCreateOptions.None,
                    out string wicNoneHash,
                    out BitmapSource? wicNoneFrame);
                BitmapStats wicPreserveStats = CaptureDirectWicFrame(
                    assetPath,
                    BitmapCreateOptions.PreservePixelFormat,
                    out string wicPreserveHash,
                    out BitmapSource? wicPreserveFrame);

                staticHashes.Add(staticHash);
                firstFrameHashes.Add(firstFrameHash);
                playerFrameHashes.Add(playerHash);
                wicNoneHashes.Add(wicNoneHash);
                wicPreserveHashes.Add(wicPreserveHash);

                iterationSummaries.Add(
                    $"[{i + 1:00}] static={staticHash} {staticStats}; first={firstFrameHash} {firstFrameStats}; " +
                    $"player={playerHash} {playerStats}; wic-none={wicNoneHash} {wicNoneStats}; " +
                    $"wic-preserve={wicPreserveHash} {wicPreserveStats}");

                SaveSnapshotIfFirstSeen(Path.Combine(assetOutputDir, "static"), staticHash, staticFrame);
                SaveSnapshotIfFirstSeen(Path.Combine(assetOutputDir, "first"), firstFrameHash, firstFrame);
                SaveSnapshotIfFirstSeen(Path.Combine(assetOutputDir, "player"), playerHash, playerFrame);
                SaveSnapshotIfFirstSeen(Path.Combine(assetOutputDir, "wic-none"), wicNoneHash, wicNoneFrame);
                SaveSnapshotIfFirstSeen(Path.Combine(assetOutputDir, "wic-preserve"), wicPreserveHash, wicPreserveFrame);
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine($"Asset: {assetPath}");
            report.AppendLine($"Iterations: {Iterations}");
            report.AppendLine($"Unique hashes static: {staticHashes.Distinct(StringComparer.Ordinal).Count()}");
            report.AppendLine($"Unique hashes first-frame: {firstFrameHashes.Distinct(StringComparer.Ordinal).Count()}");
            report.AppendLine($"Unique hashes player: {playerFrameHashes.Distinct(StringComparer.Ordinal).Count()}");
            report.AppendLine($"Unique hashes wic-none: {wicNoneHashes.Distinct(StringComparer.Ordinal).Count()}");
            report.AppendLine($"Unique hashes wic-preserve: {wicPreserveHashes.Distinct(StringComparer.Ordinal).Count()}");
            report.AppendLine("Details:");
            foreach (string line in iterationSummaries)
            {
                report.AppendLine(line);
            }

            return report.ToString();
        }

        private static BitmapStats CaptureStaticPreview(string path, out string hash, out BitmapSource? source)
        {
            ImageSource image = Ao2AnimationPreview.LoadStaticPreviewImage(path, decodePixelWidth: 0);
            return ExtractStats(image, out hash, out source);
        }

        private static BitmapStats CaptureFirstFrame(string path, out string hash, out BitmapSource? source)
        {
            if (Ao2AnimationPreview.TryLoadFirstFrame(path, out ImageSource? frame, out _))
            {
                return ExtractStats(frame, out hash, out source);
            }

            hash = "first-load-failed";
            source = null;
            return BitmapStats.Empty("first-load-failed");
        }

        private static BitmapStats CapturePlayerCurrentFrame(string path, out string hash, out BitmapSource? source)
        {
            if (Ao2AnimationPreview.TryCreateAnimationPlayer(path, loop: true, out IAnimationPlayer? player) && player != null)
            {
                try
                {
                    return ExtractStats(player.CurrentFrame, out hash, out source);
                }
                finally
                {
                    player.Stop();
                }
            }

            hash = "player-create-failed";
            source = null;
            return BitmapStats.Empty("player-create-failed");
        }

        private static BitmapStats CaptureDirectWicFrame(
            string path,
            BitmapCreateOptions options,
            out string hash,
            out BitmapSource? source)
        {
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(path, UriKind.Absolute),
                    options,
                    BitmapCacheOption.OnLoad);
                BitmapFrame? first = decoder.Frames.FirstOrDefault();
                if (first == null)
                {
                    hash = "wic-empty";
                    source = null;
                    return BitmapStats.Empty("wic-empty");
                }

                BitmapSource converted = ConvertToPbgra32(first);
                return ExtractStats(converted, out hash, out source);
            }
            catch (Exception ex)
            {
                hash = $"wic-error:{ex.GetType().Name}";
                source = null;
                return BitmapStats.Empty(hash);
            }
        }

        private static BitmapStats ExtractStats(ImageSource? image, out string hash, out BitmapSource? source)
        {
            if (image is not BitmapSource bitmap)
            {
                hash = "not-bitmap";
                source = null;
                return BitmapStats.Empty("not-bitmap");
            }

            BitmapSource converted = ConvertToPbgra32(bitmap);
            source = converted;

            int width = Math.Max(1, converted.PixelWidth);
            int height = Math.Max(1, converted.PixelHeight);
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            int transparentPixels = 0;
            int opaqueBlackPixels = 0;
            int nonOpaquePixels = 0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a == 0)
                {
                    transparentPixels++;
                }

                if (a < 255)
                {
                    nonOpaquePixels++;
                }

                if (a == 255 && r == 0 && g == 0 && b == 0)
                {
                    opaqueBlackPixels++;
                }
            }

            using MD5 md5 = MD5.Create();
            byte[] digest = md5.ComputeHash(pixels);
            hash = Convert.ToHexString(digest);

            return new BitmapStats(width, height, transparentPixels, nonOpaquePixels, opaqueBlackPixels, converted.Format.ToString());
        }

        private static BitmapSource ConvertToPbgra32(BitmapSource source)
        {
            if (source.Format == PixelFormats.Pbgra32)
            {
                BitmapSource clone = source.Clone();
                if (clone.CanFreeze)
                {
                    clone.Freeze();
                }

                return clone;
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

        private static void SaveSnapshotIfFirstSeen(string directory, string hash, BitmapSource? frame)
        {
            if (frame == null || string.IsNullOrWhiteSpace(hash))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, hash + ".png");
            if (File.Exists(path))
            {
                return;
            }

            using FileStream stream = File.Create(path);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            encoder.Save(stream);
        }

        private static string? ResolveAkechiAssetPath(string fileName)
        {
            string[] candidates =
            {
                $@"D:\Programs\Attorney Online\base\characters\Akechi\(a)\{fileName}",
                Path.Combine("/mnt/d/Programs/Attorney Online/base/characters/Akechi/(a)", fileName)
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string MakeSafeFileName(string value)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            StringBuilder sanitized = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sanitized.Append(invalidChars.Contains(c) ? '_' : c);
            }

            return sanitized.ToString();
        }

        private readonly struct BitmapStats
        {
            public BitmapStats(
                int width,
                int height,
                int transparentPixels,
                int nonOpaquePixels,
                int opaqueBlackPixels,
                string format)
            {
                Width = width;
                Height = height;
                TransparentPixels = transparentPixels;
                NonOpaquePixels = nonOpaquePixels;
                OpaqueBlackPixels = opaqueBlackPixels;
                Format = format ?? string.Empty;
            }

            public int Width { get; }
            public int Height { get; }
            public int TransparentPixels { get; }
            public int NonOpaquePixels { get; }
            public int OpaqueBlackPixels { get; }
            public string Format { get; }

            public static BitmapStats Empty(string format) => new BitmapStats(0, 0, 0, 0, 0, format);

            public override string ToString()
            {
                return $"{Width}x{Height} fmt={Format} transparent={TransparentPixels} nonOpaque={NonOpaquePixels} opaqueBlack={OpaqueBlackPixels}";
            }
        }
    }
}
