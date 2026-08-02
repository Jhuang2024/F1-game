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
        // rubber lasts longer; hotter degrades faster. Anchors recalibrated
        // against observed live wear (per report: "hards showing 4 laps last 5,
        // softs showing 1 lap last 2" - the old table ran ~1 lap pessimistic
        // across the board, which also forced the AI into two-stops that were
        // never actually needed): at 15C soft/medium/hard last 4/5/6 laps, at
        // 22.5C 3/4/6, at 30C 2/3/4. TyreState's baseWear values are calibrated
        // to the same anchors so life on track matches what is displayed and
        // planned. Everything below reads off these so the wear model
        // (TyreState), the AI strategy and the pre-race screen all agree.
        // Real F1 track surface temperature runs ~15 C (Las Vegas at night, a cold
        // Spa) to 55-60 C (Bahrain, Qatar, Miami in the day). The old 15/22.5/30
        // gradient clamped away the entire upper half of the real range - which is
        // exactly where thermal degradation, blistering and overheating live.
        public const float CoolTrackTempC = 15f;
        public const float StandardTrackTempC = 35f;
        public const float HotTrackTempC = 55f;

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
                    return LifeGradient(trackTempC, 4f, 3f, 2f);
                case Compound.Hard:
                    return LifeGradient(trackTempC, 6f, 6f, 4f);
                default:
                    // Medium (and Intermediate/Wet, mapped here by the caller).
                    return LifeGradient(trackTempC, 5f, 4f, 3f);
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
        /// Whole-lap RAW life used for DISPLAY ("softs last ~N laps"), floored.
        /// This is the tyre's actual life on the calibrated gradient; the strategy
        /// below plans against PlanningStintLaps instead, which also accounts for
        /// the once-a-lap pit window. Always at least 1.
        /// </summary>
        public static int StintLapsForPlanning(int compound, float trackTempC)
        {
            int laps = (int)System.Math.Floor(ExpectedStintLapsAtTemp(compound, trackTempC));
            return laps < 1 ? 1 : laps;
        }

        /// <summary>
        /// USABLE whole-lap stint for strategy PLANNING. A car can only pit once a
        /// lap (~85% of the way round), so it has to box at the last pass before
        /// the tyre's grip collapses - which costs up to ~1 lap of the raw life.
        /// This is the count the AI's actual stops and the strategy optimiser both
        /// reason from, so a "3-lap" tyre is planned as ~2 usable laps and the
        /// recommended stop count matches what really happens on track. Always at
        /// least 1.
        /// </summary>
        public static int PlanningStintLaps(int compound, float trackTempC)
        {
            float life = ExpectedStintLapsAtTemp(compound, trackTempC);
            int usable = (int)System.Math.Floor(0.85f * life + 0.15f);
            return usable < 1 ? 1 : usable;
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
            if (lapsRemainingAfterStop <= PlanningStintLaps(Compound.Soft, trackTempC))
            {
                return Compound.Soft;
            }

            if (lapsRemainingAfterStop <= PlanningStintLaps(Compound.Medium, trackTempC))
            {
                return Compound.Medium;
            }

            return Compound.Hard;
        }

        // Relative per-lap pace penalty by compound (seconds/lap slower than the
        // soft) and the time lost per pit stop, used by the strategy optimiser
        // below. Softer rubber is faster per lap but forces stops sooner.
        static float CompoundPacePenaltySeconds(int compound)
        {
            switch (compound)
            {
                case Compound.Soft:
                    return 0f;
                case Compound.Medium:
                    return 0.45f;
                default:
                    return 0.9f; // Hard
            }
        }

        public const float PitStopLossSeconds = 22f;
        // Within-stint degradation cost: a longer stint spends more of its life on
        // worn rubber, so total time lost to wear grows faster than linearly with
        // stint length - modelled as ~coeff * laps^2 per stint. This is what makes
        // an extra stop worthwhile once stints get long, so the optimiser can
        // prefer a 2-stop when it is genuinely quicker rather than always minimising
        // stops.
        public const float StintDegPenaltyCoeffSeconds = 0.13f;

        static int CeilDivStrategy(int a, int b)
        {
            return b <= 0 ? a : (a + b - 1) / b;
        }

        /// <summary>
        /// The FASTEST dry strategy (starting compound + number of stops) for a race
        /// of raceLaps at this track temperature, weighing compound pace, in-stint
        /// degradation and the ~22s lost per pit stop against each other. Stint
        /// feasibility uses the same per-temperature stint lengths the AI and the
        /// wear model use, so a hot track that forces short stints naturally comes
        /// out as a two- or three-stopper, while a cool track can one-stop on softs.
        /// A 4+ lap race carries the mandatory pit (min one stop).
        /// </summary>
        public static void FastestDryStrategy(int raceLaps, float trackTempC, out int startCompound, out int stopCount)
        {
            int laps = raceLaps > 0 ? raceLaps : 5;
            int softLife = PlanningStintLaps(Compound.Soft, trackTempC);
            int medLife = PlanningStintLaps(Compound.Medium, trackTempC);
            int hardLife = PlanningStintLaps(Compound.Hard, trackTempC);

            int minStops = laps >= 4 ? 1 : 0;

            float best = float.MaxValue;
            int bestStops = minStops;
            int bestCompound = Compound.Hard;

            for (int stops = minStops; stops <= laps; stops++)
            {
                int stints = stops + 1;
                int maxStintLen = CeilDivStrategy(laps, stints);

                int compound;
                if (maxStintLen <= softLife)
                {
                    compound = Compound.Soft;
                }
                else if (maxStintLen <= medLife)
                {
                    compound = Compound.Medium;
                }
                else if (maxStintLen <= hardLife)
                {
                    compound = Compound.Hard;
                }
                else
                {
                    // Even the hard cannot cover a stint this long - this few stops
                    // is physically impossible at this temperature; need more.
                    continue;
                }

                float avgStint = (float)laps / stints;
                float paceTime = CompoundPacePenaltySeconds(compound) * laps;
                float degTime = StintDegPenaltyCoeffSeconds * stints * avgStint * avgStint;
                float pitTime = stops * PitStopLossSeconds;
                float total = paceTime + degTime + pitTime;

                if (total < best)
                {
                    best = total;
                    bestStops = stops;
                    bestCompound = compound;
                }
            }

            startCompound = bestCompound;
            stopCount = bestStops;
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
            if (pick == Compound.Soft && PlanningStintLaps(Compound.Soft, trackTempC) < 2)
            {
                return Compound.Medium;
            }

            return pick;
        }
    }
}
