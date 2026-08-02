namespace F1Game.Race.Rules
{
    /// <summary>
    /// Championship points rules, extracted verbatim from
    /// <c>CareerManager.Points</c> / <c>ApplyDriverPoints</c> / <c>SortStandings</c>
    /// so the table and tiebreaks exist exactly once and are unit-testable.
    ///
    /// Current rulebook: top 10 score 25-18-15-12-10-8-6-4-2-1, no fastest-lap
    /// bonus, standings tiebreak points → wins → podiums (all descending).
    /// </summary>
    public static class ChampionshipPoints
    {
        static readonly int[] Table = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };

        /// <summary>Sprint race points: top 8 only, 8-7-6-5-4-3-2-1.</summary>
        static readonly int[] SprintTable = { 8, 7, 6, 5, 4, 3, 2, 1 };

        // Suspended-race points scale (Sporting Regulations Art. 6.5, as revised
        // after Spa 2021 - it replaced the old flat "half points" rule). Points
        // awarded depend on how much of the scheduled distance the LEADER had
        // completed when the race was suspended and not resumed.
        static readonly int[] TwoLapsToQuarter = { 6, 4, 3, 2, 1 };
        static readonly int[] QuarterToHalf = { 13, 10, 8, 6, 5, 4, 3, 2, 1 };
        static readonly int[] HalfToThreeQuarters = { 19, 14, 12, 9, 8, 6, 5, 3, 2, 1 };

        public static int ScoringPositions => Table.Length;

        public static int SprintScoringPositions => SprintTable.Length;

        /// <summary>Points for a 1-based finishing position; 0 outside the top 10.</summary>
        public static int ForPosition(int finishingPosition)
        {
            if (finishingPosition < 1 || finishingPosition > Table.Length)
            {
                return 0;
            }

            return Table[finishingPosition - 1];
        }

        /// <summary>
        /// Sprint race points for a 1-based finishing position; 0 outside the top 8.
        /// Sprints have paid 8-7-6-5-4-3-2-1 since the 2022 format change, to both
        /// the drivers' and the constructors' championships.
        /// </summary>
        public static int ForSprintPosition(int finishingPosition)
        {
            if (finishingPosition < 1 || finishingPosition > SprintTable.Length)
            {
                return 0;
            }

            return SprintTable[finishingPosition - 1];
        }

        /// <summary>
        /// Points for a race that was suspended and never resumed, on the FIA's
        /// sliding scale. <paramref name="fractionOfDistanceCompleted"/> is the
        /// leader's completed distance as a fraction of the scheduled distance.
        ///
        /// Under 2 laps: no points at all. 2 laps to 25%: top 5 score 6-4-3-2-1.
        /// 25-50%: top 9 score 13-10-8-6-5-4-3-2-1. 50-75%: top 10 score
        /// 19-14-12-9-8-6-5-3-2-1. 75% or more: the full table.
        ///
        /// The game previously always awarded full points regardless of how far a
        /// red-flagged race had actually run.
        /// </summary>
        public static int ForPosition(int finishingPosition, float fractionOfDistanceCompleted, int lapsCompletedByLeader)
        {
            if (finishingPosition < 1)
            {
                return 0;
            }

            // Fewer than two laps completed: the race is void for points.
            if (lapsCompletedByLeader < 2)
            {
                return 0;
            }

            if (fractionOfDistanceCompleted >= 0.75f)
            {
                return ForPosition(finishingPosition);
            }

            int[] table = fractionOfDistanceCompleted >= 0.5f ? HalfToThreeQuarters
                : (fractionOfDistanceCompleted >= 0.25f ? QuarterToHalf : TwoLapsToQuarter);

            return finishingPosition > table.Length ? 0 : table[finishingPosition - 1];
        }

        public static bool CountsAsWin(int finishingPosition)
        {
            return finishingPosition == 1;
        }

        public static bool CountsAsPodium(int finishingPosition)
        {
            return finishingPosition >= 1 && finishingPosition <= 3;
        }

        /// <summary>
        /// Standings comparison: points desc, then wins desc, then podiums desc.
        /// Returns negative when A ranks ahead of B (for use in List.Sort).
        ///
        /// Kept for callers with no finish histogram. Prefer the countback overload
        /// below - "podiums" is NOT the real tiebreak.
        /// </summary>
        public static int CompareStandings(
            int pointsA, int winsA, int podiumsA,
            int pointsB, int winsB, int podiumsB)
        {
            int pointsCompare = pointsB.CompareTo(pointsA);
            if (pointsCompare != 0)
            {
                return pointsCompare;
            }

            int winsCompare = winsB.CompareTo(winsA);
            if (winsCompare != 0)
            {
                return winsCompare;
            }

            return podiumsB.CompareTo(podiumsA);
        }

        /// <summary>
        /// Standings comparison using the REAL F1 countback: points, then most wins,
        /// then most 2nd places, then most 3rd places, and so on down the order until
        /// the tie breaks. (If it never breaks, the FIA nominates - callers should
        /// apply their own deterministic final tiebreak.)
        ///
        /// The previous rule stopped at "podiums", which lumps 2nd and 3rd together -
        /// so two drivers level on points and wins with five 2nd places against five
        /// 3rd places compared as exactly equal, when the first is clearly ahead in
        /// the real regulations.
        ///
        /// finishCountsA/B are histograms indexed 0 = P1.
        /// </summary>
        public static int CompareStandingsWithCountback(
            int pointsA, int[] finishCountsA,
            int pointsB, int[] finishCountsB)
        {
            int pointsCompare = pointsB.CompareTo(pointsA);
            if (pointsCompare != 0)
            {
                return pointsCompare;
            }

            int length = 0;
            if (finishCountsA != null)
            {
                length = finishCountsA.Length;
            }

            if (finishCountsB != null && finishCountsB.Length > length)
            {
                length = finishCountsB.Length;
            }

            for (int position = 0; position < length; position++)
            {
                int a = finishCountsA != null && position < finishCountsA.Length ? finishCountsA[position] : 0;
                int b = finishCountsB != null && position < finishCountsB.Length ? finishCountsB[position] : 0;
                if (a != b)
                {
                    // More finishes at this position ranks ahead.
                    return b.CompareTo(a);
                }
            }

            return 0;
        }
    }
}
