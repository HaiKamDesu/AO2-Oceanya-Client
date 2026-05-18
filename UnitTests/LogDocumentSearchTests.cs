using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using OceanyaClient.Components;
using NUnit.Framework;

namespace UnitTests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[Category("NoNetworkCall")]
public class LogDocumentSearchTests
{
    [SetUp]
    public void Setup()
    {
        _ = WpfTestApplicationContext.EnsureCreated();
    }

    [Test]
    public void Find_CaseSensitive_FiltersMatches()
    {
        FlowDocument document = BuildDocument(new Run("Alpha alpha ALPHA"));

        var insensitive = LogDocumentSearch.Find(document, "alpha", matchCase: false, wholeWord: false, useRegex: false);
        var sensitive = LogDocumentSearch.Find(document, "alpha", matchCase: true, wholeWord: false, useRegex: false);

        Assert.Multiple(() =>
        {
            Assert.That(insensitive, Has.Count.EqualTo(3));
            Assert.That(sensitive, Has.Count.EqualTo(1));
            Assert.That(ReadMatch(sensitive[0]), Is.EqualTo("alpha"));
        });
    }

    [Test]
    public void Find_WholeWord_UsesFullDocumentBoundariesAcrossRuns()
    {
        FlowDocument document = BuildDocument(
            new Run("sc"),
            new Run("ar scar "),
            new Run("car"),
            new Run(" scarlet"));

        var partial = LogDocumentSearch.Find(document, "car", matchCase: false, wholeWord: false, useRegex: false);
        var wholeWord = LogDocumentSearch.Find(document, "car", matchCase: false, wholeWord: true, useRegex: false);

        Assert.Multiple(() =>
        {
            Assert.That(partial, Has.Count.EqualTo(4));
            Assert.That(wholeWord, Has.Count.EqualTo(1));
            Assert.That(ReadMatch(wholeWord[0]), Is.EqualTo("car"));
        });
    }

    [Test]
    public void Find_RegexAndWholeWord_WorkAcrossFormattedRuns()
    {
        FlowDocument document = BuildDocument(
            new Run("Case A-12 "),
            new Run("A-123 "),
            new Run("A-12x"));

        var matches = LogDocumentSearch.Find(document, @"A-\d{2}", matchCase: true, wholeWord: true, useRegex: true);

        Assert.Multiple(() =>
        {
            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(ReadMatch(matches[0]), Is.EqualTo("A-12"));
        });
    }

    [Test]
    public void Find_InvalidRegex_ReturnsNoMatches()
    {
        FlowDocument document = BuildDocument(new Run("anything"));

        var matches = LogDocumentSearch.Find(document, "[", matchCase: false, wholeWord: false, useRegex: true);

        Assert.That(matches, Is.Empty);
    }

    [Test]
    [Explicit("Manual log find benchmark; intentionally excluded from normal verification.")]
    [Category("ManualBenchmark")]
    public void Find_TenThousandLineManualBenchmark()
    {
        FlowDocument document = new FlowDocument();
        for (int i = 0; i < 10_000; i++)
        {
            Paragraph paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run($"[{i:00000}] Alpha witness testimony line "));
            paragraph.Inlines.Add(new Run(i % 10 == 0 ? "needle " : "filler "));
            paragraph.Inlines.Add(new Run("with formatted runs and repeated courtroom text."));
            document.Blocks.Add(paragraph);
        }

        Stopwatch indexTimer = Stopwatch.StartNew();
        LogDocumentSearch.DocumentTextIndex index = LogDocumentSearch.CreateIndex(document);
        indexTimer.Stop();

        Stopwatch plainTimer = Stopwatch.StartNew();
        var offsets = LogDocumentSearch.FindOffsets(index, "needle", matchCase: false, wholeWord: true, useRegex: false);
        plainTimer.Stop();

        Stopwatch resolveTimer = Stopwatch.StartNew();
        var resolved = LogDocumentSearch.ResolveMatches(index, offsets);
        resolveTimer.Stop();

        TestContext.WriteLine($"10k log lines: index={indexTimer.ElapsedMilliseconds}ms, search={plainTimer.ElapsedMilliseconds}ms, resolve={resolveTimer.ElapsedMilliseconds}ms, matches={resolved.Count}");

        Assert.That(resolved, Has.Count.EqualTo(1_000));
    }

    private static FlowDocument BuildDocument(params Inline[] inlines)
    {
        FlowDocument document = new FlowDocument();
        Paragraph paragraph = new Paragraph();
        foreach (Inline inline in inlines)
        {
            paragraph.Inlines.Add(inline);
        }

        document.Blocks.Add(paragraph);
        return document;
    }

    private static string ReadMatch(LogTextMatch match)
    {
        return new TextRange(match.Start, match.End).Text;
    }
}
