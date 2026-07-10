using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class QualifyingProgressionTests
    {
        [Test]
        public void FullTwentyTwoCarFieldEliminatesSixThenSix()
        {
            Assert.AreEqual(6, QualifyingProgression.EliminationCount(1, 22));
            Assert.AreEqual(6, QualifyingProgression.EliminationCount(2, 16));
            Assert.AreEqual(0, QualifyingProgression.EliminationCount(3, 10));
        }

        [Test]
        public void UndersizedFieldsDegradeInsteadOfEmptyingTheSession()
        {
            // 18 cars: Q1 drops only 2, Q2 drops 6, Q3 runs 10.
            Assert.AreEqual(2, QualifyingProgression.EliminationCount(1, 18));
            Assert.AreEqual(6, QualifyingProgression.EliminationCount(2, 16));

            // 12 cars: Q1 drops none, Q2 drops 2.
            Assert.AreEqual(0, QualifyingProgression.EliminationCount(1, 12));
            Assert.AreEqual(2, QualifyingProgression.EliminationCount(2, 12));
        }

        [Test]
        public void EliminationIsCappedAtSixEvenForOversizedFields()
        {
            Assert.AreEqual(6, QualifyingProgression.EliminationCount(1, 30));
        }

        [Test]
        public void FinalPhaseNeverEliminates()
        {
            Assert.AreEqual(0, QualifyingProgression.EliminationCount(3, 22));
            Assert.IsTrue(QualifyingProgression.IsFinalPhase(3));
            Assert.IsFalse(QualifyingProgression.IsFinalPhase(2));
        }

        [Test]
        public void FirstEliminatedIndexMarksTheSlowestTail()
        {
            // 22 cars sorted ascending by time: indices 16..21 are eliminated in Q1.
            Assert.AreEqual(16, QualifyingProgression.FirstEliminatedIndex(1, 22));
            Assert.AreEqual(10, QualifyingProgression.FirstEliminatedIndex(2, 16));
            Assert.AreEqual(10, QualifyingProgression.FirstEliminatedIndex(3, 10));
        }

        [Test]
        public void PhaseAdvancementStopsAtQ3()
        {
            Assert.IsTrue(SessionFlow.TryAdvanceQualifyingPhase(1, out int q2));
            Assert.AreEqual(2, q2);
            Assert.IsTrue(SessionFlow.TryAdvanceQualifyingPhase(2, out int q3));
            Assert.AreEqual(3, q3);
            Assert.IsFalse(SessionFlow.TryAdvanceQualifyingPhase(3, out _));
        }
    }
}
