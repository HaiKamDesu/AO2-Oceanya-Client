using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace OceanyaClient.Features.FileHivemind
{
    public static class FileHivemindBackgroundAgentCommandLine
    {
        public const string AgentArgument = "--hivemind-agent";
        public const string AutoStartValueName = "OceanyaClient.FileHivemindAgent";
        public const string AgentMutexName = @"Local\OceanyaClient.FileHivemind.Agent";

        public static bool IsAgentMode(string[]? args)
        {
            return (args ?? Array.Empty<string>()).Any(argument =>
                string.Equals(argument?.Trim(), AgentArgument, StringComparison.OrdinalIgnoreCase));
        }

        public static string BuildAutoStartCommand(string executablePath)
        {
            string trimmedExecutablePath = executablePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedExecutablePath))
            {
                throw new InvalidOperationException("The Oceanya executable path could not be resolved.");
            }

            return "\"" + trimmedExecutablePath + "\" " + AgentArgument;
        }
    }

    public interface IFileHivemindAutoStartRegistrar
    {
        bool IsRegistered();

        void SetRegistered(bool enabled);
    }

    public sealed class WindowsFileHivemindAutoStartRegistrar : IFileHivemindAutoStartRegistrar
    {
        private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly Func<string> executablePathResolver;

        public WindowsFileHivemindAutoStartRegistrar(Func<string>? executablePathResolver = null)
        {
            this.executablePathResolver = executablePathResolver ?? ResolveCurrentExecutablePath;
        }

        public bool IsRegistered()
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
            return runKey?.GetValue(FileHivemindBackgroundAgentCommandLine.AutoStartValueName) is string value
                && !string.IsNullOrWhiteSpace(value);
        }

        public void SetRegistered(bool enabled)
        {
            using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath)
                ?? throw new InvalidOperationException("Could not access the current user's startup registry key.");

            if (!enabled)
            {
                runKey.DeleteValue(FileHivemindBackgroundAgentCommandLine.AutoStartValueName, throwOnMissingValue: false);
                return;
            }

            string command = FileHivemindBackgroundAgentCommandLine.BuildAutoStartCommand(executablePathResolver());
            runKey.SetValue(FileHivemindBackgroundAgentCommandLine.AutoStartValueName, command, RegistryValueKind.String);
        }

        private static string ResolveCurrentExecutablePath()
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                return processPath.Trim();
            }

            string? fallback = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback.Trim();
            }

            throw new InvalidOperationException("The Oceanya executable path could not be resolved.");
        }
    }

    public sealed class FileHivemindBackgroundAgentLauncher
    {
        private readonly IFileHivemindAutoStartRegistrar autoStartRegistrar;
        private readonly Func<string, bool> startAgentProcess;
        private readonly Func<bool> isAgentRunning;

        public FileHivemindBackgroundAgentLauncher(
            IFileHivemindAutoStartRegistrar? autoStartRegistrar = null,
            Func<string, bool>? startAgentProcess = null,
            Func<bool>? isAgentRunning = null)
        {
            this.autoStartRegistrar = autoStartRegistrar ?? new WindowsFileHivemindAutoStartRegistrar();
            this.startAgentProcess = startAgentProcess ?? LaunchHiddenAgentProcess;
            this.isAgentRunning = isAgentRunning ?? IsAgentRunning;
        }

        public void ApplyRegistration(FileHivemindSettings settings)
        {
            autoStartRegistrar.SetRegistered(ShouldRunForSettings(settings));
        }

        public bool EnsureRunningForCurrentSession(FileHivemindSettings settings)
        {
            ApplyRegistration(settings);
            if (!ShouldRunForSettings(settings))
            {
                return false;
            }

            if (isAgentRunning())
            {
                return true;
            }

            return startAgentProcess(FileHivemindBackgroundAgentCommandLine.AgentArgument);
        }

        public bool IsRegistered()
        {
            return autoStartRegistrar.IsRegistered();
        }

        public bool IsAgentRunning()
        {
            if (!Mutex.TryOpenExisting(FileHivemindBackgroundAgentCommandLine.AgentMutexName, out Mutex? mutex))
            {
                return false;
            }

            mutex.Dispose();
            return true;
        }

        public static bool ShouldRunForSettings(FileHivemindSettings? settings)
        {
            return settings?.RunAgentAtStartup == true
                && (settings.Connections ?? new List<FileHivemindConnectionProfile>())
                    .Any(IsEligibleConnection);
        }

        public static bool IsEligibleConnection(FileHivemindConnectionProfile? connection)
        {
            if (connection == null)
            {
                return false;
            }

            if (!string.Equals(connection.ProviderId, FileHivemindProviderIds.GoogleDrive, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            GoogleDriveSyncSettings settings = connection.GoogleDrive ?? new GoogleDriveSyncSettings();
            return !string.IsNullOrWhiteSpace(settings.RemoteFolderId)
                && !string.IsNullOrWhiteSpace(settings.LocalFolderPath)
                && !string.IsNullOrWhiteSpace(settings.TokenStoreKey)
                && (!string.IsNullOrWhiteSpace(settings.LastSignedInEmail)
                    || !string.IsNullOrWhiteSpace(settings.LastSignedInDisplayName));
        }

        private static bool LaunchHiddenAgentProcess(string argument)
        {
            string? executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = argument,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return true;
        }
    }

    public sealed class FileHivemindConnectionExecutionLock : IDisposable
    {
        private readonly Mutex mutex;
        private bool disposed;

        private FileHivemindConnectionExecutionLock(Mutex mutex)
        {
            this.mutex = mutex;
        }

        public static FileHivemindConnectionExecutionLock? TryAcquire(string connectionId, TimeSpan timeout)
        {
            string trimmedConnectionId = connectionId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedConnectionId))
            {
                throw new ArgumentException("A hivemind connection ID is required.", nameof(connectionId));
            }

            string mutexName = @"Local\OceanyaClient.FileHivemind.Connection." + trimmedConnectionId;
            Mutex mutex = new Mutex(initiallyOwned: false, mutexName);
            bool acquired = false;

            try
            {
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }

            return new FileHivemindConnectionExecutionLock(mutex);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }
}
