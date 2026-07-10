using System.Collections.Generic;
using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class RaceClassifierTests
    {
        class Entry
        {
            public string Name;
            public float TotalTime;
            public float Penalty;
            public int Position;
        }

        static void Classify(List<Entry> entries)
        {
            RaceClassifier.AssignFinishingOrder(
                entries,
                e => e.TotalTime,
                e => e.Penalty,
                (e, p) => e.Position = p);
        }

        [Test]
        public void PenaltySecondsReorderCloseFinishers()
        {
            var entries = new List<Entry>
            {
                new Entry { Name = "winner-on-road", TotalTime = 5400f, Penalty = 5f },
                new Entry { Name = "second-on-road", TotalTime = 5402f, Penalty = 0f },
            };

            Classify(entries);

            Assert.AreEqual("second-on-road", entries[0].Name);
            Assert.AreEqual(1, entries[0].Position);
            Assert.AreEqual(2, entries.Find(e => e.Name == "winner-on-road").Position);
        }

        [Test]
        public void PenaltyThatDoesNotCloseTheGapKeepsOrder()
        {
            var entries = new List<Entry>
            {
                new Entry { Name = "leader", TotalTime = 5400f, Penalty = 5f },
                new Entry { Name = "chaser", TotalTime = 5410f, Penalty = 0f },
            };

            Classify(entries);
            Assert.AreEqual("leader", entries[0].Name);
        }

        [Test]
        public void RetirementStampAlwaysClassifiesBehindAnyRunningCar()
        {
            float raceElapsed = 3000f;
            int raceLaps = 50;

            // Car that retired on lap 2 of 50.
            float retired = RaceClassifier.RetirementTimeStamp(raceElapsed, raceLaps, 2);
            // Slowest possible running car: zero progress into the race distance.
            float lappedEstimate = RaceClassifier.EstimateUnfinishedTotalTime(raceElapsed, raceLaps, 0f, 7000f);

            Assert.Greater(retired, lappedEstimate + 5000f,
                "A DNF must classify behind every running car by a wide margin");
        }

        [Test]
        public void EarlierRetirementClassifiesLowerThanLaterRetirement()
        {
            float raceElapsed = 3000f;
            float retiredLap2 = RaceClassifier.RetirementTimeStamp(raceElapsed, 50, 2);
            float retiredLap40 = RaceClassifier.RetirementTimeStamp(raceElapsed, 50, 40);
            Assert.Greater(retiredLap2, retiredLap40);
        }

        [Test]
        public void UnfinishedEstimateIsMonotonicWithRaceDistance()
        {
            // The lapped-car bug this logic fixed: more progress must always mean
            // a strictly smaller estimated total time.
            float furtherAlong = RaceClassifier.EstimateUnfinishedTotalTime(5400f, 50, 49.9f, 5000f);
            float lessFarAlong = RaceClassifier.EstimateUnfinishedTotalTime(5400f, 50, 49.1f, 5000f);
            float lappedCar = RaceClassifier.EstimateUnfinishedTotalTime(5400f, 50, 48.95f, 5000f);

            Assert.Less(furtherAlong, lessFarAlong);
            Assert.Less(lessFarAlong, lappedCar);
        }

        [Test]
        public void NominalLapNeverDropsBelowFloor()
        {
            Assert.AreEqual(72f, RaceClassifier.NominalLapSeconds(1000f));
            Assert.AreEqual(125f, RaceClassifier.NominalLapSeconds(7000f), 0.001f);
        }

        [Test]
        public void CompletedRaceDistanceEstimatesNoExtraTime()
        {
            float estimate = RaceClassifier.EstimateUnfinishedTotalTime(5400f, 50, 50f, 5000f);
            Assert.AreEqual(5400f, estimate);
        }
    }
}
