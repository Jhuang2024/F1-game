namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure dry-weather tyre-compound selection, extracted verbatim from
    /// <c>RaceManager.NextPitCompound</c> / <c>StartingTyreForParticipant</c> so the
    /// AI stint/ladder heuristic is stated and tested in one place. No engine
    /// dependency: compounds are the small int codes that match the live
    /// TyreCompound enum ordering (Soft 0, Medium 1, Hard 2 - see
    /// <see cref="Compound"/>), so the caller casts at the boundary. The wet/inter
    /// weather override, the null-guards and all live state reads stay in
    /// RaceManager; only the dry decision is delegated here.
    /// </summary>
    public static class TyreStrategyRules
    {
        /// <summary>Dry-compound codes matching the live TyreCompound enum values.</summary>
        public static class Compound
        {
            public const int Soft = 0;
            public const int Medium = 1;
            public const int Hard = 2;
        }

        /// <summary>
        /// The next dry pit compound, extracted verbatim from NextPitCompound's dry
        /// path (reached only after the wet/inter override and the missing-tyre
        /// fallback in the caller). A short remaining stint reaches for a faster
        /// compound instead of following the ladder - there is no tyre-life reason
        /// to save rubber that will never be used again, and an aggressive driver
        /// (aggression &gt;= 65) or a very short stint (&lt;= 4 laps) pushes all the
        /// way to Soft. Otherwise the usual Soft-&gt;Medium-&gt;Hard ladder (with
        /// Hard and anything unexpected settling back to Medium).
        /// </summary>
        public static int NextDryCompound(int lapsRemainingAfterStop, int aggression, int currentCompound)
        {
            if (lapsRemainingAfterStop > 0 && lapsRemainingAfterStop <= 8)
            {
                bool pushToSoft = aggression >= 65 || lapsRemainingAfterStop <= 4;
                return pushToSoft ? Compound.Soft : Compound.Medium;
            }

            if (currentCompound == Compound.Soft)
            {
                return Compound.Medium;
            }

            if (currentCompound == Compound.Medium)
            {
                return Compound.Hard;
            }

            return Compound.Medium;
        }

        /// <summary>
        /// The dry starting compound for an AI car from a 0-2 roll, extracted
        /// verbatim from StartingTyreForParticipant's dry branch: 0-&gt;Soft,
        /// 1-&gt;Medium, else Hard. The RNG that produces the roll stays in the
        /// caller so its call order is unchanged.
        /// </summary>
        public static int DryStartCompoundFromRoll(int roll)
        {
            return roll == 0 ? Compound.Soft : (roll == 1 ? Compound.Medium : Compound.Hard);
        }
    }
}
