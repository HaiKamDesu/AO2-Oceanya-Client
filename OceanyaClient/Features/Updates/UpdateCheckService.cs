using Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OceanyaClient.Features.Updates
{
    public sealed class UpdateCheckService
    {
        private readonly GitHubUpdateClient githubClient;
        private readonly UpdateStagingService stagingService;
        private readonly UpdateEnvironment environment;

        public UpdateCheckService(
            GitHubUpdateClient? githubClient = null,
            UpdateStagingService? stagingService = null,
            UpdateEnvironment? environment = null)
        {
            this.environment = environment ?? UpdateEnvironment.Current;
            this.githubClient = githubClient ?? new GitHubUpdateClient();
            this.stagingService = stagingService ?? new UpdateStagingService(paths: new UpdateStoragePaths(this.environment));
        }

        public UpdateRelease? LastAvailableRelease { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public UpdateEnvironment Environment => environment;

        public async Task<UpdateRelease?> CheckForUpdateAsync(bool interactive, CancellationToken cancellationToken)
        {
            LastError = string.Empty;
            try
            {
                if (!UpdateVersion.TryParseForChannel(AppVersionInfo.DisplayVersion, environment.Channel, out UpdateVersion currentVersion))
                {
                    throw new InvalidOperationException("The current app version is not update-checkable.");
                }

                UpdaterChannelSettings channelSettings = GetChannelSettings();
                UpdateRelease? release = await githubClient.GetLatestReleaseAsync(environment, currentVersion, cancellationToken).ConfigureAwait(false);
                channelSettings.LastCheckUtc = DateTimeOffset.UtcNow;
                if (release != null)
                {
                    channelSettings.LastSeenReleaseTag = release.Manifest.Tag;
                    channelSettings.LastSeenReleaseVersion = release.Manifest.Version;
                }

                MirrorLegacyStableSettingsIfNeeded(channelSettings);
                SaveFile.Save();
                LastAvailableRelease = release;
                return release;
            }
            catch (Exception ex) when (!interactive && IsSilentStartupFailure(ex))
            {
                LastError = ex.Message;
                UpdaterChannelSettings channelSettings = GetChannelSettings();
                channelSettings.LastFailureUtc = DateTimeOffset.UtcNow;
                MirrorLegacyStableSettingsIfNeeded(channelSettings);
                SaveFile.Save();
                CustomConsole.Warning("Automatic update check failed.", ex);
                return null;
            }
        }

        public bool IsSkipped(UpdateRelease release)
        {
            return string.Equals(
                GetChannelSettings().SkippedReleaseTag,
                release.Manifest.Tag,
                StringComparison.OrdinalIgnoreCase);
        }

        public void Skip(UpdateRelease release)
        {
            UpdaterChannelSettings channelSettings = GetChannelSettings();
            channelSettings.SkippedReleaseTag = release.Manifest.Tag;
            channelSettings.SkippedReleaseVersion = release.Manifest.Version;
            MirrorLegacyStableSettingsIfNeeded(channelSettings);
            SaveFile.Save();
        }

        public async Task<UpdateStagingResult> StageUpdateAsync(
            UpdateRelease release,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            if (!UpdateStagingService.IsInstallFolderWritable(AppContext.BaseDirectory))
            {
                throw new UnauthorizedAccessException("The current install folder is not writable. Download the release manually or move Oceanya to a user-writable folder.");
            }

            return await stagingService.DownloadVerifyAndStageAsync(release, progress, cancellationToken).ConfigureAwait(false);
        }

        public void LaunchUpdaterAndExit(UpdateRelease release, UpdateStagingResult staging)
        {
            string updaterPath = PrepareExternalUpdaterRunner();
            if (!File.Exists(updaterPath))
            {
                throw new FileNotFoundException("OceanyaUpdater.exe is missing from the install folder.", updaterPath);
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--source");
            startInfo.ArgumentList.Add(staging.PackageRoot);
            startInfo.ArgumentList.Add("--install");
            startInfo.ArgumentList.Add(AppContext.BaseDirectory);
            startInfo.ArgumentList.Add("--backup");
            startInfo.ArgumentList.Add(staging.BackupRoot);
            startInfo.ArgumentList.Add("--parent-pid");
            startInfo.ArgumentList.Add(System.Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--entry-exe");
            startInfo.ArgumentList.Add(release.Manifest.EntryExe);
            startInfo.ArgumentList.Add("--log");
            startInfo.ArgumentList.Add(staging.LogPath);

            Process.Start(startInfo);
            System.Windows.Application.Current.Shutdown();
        }

        private string PrepareExternalUpdaterRunner()
        {
            string installUpdaterPath = Path.Combine(AppContext.BaseDirectory, "OceanyaUpdater.exe");
            if (!File.Exists(installUpdaterPath))
            {
                throw new FileNotFoundException("OceanyaUpdater.exe is missing from the install folder.", installUpdaterPath);
            }

            UpdateStoragePaths paths = new UpdateStoragePaths(environment);
            string runnerDirectory = Path.Combine(paths.Root, "runner");
            Directory.CreateDirectory(runnerDirectory);

            CopyIfExists(installUpdaterPath, Path.Combine(runnerDirectory, "OceanyaUpdater.exe"));
            CopyIfExists(Path.Combine(AppContext.BaseDirectory, "OceanyaUpdater.deps.json"), Path.Combine(runnerDirectory, "OceanyaUpdater.deps.json"));
            CopyIfExists(Path.Combine(AppContext.BaseDirectory, "OceanyaUpdater.runtimeconfig.json"), Path.Combine(runnerDirectory, "OceanyaUpdater.runtimeconfig.json"));
            return Path.Combine(runnerDirectory, "OceanyaUpdater.exe");
        }

        private static void CopyIfExists(string source, string destination)
        {
            if (!File.Exists(source))
            {
                return;
            }

            File.Copy(source, destination, overwrite: true);
        }

        private static bool IsSilentStartupFailure(Exception ex)
        {
            return ex is HttpRequestException
                || ex is TaskCanceledException
                || ex is InvalidOperationException;
        }

        private UpdaterChannelSettings GetChannelSettings()
        {
            SaveFile.Data.Updater.Stable ??= new UpdaterChannelSettings();
            SaveFile.Data.Updater.Test ??= new UpdaterChannelSettings();
            return environment.Channel == UpdateChannel.Test
                ? SaveFile.Data.Updater.Test
                : SaveFile.Data.Updater.Stable;
        }

        private void MirrorLegacyStableSettingsIfNeeded(UpdaterChannelSettings channelSettings)
        {
            if (environment.Channel != UpdateChannel.Stable)
            {
                return;
            }

            SaveFile.Data.Updater.SkippedReleaseTag = channelSettings.SkippedReleaseTag;
            SaveFile.Data.Updater.SkippedReleaseVersion = channelSettings.SkippedReleaseVersion;
            SaveFile.Data.Updater.LastSeenReleaseTag = channelSettings.LastSeenReleaseTag;
            SaveFile.Data.Updater.LastSeenReleaseVersion = channelSettings.LastSeenReleaseVersion;
            SaveFile.Data.Updater.LastCheckUtc = channelSettings.LastCheckUtc;
            SaveFile.Data.Updater.LastFailureUtc = channelSettings.LastFailureUtc;
        }
    }
}
