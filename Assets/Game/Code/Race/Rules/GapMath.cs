using System;

namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure lapped-gap maths, extracted verbatim from the duplicated blocks in
    /// RaceManager.GapToLeaderText / IntervalAheadText so the "is this car a lap or
    /// more down, and how many laps" decision is stated and tested in one place. No
    /// engine dependency. The caller still owns the null-track guard, the live
    /// distance/speed reads and the string formatting.
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
        /// How many whole laps down a distance gap represents, extracted verbatim:
        /// the gap divided by a track length (floored at 1 m to avoid a divide-by-
        /// zero), rounded to the nearest lap and floored at 1. Rounding matches
        /// UnityEngine.Mathf.RoundToInt (round half to even).
        /// </summary>
        public static int LapsDown(float deltaMeters, float trackLength)
        {
            float denom = trackLength < 1f ? 1f : trackLength;
            int laps = RoundToInt(deltaMeters / denom);
            return laps < 1 ? 1 : laps;
        }

        // UnityEngine.Mathf.RoundToInt is (int)Math.Round((double)f), i.e. banker's
        // rounding (MidpointRounding.ToEven) - Math.Round's default - so a lap gap
        // landing exactly on x.5 rounds to the nearest even lap, identically.
        static int RoundToInt(float value) => (int)Math.Round((double)value);
    }
}
