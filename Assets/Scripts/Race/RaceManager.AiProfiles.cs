namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager AI difficulty profiles (partial). The per-tier
    /// decision-quality profile (braking margins, reaction time, commitment,
    /// ERS/DRS quality, pace scaling ...) consumed by AiVehicleController. Split
    /// out of the monolith verbatim; the struct stays nested as
    /// RaceManager.AiDifficultyProfile so every existing reference is unchanged.
    /// </summary>
    public partial class RaceManager
    {
        // Difficulty as decision-making quality, never a raw speed/grip multiplier.
        // brakeDistanceMultiplier, minimumCornerSpeedConfidence, apexErrorMeters,
        // throttleDelay, exitThrottleConfidence, reactionTimeSeconds,
        // mistakeChancePerLap, trafficAvoidanceCaution, overtakeCommitment,
        // defendCommitment, ersDeploymentQuality and drsUsageQuality are all consumed
        // by AiVehicleController; wetWeatherCaution and lineOffsetNoise round out the
        // per-corner and per-frame driving model. Ordering must hold on every axis:
        // Expert closest to the true limit, Easy the most forgiving.
        public struct AiDifficultyProfile
        {
            public float brakeDistanceMultiplier;
            public float minimumCornerSpeedConfidence;
            public float apexErrorMeters;
            public float throttleDelay;
            public float exitThrottleConfidence;
            public float lineOffsetNoise;
            public float reactionTimeSeconds;
            public float overtakeCommitment;
            public float defendCommitment;
            public float ersDeploymentQuality;
            public float drsUsageQuality;
            public float mistakeChancePerLap;
            public float trafficAvoidanceCaution;
            public float wetWeatherCaution;
            public float tyreSavingBias;

            // Explicit pace scaling on top of the decision-quality model above - same
            // car, same physical envelope, but a more skilled/confident difficulty
            // tier actually drives closer to that envelope instead of only deciding
            // slightly better. straightSpeedMultiplier is always clamped to <= 1.0
            // wherever it touches a real top-speed ceiling; the others may legitimately
            // exceed 1.0 for Hard/Expert since a corner apex or braking point is a
            // driving-skill judgment call, not a hard physics limit.
            public float paceMultiplier;
            // Cornering buff round 8: no longer consumed by AiVehicleController's
            // apex-speed calculation - it used to stack multiplicatively on top of
            // EstimateApexSpeedForCornerType's own skillTier-scaled floor/ceiling,
            // which could push the AI's target speed past its own straight-line top
            // speed through a corner (physically unachievable) and send it straight
            // into the wall. Left declared/assigned per-tier rather than removed
            // outright, since ordering across the four difficulty profiles still
            // documents relative intent even though nothing reads it anymore.
            public float cornerSpeedMultiplier;
            public float straightSpeedMultiplier;
            public float brakeConfidenceMultiplier;
            public float throttleAggressionMultiplier;
        }

        public AiDifficultyProfile GetAiDifficultyProfile()
        {
            RaceDifficulty difficulty = Settings.Difficulty;
            if (difficulty == RaceDifficulty.Easy)
            {
                return new AiDifficultyProfile
                {
                    brakeDistanceMultiplier = 0.80f,
                    minimumCornerSpeedConfidence = 0.72f,
                    apexErrorMeters = 2.6f,
                    throttleDelay = 0.55f,
                    exitThrottleConfidence = 0.62f,
                    lineOffsetNoise = 1.4f,
                    // Reaction-time buff: explicit per-tier target ranges requested
                    // (Easy 0.4-0.6 / Medium 0.3-0.4 / Hard 0.25-0.3 / Expert 0.2-0.25) -
                    // round 1's uniform nerf (was 0.85/0.55/0.32/0.11) overshot,
                    // especially Easy at a full second.
                    reactionTimeSeconds = 0.5f,
                    overtakeCommitment = 0.35f,
                    defendCommitment = 0.30f,
                    ersDeploymentQuality = 0.40f,
                    drsUsageQuality = 0.55f,
                    mistakeChancePerLap = 0.16f,
                    // Aggression pass (per request): every tier's caution cut so AI
                    // race for position instead of yielding it (was 1.35).
                    trafficAvoidanceCaution = 1.1f,
                    wetWeatherCaution = 1.5f,
                    tyreSavingBias = 0.35f,
                    paceMultiplier = 0.96f,
                    cornerSpeedMultiplier = 0.94f,
                    straightSpeedMultiplier = 0.95f,
                    brakeConfidenceMultiplier = 0.85f,
                    throttleAggressionMultiplier = 0.75f
                };
            }

            if (difficulty == RaceDifficulty.Medium)
            {
                return new AiDifficultyProfile
                {
                    brakeDistanceMultiplier = 0.94f,
                    minimumCornerSpeedConfidence = 0.85f,
                    apexErrorMeters = 1.4f,
                    // Corner-speed pass 3: Medium should stay clearly more
                    // cautious than Hard/Expert but shouldn't read as broken -
                    // a slightly quicker exit pickup (was 0.30/0.78).
                    throttleDelay = 0.24f,
                    // Cornering buff round 5: Medium gets a modest, deliberately small
                    // lift (was 0.82/1.00/1.00) - "decent, not broken", nowhere near
                    // the Hard/Expert jump below.
                    exitThrottleConfidence = 0.86f,
                    lineOffsetNoise = 0.75f,
                    reactionTimeSeconds = 0.35f,
                    overtakeCommitment = 0.55f,
                    defendCommitment = 0.55f,
                    ersDeploymentQuality = 0.65f,
                    drsUsageQuality = 0.75f,
                    mistakeChancePerLap = 0.09f,
                    // Aggression pass (per request): was 1.05.
                    trafficAvoidanceCaution = 0.85f,
                    wetWeatherCaution = 1.2f,
                    tyreSavingBias = 0.20f,
                    paceMultiplier = 1.01f,
                    // Cornering buff round 7: pushed up (was 1.05) alongside the wider
                    // HighSpeed/Medium bands in AiVehicleController - Medium now covers
                    // genuinely fast corners too, not just cautious ones.
                    cornerSpeedMultiplier = 1.16f,
                    straightSpeedMultiplier = 0.98f,
                    brakeConfidenceMultiplier = 1.05f,
                    throttleAggressionMultiplier = 1.05f
                };
            }

            if (difficulty == RaceDifficulty.Hard)
            {
                // Cornering buff round 5: pushed meaningfully further on top of the
                // corner-speed pass 4 numbers below - Hard should be clearly,
                // competitively fast through corners, not just "a notch above
                // Medium". trafficAvoidanceCaution raised alongside the speed/
                // confidence numbers (was 0.74) so the extra pace is paired with
                // more avoidance margin, not less - carrying more corner speed
                // shouldn't also mean crashing into traffic more often.
                return new AiDifficultyProfile
                {
                    brakeDistanceMultiplier = 1.00f,
                    // Corner-speed pass: Hard used to get none of the skillTier-
                    // gated corner-type bonuses in AiVehicleController (those were
                    // Expert-only) despite already having a fairly high confidence
                    // number here - the confidence stat alone wasn't enough to read
                    // as "meaningfully faster through corners". Bumped alongside
                    // the new skillTier blend so Hard is now clearly quicker than
                    // Medium through medium/fast corners, not just a hair sharper.
                    minimumCornerSpeedConfidence = 0.985f,
                    apexErrorMeters = 0.55f,
                    // Cornering buff round 5: exit hesitation shortened again (was
                    // 0.10) and exit confidence raised again (was 0.95) - "pick up
                    // throttle earlier on exit" was as much about this as the
                    // apex-speed floors themselves.
                    throttleDelay = 0.07f,
                    exitThrottleConfidence = 0.97f,
                    lineOffsetNoise = 0.36f,
                    reactionTimeSeconds = 0.275f,
                    overtakeCommitment = 0.77f,
                    defendCommitment = 0.79f,
                    ersDeploymentQuality = 0.87f,
                    drsUsageQuality = 0.94f,
                    mistakeChancePerLap = 0.045f,
                    // Aggression pass (per request): was 0.82.
                    trafficAvoidanceCaution = 0.62f,
                    wetWeatherCaution = 0.98f,
                    tyreSavingBias = 0.12f,
                    paceMultiplier = 1.08f,
                    // Cornering buff round 7: pushed again (was 1.22/1.24/1.28/1.30/
                    // 1.42) - "fast corners need to be A LOT faster". No longer
                    // touches genuine hairpins at all (see the Hairpin-type exemption
                    // in AiVehicleController), so this only ever buffs HighSpeed/
                    // Medium/Slow corners - safe to push hard without also making
                    // hairpins faster again.
                    cornerSpeedMultiplier = 1.60f,
                    straightSpeedMultiplier = 1.00f,
                    brakeConfidenceMultiplier = 1.34f,
                    throttleAggressionMultiplier = 1.38f
                };
            }

            // Expert - the ceiling tier. straightSpeedMultiplier is still the one
            // hard rule that can never move past 1.0, since it scales against
            // vehicle.TargetTopSpeedKph, the same DRS/ERS-aware physics ceiling the
            // player's own car uses - every buff below is a cornering-confidence/
            // braking-point/throttle-pickup number, never a straight-line speed one.
            // trafficAvoidanceCaution raised alongside the rest (was 0.31), same
            // reasoning as Hard above: more committed cornering pairs with more
            // avoidance margin, not less.
            return new AiDifficultyProfile
            {
                brakeDistanceMultiplier = 1.06f,
                // Cornering buff round 5: pushed to the practical ceiling (was
                // 0.995) - Expert should show essentially zero entry hesitation.
                minimumCornerSpeedConfidence = 0.999f,
                apexErrorMeters = 0.14f,
                throttleDelay = 0.03f,
                exitThrottleConfidence = 0.995f,
                lineOffsetNoise = 0.12f,
                reactionTimeSeconds = 0.225f,
                overtakeCommitment = 0.93f,
                defendCommitment = 0.93f,
                ersDeploymentQuality = 0.96f,
                drsUsageQuality = 0.98f,
                mistakeChancePerLap = 0.0045f,
                // Aggression pass (per request): was 0.42.
                trafficAvoidanceCaution = 0.32f,
                wetWeatherCaution = 0.88f,
                tyreSavingBias = 0.07f,
                paceMultiplier = 1.15f,
                // Cornering buff round 7: pushed further still (was 1.34/1.58/1.70/
                // 1.44/1.62) - "fast corners need to be A LOT faster". Still never
                // touches genuine hairpins (see the Hairpin-type exemption in
                // AiVehicleController) - Expert should be the fastest, most committed
                // tier through fast corners specifically, not through hairpins too.
                cornerSpeedMultiplier = 1.85f,
                straightSpeedMultiplier = 1.00f,
                brakeConfidenceMultiplier = 1.70f,
                throttleAggressionMultiplier = 1.85f
            };
        }
    }
}
