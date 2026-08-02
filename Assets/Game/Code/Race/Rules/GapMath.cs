using System;

namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure lapped-gap maths.
    ///
    /// SUPERSEDED for live timing. Lapped status is a lap-COUNT question, and
    /// answering it from a distance gap is wrong in both directions: a car on the
    /// lead lap but 0.93 laps adrift trips IsLapDownGap, while a car genuinely a lap
    /// down but running physically ahead of the leader does not. The race layer now
    /// uses RaceStateManager.GetCompletedLaps (see RaceManager.LapsDownBetween).
    /// Retained for the estimate-from-distance cases that have no lap counter.
    /// </summary>
    public static class GapMath
    {
        /// <summary>
        /// True when a distance gap is big enough to read as at least a lap down -
        /// the inline threshold was 92% of a track length (a car within the last
        /// ~8% of a lap of the reference is still shown a seconds gap, not "+1 L").
        /// </summary>
        public static bool IsLapDownGap(float deltaMeters, float trackLength)
        {
            return (double)deltaMeters >= (double)trackLength * 0.92d;
        }

        /// <summary>
        /// How many whole laps down a distance gap represents: the gap divided by
        /// the track length, rounded to the nearest lap and floored at 1. Rounding
        /// matches UnityEngine.Mathf.RoundToInt (round half to even). Returns 0 for a
        /// degenerate (sub-metre) track length, meaning "no meaningful answer".
        /// </summary>
        public static int LapsDown(float deltaMeters, float trackLength)
        {
            // A degenerate track length has no meaningful answer. This used to fall
            // back on a 1m denominator, which avoided the divide-by-zero but then
            // reported a 3m gap on a zero-length track as "3 laps down" - a
            // plausible-looking number the HUD would happily render as "+3 L". Return
            // 0 ("unknown / not lapped") so a caller can tell the difference.
            if (trackLength < 1f)
            {
                return 0;
            }

            int laps = RoundToInt(deltaMeters / trackLength);
            return laps < 1 ? 1 : laps;
        }

        // UnityEngine.Mathf.RoundToInt is (int)Math.Round((double)f), i.e. banker's
        // rounding (MidpointRounding.ToEven) - Math.Round's default - so a lap gap
        // landing exactly on x.5 rounds to the nearest even lap, identically.
        static int RoundToInt(float value) => (int)Math.Round((double)value);
    }
}
