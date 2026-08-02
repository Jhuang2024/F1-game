using System;
using System.Collections.Generic;

namespace F1Game.Race.Rules
{
    /// <summary>
    /// Final-classification rules, extracted from <c>RaceManager.FinishRace</c> and
    /// <c>RaceManager.RetireParticipant</c> so ordering behavior is testable:
    ///
    ///  - finished cars carry their real total time;
    ///  - unfinished (lapped) cars get an estimated finish time that is strictly
    ///    monotonic with real race distance (fractional laps remaining);
    ///  - retired cars carry a stamp far beyond any finisher, ordered among
    ///    themselves by how much of the race they still had left;
    ///  - the final order sorts by total time plus accumulated penalty seconds.
    /// </summary>
    public static class RaceClassifier
    {
        /// <summary>Nominal lap seconds used when estimating unfinished cars: max(72, trackLength / 56).</summary>
        public static float NominalLapSeconds(float trackLengthMeters)
        {
            return Math.Max(72f, trackLengthMeters / 56f);
        }

        /// <summary>
        /// Estimated classification time for a car still running at the flag.
        /// Mirrors FinishRace: raceElapsed + fractional laps remaining × nominal lap.
        /// </summary>
        public static float EstimateUnfinishedTotalTime(
            float raceElapsedSeconds,
            int raceLaps,
            float completedLapsPlusProgress,
            float trackLengthMeters)
        {
            float lapsRemaining = Math.Max(0f, raceLaps - completedLapsPlusProgress);
            return raceElapsedSeconds + lapsRemaining * NominalLapSeconds(trackLengthMeters);
        }

        /// <summary>
        /// Classification stamp for a retirement. Mirrors RetireParticipant:
        /// raceElapsed + 9999 + laps-not-completed × 120, which places every DNF
        /// behind every classified finisher, earliest retirements last.
        /// </summary>
        public static float RetirementTimeStamp(float raceElapsedSeconds, int raceLaps, int completedLaps)
        {
            return raceElapsedSeconds + 9999f + Math.Max(0f, raceLaps - completedLaps) * 120f;
        }

        /// <summary>
        /// The 90% rule: a car must complete at least 90% of the number of laps
        /// completed by the winner (rounded down to a whole lap) to be CLASSIFIED.
        /// An unclassified car scores no championship points.
        ///
        /// This was missing entirely - every entry, including a lap-1 retirement,
        /// was sorted into a dense 1..N order and paid points off its index, so in a
        /// high-attrition race a car that retired on the opening lap could score.
        /// </summary>
        public static bool IsClassified(int lapsCompleted, int winnerLapsCompleted)
        {
            if (winnerLapsCompleted <= 0)
            {
                return false;
            }

            return lapsCompleted >= (int)Math.Floor(winnerLapsCompleted * 0.9);
        }

        /// <summary>
        /// Sorts <paramref name="results"/> by (total time + penalty seconds) ascending
        /// and stamps 1-based finishing positions — the exact FinishRace ordering.
        /// </summary>
        public static void AssignFinishingOrder<T>(
            List<T> results,
            Func<T, float> totalTimeSeconds,
            Func<T, float> penaltySeconds,
            Action<T, int> setFinishingPosition)
        {
            results.Sort((a, b) =>
            {
                float aTime = totalTimeSeconds(a) + penaltySeconds(a);
                float bTime = totalTimeSeconds(b) + penaltySeconds(b);
                return aTime.CompareTo(bTime);
            });

            for (int i = 0; i < results.Count; i++)
            {
                setFinishingPosition(results[i], i + 1);
            }
        }

        /// <summary>
        /// As <see cref="AssignFinishingOrder{T}"/>, but disqualified cars are moved
        /// behind every other entry first. A DSQ is excluded from the classification
        /// in real F1; ordering them last is the closest thing that keeps a single
        /// dense results list for the UI.
        /// </summary>
        public static void AssignFinishingOrder<T>(
            List<T> results,
            Func<T, float> totalTimeSeconds,
            Func<T, float> penaltySeconds,
            Func<T, bool> isDisqualified,
            Action<T, int> setFinishingPosition)
        {
            results.Sort((a, b) =>
            {
                bool aDsq = isDisqualified(a);
                bool bDsq = isDisqualified(b);
                if (aDsq != bDsq)
                {
                    return aDsq ? 1 : -1;
                }

                float aTime = totalTimeSeconds(a) + penaltySeconds(a);
                float bTime = totalTimeSeconds(b) + penaltySeconds(b);
                return aTime.CompareTo(bTime);
            });

            for (int i = 0; i < results.Count; i++)
            {
                setFinishingPosition(results[i], i + 1);
            }
        }
    }
}
