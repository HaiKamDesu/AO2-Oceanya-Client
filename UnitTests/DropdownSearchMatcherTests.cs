using NUnit.Framework;
using OceanyaClient.Utilities;

namespace UnitTests;

/// <summary>
/// Covers the shared dropdown search semantics: prefix matches rank first, substring matches still appear.
/// </summary>
[TestFixture]
public sealed class DropdownSearchMatcherTests
{
    private static readonly string[] Roster =
    {
        "Phoenix Wright",
        "(pb)saber pendragon",
        "Hobo_Phoenix",
        "saber alter",
        "Miles Edgeworth",
        "Sabrina"
    };

    /// <summary>
    /// The reported case: AO folders are full of bracketed source tags, so prefix-only filtering made the
    /// character unreachable by its own name.
    /// </summary>
    [Test]
    public void Filter_FindsASubstringMatchBehindABracketedPrefix()
    {
        List<string> results = DropdownSearchMatcher.Filter(Roster, "saber", static item => item, 20);

        Assert.That(results, Does.Contain("(pb)saber pendragon"));
    }

    [Test]
    public void Filter_RanksPrefixMatchesAheadOfSubstringMatches()
    {
        List<string> results = DropdownSearchMatcher.Filter(Roster, "saber", static item => item, 20);

        Assert.That(results, Is.EqualTo(new[] { "saber alter", "(pb)saber pendragon" }));
    }

    [Test]
    public void Filter_IsCaseInsensitive()
    {
        List<string> results = DropdownSearchMatcher.Filter(Roster, "PHOENIX", static item => item, 20);

        Assert.That(results, Is.EqualTo(new[] { "Phoenix Wright", "Hobo_Phoenix" }));
    }

    [Test]
    public void Filter_EmptyQueryReturnsTheSourceOrderUpToTheLimit()
    {
        List<string> results = DropdownSearchMatcher.Filter(Roster, string.Empty, static item => item, 3);

        Assert.That(results, Is.EqualTo(new[] { "Phoenix Wright", "(pb)saber pendragon", "Hobo_Phoenix" }));
    }

    [Test]
    public void Filter_RespectsTheResultLimit()
    {
        List<string> results = DropdownSearchMatcher.Filter(Roster, "a", static item => item, 2);

        Assert.That(results, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// The scan must stop once prefix matches alone can fill the result; nothing later can outrank them.
    /// This is what keeps a 10,000-entry roster cheap.
    /// </summary>
    [Test]
    public void Filter_StopsScanningOncePrefixMatchesFillTheResult()
    {
        List<string> candidates = new List<string>();
        for (int i = 0; i < 10_000; i++)
        {
            candidates.Add("saber " + i);
        }

        candidates.Add("(pb)saber pendragon");

        int inspected = 0;
        List<string> results = DropdownSearchMatcher.Filter(
            candidates,
            "saber",
            item =>
            {
                inspected++;
                return item;
            },
            20);

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(20));
            Assert.That(inspected, Is.EqualTo(20), "the scan must stop as soon as the result is full of prefix matches");
        });
    }

    [Test]
    public void FindBestMatch_PrefersExactThenPrefixThenSubstring()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                DropdownSearchMatcher.FindBestMatch(Roster, "sabrina", static item => item),
                Is.EqualTo("Sabrina"));
            Assert.That(
                DropdownSearchMatcher.FindBestMatch(Roster, "sab", static item => item),
                Is.EqualTo("saber alter"));
            Assert.That(
                DropdownSearchMatcher.FindBestMatch(Roster, "pendragon", static item => item),
                Is.EqualTo("(pb)saber pendragon"));
            Assert.That(
                DropdownSearchMatcher.FindBestMatch(Roster, "nothing here", static item => item),
                Is.Null);
        });
    }
}
