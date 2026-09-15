using Common;
using NUnit.Framework;

namespace UnitTests;

/// <summary>
/// Covers the on-disk debug log: one file for the current session, a bounded history of previous ones.
/// </summary>
[TestFixture]
public sealed class DebugFileLoggerTests
{
    private string logDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        logDirectory = Path.Combine(Path.GetTempPath(), "OceanyaDebugLoggerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        DebugFileLogger.Stop();
        try
        {
            Directory.Delete(logDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Test]
    public void Start_WritesTheSessionToDebugTxt()
    {
        DebugFileLogger.Start(logDirectory, new[] { "Test header line" });
        CustomConsole.Info("a line that must reach the file");
        DebugFileLogger.Stop();

        string livePath = Path.Combine(logDirectory, "DEBUG.txt");
        Assert.That(File.Exists(livePath), Is.True);

        string contents = File.ReadAllText(livePath);
        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Contain("OCEANYA CLIENT DEBUG LOG"));
            Assert.That(contents, Does.Contain("Test header line"));
            Assert.That(contents, Does.Contain("a line that must reach the file"));
            Assert.That(contents, Does.Contain("SESSION END"));
        });
    }

    /// <summary>
    /// DEBUG.txt must always be exactly the latest session; older ones move to DebugHistory and the folder
    /// is capped, so the logger cannot grow without bound on disk.
    /// </summary>
    [Test]
    public void Start_RotatesPreviousSessionsAndCapsHistory()
    {
        const int sessionCount = DebugFileLogger.MaxHistoryFiles + 3;
        for (int session = 0; session < sessionCount; session++)
        {
            DebugFileLogger.Start(logDirectory);
            CustomConsole.Info($"session marker {session}");
            DebugFileLogger.Stop();
        }

        string livePath = Path.Combine(logDirectory, "DEBUG.txt");
        string historyDirectory = Path.Combine(logDirectory, "DebugHistory");

        Assert.That(File.Exists(livePath), Is.True);
        Assert.That(File.ReadAllText(livePath), Does.Contain($"session marker {sessionCount - 1}"),
            "DEBUG.txt must hold the most recent session");

        string[] archived = Directory.GetFiles(historyDirectory, "debug_*.txt");
        Assert.That(archived, Has.Length.LessThanOrEqualTo(DebugFileLogger.MaxHistoryFiles));
    }

    [Test]
    public void MemorySample_IncludesProcessCountersAndCustomProviders()
    {
        DebugFileLogger.MemorySampleProviders.Add(() => "testProvider=42");
        try
        {
            DebugFileLogger.Start(logDirectory);
            DebugFileLogger.WriteMemorySample("unit test");
            DebugFileLogger.Stop();
        }
        finally
        {
            DebugFileLogger.MemorySampleProviders.RemoveAll(provider => provider() == "testProvider=42");
        }

        string contents = File.ReadAllText(Path.Combine(logDirectory, "DEBUG.txt"));
        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Contain("[MEM] (unit test)"));
            Assert.That(contents, Does.Contain("workingSetMB="));

            // Growth columns: absolutes alone make a slow leak unreadable, because "fine for two hours,
            // then it stacks" is a claim about the shape of the curve across hundreds of samples.
            Assert.That(contents, Does.Contain("deltaMB="));
            Assert.That(contents, Does.Contain("sinceStartMB="));
            Assert.That(contents, Does.Contain("peakPrivateMB="));
            Assert.That(contents, Does.Contain("upMin="));
            Assert.That(contents, Does.Contain("lohMB="));
            Assert.That(contents, Does.Contain("fragmentedMB="));
            Assert.That(contents, Does.Contain("managedMB="));
            Assert.That(contents, Does.Contain("testProvider=42"));
        });
    }

    /// <summary>
    /// The in-memory console buffer used to keep a million entries in two parallel collections, which is a
    /// large share of a long session's memory. It is now bounded and single-copy.
    /// </summary>
    [Test]
    public void CustomConsole_BoundsItsInMemoryBuffer()
    {
        CustomConsole.ClearStoredEntriesForTests();
        for (int index = 0; index < CustomConsole.MaxStoredEntries + 500; index++)
        {
            CustomConsole.Info("bounded buffer line " + index);
        }

        Assert.That(CustomConsole.StoredEntryCount, Is.EqualTo(CustomConsole.MaxStoredEntries));

        List<CustomConsole.LogEntry> snapshot = CustomConsole.GetLogEntriesSnapshot();
        Assert.That(snapshot[^1].Text, Does.Contain("bounded buffer line " + (CustomConsole.MaxStoredEntries + 499)),
            "the newest entries must be the ones kept");
    }
}
