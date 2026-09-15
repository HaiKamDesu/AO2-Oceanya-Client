using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Common
{
    /// <summary>
    /// Mirrors the debug console to <c>DEBUG.txt</c> on disk, keeping the previous sessions in
    /// <c>DebugHistory/</c>.
    /// </summary>
    /// <remarks>
    /// The debug console only ever existed in memory, so diagnosing anything that happened in a real
    /// session meant asking the user to reproduce it with the console window open and copy text out. This
    /// writes the same stream to a file instead: <c>DEBUG.txt</c> always holds exactly the current session,
    /// and the previous <see cref="MaxHistoryFiles"/> sessions are rotated into <c>DebugHistory/</c>. Run
    /// the app, reproduce, read the file.
    ///
    /// Writing is done on a dedicated background thread fed by a queue, so a log call never touches the
    /// disk on the calling thread. That matters because logging happens on the UI thread, the network read
    /// loop, and the viewport render path.
    /// </remarks>
    public static class DebugFileLogger
    {
        /// <summary>How many previous sessions are kept in <c>DebugHistory/</c>.</summary>
        public const int MaxHistoryFiles = 5;

        /// <summary>Hard cap on the live <c>DEBUG.txt</c> so a runaway session cannot fill the disk.</summary>
        private const long MaxLiveLogBytes = 256L * 1024 * 1024;

        private const string LiveLogFileName = "DEBUG.txt";
        private const string HistoryFolderName = "DebugHistory";

        private static readonly object startStopLock = new object();

        /// <summary>
        /// Queue feeding the writer thread. Recreated on every <see cref="Start"/>: completing a
        /// <see cref="BlockingCollection{T}"/> is permanent, so a single shared instance would leave the
        /// logger dead after the first <see cref="Stop"/>.
        /// </summary>
        private static BlockingCollection<string> pendingLines =
            new BlockingCollection<string>(new ConcurrentQueue<string>());

        private static Thread? writerThread;
        private static StreamWriter? writer;
        private static Timer? memorySampleTimer;
        private static string liveLogPath = string.Empty;
        private static long writtenBytes;
        private static bool capReached;
        private static bool started;

        /// <summary>Baseline and running extremes for the growth columns on each <c>[MEM]</c> line.</summary>
        /// <remarks>
        /// A slow leak reads as several hundred near-identical absolute numbers: "it was fine for two hours,
        /// then it stacks" is a REPORT about the shape of the curve, and absolutes alone make whoever reads
        /// the log reconstruct that curve by hand across hundreds of samples. Carrying the delta since the
        /// previous sample, the total since session start, and the peak puts the inflection point on the
        /// line where it happens.
        /// </remarks>
        private static long sessionStartPrivateMb = -1;
        private static long previousPrivateMb = -1;
        private static long peakPrivateMb;
        private static long peakWorkingSetMb;
        private static DateTime sessionStartUtc = DateTime.UtcNow;
        private static int memorySampleCount;

        /// <summary>Absolute path of the current session's log file, or empty when not started.</summary>
        public static string LiveLogPath => liveLogPath;

        /// <summary>Whether the logger is currently mirroring to disk.</summary>
        public static bool IsRunning => started;

        /// <summary>
        /// Extra per-sample diagnostics appended to the periodic <c>[MEM]</c> line.
        /// </summary>
        /// <remarks>
        /// Owned by callers that can see things this assembly cannot (cached character count, open viewport
        /// panes, image cache sizes). Each provider returns one <c>key=value</c> fragment. A provider that
        /// throws is dropped rather than breaking the sample.
        /// </remarks>
        public static readonly List<Func<string>> MemorySampleProviders = new List<Func<string>>();

        /// <summary>
        /// Rotates the previous session's log out, opens a fresh <c>DEBUG.txt</c>, and starts mirroring.
        /// </summary>
        /// <param name="logDirectory">Directory to write into; defaults to the application base directory.</param>
        /// <param name="sessionHeaderLines">Lines describing this session, written at the top of the file.</param>
        public static void Start(string? logDirectory = null, IEnumerable<string>? sessionHeaderLines = null)
        {
            lock (startStopLock)
            {
                if (started)
                {
                    return;
                }

                try
                {
                    string directory = string.IsNullOrWhiteSpace(logDirectory)
                        ? AppDomain.CurrentDomain.BaseDirectory
                        : logDirectory;
                    Directory.CreateDirectory(directory);
                    liveLogPath = Path.Combine(directory, LiveLogFileName);
                    RotatePreviousSession(directory, liveLogPath);

                    pendingLines = new BlockingCollection<string>(new ConcurrentQueue<string>());

                    writer = new StreamWriter(
                        new FileStream(liveLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                        Encoding.UTF8);
                    writtenBytes = 0;
                    capReached = false;

                    BlockingCollection<string> sessionQueue = pendingLines;
                    StreamWriter sessionWriter = writer;
                    writerThread = new Thread(() => WriterLoop(sessionQueue, sessionWriter))
                    {
                        IsBackground = true,
                        Name = "OceanyaDebugFileLogger"
                    };
                    writerThread.Start();
                    started = true;

                    foreach (string headerLine in BuildSessionHeader(sessionHeaderLines))
                    {
                        pendingLines.Add(headerLine);
                    }

                    CustomConsole.OnWriteLine += Enqueue;
                    memorySampleTimer = new Timer(
                        _ => WriteMemorySample(),
                        null,
                        TimeSpan.FromSeconds(15),
                        TimeSpan.FromSeconds(15));
                }
                catch (Exception ex)
                {
                    // Logging must never take the app down. Fall back to memory-only logging.
                    started = false;
                    writer = null;
                    liveLogPath = string.Empty;
                    Debug.WriteLine("DebugFileLogger could not start: " + ex);
                }
            }
        }

        /// <summary>Flushes and closes the current session's log file.</summary>
        public static void Stop()
        {
            lock (startStopLock)
            {
                if (!started)
                {
                    return;
                }

                started = false;
                CustomConsole.OnWriteLine -= Enqueue;
                memorySampleTimer?.Dispose();
                memorySampleTimer = null;

                try
                {
                    pendingLines.Add(BuildMemorySampleLine("session end"));
                    pendingLines.Add($"=== SESSION END {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    pendingLines.CompleteAdding();
                    writerThread?.Join(TimeSpan.FromSeconds(5));
                }
                catch
                {
                    // Best-effort shutdown.
                }
                finally
                {
                    try
                    {
                        // The writer thread has drained and exited by now (or timed out); closing the
                        // stream here is what releases the file handle for the next session.
                        writer?.Flush();
                        writer?.Dispose();
                    }
                    catch
                    {
                        // Best-effort shutdown.
                    }

                    writer = null;
                    writerThread = null;
                    liveLogPath = string.Empty;
                }
            }
        }

        /// <summary>Writes one line straight into the session log, bypassing the console formatting.</summary>
        public static void WriteRaw(string line)
        {
            if (!started)
            {
                return;
            }

            Enqueue(line);
        }

        /// <summary>Appends a memory sample to the log immediately, tagged with the supplied reason.</summary>
        public static void WriteMemorySample(string reason = "periodic")
        {
            if (!started)
            {
                return;
            }

            Enqueue(BuildMemorySampleLine(reason));
        }

        private static string BuildMemorySampleLine(string reason)
        {
            string details;
            try
            {
                using Process process = Process.GetCurrentProcess();
                long workingSetMb = process.WorkingSet64 / (1024 * 1024);
                long privateMb = process.PrivateMemorySize64 / (1024 * 1024);
                long managedMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
                GCMemoryInfo gcInfo = GC.GetGCMemoryInfo();
                long heapMb = gcInfo.HeapSizeBytes / (1024 * 1024);

                memorySampleCount++;
                if (sessionStartPrivateMb < 0)
                {
                    sessionStartPrivateMb = privateMb;
                    sessionStartUtc = DateTime.UtcNow;
                }

                long deltaMb = previousPrivateMb < 0 ? 0 : privateMb - previousPrivateMb;
                previousPrivateMb = privateMb;
                peakPrivateMb = Math.Max(peakPrivateMb, privateMb);
                peakWorkingSetMb = Math.Max(peakWorkingSetMb, workingSetMb);

                // Managed bytes the GC is holding but not using, and the large object heap, which is where
                // decoded bitmaps and big strings land. A rising LOH with a flat managed heap is a very
                // different bug from a rising managed heap.
                long fragmentedMb = gcInfo.FragmentedBytes / (1024 * 1024);
                long committedMb = gcInfo.TotalCommittedBytes / (1024 * 1024);
                long lohMb = ReadLargeObjectHeapMb(gcInfo);

                details =
                    $"workingSetMB={workingSetMb} privateMB={privateMb} managedMB={managedMb} gcHeapMB={heapMb}"
                    + $" deltaMB={deltaMb:+#;-#;0} sinceStartMB={privateMb - sessionStartPrivateMb:+#;-#;0}"
                    + $" peakPrivateMB={peakPrivateMb} peakWorkingSetMB={peakWorkingSetMb}"
                    + $" upMin={(DateTime.UtcNow - sessionStartUtc).TotalMinutes:0}"
                    + $" lohMB={lohMb} fragmentedMB={fragmentedMb} committedMB={committedMb}"
                    + $" gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}"
                    + $" threads={process.Threads.Count} handles={process.HandleCount}"
                    + $" logEntries={CustomConsole.StoredEntryCount}";
            }
            catch (Exception ex)
            {
                details = "unavailable (" + ex.GetType().Name + ")";
            }

            foreach (Func<string> provider in MemorySampleProviders.ToArray())
            {
                try
                {
                    string fragment = provider()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(fragment))
                    {
                        details += " " + fragment;
                    }
                }
                catch
                {
                    // A broken provider must not break the sample.
                }
            }

            return $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [MEM] ({reason}) {details}";
        }

        /// <summary>Large object heap size, which is not exposed directly on every runtime.</summary>
        private static long ReadLargeObjectHeapMb(GCMemoryInfo gcInfo)
        {
            try
            {
                ReadOnlySpan<GCGenerationInfo> generations = gcInfo.GenerationInfo;

                // Generations are gen0, gen1, gen2, LOH, POH; older runtimes stop earlier.
                return generations.Length > 3 ? generations[3].SizeAfterBytes / (1024 * 1024) : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static void Enqueue(string line)
        {
            try
            {
                if (!pendingLines.IsAddingCompleted)
                {
                    pendingLines.Add(line);
                }
            }
            catch
            {
                // Queue closed during shutdown.
            }
        }

        private static void WriterLoop(BlockingCollection<string> queue, StreamWriter sessionWriter)
        {
            try
            {
                foreach (string line in queue.GetConsumingEnumerable())
                {
                    if (capReached)
                    {
                        continue;
                    }

                    try
                    {
                        sessionWriter.WriteLine(line);
                        writtenBytes += line.Length + Environment.NewLine.Length;
                        if (writtenBytes >= MaxLiveLogBytes)
                        {
                            sessionWriter.WriteLine(
                                $"=== LOG CAP REACHED ({MaxLiveLogBytes / (1024 * 1024)} MB); further lines dropped ===");
                            capReached = true;
                        }

                        sessionWriter.Flush();
                    }
                    catch
                    {
                        // A failed write must not kill the writer thread.
                    }
                }
            }
            catch
            {
                // Collection completed while enumerating.
            }
        }

        private static IEnumerable<string> BuildSessionHeader(IEnumerable<string>? extraLines)
        {
            List<string> header = new List<string>
            {
                "=== OCEANYA CLIENT DEBUG LOG ===",
                $"Session start: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (UTC {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss})",
                $"OS: {Environment.OSVersion} ({(Environment.Is64BitProcess ? "x64" : "x86")} process, {Environment.ProcessorCount} cores)",
                $"Runtime: {Environment.Version}",
                $"Base directory: {AppDomain.CurrentDomain.BaseDirectory}"
            };

            if (extraLines != null)
            {
                header.AddRange(extraLines.Where(line => !string.IsNullOrWhiteSpace(line)));
            }

            header.Add("================================");
            return header;
        }

        /// <summary>
        /// Moves the previous <c>DEBUG.txt</c> into <c>DebugHistory/</c> and prunes to the newest
        /// <see cref="MaxHistoryFiles"/>.
        /// </summary>
        internal static void RotatePreviousSession(string directory, string livePath)
        {
            try
            {
                string historyDirectory = Path.Combine(directory, HistoryFolderName);
                Directory.CreateDirectory(historyDirectory);

                if (File.Exists(livePath))
                {
                    DateTime previousSessionTime = File.GetLastWriteTime(livePath);
                    string archivedName = $"debug_{previousSessionTime:yyyyMMdd_HHmmss}.txt";
                    string archivedPath = Path.Combine(historyDirectory, archivedName);
                    int duplicateSuffix = 1;
                    while (File.Exists(archivedPath))
                    {
                        archivedPath = Path.Combine(
                            historyDirectory,
                            $"debug_{previousSessionTime:yyyyMMdd_HHmmss}_{duplicateSuffix}.txt");
                        duplicateSuffix++;
                    }

                    File.Move(livePath, archivedPath);
                }

                PruneHistory(historyDirectory);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("DebugFileLogger could not rotate the previous session: " + ex);
            }
        }

        private static void PruneHistory(string historyDirectory)
        {
            try
            {
                List<FileInfo> archived = new DirectoryInfo(historyDirectory)
                    .GetFiles("debug_*.txt")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToList();

                foreach (FileInfo staleFile in archived.Skip(MaxHistoryFiles))
                {
                    try
                    {
                        staleFile.Delete();
                    }
                    catch
                    {
                        // Best-effort pruning.
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("DebugFileLogger could not prune history: " + ex);
            }
        }
    }
}
