using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OceanyaClient.Features.Chat;
using NUnit.Framework;

namespace UnitTests;

[TestFixture]
[Category("NoNetworkCall")]
public class AllLogSearchServiceTests
{
    [Test]
    public async Task SearchAsync_OrdersMatchingFilesByNewestFirst()
    {
        string root = CreateTempDirectory();
        try
        {
            string oldFile = Path.Combine(root, "server", "old.log");
            string newFile = Path.Combine(root, "server", "new.log");
            Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
            await File.WriteAllTextAsync(oldFile, "[Mon Jan 1 00:00:00 2024 UTC] Kam: old");
            await File.WriteAllTextAsync(newFile, "[Mon Jan 2 00:00:00 2024 UTC] Kam: new");
            File.SetLastWriteTime(oldFile, new DateTime(2024, 1, 1, 0, 0, 0));
            File.SetLastWriteTime(newFile, new DateTime(2024, 1, 2, 0, 0, 0));

            AllLogSearchService service = new AllLogSearchService();
            var summary = await service.SearchAsync(
                root,
                new AllLogSearchOptions("Kam", IncludeIc: true, IncludeOoc: true, MatchCase: false, WholeWord: false, UseRegex: false),
                progress: null,
                CancellationToken.None);
            var results = summary.Results;

            Assert.Multiple(() =>
            {
                Assert.That(results, Has.Count.EqualTo(2));
                Assert.That(Path.GetFileName(results[0].FilePath), Is.EqualTo("new.log"));
                Assert.That(Path.GetFileName(results[1].FilePath), Is.EqualTo("old.log"));
                Assert.That(summary.TotalLogFiles, Is.EqualTo(2));
                Assert.That(summary.TotalTextLines, Is.EqualTo(2));
                Assert.That(summary.SearchElapsed, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SearchAsync_UsesIcOocScopeFilters()
    {
        string root = CreateTempDirectory();
        try
        {
            string file = Path.Combine(root, "scope.log");
            await File.WriteAllTextAsync(
                file,
                "[OOC][Mon Jan 1 00:00:00 2024 UTC] Kam: ooc" + Environment.NewLine
                + "[Mon Jan 1 00:00:01 2024 UTC] Kam: ic");

            AllLogSearchService service = new AllLogSearchService();
            var oocOnlySummary = await service.SearchAsync(
                root,
                new AllLogSearchOptions("Kam", IncludeIc: false, IncludeOoc: true, MatchCase: false, WholeWord: false, UseRegex: false),
                progress: null,
                CancellationToken.None);
            var icOnlySummary = await service.SearchAsync(
                root,
                new AllLogSearchOptions("Kam", IncludeIc: true, IncludeOoc: false, MatchCase: false, WholeWord: false, UseRegex: false),
                progress: null,
                CancellationToken.None);
            var bothSummary = await service.SearchAsync(
                root,
                new AllLogSearchOptions("Kam", IncludeIc: true, IncludeOoc: true, MatchCase: false, WholeWord: false, UseRegex: false),
                progress: null,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                var oocOnly = oocOnlySummary.Results;
                var icOnly = icOnlySummary.Results;
                var both = bothSummary.Results;
                Assert.That(oocOnly.Single().MatchCount, Is.EqualTo(1));
                Assert.That(icOnly.Single().MatchCount, Is.EqualTo(1));
                Assert.That(both.Single().MatchCount, Is.EqualTo(2));
                Assert.That(bothSummary.TotalLogFiles, Is.EqualTo(1));
                Assert.That(bothSummary.TotalTextLines, Is.EqualTo(2));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SearchAsync_ReportsScannedLogAndLineCountsWhenNoFilesMatch()
    {
        string root = CreateTempDirectory();
        try
        {
            string file = Path.Combine(root, "empty-result.log");
            await File.WriteAllTextAsync(
                file,
                "[Mon Jan 1 00:00:00 2024 UTC] Phoenix: objection" + Environment.NewLine
                + "[OOC][Mon Jan 1 00:00:01 2024 UTC] Maya: no match here");

            AllLogSearchService service = new AllLogSearchService();
            var summary = await service.SearchAsync(
                root,
                new AllLogSearchOptions("Kam", IncludeIc: true, IncludeOoc: true, MatchCase: false, WholeWord: false, UseRegex: false),
                progress: null,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(summary.Results, Is.Empty);
                Assert.That(summary.TotalLogFiles, Is.EqualTo(1));
                Assert.That(summary.TotalTextLines, Is.EqualTo(2));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void FindOffsetsInText_ReturnsScopedAbsoluteOffsetsForPreviewHighlighting()
    {
        string text = "[OOC] Kam" + Environment.NewLine + "Kam IC";
        var options = new AllLogSearchOptions("Kam", IncludeIc: false, IncludeOoc: true, MatchCase: false, WholeWord: false, UseRegex: false);

        var matches = AllLogSearchService.FindOffsetsInText(text, options);

        Assert.Multiple(() =>
        {
            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(matches[0].StartIndex, Is.EqualTo(6));
        });
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "OceanyaAllLogSearchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
