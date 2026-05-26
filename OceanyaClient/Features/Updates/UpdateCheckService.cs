using Common;
using OceanyaClient.Features.FileHivemind;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OceanyaClient.Features.Updates
{
    public sealed class UpdateCheckService
    {
        private static readonly string[] UpdaterRuntimeFiles =
        {
            "OceanyaUpdater.exe",
            "OceanyaUpdater.dll",
            "OceanyaUpdater.deps.json",
            "OceanyaUpdater.runtimeconfig.json"
        };

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

        public async Task<FileHivemindAgentStopResult> StopFileHivemindForUpdateAsync(
            Action<string>? trace,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                FileHivemindAgentStopCoordinator stopCoordinator = new FileHivemindAgentStopCoordinator(
                    installDirectory: AppContext.BaseDirectory,
                    trace: trace);
                FileHivemindAgentStopResult result = stopCoordinator.RequestStopAndWait(TimeSpan.FromSeconds(20));
                if (!result.Stopped)
                {
                    throw new InvalidOperationException(
                        "The Oceanyan File Hivemind could not be stopped. Update cancelled before download. "
                        + "Stop requested: "
                        + (result.StopRequested ? "yes" : "no")
                        + "; forced processes found: "
                        + result.ForcedProcessCount
                        + ".");
                }

                return result;
            }, cancellationToken).ConfigureAwait(false);
        }

        public bool RestartFileHivemindAfterFailedUpdatePreparation()
        {
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher();
            if (!FileHivemindBackgroundAgentLauncher.HasEligibleConnections(SaveFile.Data.FileHivemind))
            {
                return false;
            }

            return launcher.StartForCurrentSession(SaveFile.Data.FileHivemind);
        }

        public void LaunchUpdaterAndExit(UpdateRelease release, UpdateStagingResult staging)
        {
            string updaterPath = PrepareExternalUpdaterRunner();
            ValidateStagedPackage(staging, release.Manifest.EntryExe);
            UpdateStoragePaths paths = new UpdateStoragePaths(environment);
            paths.EnsureCreated();
            string clientExePath = Path.Combine(AppContext.BaseDirectory, release.Manifest.EntryExe);
            string handoffPath = Path.Combine(
                paths.Handoffs,
                "handoff-" + SanitizeFileName(release.Manifest.Tag) + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(updaterPath) ?? AppContext.BaseDirectory
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
            startInfo.ArgumentList.Add("--download");
            startInfo.ArgumentList.Add(staging.DownloadPath);
            startInfo.ArgumentList.Add("--extraction-root");
            startInfo.ArgumentList.Add(staging.ExtractionRoot);
            startInfo.ArgumentList.Add("--channel");
            startInfo.ArgumentList.Add(environment.ChannelName);
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(release.Manifest.Version);
            startInfo.ArgumentList.Add("--client-exe");
            startInfo.ArgumentList.Add(clientExePath);
            startInfo.ArgumentList.Add("--handoff");
            startInfo.ArgumentList.Add(handoffPath);
            startInfo.ArgumentList.Add("--visible");

            WriteUpdaterHandoff(handoffPath, release, staging, updaterPath, clientExePath, startInfo);
            Process? updaterProcess = Process.Start(startInfo);
            if (updaterProcess == null)
            {
                throw new InvalidOperationException(
                    "Windows did not start OceanyaUpdater.exe. Handoff details were written to: " + handoffPath);
            }

            System.Windows.Application.Current.Shutdown();
        }

        private string PrepareExternalUpdaterRunner()
        {
            ValidateUpdaterRuntimeFiles(AppContext.BaseDirectory);

            UpdateStoragePaths paths = new UpdateStoragePaths(environment);
            string runnerDirectory = Path.Combine(paths.Root, "runner");
            Directory.CreateDirectory(runnerDirectory);

            foreach (string fileName in UpdaterRuntimeFiles)
            {
                CopyRequired(Path.Combine(AppContext.BaseDirectory, fileName), Path.Combine(runnerDirectory, fileName));
            }

            ValidateUpdaterRuntimeFiles(runnerDirectory);
            return Path.Combine(runnerDirectory, "OceanyaUpdater.exe");
        }

        public static void ValidateUpdaterRuntimeFiles(string directory)
        {
            string[] missing = UpdaterRuntimeFiles
                .Select(file => Path.Combine(directory, file))
                .Where(path => !File.Exists(path))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new FileNotFoundException(
                    "The external updater cannot be started because required files are missing beside OceanyaClient.exe: "
                    + string.Join(", ", missing));
            }

            string runtimeConfigPath = Path.Combine(directory, "OceanyaUpdater.runtimeconfig.json");
            if (!RuntimeConfigIncludesWindowsDesktop(runtimeConfigPath))
            {
                throw new InvalidOperationException(
                    "The external updater runtime config is stale or invalid. "
                    + "It must reference Microsoft.WindowsDesktop.App so the visible updater window can start: "
                    + runtimeConfigPath);
            }
        }

        private static void CopyRequired(string source, string destination)
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Required updater runtime file is missing.", source);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
            File.Copy(source, destination, overwrite: true);
        }

        private static void ValidateStagedPackage(UpdateStagingResult staging, string entryExe)
        {
            if (string.IsNullOrWhiteSpace(staging.PackageRoot) || !Directory.Exists(staging.PackageRoot))
            {
                throw new DirectoryNotFoundException("The staged update folder was not found: " + staging.PackageRoot);
            }

            string stagedEntry = Path.Combine(staging.PackageRoot, entryExe);
            if (!File.Exists(stagedEntry))
            {
                throw new FileNotFoundException("The staged update is missing " + entryExe + ".", stagedEntry);
            }

            EnsureStagedUpdaterRuntime(staging.PackageRoot);
        }

        private static void EnsureStagedUpdaterRuntime(string packageRoot)
        {
            foreach (string fileName in UpdaterRuntimeFiles)
            {
                string stagedPath = Path.Combine(packageRoot, fileName);
                string currentPath = Path.Combine(AppContext.BaseDirectory, fileName);
                if (!File.Exists(currentPath))
                {
                    throw new FileNotFoundException(
                        "The staged update and current install are missing required updater runtime file " + fileName + ".",
                        stagedPath);
                }

                File.Copy(currentPath, stagedPath, overwrite: true);
            }

            ValidateUpdaterRuntimeFiles(packageRoot);
        }

        private static bool RuntimeConfigIncludesWindowsDesktop(string runtimeConfigPath)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));
                if (!document.RootElement.TryGetProperty("runtimeOptions", out JsonElement runtimeOptions))
                {
                    return false;
                }

                if (runtimeOptions.TryGetProperty("framework", out JsonElement framework)
                    && IsWindowsDesktopFramework(framework))
                {
                    return true;
                }

                if (!runtimeOptions.TryGetProperty("frameworks", out JsonElement frameworks)
                    || frameworks.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (JsonElement item in frameworks.EnumerateArray())
                {
                    if (IsWindowsDesktopFramework(item))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsWindowsDesktopFramework(JsonElement framework)
        {
            return framework.ValueKind == JsonValueKind.Object
                && framework.TryGetProperty("name", out JsonElement name)
                && string.Equals(name.GetString(), "Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase);
        }

        private void WriteUpdaterHandoff(
            string handoffPath,
            UpdateRelease release,
            UpdateStagingResult staging,
            string updaterPath,
            string clientExePath,
            ProcessStartInfo startInfo)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(handoffPath) ?? string.Empty);
            var handoff = new
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                Channel = environment.ChannelName,
                Version = release.Manifest.Version,
                Tag = release.Manifest.Tag,
                StagedPath = staging.PackageRoot,
                DownloadPath = staging.DownloadPath,
                TargetInstallPath = AppContext.BaseDirectory,
                ClientExePath = clientExePath,
                UpdaterPath = updaterPath,
                ParentProcessId = System.Environment.ProcessId,
                LogPath = staging.LogPath,
                Arguments = startInfo.ArgumentList.ToArray()
            };

            File.WriteAllText(
                handoffPath,
                JsonSerializer.Serialize(handoff, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string SanitizeFileName(string value)
        {
            string safe = value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(safe) ? "update" : safe;
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
