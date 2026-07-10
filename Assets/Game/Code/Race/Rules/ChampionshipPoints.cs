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

        public static int ScoringPositions => Table.Length;

        /// <summary>Points for a 1-based finishing position; 0 outside the top 10.</summary>
        public static int ForPosition(int finishingPosition)
        {
            if (finishingPosition < 1 || finishingPosition > Table.Length)
            {
                return 0;
            }

            return Table[finishingPosition - 1];
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
    }
}
