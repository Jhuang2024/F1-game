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
        /// Expected dry stint length in laps for each slick compound. MUST stay in
        /// sync with the baseWear calibration in TyreState.Reset (Soft baseWear
        /// 2.2 ~ 2 laps, Medium 1.47 ~ 3 laps, Hard 1.1 ~ 4 laps) - the strategy
        /// below reasons about "will this tyre reach the flag" directly off these
        /// numbers, so if the wear model is retuned these move with it.
        /// </summary>
        public static int DryStintLaps(int compound)
        {
            switch (compound)
            {
                case Compound.Soft:
                    return 2;
                case Compound.Medium:
                    return 3;
                case Compound.Hard:
                    return 4;
                default:
                    return 3;
            }
        }

        /// <summary>
        /// The next dry pit compound (reached only after the wet/inter override and
        /// the missing-tyre fallback in the caller). Picks the SOFTEST - fastest -
        /// compound whose stint life still reaches the flag, so this becomes the
        /// final stop instead of committing the car to yet another one. The old
        /// heuristic reached for a soft whenever <= 4 laps remained, but a soft now
        /// only lasts ~2 laps, so it produced the reported "soft-&gt;soft 2-stopper
        /// in a 5-lap race": fitting a soft with 3-4 laps to go guaranteed a second
        /// stop. When even the hard can't reach the flag in one stint (a long race
        /// still far from the end), the hard is still the right call - the most
        /// durable tyre minimises how many further stops remain. aggression is no
        /// longer used to gamble onto a too-soft tyre (that reintroduced the extra
        /// stop); stint length alone drives the pick.
        /// </summary>
        public static int NextDryCompound(int lapsRemainingAfterStop, int aggression, int currentCompound)
        {
            if (lapsRemainingAfterStop <= DryStintLaps(Compound.Soft))
            {
                return Compound.Soft;
            }

            if (lapsRemainingAfterStop <= DryStintLaps(Compound.Medium))
            {
                return Compound.Medium;
            }

            return Compound.Hard;
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
