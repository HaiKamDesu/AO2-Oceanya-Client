using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace OceanyaUpdater
{
    public sealed class UpdaterArguments
    {
        public string Source { get; init; } = string.Empty;
        public string Install { get; init; } = string.Empty;
        public string Backup { get; init; } = string.Empty;
        public int ParentPid { get; init; }
        public string EntryExe { get; init; } = "OceanyaClient.exe";
        public string Log { get; init; } = string.Empty;

        public static bool TryParse(string[] args, out UpdaterArguments parsed, out string error)
        {
            parsed = new UpdaterArguments();
            error = string.Empty;
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index++)
            {
                string key = args[index]?.Trim() ?? string.Empty;
                if (!key.StartsWith("--", StringComparison.Ordinal))
                {
                    error = "Unexpected argument: " + key;
                    return false;
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

            parsed = new UpdaterArguments
            {
                Source = Path.GetFullPath(source),
                Install = EnsureTrailingSeparator(Path.GetFullPath(install)),
                Backup = Path.GetFullPath(backup),
                ParentPid = parentPid,
                EntryExe = entryExe,
                Log = Path.GetFullPath(log)
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
        private const string HivemindAgentMutexName = @"Local\OceanyaClient.FileHivemind.Agent";
        private const string HivemindAgentStopSignalEventName = @"Local\OceanyaClient.FileHivemind.Agent.Stop";

        private static int Main(string[] args)
        {
            if (!UpdaterArguments.TryParse(args, out UpdaterArguments options, out string error))
            {
                Console.Error.WriteLine(error);
                return 2;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(options.Log) ?? string.Empty);
            using StreamWriter log = new StreamWriter(new FileStream(options.Log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };

            try
            {
                WriteLog(log, "Updater started.");
                WaitForParentExit(options.ParentPid, log);
                StopHivemindAgent(log);
                ValidatePaths(options);
                BackupInstall(options, log);
                ReplaceInstall(options, log);
                WriteLog(log, "Update applied successfully.");
                Relaunch(options, log);
                return 0;
            }
            catch (Exception ex)
            {
                WriteLog(log, "Update failed: " + ex);
                TryRollback(options, log);
                return 1;
            }
        }

        private static void WaitForParentExit(int parentPid, TextWriter log)
        {
            if (parentPid <= 0)
            {
                return;
            }

            try
            {
                using Process process = Process.GetProcessById(parentPid);
                WriteLog(log, "Waiting for parent process " + parentPid + " to exit.");
                process.WaitForExit(30000);
            }
            catch (ArgumentException)
            {
            }
        }

        private static void StopHivemindAgent(TextWriter log)
        {
            try
            {
                using EventWaitHandle stopSignal = new EventWaitHandle(false, EventResetMode.ManualReset, HivemindAgentStopSignalEventName);
                stopSignal.Set();
                WriteLog(log, "Hivemind stop signal sent.");
            }
            catch (Exception ex)
            {
                WriteLog(log, "Could not send Hivemind stop signal: " + ex.Message);
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (!Mutex.TryOpenExisting(HivemindAgentMutexName, out Mutex? mutex))
                {
                    return;
                }

                mutex.Dispose();
                Thread.Sleep(250);
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
            foreach (string file in Directory.EnumerateFiles(options.Install, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string directory in Directory.EnumerateDirectories(options.Install, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

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

                CopyDirectory(options.Backup, options.Install, log);
                WriteLog(log, "Rollback completed.");
            }
            catch (Exception rollbackEx)
            {
                WriteLog(log, "Rollback failed. Manual recovery may be required from: " + options.Backup);
                WriteLog(log, rollbackEx.ToString());
            }
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
            string entryPath = Path.Combine(options.Install, options.EntryExe);
            if (!File.Exists(entryPath))
            {
                WriteLog(log, "Relaunch skipped; entry executable missing: " + entryPath);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = entryPath,
                WorkingDirectory = options.Install,
                UseShellExecute = true
            });
        }

        private static void WriteLog(TextWriter log, string message)
        {
            log.WriteLine(DateTimeOffset.Now.ToString("O") + " " + message);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
        }
    }
}
