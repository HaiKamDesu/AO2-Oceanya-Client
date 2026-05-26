using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace OceanyaClient
{
    public static class FileHivemindAgentProcessContract
    {
        public const string AgentMutexName = @"Local\OceanyaClient.FileHivemind.Agent";
        public const string AgentStopSignalEventName = @"Local\OceanyaClient.FileHivemind.Agent.Stop";
        public const string AgentStoppedSignalEventName = @"Local\OceanyaClient.FileHivemind.Agent.Stopped";
        public const string AgentExecutableFileName = "OceanyaHivemindAgent.exe";
        public const string MainApplicationExecutableFileName = "OceanyaClient.exe";
    }

    public readonly struct FileHivemindAgentStopResult
    {
        public FileHivemindAgentStopResult(
            bool wasRunning,
            bool stopRequested,
            bool stopped,
            int forcedProcessCount = 0)
        {
            WasRunning = wasRunning;
            StopRequested = stopRequested;
            Stopped = stopped;
            ForcedProcessCount = forcedProcessCount;
        }

        public bool WasRunning { get; }
        public bool StopRequested { get; }
        public bool Stopped { get; }
        public int ForcedProcessCount { get; }
    }

    public sealed class FileHivemindAgentStopCoordinator
    {
        private readonly Func<bool> isAgentRunning;
        private readonly Func<bool> requestAgentStop;
        private readonly Func<TimeSpan, bool> waitForStoppedSignal;
        private readonly Action<TimeSpan> sleep;
        private readonly Func<int> forceStopAgent;
        private readonly TimeSpan forceStopWaitTimeout;
        private readonly Action? beforeForceStop;

        public FileHivemindAgentStopCoordinator(
            Func<bool>? isAgentRunning = null,
            Func<bool>? requestAgentStop = null,
            Func<TimeSpan, bool>? waitForStoppedSignal = null,
            Action<TimeSpan>? sleep = null,
            Func<int>? forceStopAgent = null,
            TimeSpan? forceStopWaitTimeout = null,
            Action? beforeForceStop = null,
            string? installDirectory = null)
        {
            this.isAgentRunning = isAgentRunning ?? IsAgentRunning;
            this.requestAgentStop = requestAgentStop ?? SignalAgentStopRequest;
            this.waitForStoppedSignal = waitForStoppedSignal ?? WaitForStoppedSignal;
            this.sleep = sleep ?? Thread.Sleep;
            this.forceStopAgent = forceStopAgent ?? (() => ForceStopAgentProcesses(installDirectory));
            this.forceStopWaitTimeout = forceStopWaitTimeout ?? TimeSpan.FromSeconds(5);
            this.beforeForceStop = beforeForceStop;
        }

        public FileHivemindAgentStopResult RequestStopAndWait(TimeSpan timeout)
        {
            if (!isAgentRunning())
            {
                return new FileHivemindAgentStopResult(false, false, true);
            }

            bool requested = requestAgentStop();
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (!isAgentRunning())
                {
                    return new FileHivemindAgentStopResult(true, requested, true);
                }

                TimeSpan remaining = deadline - DateTime.UtcNow;
                TimeSpan waitSlice = remaining < TimeSpan.FromMilliseconds(250)
                    ? remaining
                    : TimeSpan.FromMilliseconds(250);
                if (waitSlice > TimeSpan.Zero && waitForStoppedSignal(waitSlice))
                {
                    return new FileHivemindAgentStopResult(true, requested, true);
                }

                sleep(TimeSpan.FromMilliseconds(100));
            }

            beforeForceStop?.Invoke();
            int forcedProcessCount = forceStopAgent();
            DateTime forceDeadline = DateTime.UtcNow.Add(forceStopWaitTimeout);
            while (DateTime.UtcNow < forceDeadline)
            {
                if (!isAgentRunning())
                {
                    return new FileHivemindAgentStopResult(true, requested, true, forcedProcessCount);
                }

                TimeSpan remaining = forceDeadline - DateTime.UtcNow;
                TimeSpan waitSlice = remaining < TimeSpan.FromMilliseconds(250)
                    ? remaining
                    : TimeSpan.FromMilliseconds(250);
                if (waitSlice > TimeSpan.Zero && waitForStoppedSignal(waitSlice))
                {
                    return new FileHivemindAgentStopResult(true, requested, true, forcedProcessCount);
                }

                sleep(TimeSpan.FromMilliseconds(100));
            }

            return new FileHivemindAgentStopResult(true, requested, !isAgentRunning(), forcedProcessCount);
        }

        private static bool IsAgentRunning()
        {
            if (!Mutex.TryOpenExisting(FileHivemindAgentProcessContract.AgentMutexName, out Mutex? mutex))
            {
                return false;
            }

            mutex.Dispose();
            return true;
        }

        private static bool SignalAgentStopRequest()
        {
            using EventWaitHandle stopSignal = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                FileHivemindAgentProcessContract.AgentStopSignalEventName);
            return stopSignal.Set();
        }

        private static bool WaitForStoppedSignal(TimeSpan timeout)
        {
            using EventWaitHandle stoppedSignal = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                FileHivemindAgentProcessContract.AgentStoppedSignalEventName);
            return stoppedSignal.WaitOne(timeout);
        }

        private static int ForceStopAgentProcesses(string? installDirectory)
        {
            int forcedProcessCount = 0;
            foreach (Process process in FindAgentProcessCandidates(installDirectory))
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            continue;
                        }

                        forcedProcessCount++;
                        try
                        {
                            if (process.CloseMainWindow() && process.WaitForExit(2500))
                            {
                                continue;
                            }
                        }
                        catch
                        {
                        }

                        process.Kill(entireProcessTree: true);
                        _ = process.WaitForExit(5000);
                    }
                    catch
                    {
                    }
                }
            }

            return forcedProcessCount;
        }

        private static List<Process> FindAgentProcessCandidates(string? installDirectory)
        {
            Dictionary<int, Process> candidates = new Dictionary<int, Process>();
            AddProcessCandidatesByName(
                candidates,
                Path.GetFileNameWithoutExtension(FileHivemindAgentProcessContract.AgentExecutableFileName),
                _ => true);
            AddProcessCandidatesByName(
                candidates,
                Path.GetFileNameWithoutExtension(FileHivemindAgentProcessContract.MainApplicationExecutableFileName),
                process => IsProcessInAllowedDirectory(process, installDirectory) && process.MainWindowHandle == IntPtr.Zero);
            return candidates.Values.ToList();
        }

        private static void AddProcessCandidatesByName(
            Dictionary<int, Process> candidates,
            string processName,
            Func<Process, bool> isCandidate)
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    int processId = process.Id;
                    if (processId == Environment.ProcessId || candidates.ContainsKey(processId))
                    {
                        process.Dispose();
                        continue;
                    }

                    if (!isCandidate(process))
                    {
                        process.Dispose();
                        continue;
                    }

                    candidates[processId] = process;
                }
                catch
                {
                    process.Dispose();
                }
            }
        }

        private static bool IsProcessInAllowedDirectory(Process process, string? installDirectory)
        {
            try
            {
                string? processPath = process.MainModule?.FileName;
                string? processDirectory = Path.GetDirectoryName(processPath ?? string.Empty);
                if (string.IsNullOrWhiteSpace(processDirectory))
                {
                    return false;
                }

                return GetAllowedCandidateDirectories(installDirectory)
                    .Any(directory => string.Equals(
                        Path.GetFullPath(directory),
                        Path.GetFullPath(processDirectory),
                        StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> GetAllowedCandidateDirectories(string? installDirectory)
        {
            string? currentPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            string? currentDirectory = Path.GetDirectoryName(currentPath ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(currentDirectory))
            {
                yield return currentDirectory;
            }

            if (!string.IsNullOrWhiteSpace(installDirectory))
            {
                yield return installDirectory;
            }
        }
    }
}
