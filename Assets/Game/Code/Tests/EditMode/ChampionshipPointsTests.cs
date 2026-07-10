using System.Collections.Generic;
using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class ChampionshipPointsTests
    {
        [TestCase(1, 25)]
        [TestCase(2, 18)]
        [TestCase(3, 15)]
        [TestCase(4, 12)]
        [TestCase(5, 10)]
        [TestCase(6, 8)]
        [TestCase(7, 6)]
        [TestCase(8, 4)]
        [TestCase(9, 2)]
        [TestCase(10, 1)]
        public void TopTenScoreTheHistoricTable(int position, int expected)
        {
            Assert.AreEqual(expected, ChampionshipPoints.ForPosition(position));
        }

        [TestCase(11)]
        [TestCase(22)]
        [TestCase(0)]
        [TestCase(-3)]
        public void OutsideTopTenScoresZero(int position)
        {
            Assert.AreEqual(0, ChampionshipPoints.ForPosition(position));
        }

        [Test]
        public void WinAndPodiumThresholdsMatchRulebook()
        {
            Assert.IsTrue(ChampionshipPoints.CountsAsWin(1));
            Assert.IsFalse(ChampionshipPoints.CountsAsWin(2));
            Assert.IsTrue(ChampionshipPoints.CountsAsPodium(3));
            Assert.IsFalse(ChampionshipPoints.CountsAsPodium(4));
            Assert.IsFalse(ChampionshipPoints.CountsAsPodium(0));
        }

        [Test]
        public void StandingsTiebreakIsPointsThenWinsThenPodiums()
        {
            // (points, wins, podiums, expected rank label)
            var standings = new List<(int points, int wins, int podiums, string name)>
            {
                (50, 1, 2, "B"),
                (50, 2, 1, "A"),   // same points, more wins -> ahead
                (60, 0, 0, "LEAD"),
                (50, 1, 3, "B2"),  // same points+wins as B, more podiums -> ahead of B
            };

            standings.Sort((a, b) => ChampionshipPoints.CompareStandings(
                a.points, a.wins, a.podiums, b.points, b.wins, b.podiums));

            Assert.AreEqual("LEAD", standings[0].name);
            Assert.AreEqual("A", standings[1].name);
            Assert.AreEqual("B2", standings[2].name);
            Assert.AreEqual("B", standings[3].name);
        }
    }
}
