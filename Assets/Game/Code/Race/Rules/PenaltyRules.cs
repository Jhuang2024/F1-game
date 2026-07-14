namespace F1Game.Race.Rules
{
    /// <summary>
    /// Penalty rulebook constants and pure decision helpers, extracted from
    /// <c>RaceManager.HandleTrackLimits</c> / <c>ApplyMandatoryPitPenalty</c> /
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

        // --- Mandatory pit stop (ApplyMandatoryPitPenalty) ---

        // Must always exceed the real cost of actually making the stop (pit-lane
        // transit at the limiter plus the service hold, ~20-30s on these layouts):
        // at the old 10s, skipping the mandatory stop and eating the penalty was
        // strictly faster than pitting - the rule punished compliance. 30s mirrors
        // the real-sport tariff for a skipped mandatory stop.
        public const float MandatoryPitPenaltySeconds = 30f;
        public const string MandatoryPitReason = "No mandatory stop";

        /// <summary>Races at or below this lap count never require a stop.</summary>
        public const int MandatoryPitMinimumRaceLaps = 4;

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
        /// Whether the no-mandatory-stop penalty applies at the flag. Mirrors
        /// ApplyMandatoryPitPenalty's gates exactly.
        /// </summary>
        public static bool ShouldApplyMandatoryPitPenalty(
            bool isQualifying,
            bool isTimeTrial,
            int raceLaps,
            int pitStops,
            bool alreadyApplied)
        {
            if (isQualifying || isTimeTrial)
            {
                return false;
            }

            if (raceLaps < MandatoryPitMinimumRaceLaps)
            {
                return false;
            }

            if (pitStops > 0 || alreadyApplied)
            {
                return false;
            }

            return true;
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
