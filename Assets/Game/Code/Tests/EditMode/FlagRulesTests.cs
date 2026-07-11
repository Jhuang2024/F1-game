using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class FlagRulesTests
    {
        [Test]
        public void OvertakingBannedUnderEveryCaution()
        {
            Assert.IsTrue(FlagRules.OvertakingAllowed(RaceFlag.Green));
            Assert.IsTrue(FlagRules.OvertakingAllowed(RaceFlag.Blue));
            Assert.IsTrue(FlagRules.OvertakingAllowed(RaceFlag.Chequered));

            Assert.IsFalse(FlagRules.OvertakingAllowed(RaceFlag.LocalYellow));
            Assert.IsFalse(FlagRules.OvertakingAllowed(RaceFlag.DoubleYellow));
            Assert.IsFalse(FlagRules.OvertakingAllowed(RaceFlag.FullCourseYellow));
            Assert.IsFalse(FlagRules.OvertakingAllowed(RaceFlag.VirtualSafetyCar));
            Assert.IsFalse(FlagRules.OvertakingAllowed(RaceFlag.SafetyCar));
            Assert.IsFalse(FlagRules.OvertakingAllowed(RaceFlag.Red));
        }

        [Test]
        public void DrsOnlyUnderGreenConditions()
        {
            Assert.IsTrue(FlagRules.DrsAllowed(RaceFlag.Green));
            Assert.IsTrue(FlagRules.DrsAllowed(RaceFlag.Blue));
            Assert.IsTrue(FlagRules.DrsAllowed(RaceFlag.White));

            Assert.IsFalse(FlagRules.DrsAllowed(RaceFlag.LocalYellow));
            Assert.IsFalse(FlagRules.DrsAllowed(RaceFlag.VirtualSafetyCar));
            Assert.IsFalse(FlagRules.DrsAllowed(RaceFlag.SafetyCar));
            Assert.IsFalse(FlagRules.DrsAllowed(RaceFlag.Red));
            Assert.IsFalse(FlagRules.DrsAllowed(RaceFlag.Chequered));
        }

        [Test]
        public void PaceControlRequiredOnlyForDeltaFlags()
        {
            Assert.IsTrue(FlagRules.RequiresPaceControl(RaceFlag.VirtualSafetyCar));
            Assert.IsTrue(FlagRules.RequiresPaceControl(RaceFlag.SafetyCar));
            Assert.IsTrue(FlagRules.RequiresPaceControl(RaceFlag.FullCourseYellow));

            Assert.IsFalse(FlagRules.RequiresPaceControl(RaceFlag.Green));
            Assert.IsFalse(FlagRules.RequiresPaceControl(RaceFlag.LocalYellow));
            Assert.IsFalse(FlagRules.RequiresPaceControl(RaceFlag.Red));
        }

        [Test]
        public void ParticipationAndYieldFlags()
        {
            Assert.IsTrue(FlagRules.MustYield(RaceFlag.Blue));
            Assert.IsFalse(FlagRules.MustYield(RaceFlag.Green));

            Assert.IsTrue(FlagRules.EndsParticipation(RaceFlag.Black));
            Assert.IsFalse(FlagRules.EndsParticipation(RaceFlag.BlackWhiteWarning));

            Assert.IsTrue(FlagRules.RequiresPitForRepair(RaceFlag.MechanicalBlackOrange));
            Assert.IsFalse(FlagRules.RequiresPitForRepair(RaceFlag.Black));
        }

        [Test]
        public void CautionSpeedCapsAreOrderedSensibly()
        {
            // The VSC cap is tighter than a local-yellow cap; both are real caps.
            Assert.Greater(FlagRules.LocalYellowSpeedCapKph, FlagRules.VirtualSafetyCarSpeedCapKph);
            Assert.Greater(FlagRules.VirtualSafetyCarSpeedCapKph, 0f);
        }
    }
}
