using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OceanyaUpdater
{
    public static class UpdaterExitCodes
    {
        public const int Success = 0;
        public const int ApplyFailed = 1;
        public const int ArgumentError = 2;
        public const int ParentTimeout = 3;
        public const int RelaunchFailed = 4;
    }

    public sealed class UpdaterArguments
    {
        public string Source { get; init; } = string.Empty;
        public string Install { get; init; } = string.Empty;
        public string Backup { get; init; } = string.Empty;
        public int ParentPid { get; init; }
        public string EntryExe { get; init; } = "OceanyaClient.exe";
        public string Log { get; init; } = string.Empty;
        public string Channel { get; init; } = "stable";
        public string Version { get; init; } = string.Empty;
        public string ClientExe { get; init; } = string.Empty;
        public string Handoff { get; init; } = string.Empty;
        public string Download { get; init; } = string.Empty;
        public string ExtractionRoot { get; init; } = string.Empty;
        public bool Quiet { get; init; }

        public static bool TryParse(string[] args, out UpdaterArguments parsed, out string error)
        {
            parsed = new UpdaterArguments();
            error = string.Empty;
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index++)
            {
                string key = args[index]?.Trim() ?? string.Empty;
                if (!key.StartsWith("--", StringComparison.Ordinal))
                {
                    error = "Unexpected argument: " + key;
                    return false;
                }

                if (string.Equals(key, "--quiet", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "--visible", StringComparison.OrdinalIgnoreCase))
                {
                    flags.Add(key);
                    continue;
                }

                if (index + 1 >= args.Length)
                {
                    error = "Missing value for " + key;
                    return false;
                }

                values[key] = args[++index];
            }

            string source = Get(values, "--source");
            string install = Get(values, "--install");
            string backup = Get(values, "--backup");
            string entryExe = Get(values, "--entry-exe");
            string log = Get(values, "--log");
            string parentPidRaw = Get(values, "--parent-pid");
            string channel = Get(values, "--channel");
            string version = Get(values, "--version");
            string clientExe = Get(values, "--client-exe");
            string handoff = Get(values, "--handoff");
            string download = Get(values, "--download");
            string extractionRoot = Get(values, "--extraction-root");

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(install)
                || string.IsNullOrWhiteSpace(backup) || string.IsNullOrWhiteSpace(log))
            {
                error = "Source, install, backup, and log paths are required.";
                return false;
            }

            if (!int.TryParse(parentPidRaw, out int parentPid) || parentPid < 0)
            {
                error = "The parent process id is invalid.";
                return false;
            }

            if (!string.Equals(entryExe, "OceanyaClient.exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(entryExe) != entryExe)
            {
                error = "The entry executable is invalid.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(channel)
                && !string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(channel, "test", StringComparison.OrdinalIgnoreCase))
            {
                error = "The update channel is invalid.";
                return false;
            }

            parsed = new UpdaterArguments
            {
                Source = Path.GetFullPath(source),
                Install = EnsureTrailingSeparator(Path.GetFullPath(install)),
                Backup = Path.GetFullPath(backup),
                ParentPid = parentPid,
                EntryExe = entryExe,
                Log = Path.GetFullPath(log),
                Channel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim().ToLowerInvariant(),
                Version = version.Trim(),
                ClientExe = string.IsNullOrWhiteSpace(clientExe) ? string.Empty : Path.GetFullPath(clientExe),
                Handoff = string.IsNullOrWhiteSpace(handoff) ? string.Empty : Path.GetFullPath(handoff),
                Download = string.IsNullOrWhiteSpace(download) ? string.Empty : Path.GetFullPath(download),
                ExtractionRoot = string.IsNullOrWhiteSpace(extractionRoot) ? string.Empty : Path.GetFullPath(extractionRoot),
                Quiet = flags.Contains("--quiet")
            };
            return true;
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out string? value) ? value?.Trim() ?? string.Empty : string.Empty;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
        }
    }

    internal static class Program
    {
        private static TextWriter? activeLog;

        [STAThread]
        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                activeLog?.WriteLine(DateTimeOffset.Now.ToString("O") + " Unhandled exception: " + e.ExceptionObject);
                activeLog?.Flush();
            };

            string earlyLogPath = TryGetRawArgValue(args, "--log")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OceanyaClient",
                    "Updates",
                    "logs",
                    "updater.log");
            using StreamWriter log = OpenLog(earlyLogPath);
            activeLog = log;
            WriteLog(log, "Updater process started. Raw args: " + string.Join(" ", args.Select(EscapeArgForLog)));

            if (!UpdaterArguments.TryParse(args, out UpdaterArguments options, out string error))
            {
                WriteLog(log, "Argument parse failed: " + error);
                MessageBox.Show(error, "Oceanya Updater", MessageBoxButton.OK, MessageBoxImage.Error);
                return UpdaterExitCodes.ArgumentError;
            }

            UpdaterStatusWindow? status = options.Quiet ? null : new UpdaterStatusWindow(options);
            status?.ShowStatus("Starting Oceanya update...");

            try
            {
                WriteLog(log, $"Updater parsed args. channel={options.Channel}; version={options.Version}; source={options.Source}; install={options.Install}; backup={options.Backup}; clientExe={options.ClientExe}; handoff={options.Handoff}; download={options.Download}; extractionRoot={options.ExtractionRoot}");
                status?.ShowStatus("Waiting for Oceanya Client to close...");
                WaitForParentExit(options.ParentPid, log);
                status?.ShowStatus("Checking update files...");
                ValidatePaths(options);
                status?.ShowStatus("Backing up current files...");
                BackupInstall(options, log);
                status?.ShowStatus("Applying update files...");
                ReplaceInstall(options, log);
                WriteLog(log, "Update applied successfully.");
                status?.ShowStatus("Reopening Oceanya Client...");
                Relaunch(options, log);
                status?.ShowStatus("Cleaning up update files...");
                CleanupSuccessfulUpdate(options, log);
                status?.ShowStatus("Update complete. Reopening Oceanya Client...");
                Thread.Sleep(options.Quiet ? 0 : 1200);
                status?.Close();
                return UpdaterExitCodes.Success;
            }
            catch (ParentProcessTimeoutException ex)
            {
                WriteFailure(log, status, ex);
                TryRollback(options, log);
                return UpdaterExitCodes.ParentTimeout;
            }
            catch (RelaunchFailedException ex)
            {
                WriteFailure(log, status, ex);
                return UpdaterExitCodes.RelaunchFailed;
            }
            catch (Exception ex)
            {
                WriteFailure(log, status, ex);
                TryRollback(options, log);
                return UpdaterExitCodes.ApplyFailed;
            }
        }

        private static StreamWriter OpenLog(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            return new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
        }

        private static string? TryGetRawArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static void WaitForParentExit(int parentPid, TextWriter log)
        {
            if (parentPid <= 0)
            {
                WriteLog(log, "No parent process id supplied; continuing.");
                return;
            }

            try
            {
                using Process process = Process.GetProcessById(parentPid);
                WriteLog(log, "Waiting for parent process " + parentPid + " to exit.");
                if (!process.WaitForExit(45000))
                {
                    throw new ParentProcessTimeoutException("OceanyaClient.exe did not exit within 45 seconds. Update was not applied.");
                }
            }
            catch (ArgumentException)
            {
                WriteLog(log, "Parent process " + parentPid + " is already gone.");
            }
        }

        private static void ValidatePaths(UpdaterArguments options)
        {
            if (!Directory.Exists(options.Source))
            {
                throw new DirectoryNotFoundException("Staged source folder not found: " + options.Source);
            }

            if (!Directory.Exists(options.Install))
            {
                throw new DirectoryNotFoundException("Install folder not found: " + options.Install);
            }

            string sourceRoot = EnsureTrailingSeparator(Path.GetFullPath(options.Source));
            string installRoot = EnsureTrailingSeparator(Path.GetFullPath(options.Install));
            string backupRoot = EnsureTrailingSeparator(Path.GetFullPath(options.Backup));
            if (installRoot.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)
                || sourceRoot.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase)
                || backupRoot.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Updater paths overlap unsafely.");
            }

            if (!File.Exists(Path.Combine(options.Source, options.EntryExe)))
            {
                throw new FileNotFoundException("Staged update is missing " + options.EntryExe);
            }
        }

        private static void BackupInstall(UpdaterArguments options, TextWriter log)
        {
            if (Directory.Exists(options.Backup))
            {
                Directory.Delete(options.Backup, recursive: true);
            }

            Directory.CreateDirectory(options.Backup);
            CopyDirectory(options.Install, options.Backup, log);
            WriteLog(log, "Backup created at " + options.Backup);
        }

        private static void ReplaceInstall(UpdaterArguments options, TextWriter log)
        {
            ClearDirectory(options.Install, log);
            CopyDirectory(options.Source, options.Install, log);
        }

        private static void TryRollback(UpdaterArguments options, TextWriter log)
        {
            try
            {
                if (!Directory.Exists(options.Backup))
                {
                    WriteLog(log, "Rollback unavailable; backup folder does not exist.");
                    return;
                }

                ClearDirectory(options.Install, log);
                CopyDirectory(options.Backup, options.Install, log);
                WriteLog(log, "Rollback completed.");
            }
            catch (Exception rollbackEx)
            {
                WriteLog(log, "Rollback failed. Manual recovery may be required from: " + options.Backup);
                WriteLog(log, rollbackEx.ToString());
            }
        }

        private static void CleanupSuccessfulUpdate(UpdaterArguments options, TextWriter log)
        {
            TryDeleteDirectory(options.Backup, log, "backup");
            TryDeleteFile(options.Download, log, "downloaded package");
            TryDeleteDirectory(options.ExtractionRoot, log, "staged extraction");
        }

        private static void TryDeleteFile(string path, TextWriter log, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                WriteLog(log, "Deleted " + label + ": " + path);
            }
            catch (Exception ex)
            {
                WriteLog(log, "Could not delete " + label + " " + path + ": " + ex.Message);
            }
        }

        private static void TryDeleteDirectory(string path, TextWriter log, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
                WriteLog(log, "Deleted " + label + ": " + path);
            }
            catch (Exception ex)
            {
                WriteLog(log, "Could not delete " + label + " " + path + ": " + ex.Message);
            }
        }

        private static void ClearDirectory(string directory, TextWriter log)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(childDirectory).Any())
                {
                    Directory.Delete(childDirectory);
                }
            }

            WriteLog(log, "Cleared install directory " + directory);
        }

        private static void CopyDirectory(string source, string destination, TextWriter log)
        {
            string sourceRoot = EnsureTrailingSeparator(Path.GetFullPath(source));
            string destinationRoot = EnsureTrailingSeparator(Path.GetFullPath(destination));
            foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceRoot, directory);
                Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
            }

            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceRoot, file);
                string target = Path.Combine(destinationRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destinationRoot);
                File.Copy(file, target, overwrite: true);
            }

            WriteLog(log, "Copied " + source + " -> " + destination);
        }

        private static void Relaunch(UpdaterArguments options, TextWriter log)
        {
            string entryPath = string.IsNullOrWhiteSpace(options.ClientExe)
                ? Path.Combine(options.Install, options.EntryExe)
                : options.ClientExe;
            if (!File.Exists(entryPath))
            {
                throw new RelaunchFailedException("Relaunch failed; entry executable is missing: " + entryPath);
            }

            Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = entryPath,
                WorkingDirectory = Path.GetDirectoryName(entryPath) ?? options.Install,
                UseShellExecute = true
            });
            if (process == null)
            {
                throw new RelaunchFailedException("Windows did not start " + entryPath);
            }

            WriteLog(log, "Relaunched Oceanya Client pid=" + process.Id + " path=" + entryPath);
        }

        private static void WriteFailure(TextWriter log, UpdaterStatusWindow? status, Exception ex)
        {
            WriteLog(log, "Update failed: " + ex);
            status?.ShowError("Update failed", ex.Message);
        }

        private static void WriteLog(TextWriter log, string message)
        {
            log.WriteLine(DateTimeOffset.Now.ToString("O") + " " + message);
        }

        private static string EscapeArgForLog(string arg)
        {
            return arg.Contains(' ', StringComparison.Ordinal) ? "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : arg;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
        }

    }

    internal sealed class UpdaterStatusWindow : Window
    {
        private readonly TextBlock statusText = new TextBlock();
        private readonly ProgressBar progressBar = new ProgressBar();

        public UpdaterStatusWindow(UpdaterArguments options)
        {
            Title = "Oceanya Updater";
            Width = 300;
            Height = 120;
            MinWidth = 300;
            MinHeight = 120;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = true;
            Topmost = true;

            Border outerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5)
            };

            Grid grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(127, 0, 0, 0))
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            string? logoPath = ResolveLogoPath(options);
            if (!string.IsNullOrWhiteSpace(logoPath))
            {
                System.Windows.Shapes.Rectangle logoMask = new System.Windows.Shapes.Rectangle
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Fill = new SolidColorBrush(Color.FromArgb(191, 0, 0, 0)),
                    OpacityMask = new ImageBrush(LoadBitmapImage(logoPath))
                    {
                        Stretch = Stretch.UniformToFill
                    }
                };
                Grid.SetRowSpan(logoMask, 3);
                grid.Children.Add(logoMask);
            }

            TextBlock titleText = new TextBlock
            {
                Text = "Updating Oceanya Client",
                Foreground = Brushes.White,
                FontSize = 16,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20, 20, 20, 5)
            };
            Grid.SetRow(titleText, 0);

            statusText.Text = "Starting...";
            statusText.Foreground = Brushes.LightGray;
            statusText.FontSize = 12;
            statusText.TextAlignment = TextAlignment.Center;
            statusText.TextWrapping = TextWrapping.Wrap;
            statusText.HorizontalAlignment = HorizontalAlignment.Center;
            statusText.Margin = new Thickness(20, 0, 20, 10);
            Grid.SetRow(statusText, 1);

            progressBar.IsIndeterminate = true;
            progressBar.Height = 5;
            progressBar.Margin = new Thickness(20, 8, 20, 18);
            progressBar.BorderBrush = Brushes.Transparent;
            progressBar.Foreground = Brushes.White;
            progressBar.Background = new SolidColorBrush(Color.FromArgb(0, 230, 230, 230));
            Grid.SetRow(progressBar, 2);

            grid.Children.Add(titleText);
            grid.Children.Add(statusText);
            grid.Children.Add(progressBar);
            outerBorder.Child = grid;
            Content = outerBorder;
        }

        private static string? ResolveLogoPath(UpdaterArguments options)
        {
            foreach (string root in new[] { options.Source, options.Install, AppContext.BaseDirectory })
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string path = Path.Combine(root, "Resources", "OceanyaFullLogo.png");
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static BitmapImage LoadBitmapImage(string path)
        {
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }

        public void ShowStatus(string message)
        {
            if (!IsVisible)
            {
                Show();
            }

            statusText.Text = message;
            UpdateLayout();
            ProgramPump();
        }

        public void ShowError(string title, string message)
        {
            ShowStatus(message);
            progressBar.IsIndeterminate = false;
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static void ProgramPump()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    internal sealed class ParentProcessTimeoutException : Exception
    {
        public ParentProcessTimeoutException(string message)
            : base(message)
        {
        }
    }

    internal sealed class RelaunchFailedException : Exception
    {
        public RelaunchFailedException(string message)
            : base(message)
        {
        }
    }
}
