using Microsoft.Win32;
using OceanyaClient.Features.GoogleDriveSync;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace OceanyaClient.Features.FileHivemind
{
    public static class FileHivemindBackgroundAgentCommandLine
    {
        public const string AgentArgument = "--hivemind-agent";
        public const string AutoStartValueName = "OceanyaClient.FileHivemindAgent";
        public const string AgentMutexName = @"Local\OceanyaClient.FileHivemind.Agent";
        public const string AgentStopSignalEventName = @"Local\OceanyaClient.FileHivemind.Agent.Stop";
        public const string AgentExecutableFileName = "OceanyaHivemindAgent.exe";
        public const string MainApplicationExecutableFileName = "OceanyaClient.exe";

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

        public static string ResolveAgentExecutablePath(string? currentExecutablePath = null)
        {
            string resolvedCurrentPath = ResolveCurrentExecutablePath(currentExecutablePath);
            string currentFileName = Path.GetFileName(resolvedCurrentPath);
            if (string.Equals(currentFileName, AgentExecutableFileName, StringComparison.OrdinalIgnoreCase))
            {
                return resolvedCurrentPath;
            }

            string candidate = Path.Combine(
                Path.GetDirectoryName(resolvedCurrentPath) ?? string.Empty,
                AgentExecutableFileName);
            return File.Exists(candidate) ? candidate : resolvedCurrentPath;
        }

        public static string ResolveMainApplicationExecutablePath(string? currentExecutablePath = null)
        {
            string resolvedCurrentPath = ResolveCurrentExecutablePath(currentExecutablePath);
            string currentFileName = Path.GetFileName(resolvedCurrentPath);
            if (string.Equals(currentFileName, MainApplicationExecutableFileName, StringComparison.OrdinalIgnoreCase))
            {
                return resolvedCurrentPath;
            }

            string candidate = Path.Combine(
                Path.GetDirectoryName(resolvedCurrentPath) ?? string.Empty,
                MainApplicationExecutableFileName);
            return File.Exists(candidate) ? candidate : resolvedCurrentPath;
        }

        private static string ResolveCurrentExecutablePath(string? currentExecutablePath = null)
        {
            string trimmedCurrentPath = currentExecutablePath?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(trimmedCurrentPath))
            {
                return trimmedCurrentPath;
            }

            string? processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                return processPath.Trim();
            }

            throw new InvalidOperationException("The Oceanya executable path could not be resolved.");
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
            this.executablePathResolver = executablePathResolver ?? (() =>
                FileHivemindBackgroundAgentCommandLine.ResolveAgentExecutablePath());
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
    }

    public sealed class FileHivemindBackgroundAgentLauncher
    {
        private readonly IFileHivemindAutoStartRegistrar autoStartRegistrar;
        private readonly Func<string, bool> startAgentProcess;
        private readonly Func<bool> isAgentRunning;
        private readonly Func<bool> requestAgentStop;

        public FileHivemindBackgroundAgentLauncher(
            IFileHivemindAutoStartRegistrar? autoStartRegistrar = null,
            Func<string, bool>? startAgentProcess = null,
            Func<bool>? isAgentRunning = null,
            Func<bool>? requestAgentStop = null)
        {
            this.autoStartRegistrar = autoStartRegistrar ?? new WindowsFileHivemindAutoStartRegistrar();
            this.startAgentProcess = startAgentProcess ?? LaunchHiddenAgentProcess;
            this.isAgentRunning = isAgentRunning ?? IsAgentRunning;
            this.requestAgentStop = requestAgentStop ?? SignalAgentStopRequest;
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

            ResetAgentStopRequest();
            return startAgentProcess(FileHivemindBackgroundAgentCommandLine.AgentArgument);
        }

        public bool StartForCurrentSession(FileHivemindSettings settings)
        {
            ApplyRegistration(settings);
            if (!HasEligibleConnections(settings))
            {
                return false;
            }

            if (isAgentRunning())
            {
                return true;
            }

            ResetAgentStopRequest();
            return startAgentProcess(FileHivemindBackgroundAgentCommandLine.AgentArgument);
        }

        public bool RequestStopForCurrentSession()
        {
            if (!isAgentRunning())
            {
                return false;
            }

            return requestAgentStop();
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
                && HasEligibleConnections(settings);
        }

        public static bool HasEligibleConnections(FileHivemindSettings? settings)
        {
            return (settings?.Connections ?? new List<FileHivemindConnectionProfile>())
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
            return GoogleDriveConnectionCredentialSupport.TryBuildConfiguration(
                    settings,
                    out _,
                    out _,
                    allowLegacyFallback: false)
                && !string.IsNullOrWhiteSpace(settings.RemoteFolderId)
                && !string.IsNullOrWhiteSpace(settings.LocalFolderPath)
                && !string.IsNullOrWhiteSpace(settings.TokenStoreKey)
                && (!string.IsNullOrWhiteSpace(settings.LastSignedInEmail)
                    || !string.IsNullOrWhiteSpace(settings.LastSignedInDisplayName));
        }

        private static bool LaunchHiddenAgentProcess(string argument)
        {
            string executablePath = FileHivemindBackgroundAgentCommandLine.ResolveAgentExecutablePath();

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = argument,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return true;
        }

        private static void ResetAgentStopRequest()
        {
            using EventWaitHandle stopSignal = OpenOrCreateStopSignalEvent();
            stopSignal.Reset();
        }

        private static bool SignalAgentStopRequest()
        {
            using EventWaitHandle stopSignal = OpenOrCreateStopSignalEvent();
            return stopSignal.Set();
        }

        private static EventWaitHandle OpenOrCreateStopSignalEvent()
        {
            return new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                FileHivemindBackgroundAgentCommandLine.AgentStopSignalEventName);
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
