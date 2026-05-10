using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Lightweight startup-phase timer that writes a human-readable log to
    /// %AppData%\OceanyaClient\startup_timing.log after each session.
    /// Useful for profiling the launch critical path.
    /// </summary>
    public static class StartupTimingLogger
    {
        private static readonly Stopwatch _wall = Stopwatch.StartNew();
        private static readonly DateTime _startUtc = DateTime.UtcNow;
        private static long _launchClickMs = -1;
        private static readonly List<(long Ms, string Phase, string? Details)> _events = new();
        private static readonly object _lock = new object();

        /// <summary>
        /// Records a named phase event with the current elapsed time.
        /// </summary>
        public static void Log(string phase, string? details = null)
        {
            long ms = _wall.ElapsedMilliseconds;
            lock (_lock)
            {
                _events.Add((ms, phase, details));
            }
        }

        /// <summary>
        /// Records the moment the user clicked Launch and also logs the phase.
        /// </summary>
        public static void MarkLaunchClick()
        {
            _launchClickMs = _wall.ElapsedMilliseconds;
            Log("launch_clicked");
        }

        /// <summary>
        /// Writes the collected timing log to disk.
        /// </summary>
        public static void WriteLog()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OceanyaClient");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "startup_timing.log");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== Oceanya Startup Timing ===");
                sb.AppendLine($"Session: {_startUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
                sb.AppendLine($"App version: {AppVersionInfo.AssemblyVersion}");
                sb.AppendLine();
                sb.AppendLine($"{"Phase",-45} | {"Abs(ms)",8} | {"Delta(ms)",9}");
                sb.AppendLine(new string('-', 70));

                List<(long Ms, string Phase, string? Details)> events;
                lock (_lock)
                {
                    events = new List<(long, string, string?)>(_events);
                }

                long prevMs = 0;
                foreach ((long ms, string phase, string? details) in events)
                {
                    long delta = ms - prevMs;
                    string label = details != null ? $"{phase} ({details})" : phase;
                    sb.AppendLine($"{label,-45} | {ms,8} | {delta,9}");
                    prevMs = ms;
                }

                sb.AppendLine();
                sb.AppendLine("=== Summary ===");

                long totalMs = events.Count > 0 ? events[^1].Ms : 0;
                sb.AppendLine($"App startup → last event:    {totalMs}ms  ({totalMs / 1000.0:F1}s)");

                if (_launchClickMs >= 0 && events.Count > 0)
                {
                    long fromLaunch = events[^1].Ms - _launchClickMs;
                    sb.AppendLine($"launch_clicked → last event: {fromLaunch}ms  ({fromLaunch / 1000.0:F1}s)");
                }

                long fakeLoadMs = GetPhaseDuration("fake_loading_begin", "fake_loading_end");
                if (fakeLoadMs >= 0)
                {
                    sb.AppendLine($"Fake loading duration:       {fakeLoadMs}ms");
                }

                long bgCheckMs = GetPhaseDuration("background_tracked_check_begin", "background_tracked_check_end");
                if (bgCheckMs >= 0)
                {
                    sb.AppendLine($"Background asset check:      {bgCheckMs}ms");
                }

                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Failed to write startup timing log.", ex);
            }
        }

        private static long GetPhaseDuration(string startPhase, string endPhase)
        {
            long start = -1;
            long end = -1;
            lock (_lock)
            {
                foreach ((long ms, string phase, string? _) in _events)
                {
                    if (start < 0 && string.Equals(phase, startPhase, StringComparison.Ordinal))
                    {
                        start = ms;
                    }
                    else if (start >= 0 && string.Equals(phase, endPhase, StringComparison.Ordinal))
                    {
                        end = ms;
                        break;
                    }
                }
            }

            return start >= 0 && end >= 0 ? end - start : -1;
        }
    }
}
