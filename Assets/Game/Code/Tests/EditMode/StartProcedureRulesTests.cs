using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class StartProcedureRulesTests
    {
        [Test]
        public void LightSequenceLightsInOrderAndClampsAtFive()
        {
            Assert.AreEqual(0, StartProcedureRules.LitLightCount(0f));
            Assert.AreEqual(0, StartProcedureRules.LitLightCount(StartProcedureRules.FirstLightDelaySeconds - 0.01f));
            Assert.AreEqual(1, StartProcedureRules.LitLightCount(StartProcedureRules.FirstLightDelaySeconds));
            Assert.AreEqual(2, StartProcedureRules.LitLightCount(
                StartProcedureRules.FirstLightDelaySeconds + StartProcedureRules.LightStepSeconds));

            // All five are lit before the shortest possible hold expires, and
            // the count never exceeds the light count however long the hold is.
            float allLit = StartProcedureRules.FirstLightDelaySeconds +
                (StartProcedureRules.LightCount - 1) * StartProcedureRules.LightStepSeconds;
            Assert.AreEqual(StartProcedureRules.LightCount, StartProcedureRules.LitLightCount(allLit));
            Assert.Less(allLit, StartProcedureRules.MinRaceSequenceSeconds);
            Assert.AreEqual(StartProcedureRules.LightCount, StartProcedureRules.LitLightCount(999f));
        }

        [Test]
        public void RaceSequenceDurationCoversTheHoldWindowAndClampsRandomInput()
        {
            Assert.AreEqual(StartProcedureRules.MinRaceSequenceSeconds, StartProcedureRules.RaceSequenceDuration(0f), 0.0001f);
            Assert.AreEqual(StartProcedureRules.MaxRaceSequenceSeconds, StartProcedureRules.RaceSequenceDuration(1f), 0.0001f);
            Assert.AreEqual(StartProcedureRules.MinRaceSequenceSeconds, StartProcedureRules.RaceSequenceDuration(-3f), 0.0001f);
            Assert.AreEqual(StartProcedureRules.MaxRaceSequenceSeconds, StartProcedureRules.RaceSequenceDuration(7f), 0.0001f);
        }

        [Test]
        public void JudgeRanksInfractions()
        {
            // Movement before lights-out is always a jump start.
            Assert.AreEqual(StartInfraction.JumpStart, StartProcedureRules.Judge(true, 0.5f, true));

            // Grid-box violation outranks reaction analysis.
            Assert.AreEqual(StartInfraction.OutOfPosition, StartProcedureRules.Judge(false, 0.5f, false));

            // Sub-threshold reaction is anticipation; a plausible one is clean.
            Assert.AreEqual(StartInfraction.FalseStart, StartProcedureRules.Judge(false, 0.05f, true));
            Assert.AreEqual(StartInfraction.None, StartProcedureRules.Judge(false, StartProcedureRules.AnticipationThresholdSeconds, true));

            // Negative reaction means "not measured" - never an infraction.
            Assert.AreEqual(StartInfraction.None, StartProcedureRules.Judge(false, -1f, true));
        }

        [Test]
        public void PenaltyTariffOnlyPunishesLaunchInfractions()
        {
            Assert.Greater(StartProcedureRules.PenaltySeconds(StartInfraction.JumpStart), 0f);
            Assert.Greater(StartProcedureRules.PenaltySeconds(StartInfraction.FalseStart), 0f);
            Assert.AreEqual(0f, StartProcedureRules.PenaltySeconds(StartInfraction.None));
            Assert.AreEqual(0f, StartProcedureRules.PenaltySeconds(StartInfraction.OutOfPosition));
        }

        [Test]
        public void StartTypeResolution()
        {
            Assert.AreEqual(StartType.Standing, StartProcedureRules.ResolveStartType(false, false, false));
            Assert.AreEqual(StartType.SafetyCarStart, StartProcedureRules.ResolveStartType(true, false, false));
            Assert.AreEqual(StartType.SafetyCarStart, StartProcedureRules.ResolveStartType(false, true, false));

            // An elected pit-lane start wins over weather.
            Assert.AreEqual(StartType.PitLaneStart, StartProcedureRules.ResolveStartType(true, true, true));
        }
    }
}
