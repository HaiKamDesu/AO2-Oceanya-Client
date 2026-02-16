using System.Collections.Generic;
using AOBot_Testing;
using AOBot_Testing.Structures;
using NUnit.Framework;

namespace UnitTests;

[TestFixture]
public class BotJSONHandlingTests
{
    [Test]
    public void ParseGetArea_ParsesCmAndShownames()
    {
        string input = "People in this area:\n" +
                       "=================\n" +
                       "[CM] [1] Phoenix (JusticeForAll)\n" +
                       "[2] Franziska (WhipUser)\n" +
                       "=================\n";

        List<Player> players = AO2Parser.ParseGetArea(input);

        Assert.Multiple(() =>
        {
            Assert.That(players, Has.Count.EqualTo(2));
            Assert.That(players[0].IsCM, Is.True);
            Assert.That(players[0].PlayerID, Is.EqualTo(1));
            Assert.That(players[0].ICCharacterName, Is.EqualTo("Phoenix"));
            Assert.That(players[0].OOCShowname, Is.EqualTo("JusticeForAll"));
            Assert.That(players[1].ICCharacterName, Is.EqualTo("Franziska"));
            Assert.That(players[1].OOCShowname, Is.EqualTo("WhipUser"));
        });
    }

    [Test]
    public void ParseGetArea_ParsesEntriesWithoutShownames()
    {
        string input = "People in this area:\n" +
                       "=================\n" +
                       "[1] Phoenix\n" +
                       "[2] Miles\n" +
                       "=================\n";

        List<Player> players = AO2Parser.ParseGetArea(input);

        Assert.Multiple(() =>
        {
            Assert.That(players, Has.Count.EqualTo(2));
            Assert.That(players[0].ICCharacterName, Is.EqualTo("Phoenix"));
            Assert.That(players[0].OOCShowname, Is.Null);
            Assert.That(players[1].ICCharacterName, Is.EqualTo("Miles"));
            Assert.That(players[1].OOCShowname, Is.Null);
        });
    }

    [Test]
    public void ParseGetArea_ReturnsEmptyOnInvalidInput()
    {
        List<Player> players = AO2Parser.ParseGetArea("Not a player list");
        Assert.That(players, Is.Empty);
    }
}
