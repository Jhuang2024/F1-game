using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// ActiveAeroRules is the live authority for the 2026 movable-wing and Override
    /// Mode decisions (RaceManager.IsDrsAvailable and IsOverrideAvailable delegate to
    /// it). The two must stay genuinely different rules: the wings are ordinary
    /// aerodynamics the whole field has in an activation zone, Override is the
    /// gap-gated, energy-limited overtaking aid and is not zone-restricted.
    /// </summary>
    public class ActiveAeroRulesTests
    {
        static bool Wing(bool wet = false, bool cooldown = false, bool flagAllows = true, int zone = 1)
        {
            return ActiveAeroRules.WingModeAvailable(wet, cooldown, flagAllows, zone);
        }

        static bool Override(bool wet = false, bool cooldown = false, bool flagAllows = true,
            bool qualiOrTt = false, int laps = 5, float gap = 0.5f, float energy = 1f)
        {
            return ActiveAeroRules.OverrideAvailable(wet, cooldown, flagAllows, qualiOrTt, laps, gap, energy);
        }

        [Test]
        public void HardGatesShutTheWingsOff()
        {
            Assert.IsFalse(Wing(wet: true));
            Assert.IsFalse(Wing(cooldown: true));
            Assert.IsFalse(Wing(flagAllows: false));
        }

        [Test]
        public void OutsideAnActivationZoneTheWingsStayShut()
        {
            Assert.IsFalse(Wing(zone: 0));
        }

        [Test]
        public void TheWholeFieldGetsTheWingsWithNoGapRequirement()
        {
            // The defining 2026 change: a leader in clear air, on lap one, in every
            // zone including a third one, gets low-drag mode. Under DRS none of these
            // would have opened the wing.
            Assert.IsTrue(Wing(zone: 1));
            Assert.IsTrue(Wing(zone: 2));
            Assert.IsTrue(Wing(zone: 3));
        }

        [Test]
        public void OverrideNeedsTheGapTheLapsAndTheEnergy()
        {
            Assert.IsTrue(Override());
            Assert.IsFalse(Override(gap: 1.5f));    // too far back
            Assert.IsFalse(Override(laps: 0));      // opening-lap gate
            Assert.IsFalse(Override(energy: 0f));   // budget spent
            Assert.IsTrue(Override(gap: 1.0f));     // exactly one second
        }

        [Test]
        public void OverrideIsNotAvailableInQualifyingOrUnderFlags()
        {
            Assert.IsFalse(Override(qualiOrTt: true));
            Assert.IsFalse(Override(flagAllows: false));
            Assert.IsFalse(Override(cooldown: true));
        }

        [Test]
        public void OverrideIsNotZoneRestricted()
        {
            // There is no zone argument at all - that is the point. A driver may use
            // Override wherever they are within a second, not only on a designated
            // straight.
            Assert.IsTrue(Override(gap: 0.9f));
        }
    }
}
