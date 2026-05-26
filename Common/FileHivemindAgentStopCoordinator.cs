using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace OceanyaClient
{
#if OCEANYA_UPDATER_EMBEDDED_COORDINATOR
    internal
#else
    public
#endif
    static class FileHivemindAgentProcessContract
    {
        public const string AgentMutexName = @"Local\OceanyaClient.FileHivemind.Agent";
        public const string AgentStopSignalEventName = @"Local\OceanyaClient.FileHivemind.Agent.Stop";
        public const string AgentStoppedSignalEventName = @"Local\OceanyaClient.FileHivemind.Agent.Stopped";
        public const string AgentExecutableFileName = "OceanyaHivemindAgent.exe";
        public const string MainApplicationExecutableFileName = "OceanyaClient.exe";
    }

#if OCEANYA_UPDATER_EMBEDDED_COORDINATOR
    internal
#else
    public
#endif
    readonly struct FileHivemindAgentStopResult
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

#if OCEANYA_UPDATER_EMBEDDED_COORDINATOR
    internal
#else
    public
#endif
    sealed class FileHivemindAgentStopCoordinator
    {
        private readonly Func<bool> isAgentRunning;
        private readonly Func<bool> requestAgentStop;
        private readonly Func<TimeSpan, bool> waitForStoppedSignal;
        private readonly Action<TimeSpan> sleep;
        private readonly Func<int> forceStopAgent;
        private readonly TimeSpan forceStopWaitTimeout;
        private readonly Action? beforeForceStop;
        private readonly Action<string>? trace;

        public FileHivemindAgentStopCoordinator(
            Func<bool>? isAgentRunning = null,
            Func<bool>? requestAgentStop = null,
            Func<TimeSpan, bool>? waitForStoppedSignal = null,
            Action<TimeSpan>? sleep = null,
            Func<int>? forceStopAgent = null,
            TimeSpan? forceStopWaitTimeout = null,
            Action? beforeForceStop = null,
            string? installDirectory = null,
            Action<string>? trace = null)
        {
            this.isAgentRunning = isAgentRunning ?? IsAgentRunning;
            this.requestAgentStop = requestAgentStop ?? SignalAgentStopRequest;
            this.waitForStoppedSignal = waitForStoppedSignal ?? WaitForStoppedSignal;
            this.sleep = sleep ?? Thread.Sleep;
            this.forceStopAgent = forceStopAgent ?? (() => ForceStopAgentProcesses(installDirectory, trace));
            this.forceStopWaitTimeout = forceStopWaitTimeout ?? TimeSpan.FromSeconds(5);
            this.beforeForceStop = beforeForceStop;
            this.trace = trace;
        }

        public FileHivemindAgentStopResult RequestStopAndWait(TimeSpan timeout)
        {
            trace?.Invoke("Checking File Hivemind agent mutex.");
            if (!isAgentRunning())
            {
                trace?.Invoke("File Hivemind agent mutex is absent; treating agent as stopped.");
                return new FileHivemindAgentStopResult(false, false, true);
            }

            trace?.Invoke("File Hivemind agent mutex is present; sending stop signal.");
            bool requested = requestAgentStop();
            trace?.Invoke("Stop signal Set() returned " + requested + ".");
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (!isAgentRunning())
                {
                    trace?.Invoke("File Hivemind agent mutex disappeared during graceful wait.");
                    return new FileHivemindAgentStopResult(true, requested, true);
                }

                TimeSpan remaining = deadline - DateTime.UtcNow;
                TimeSpan waitSlice = remaining < TimeSpan.FromMilliseconds(250)
                    ? remaining
                    : TimeSpan.FromMilliseconds(250);
                if (waitSlice > TimeSpan.Zero && waitForStoppedSignal(waitSlice))
                {
                    trace?.Invoke("File Hivemind stopped ack was signaled during graceful wait.");
                    return new FileHivemindAgentStopResult(true, requested, true);
                }

                sleep(TimeSpan.FromMilliseconds(100));
            }

            beforeForceStop?.Invoke();
            trace?.Invoke("Graceful wait timed out after " + timeout.TotalSeconds.ToString("0.###") + " seconds; enumerating force-stop candidates.");
            int forcedProcessCount = forceStopAgent();
            trace?.Invoke("Force-stop candidate count handled: " + forcedProcessCount + ".");
            DateTime forceDeadline = DateTime.UtcNow.Add(forceStopWaitTimeout);
            while (DateTime.UtcNow < forceDeadline)
            {
                if (!isAgentRunning())
                {
                    trace?.Invoke("File Hivemind agent mutex disappeared during force-stop wait.");
                    return new FileHivemindAgentStopResult(true, requested, true, forcedProcessCount);
                }

                TimeSpan remaining = forceDeadline - DateTime.UtcNow;
                TimeSpan waitSlice = remaining < TimeSpan.FromMilliseconds(250)
                    ? remaining
                    : TimeSpan.FromMilliseconds(250);
                if (waitSlice > TimeSpan.Zero && waitForStoppedSignal(waitSlice))
                {
                    trace?.Invoke("File Hivemind stopped ack was signaled during force-stop wait.");
                    return new FileHivemindAgentStopResult(true, requested, true, forcedProcessCount);
                }

                sleep(TimeSpan.FromMilliseconds(100));
            }

            bool stopped = !isAgentRunning();
            trace?.Invoke("Final File Hivemind mutex check after force-stop wait: stopped=" + stopped + ".");
            return new FileHivemindAgentStopResult(true, requested, stopped, forcedProcessCount);
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

        private static int ForceStopAgentProcesses(string? installDirectory, Action<string>? trace)
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
                        trace?.Invoke("Force-stop candidate pid=" + process.Id + "; name=" + SafeProcessName(process) + "; path=" + SafeProcessPath(process) + ".");
                        try
                        {
                            if (process.CloseMainWindow() && process.WaitForExit(2500))
                            {
                                trace?.Invoke("Candidate exited after CloseMainWindow pid=" + SafeProcessId(process) + ".");
                                continue;
                            }
                        }
                        catch
                        {
                        }

                        process.Kill(entireProcessTree: true);
                        _ = process.WaitForExit(5000);
                        trace?.Invoke("Kill requested for candidate pid=" + SafeProcessId(process) + "; hasExited=" + SafeHasExited(process) + ".");
                    }
                    catch (Exception ex)
                    {
                        trace?.Invoke("Force-stop candidate handling failed: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }

            return forcedProcessCount;
        }

        private static int SafeProcessId(Process process)
        {
            try
            {
                return process.Id;
            }
            catch
            {
                return -1;
            }
        }

        private static string SafeProcessName(Process process)
        {
            try
            {
                return process.ProcessName;
            }
            catch
            {
                return "(unknown)";
            }
        }

        private static string SafeProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? "(unknown)";
            }
            catch
            {
                return "(unavailable)";
            }
        }

        private static bool SafeHasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch
            {
                return false;
            }
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
