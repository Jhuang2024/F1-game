using System;

namespace F1Game.Race.Rules
{
    /// <summary>
    /// Q1/Q2/Q3 progression rules, extracted from
    /// <c>RaceManager.QualifyingEliminationCount</c> / <c>ApplyQualifyingElimination</c>.
    ///
    /// A 22-car field runs Q1 (eliminate to 16), Q2 (eliminate to 10), Q3.
    /// Elimination per phase is capped at 6 so undersized fields degrade the way
    /// the original code did rather than emptying the session.
    /// </summary>
    public static class QualifyingProgression
    {
        public const int Q1SurvivorCount = 16;
        public const int Q2SurvivorCount = 10;
        public const int MaxEliminationsPerPhase = 6;
        public const int FinalPhase = 3;

        public static bool IsFinalPhase(int phase)
        {
            return phase >= FinalPhase;
        }

        public static int NextPhase(int phase)
        {
            return IsFinalPhase(phase) ? phase : phase + 1;
        }

        /// <summary>Cars eliminated at the end of the given 1-based phase for the given active car count.</summary>
        public static int EliminationCount(int phase, int activeCount)
        {
            if (phase == 1)
            {
                return Clamp(activeCount - Q1SurvivorCount, 0, MaxEliminationsPerPhase);
            }

            if (phase == 2)
            {
                return Clamp(activeCount - Q2SurvivorCount, 0, MaxEliminationsPerPhase);
            }

            return 0;
        }

        /// <summary>
        /// First index (into the phase's ascending-time order) that is eliminated;
        /// entries [index, activeCount) drop out, mirroring ApplyQualifyingElimination.
        /// </summary>
        public static int FirstEliminatedIndex(int phase, int activeCount)
        {
            return activeCount - EliminationCount(phase, activeCount);
        }

        public static string PhaseLabel(int phase)
        {
            return "Q" + phase;
        }

        static int Clamp(int value, int min, int max)
        {
            return Math.Min(max, Math.Max(min, value));
        }
    }
}
