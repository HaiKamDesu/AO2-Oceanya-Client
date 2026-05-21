using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OceanyaClient.Features.Updates
{
    public sealed class UpdateStagingResult
    {
        public string DownloadPath { get; set; } = string.Empty;
        public string ExtractionRoot { get; set; } = string.Empty;
        public string PackageRoot { get; set; } = string.Empty;
        public string BackupRoot { get; set; } = string.Empty;
        public string LogPath { get; set; } = string.Empty;
    }

    public sealed class UpdateStagingService
    {
        private readonly HttpClient httpClient;
        private readonly UpdateStoragePaths paths;

        public UpdateStagingService(HttpClient? httpClient = null, UpdateStoragePaths? paths = null)
        {
            this.httpClient = httpClient ?? new HttpClient();
            this.paths = paths ?? new UpdateStoragePaths();
        }

        public async Task<UpdateStagingResult> DownloadVerifyAndStageAsync(
            UpdateRelease release,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            paths.EnsureCreated();
            string safeTag = release.Manifest.Tag.Trim().Replace('/', '_').Replace('\\', '_');
            string downloadPath = Path.Combine(paths.Downloads, release.Manifest.AssetName);
            string extractionRoot = Path.Combine(paths.Staged, safeTag);
            string backupRoot = Path.Combine(paths.Backups, safeTag + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));

            await DownloadAsync(release.PackageAsset.BrowserDownloadUrl, downloadPath, progress, cancellationToken).ConfigureAwait(false);
            string actualHash = await ComputeSha256Async(downloadPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, release.Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The downloaded update package SHA-256 does not match the manifest.");
            }

            string packageRoot = UpdateZipValidator.ExtractValidatedPackage(downloadPath, extractionRoot);
            return new UpdateStagingResult
            {
                DownloadPath = downloadPath,
                ExtractionRoot = extractionRoot,
                PackageRoot = packageRoot,
                BackupRoot = backupRoot,
                LogPath = paths.UpdaterLogPath
            };
        }

        private async Task DownloadAsync(
            string url,
            string destinationPath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The update package URL is not a recognized GitHub HTTPS URL.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? string.Empty);
            string tempPath = destinationPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using HttpResponseMessage response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream destination = File.Create(tempPath);
            byte[] buffer = new byte[1024 * 128];
            long readTotal = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                readTotal += read;
                if (totalBytes > 0)
                {
                    progress?.Report(Math.Clamp((double)readTotal / totalBytes.Value, 0d, 1d));
                }
            }

            destination.Close();
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
            progress?.Report(1d);
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static bool IsInstallFolderWritable(string installDirectory)
        {
            try
            {
                Directory.CreateDirectory(installDirectory);
                string probePath = Path.Combine(installDirectory, ".oceanya-update-write-test-" + Process.GetCurrentProcess().Id);
                File.WriteAllText(probePath, "test");
                File.Delete(probePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
