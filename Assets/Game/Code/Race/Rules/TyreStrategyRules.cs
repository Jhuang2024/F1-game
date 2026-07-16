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

        // Track-temperature control points for the stint-length gradient. Cooler
        // rubber lasts longer; hotter degrades faster. The three anchors are the
        // requested targets: at 15C soft/medium/hard last 3/4/5 laps, at 22.5C
        // 2/3/5, at 30C 1/2/3. Everything below reads off these so the wear
        // model (TyreState), the AI strategy and the pre-race screen all agree.
        public const float CoolTrackTempC = 15f;
        public const float StandardTrackTempC = 22.5f;
        public const float HotTrackTempC = 30f;

        /// <summary>
        /// Session track-surface temperature (C) on the calibrated 15-30 gradient,
        /// derived from an event's weather profile plus a small deterministic
        /// per-track offset (hashed from trackId) so two same-profile circuits
        /// don't wear tyres identically. Single source of truth shared by the
        /// track build (TrackManager) and the pre-race UI so the displayed temp,
        /// the recommendation and the on-track wear all agree. Clamped to [15, 30].
        /// </summary>
        public static float TrackTemperatureFor(string weatherProfile, string trackId)
        {
            string p = string.IsNullOrEmpty(weatherProfile) ? "" : weatherProfile.ToLowerInvariant();

            float baseTemp;
            if (p.Contains("wet") || p.Contains("rain") || p.Contains("mixed"))
            {
                baseTemp = CoolTrackTempC;
            }
            else if (p.Contains("hot") || p.Contains("desert"))
            {
                baseTemp = HotTrackTempC;
            }
            else if (p.Contains("cloud") || p.Contains("overcast") || p.Contains("cool") ||
                     p.Contains("cold") || p.Contains("night"))
            {
                baseTemp = CoolTrackTempC + 3f;
            }
            else if (p.Contains("warm"))
            {
                baseTemp = StandardTrackTempC + 3.5f;
            }
            else
            {
                baseTemp = StandardTrackTempC;
            }

            float offset = 0f;
            if (!string.IsNullOrEmpty(trackId))
            {
                int hash = 17;
                for (int i = 0; i < trackId.Length; i++)
                {
                    hash = unchecked(hash * 31 + trackId[i]);
                }

                offset = ((hash & 0x7fffffff) % 1001) / 1000f * 5f - 2.5f;
            }

            float temp = baseTemp + offset;
            return temp < CoolTrackTempC ? CoolTrackTempC : (temp > HotTrackTempC ? HotTrackTempC : temp);
        }

        /// <summary>
        /// Expected stint life in LAPS for a compound at a given track temperature,
        /// piecewise-linear through the calibrated gradient (see the temp anchors).
        /// Continuous, so the wear model can derive an exact multiplier from it and
        /// the planner/UI can round it. Intermediate and Wet aren't dry compounds -
        /// they always mirror the Medium curve (per request: "inters and wets
        /// reflect the medium tyre's durability at any temperature"); the caller
        /// passes Compound.Medium for them. Temp is clamped to [15, 30] so a very
        /// cold or very hot session never runs off the ends of the gradient.
        /// </summary>
        public static float ExpectedStintLapsAtTemp(int compound, float trackTempC)
        {
            switch (compound)
            {
                case Compound.Soft:
                    return LifeGradient(trackTempC, 3f, 2f, 1f);
                case Compound.Hard:
                    return LifeGradient(trackTempC, 5f, 5f, 3f);
                default:
                    // Medium (and Intermediate/Wet, mapped here by the caller).
                    return LifeGradient(trackTempC, 4f, 3f, 2f);
            }
        }

        // Two-segment linear interpolation across the cool/standard/hot anchors,
        // temp clamped to the defined range so the ends hold flat rather than
        // extrapolating to absurd (or negative) life.
        static float LifeGradient(float trackTempC, float atCool, float atStandard, float atHot)
        {
            float t = trackTempC < CoolTrackTempC ? CoolTrackTempC : (trackTempC > HotTrackTempC ? HotTrackTempC : trackTempC);
            if (t <= StandardTrackTempC)
            {
                float u = (t - CoolTrackTempC) / (StandardTrackTempC - CoolTrackTempC);
                return atCool + (atStandard - atCool) * u;
            }

            float v = (t - StandardTrackTempC) / (HotTrackTempC - StandardTrackTempC);
            return atStandard + (atHot - atStandard) * v;
        }

        /// <summary>
        /// Whole-lap stint life used for compound PLANNING, floored so the AI only
        /// commits to a compound it can genuinely see through to the flag (never
        /// one that would fall fractionally short and force an extra stop). Always
        /// at least 1.
        /// </summary>
        public static int StintLapsForPlanning(int compound, float trackTempC)
        {
            int laps = (int)System.Math.Floor(ExpectedStintLapsAtTemp(compound, trackTempC));
            return laps < 1 ? 1 : laps;
        }

        /// <summary>
        /// The next dry pit compound (reached only after the wet/inter override and
        /// the missing-tyre fallback in the caller). Picks the SOFTEST - fastest -
        /// compound whose stint life at THIS track temperature still reaches the
        /// flag, so this becomes the final stop instead of committing the car to
        /// yet another one (the reported "soft-&gt;soft 2-stopper"). Because stint
        /// life shrinks as the track heats, a hot track automatically pushes the
        /// AI onto harder compounds and more stops without any special-casing.
        /// When even the hard can't reach the flag in one stint (a long race still
        /// far from the end), the hard is still the right call - the most durable
        /// tyre minimises how many further stops remain.
        /// </summary>
        public static int NextDryCompound(int lapsRemainingAfterStop, float trackTempC)
        {
            if (lapsRemainingAfterStop <= StintLapsForPlanning(Compound.Soft, trackTempC))
            {
                return Compound.Soft;
            }

            if (lapsRemainingAfterStop <= StintLapsForPlanning(Compound.Medium, trackTempC))
            {
                return Compound.Medium;
            }

            return Compound.Hard;
        }

        /// <summary>
        /// The dry starting compound for an AI car from a 0-2 roll: 0-&gt;Soft,
        /// 1-&gt;Medium, else Hard. The RNG that produces the roll stays in the
        /// caller so its call order is unchanged.
        /// </summary>
        public static int DryStartCompoundFromRoll(int roll)
        {
            return roll == 0 ? Compound.Soft : (roll == 1 ? Compound.Medium : Compound.Hard);
        }

        /// <summary>
        /// Temperature-aware starting compound: the same roll-&gt;compound variety,
        /// but a tyre that wouldn't survive at least two laps at this track
        /// temperature is never a starting choice (otherwise a hot track would
        /// have AI starting on a soft that must pit on lap 1). At the top of the
        /// gradient the soft drops out and the field starts on mediums and hards.
        /// </summary>
        public static int DryStartCompoundFromRoll(int roll, float trackTempC)
        {
            int pick = DryStartCompoundFromRoll(roll);
            if (pick == Compound.Soft && StintLapsForPlanning(Compound.Soft, trackTempC) < 2)
            {
                return Compound.Medium;
            }

            return pick;
        }
    }
}
