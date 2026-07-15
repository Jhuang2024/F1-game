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
        public void AiJumpStartChanceAndWindow()
        {
            // Chance shrinks with consistency but never reaches zero.
            Assert.AreEqual(StartProcedureRules.BaseAiJumpStartChance, StartProcedureRules.AiJumpStartChance(0f), 0.0001f);
            Assert.Greater(StartProcedureRules.AiJumpStartChance(0f), StartProcedureRules.AiJumpStartChance(1f));
            Assert.Greater(StartProcedureRules.AiJumpStartChance(1f), 0f);

            // Launch window spans the tuned band and clamps its roll.
            Assert.AreEqual(StartProcedureRules.MinJumpLaunchSeconds, StartProcedureRules.JumpLaunchWindowSeconds(0f), 0.0001f);
            Assert.AreEqual(StartProcedureRules.MaxJumpLaunchSeconds, StartProcedureRules.JumpLaunchWindowSeconds(1f), 0.0001f);
            Assert.AreEqual(StartProcedureRules.MaxJumpLaunchSeconds, StartProcedureRules.JumpLaunchWindowSeconds(5f), 0.0001f);
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

        [Test]
        public void AiReactionSkillBlendAveragesAwarenessAndConsistency()
        {
            Assert.AreEqual(1f, StartProcedureRules.AiReactionSkillBlend(100f, 100f), 0.0001f);
            Assert.AreEqual(0f, StartProcedureRules.AiReactionSkillBlend(0f, 0f), 0.0001f);
            Assert.AreEqual(0.8f, StartProcedureRules.AiReactionSkillBlend(80f, 80f), 0.0001f);
            // Out-of-range stats clamp to 0-1.
            Assert.AreEqual(1f, StartProcedureRules.AiReactionSkillBlend(150f, 150f), 0.0001f);
        }

        [Test]
        public void AiReactionDelayAndVarianceShrinkWithSkill()
        {
            // Base delay: tier reaction time scaled 1.4x (low skill) to 0.9x (high) -
            // the human-plausible band (Expert top driver ~0.2s, Easy poor ~0.7s).
            Assert.AreEqual(1.4f, StartProcedureRules.AiReactionBaseDelaySeconds(1f, 0f), 0.0001f);
            Assert.AreEqual(0.9f, StartProcedureRules.AiReactionBaseDelaySeconds(1f, 1f), 0.0001f);
            Assert.AreEqual(1.15f, StartProcedureRules.AiReactionBaseDelaySeconds(1f, 0.5f), 0.0001f);
            Assert.Greater(StartProcedureRules.AiReactionBaseDelaySeconds(1f, 0.2f),
                           StartProcedureRules.AiReactionBaseDelaySeconds(1f, 0.9f));

            // Variance: 0.14 s wide at low skill, narrowing to 0.03 s at high skill.
            Assert.AreEqual(0.14f, StartProcedureRules.AiReactionVarianceSeconds(0f), 0.0001f);
            Assert.AreEqual(0.03f, StartProcedureRules.AiReactionVarianceSeconds(1f), 0.0001f);
            Assert.AreEqual(0.085f, StartProcedureRules.AiReactionVarianceSeconds(0.5f), 0.0001f);
        }
    }
}
