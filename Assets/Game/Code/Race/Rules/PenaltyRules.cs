namespace F1Game.Race.Rules
{
    /// <summary>
    /// Penalty rulebook constants and pure decision helpers, extracted from
    /// <c>RaceManager.HandleTrackLimits</c> / the two-compound rule /
    /// <c>AddPenalty</c>. The monolith keeps the physics-side detection; the
    /// thresholds and decisions live here so they are documented and testable.
    /// </summary>
    public static class PenaltyRules
    {
        // --- Track limits (HandleTrackLimits) ---

        /// <summary>Metres beyond the local half width that count as fully outside the white line.</summary>
        public const float TrackLimitsLateralMargin = 0.5f;

        /// <summary>Metres beyond half width past which a car is judged to be gaining time.</summary>
        public const float GainedTimeLateralMargin = 1.0f;

        /// <summary>Minimum speed (km/h) for an off-track excursion to count as gaining time.</summary>
        public const float GainedTimeMinSpeedKph = 70f;

        /// <summary>Seconds a car must stay outside the line before a warning is registered.</summary>
        public const float OffTrackWarningSeconds = 0.75f;

        /// <summary>Warnings accumulated before a time penalty is issued.</summary>
        public const int WarningsBeforePenalty = 3;

        public const float TrackLimitsPenaltySeconds = 5f;
        public const string TrackLimitsReason = "Track limits";

        // --- Blue flags (RaceManager.UpdateBlueFlags) ---

        /// <summary>Seconds a car may hold up a lapping car before being penalised.</summary>
        public const float BlueFlagComplianceSeconds = 20f;
        public const float IgnoredBlueFlagPenaltySeconds = 5f;
        public const string IgnoredBlueFlagReason = "Ignoring blue flags";

        public static bool ShouldPenaliseIgnoredBlueFlag(float heldSeconds, bool alreadyPenalisedThisEpisode)
        {
            return !alreadyPenalisedThisEpisode && heldSeconds >= BlueFlagComplianceSeconds;
        }

        // --- Two-compound rule (Sporting Regulations, dry race) ---

        // THE REAL RULE. In a race declared DRY, each driver must use at least two
        // different SPECIFICATIONS of dry-weather tyre. It is not a "must pit" rule:
        // a soft->soft stop does not comply even though the car pitted, and a driver
        // who runs intermediates or wets at any point is exempt entirely. The
        // sanction for breach is DISQUALIFICATION, not a time penalty.
        //
        // What used to be here was a "mandatory pit stop" carrying a 30-second time
        // penalty, gated purely on pitStops > 0 - a rule that does not exist in F1.
        // It got three things wrong at once: it never looked at which compounds were
        // fitted (so a two-stopper on three sets of the same compound was "legal"),
        // it applied in wet races where the real requirement is void, and by making
        // non-compliance a survivable 30s it turned a hard constraint into a
        // cost-benefit choice. The 30s figure itself was chosen to exceed the cost of
        // a stop - a game-balance argument, not a regulation.
        public const string TwoCompoundReason = "Two-compound rule not met";

        /// <summary>
        /// Below this race length the two-compound requirement is not enforced.
        /// Real F1 applies it to any dry race; this floor only exists so the game's
        /// very short arcade race presets (3-5 laps) remain playable.
        /// </summary>
        public const int TwoCompoundMinimumRaceLaps = 4;

        /// <summary>
        /// Whether a car must be disqualified for failing the two-compound rule.
        ///
        /// <paramref name="distinctDryCompounds"/> counts the distinct DRY
        /// specifications the car actually ran (starting set plus every set fitted).
        /// <paramref name="usedWetOrIntermediate"/> voids the requirement, exactly as
        /// running a wet-weather tyre does in the real regulation.
        /// </summary>
        public static bool ShouldDisqualifyForTwoCompoundRule(
            bool isQualifying,
            bool isTimeTrial,
            bool raceDeclaredWet,
            bool usedWetOrIntermediate,
            int raceLaps,
            int distinctDryCompounds,
            bool retired)
        {
            if (isQualifying || isTimeTrial || retired)
            {
                return false;
            }

            if (raceLaps < TwoCompoundMinimumRaceLaps)
            {
                return false;
            }

            // A wet race, or any wet-weather tyre used, voids the requirement.
            if (raceDeclaredWet || usedWetOrIntermediate)
            {
                return false;
            }

            return distinctDryCompounds < 2;
        }

        public static bool IsOutsideTrackLimits(float lateralOffset, float localHalfWidth)
        {
            return lateralOffset > localHalfWidth + TrackLimitsLateralMargin;
        }

        public static bool IsGainingTime(float lateralOffset, float localHalfWidth, float speedKph)
        {
            return lateralOffset > localHalfWidth + GainedTimeLateralMargin && speedKph > GainedTimeMinSpeedKph;
        }

        public static bool ShouldPenaliseTrackLimits(int accumulatedWarnings)
        {
            return accumulatedWarnings >= WarningsBeforePenalty;
        }

        /// <summary>
        /// Reason-string accumulation with the monolith's dedup behavior: first
        /// reason verbatim, later distinct reasons comma-appended, repeats dropped.
        /// </summary>
        public static string AppendPenaltyReason(string existingReason, string newReason)
        {
            if (string.IsNullOrEmpty(existingReason))
            {
                return newReason;
            }

            if (existingReason.Contains(newReason))
            {
                return existingReason;
            }

            return existingReason + ", " + newReason;
        }
    }
}
