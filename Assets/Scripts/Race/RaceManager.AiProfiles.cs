using UnityEngine;

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
        // Difficulty raise round 4 (per request - "make the AI more
        // difficult across all levels in a realistic way, not straight-line
        // speed or unfair advantages"): every tier's DRIVING-QUALITY knobs
        // lifted about a half-tier - later and more accurate braking, tighter
        // apexes, earlier throttle pickup, sharper reactions, fewer mistakes,
        // better ERS/DRS usage, and driving closer to the car's real envelope
        // (paceMultiplier +2-3%, still bounded by the physical feasibility
        // caps). straightSpeedMultiplier untouched; no physics advantage of
        // any kind - the AI simply drive better at every level.
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

        // [PaceDiag] ground-truth lap-time capture (per report - the player time-
        // trials this circuit in ~1:06 while the statistical qualifying model
        // estimates a Verstappen lap at ~1:20; those two systems are disconnected,
        // so before tuning the RACING AI's pace we need its ACTUAL physics lap time,
        // per driver and per difficulty, next to the player's, rather than trusting
        // the quali model). Fires once per completed valid lap: who, their pace stat,
        // the difficulty, the real lap time and the implied average speed. The
        // per-driver spread across these lines is the direct read on how far the AI
        // is off the player AND how much (or little) driver skill currently separates
        // the field.
        System.Collections.Generic.Dictionary<RaceParticipant, int> paceDiagLastLaps;

        void DiagnoseAiPace(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null)
            {
                return;
            }

            if (paceDiagLastLaps == null)
            {
                paceDiagLastLaps = new System.Collections.Generic.Dictionary<RaceParticipant, int>();
            }

            int laps = participant.lapTracker.CompletedLaps;
            int previous;
            if (!paceDiagLastLaps.TryGetValue(participant, out previous))
            {
                paceDiagLastLaps[participant] = laps;
                return;
            }

            if (laps <= previous)
            {
                return;
            }

            paceDiagLastLaps[participant] = laps;

            float lapTime = participant.lapTracker.LastLapTime;
            string who = participant.driverData != null ? participant.driverData.displayName : "AI";
            int pace = participant.driverData != null ? participant.driverData.pace : 0;
            string role = participant.isPlayer ? "PLAYER" : "AI";

            // "PaceDiag is gone" was a silent symptom of a broken track: a lap that
            // never closes cleanly (or is invalidated by track limits every time)
            // used to hit this early return and print nothing, so the diagnostic
            // looked disabled when it was really telling us no valid lap existed.
            // Now say so out loud - an [INVALID] line proves laps ARE completing but
            // getting thrown out, which is a very different problem from cars never
            // finishing a lap at all.
            if (lapTime <= 0f || participant.lapTracker.LastLapInvalidated)
            {
                Debug.Log("[PaceDiag] " + who + " (" + role + ") pace=" + pace + " diff=" + Settings.Difficulty +
                          " lap=" + lapMinSec(lapTime) + " [INVALID: track-limits or unclosed lap - not a pace sample]");
                return;
            }

            float trackLength = Track != null ? Track.length : 0f;
            float avgKph = lapTime > 0f ? (trackLength / lapTime) * 3.6f : 0f;
            int lapMin = (int)(lapTime / 60f);
            float lapSec = lapTime - lapMin * 60f;
            float best = participant.lapTracker.BestLapTime;
            int bestMin = (int)(best / 60f);
            float bestSec = best - bestMin * 60f;

            Debug.Log("[PaceDiag] " + who + " (" + role + ") pace=" + pace + " diff=" + Settings.Difficulty +
                      " lap=" + lapMin + ":" + lapSec.ToString("00.000") +
                      " best=" + bestMin + ":" + bestSec.ToString("00.000") +
                      " | trackLen=" + trackLength.ToString("0") + "m avgSpeed=" + avgKph.ToString("0") + "kph");
        }

        static string lapMinSec(float seconds)
        {
            if (seconds <= 0f)
            {
                return "--:--.---";
            }

            int m = (int)(seconds / 60f);
            return m + ":" + (seconds - m * 60f).ToString("00.000");
        }

        // [CornerDiag] corner-by-corner "where does the AI lose time vs you"
        // diagnostic. The only natural pace lever left is letting the elite carry
        // more corner speed (where they're capped) - but that is the exact lever
        // behind the old wall-crash regressions, so MEASURE first: buckets the lap
        // into fixed micro-sectors and, on each car's best VALID lap, records the
        // time spent and the minimum (apex) speed per sector. Once the player and
        // at least one AI both have a best lap, it logs the net delta split into
        // corner vs straight time, then the individual sectors where the AI loses
        // most - each tagged corner/straight with the apex-kph deficit. A big
        // corner loss WITH a matching apex-speed deficit points straight at the
        // corner-speed cap; a straight-only loss means the fix is elsewhere.
        const int CornerDiagSectors = 32;
        System.Collections.Generic.Dictionary<RaceParticipant, float[]> cornerDiagCurTime;
        System.Collections.Generic.Dictionary<RaceParticipant, float[]> cornerDiagCurMinSpeed;
        System.Collections.Generic.Dictionary<RaceParticipant, float[]> cornerDiagBestTime;
        System.Collections.Generic.Dictionary<RaceParticipant, float[]> cornerDiagBestMinSpeed;
        System.Collections.Generic.Dictionary<RaceParticipant, float> cornerDiagBestLap;
        System.Collections.Generic.Dictionary<RaceParticipant, int> cornerDiagLastLaps;
        float cornerDiagLastLogTime;

        void DiagnoseCornerTimeLoss(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || participant.vehicle == null || Track == null || Track.length <= 1f)
            {
                return;
            }

            if (cornerDiagCurTime == null)
            {
                cornerDiagCurTime = new System.Collections.Generic.Dictionary<RaceParticipant, float[]>();
                cornerDiagCurMinSpeed = new System.Collections.Generic.Dictionary<RaceParticipant, float[]>();
                cornerDiagBestTime = new System.Collections.Generic.Dictionary<RaceParticipant, float[]>();
                cornerDiagBestMinSpeed = new System.Collections.Generic.Dictionary<RaceParticipant, float[]>();
                cornerDiagBestLap = new System.Collections.Generic.Dictionary<RaceParticipant, float>();
                cornerDiagLastLaps = new System.Collections.Generic.Dictionary<RaceParticipant, int>();
            }

            float[] curT;
            if (!cornerDiagCurTime.TryGetValue(participant, out curT))
            {
                curT = new float[CornerDiagSectors];
                cornerDiagCurTime[participant] = curT;
            }

            float[] curV;
            if (!cornerDiagCurMinSpeed.TryGetValue(participant, out curV))
            {
                curV = new float[CornerDiagSectors];
                for (int i = 0; i < CornerDiagSectors; i++)
                {
                    curV[i] = float.MaxValue;
                }

                cornerDiagCurMinSpeed[participant] = curV;
            }

            float norm = Mathf.Repeat(participant.lapTracker.CurrentProgress.normalized, 1f);
            int s = Mathf.Clamp((int)(norm * CornerDiagSectors), 0, CornerDiagSectors - 1);
            curT[s] += Time.deltaTime;
            float speedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);
            if (speedKph < curV[s])
            {
                curV[s] = speedKph;
            }

            int laps = participant.lapTracker.CompletedLaps;
            int prevLaps;
            if (!cornerDiagLastLaps.TryGetValue(participant, out prevLaps))
            {
                cornerDiagLastLaps[participant] = laps;
                return;
            }

            if (laps > prevLaps)
            {
                cornerDiagLastLaps[participant] = laps;
                float lapTime = participant.lapTracker.LastLapTime;
                if (lapTime > 0f && !participant.lapTracker.LastLapInvalidated)
                {
                    float best;
                    if (!cornerDiagBestLap.TryGetValue(participant, out best) || lapTime < best)
                    {
                        cornerDiagBestLap[participant] = lapTime;
                        cornerDiagBestTime[participant] = (float[])curT.Clone();
                        cornerDiagBestMinSpeed[participant] = (float[])curV.Clone();
                    }
                }

                // Reset accumulators for the new lap.
                for (int i = 0; i < CornerDiagSectors; i++)
                {
                    curT[i] = 0f;
                    curV[i] = float.MaxValue;
                }
            }

            MaybeLogCornerTimeLoss();
        }

        void MaybeLogCornerTimeLoss()
        {
            if (Time.time - cornerDiagLastLogTime < 12f)
            {
                return;
            }

            if (PlayerParticipant == null || cornerDiagBestTime == null || !cornerDiagBestTime.ContainsKey(PlayerParticipant))
            {
                return;
            }

            RaceParticipant bestAi = null;
            float bestAiLap = float.MaxValue;
            foreach (System.Collections.Generic.KeyValuePair<RaceParticipant, float> kv in cornerDiagBestLap)
            {
                if (kv.Key != null && !kv.Key.isPlayer && kv.Value < bestAiLap && cornerDiagBestTime.ContainsKey(kv.Key))
                {
                    bestAiLap = kv.Value;
                    bestAi = kv.Key;
                }
            }

            if (bestAi == null)
            {
                return;
            }

            cornerDiagLastLogTime = Time.time;

            float[] pT = cornerDiagBestTime[PlayerParticipant];
            float[] pV = cornerDiagBestMinSpeed[PlayerParticipant];
            float[] aT = cornerDiagBestTime[bestAi];
            float[] aV = cornerDiagBestMinSpeed[bestAi];
            float pLap = cornerDiagBestLap[PlayerParticipant];

            float cornerDelta = 0f;
            float straightDelta = 0f;
            for (int i = 0; i < CornerDiagSectors; i++)
            {
                float d = aT[i] - pT[i];
                if (CornerDiagSectorIsCorner(i))
                {
                    cornerDelta += d;
                }
                else
                {
                    straightDelta += d;
                }
            }

            int[] order = new int[CornerDiagSectors];
            for (int i = 0; i < CornerDiagSectors; i++)
            {
                order[i] = i;
            }

            System.Array.Sort(order, (a, b) => (aT[b] - pT[b]).CompareTo(aT[a] - pT[a]));

            string playerName = PlayerParticipant.driverData != null ? PlayerParticipant.driverData.displayName : "PLAYER";
            string aiName = bestAi.driverData != null ? bestAi.driverData.displayName : "AI";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[CornerDiag] fastest AI ").Append(aiName).Append(" ").Append(lapMinSec(bestAiLap))
              .Append(" vs you ").Append(playerName).Append(" ").Append(lapMinSec(pLap))
              .Append("  (gap ").Append(Signed(bestAiLap - pLap, "0.000")).Append("s)")
              .Append(" | AI net vs you: corners ").Append(Signed(cornerDelta, "0.00")).Append("s")
              .Append(", straights ").Append(Signed(straightDelta, "0.00")).Append("s.")
              .Append(" Biggest AI losses (sector: +dTime, minKph AI vs you):");

            int shown = 0;
            for (int k = 0; k < CornerDiagSectors && shown < 6; k++)
            {
                int i = order[k];
                float dt = aT[i] - pT[i];
                if (dt <= 0.02f)
                {
                    break;
                }

                float centerNorm = (i + 0.5f) / CornerDiagSectors;
                float aiApex = aV[i] >= float.MaxValue ? 0f : aV[i];
                float youApex = pV[i] >= float.MaxValue ? 0f : pV[i];
                sb.Append("\n  #").Append(i).Append(" norm ").Append(centerNorm.ToString("0.00")).Append(" ")
                  .Append(CornerDiagSectorTag(i)).Append(": +").Append(dt.ToString("0.000")).Append("s  minKph ")
                  .Append(aiApex.ToString("0")).Append(" vs ").Append(youApex.ToString("0"))
                  .Append(" (").Append(Signed(aiApex - youApex, "0")).Append(")");
                shown++;
            }

            if (shown == 0)
            {
                sb.Append(" none - you are not quicker than the fastest AI anywhere.");
            }

            Debug.Log(sb.ToString());
        }

        static string Signed(float value, string format)
        {
            return (value >= 0f ? "+" : "") + value.ToString(format);
        }

        bool CornerDiagSectorIsCorner(int sector)
        {
            return CornerDiagSectorTag(sector) != "straight";
        }

        string CornerDiagSectorTag(int sector)
        {
            if (telemetryCorners == null || telemetryCorners.Count == 0 || Track == null)
            {
                return "straight";
            }

            float startDist = (float)sector / CornerDiagSectors * Track.length;
            float endDist = (float)(sector + 1) / CornerDiagSectors * Track.length;
            for (int i = 0; i < telemetryCorners.Count; i++)
            {
                float d = telemetryCorners[i].distance;
                if (d >= startDist && d < endDist)
                {
                    return "CORNER-" + telemetryCorners[i].risk;
                }
            }

            return "straight";
        }

        public AiDifficultyProfile GetAiDifficultyProfile()
        {
            RaceDifficulty difficulty = Settings.Difficulty;
            if (difficulty == RaceDifficulty.Easy)
            {
                return new AiDifficultyProfile
                {
                    // Difficulty raise round 3 (per request - "still WAY too
                    // easy"): every tier's driving-quality knobs lifted roughly
                    // one tier - Easy now drives like the old Medium, Medium
                    // near the old Hard, Hard near Expert, Expert at the
                    // ceiling. Tier ordering and character preserved.
                    brakeDistanceMultiplier = 0.96f,
                    minimumCornerSpeedConfidence = 0.90f,
                    apexErrorMeters = 1.1f,
                    throttleDelay = 0.18f,
                    exitThrottleConfidence = 0.90f,
                    lineOffsetNoise = 0.6f,
                    // Reaction-time buff: explicit per-tier target ranges requested
                    // (Easy 0.4-0.6 / Medium 0.3-0.4 / Hard 0.25-0.3 / Expert 0.2-0.25) -
                    // round 1's uniform nerf (was 0.85/0.55/0.32/0.11) overshot,
                    // especially Easy at a full second.
                    reactionTimeSeconds = 0.42f,
                    overtakeCommitment = 0.35f,
                    defendCommitment = 0.30f,
                    ersDeploymentQuality = 0.70f,
                    drsUsageQuality = 0.80f,
                    mistakeChancePerLap = 0.08f,
                    // Aggression pass (per request): every tier's caution cut so AI
                    // race for position instead of yielding it (was 1.35).
                    trafficAvoidanceCaution = 1.1f,
                    wetWeatherCaution = 1.5f,
                    tyreSavingBias = 0.35f,
                    // Rounds 12-16 (per request - "buff the AI again 5 times the
                    // same way"): +0.02 per round at every tier (was 1.17).
                    // Rounds 17-21 (per request): +0.02 per round again (was 1.27).
                    // Rounds 22-26 (per request): +0.02 per round again (was 1.37).
                    // Rounds 27-31 (per request): +0.02 per round again (was 1.47).
                    // Rounds 32-36 (per request): +0.02 per round again (was 1.57).
                    // Rounds 37-41 (per request): +0.02 per round again (was 1.67).
                paceMultiplier = 1.77f,
                    cornerSpeedMultiplier = 1.14f,
                    straightSpeedMultiplier = 0.97f,
                    brakeConfidenceMultiplier = 1.02f,
                    throttleAggressionMultiplier = 1.0f
                };
            }

            if (difficulty == RaceDifficulty.Medium)
            {
                return new AiDifficultyProfile
                {
                    // Difficulty raise round 3: see the Easy block note.
                    brakeDistanceMultiplier = 1.01f,
                    minimumCornerSpeedConfidence = 0.975f,
                    apexErrorMeters = 0.5f,
                    // Corner-speed pass 3: Medium should stay clearly more
                    // cautious than Hard/Expert but shouldn't read as broken -
                    // a slightly quicker exit pickup (was 0.30/0.78).
                    throttleDelay = 0.07f,
                    // Cornering buff round 5: Medium gets a modest, deliberately small
                    // lift (was 0.82/1.00/1.00) - "decent, not broken", nowhere near
                    // the Hard/Expert jump below.
                    exitThrottleConfidence = 0.97f,
                    lineOffsetNoise = 0.3f,
                    reactionTimeSeconds = 0.31f,
                    // Natural-difficulty pass (see Hard): a smaller racecraft lift so
                    // Medium fights a little harder for position while staying clearly
                    // more passive than Hard.
                    overtakeCommitment = 0.62f,
                    defendCommitment = 0.63f,
                    ersDeploymentQuality = 0.88f,
                    drsUsageQuality = 0.94f,
                    mistakeChancePerLap = 0.04f,
                    // Aggression pass (per request): was 1.05.
                    trafficAvoidanceCaution = 0.85f,
                    wetWeatherCaution = 1.2f,
                    tyreSavingBias = 0.20f,
                    // Rounds 12-16: +0.02 per round (was 1.22).
                    // Rounds 17-21: +0.02 per round again (was 1.32).
                    // Rounds 22-26: +0.02 per round again (was 1.42).
                    // Rounds 27-31: +0.02 per round again (was 1.52).
                    // Rounds 32-36: +0.02 per round again (was 1.62).
                    // Rounds 37-41 (per request): +0.02 per round again (was 1.72).
                paceMultiplier = 1.82f,
                    // Cornering buff round 7: pushed up (was 1.05) alongside the wider
                    // HighSpeed/Medium bands in AiVehicleController - Medium now covers
                    // genuinely fast corners too, not just cautious ones.
                    cornerSpeedMultiplier = 1.5f,
                    straightSpeedMultiplier = 0.99f,
                    brakeConfidenceMultiplier = 1.26f,
                    throttleAggressionMultiplier = 1.3f
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
                    // Difficulty raise round 3: see the Easy block note.
                    brakeDistanceMultiplier = 1.07f,
                    // Corner-speed pass: Hard used to get none of the skillTier-
                    // gated corner-type bonuses in AiVehicleController (those were
                    // Expert-only) despite already having a fairly high confidence
                    // number here - the confidence stat alone wasn't enough to read
                    // as "meaningfully faster through corners". Bumped alongside
                    // the new skillTier blend so Hard is now clearly quicker than
                    // Medium through medium/fast corners, not just a hair sharper.
                    minimumCornerSpeedConfidence = 0.997f,
                    apexErrorMeters = 0.22f,
                    // Cornering buff round 5: exit hesitation shortened again (was
                    // 0.10) and exit confidence raised again (was 0.95) - "pick up
                    // throttle earlier on exit" was as much about this as the
                    // apex-speed floors themselves.
                    throttleDelay = 0.035f,
                    exitThrottleConfidence = 0.993f,
                    lineOffsetNoise = 0.16f,
                    // Natural-difficulty pass ([PaceDiag] - the AI's raw PACE is
                    // already competitive with a skilled human on this circuit, and
                    // 41 rounds of pace/corner-speed cranking have saturated against
                    // the physical feasibility caps, so more of that lever no longer
                    // converts to lap time. The untapped, purely-decision-quality
                    // lever is RACECRAFT: a pace-equal AI still feels easy if it
                    // reacts slowly, waves the player through, and doesn't fight to
                    // hold its line. Reaction sharpened and both the attack and the
                    // defend commitment lifted so Hard actually races the player wheel-
                    // to-wheel - no grip, no speed, purely how hard it commits.
                    reactionTimeSeconds = 0.22f,
                    overtakeCommitment = 0.85f,
                    defendCommitment = 0.88f,
                    ersDeploymentQuality = 0.95f,
                    drsUsageQuality = 0.99f,
                    // Cut 0.015 -> 0.005 (per report - AI mistakes "wayyy too
                    // common even at hard"). This value is rolled every ~3-8s
                    // (see UpdateMistake), not once per lap as the name implies,
                    // and there are 21 AI cars - so even a low per-car rate reads
                    // as a constant stream of field-wide errors. Hard should look
                    // clean; a genuine slip stays possible but rare.
                    mistakeChancePerLap = 0.005f,
                    // Aggression pass (per request): was 0.82.
                    // Natural-difficulty pass: trimmed again (0.62 -> 0.56) so Hard
                    // holds its line in a fight and makes the player find a way past,
                    // rather than ceding the position early. Still a real avoidance
                    // margin - not enough to turn into a contact machine.
                    trafficAvoidanceCaution = 0.56f,
                    wetWeatherCaution = 0.98f,
                    tyreSavingBias = 0.12f,
                    // Rounds 12-16: +0.02 per round (was 1.28).
                    // Rounds 17-21: +0.02 per round again (was 1.38).
                    // Rounds 22-26: +0.02 per round again (was 1.48).
                    // Rounds 27-31: +0.02 per round again (was 1.58).
                    // Rounds 32-36: +0.02 per round again (was 1.68).
                    // Rounds 37-41 (per request): +0.02 per round again (was 1.78).
                // Natural-difficulty pass round 2 (per report - the racecraft-only
                // lift was "still not nearly enough"): a bounded pace bump on top.
                // Most of the pace lever is saturated against the physical caps, but
                // the straight-line/exit-limited portion still converts, and once the
                // swerve slew-limiter stops the AI scrubbing speed with a 16 Hz
                // shimmy this headroom is real. Kept small so it can't outrun the
                // player's own car; PaceDiag verifies the result.
                // Round 3 ([PaceDiag] on Hard: player best 1:21.5, fastest AI
                // Leclerc/Russell 1:21.8-1:22.3 - close but the player still edges
                // the field, and the slower AI scrub time in the straight-line
                // weave that's now being trimmed). Bump again so the top AI clear
                // the player's pace; still bounded by the physical feasibility cap.
                paceMultiplier = 2.00f,
                    // Cornering buff round 7: pushed again (was 1.22/1.24/1.28/1.30/
                    // 1.42) - "fast corners need to be A LOT faster". No longer
                    // touches genuine hairpins at all (see the Hairpin-type exemption
                    // in AiVehicleController), so this only ever buffs HighSpeed/
                    // Medium/Slow corners - safe to push hard without also making
                    // hairpins faster again.
                    cornerSpeedMultiplier = 1.66f,
                    straightSpeedMultiplier = 1.00f,
                    brakeConfidenceMultiplier = 1.42f,
                    throttleAggressionMultiplier = 1.46f
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
                // Difficulty raise round 3: see the Easy block note.
                brakeDistanceMultiplier = 1.12f,
                // Cornering buff round 5: pushed to the practical ceiling (was
                // 0.995) - Expert should show essentially zero entry hesitation.
                minimumCornerSpeedConfidence = 0.999f,
                apexErrorMeters = 0.10f,
                throttleDelay = 0.025f,
                exitThrottleConfidence = 0.997f,
                lineOffsetNoise = 0.09f,
                // Natural-difficulty pass (see the Hard block): pace is saturated at
                // the caps, so Expert is sharpened as a racer instead - near-instant
                // reaction and near-maximal attack/defend commitment, so it hounds the
                // player and shuts the door. Purely decision quality; ordering above
                // Hard preserved.
                reactionTimeSeconds = 0.18f,
                overtakeCommitment = 0.97f,
                defendCommitment = 0.98f,
                ersDeploymentQuality = 0.975f,
                drsUsageQuality = 0.98f,
                // Cut 0.002 -> 0.0008 alongside the Hard reduction: Expert should
                // be the cleanest field on the grid, essentially error-free.
                mistakeChancePerLap = 0.0008f,
                // Aggression pass (per request): was 0.42.
                // Natural-difficulty pass: trimmed (0.32 -> 0.27) so Expert commits to
                // holding its line, still leaving a genuine racing margin.
                trafficAvoidanceCaution = 0.27f,
                wetWeatherCaution = 0.88f,
                tyreSavingBias = 0.07f,
                // Rounds 12-16: +0.02 per round (was 1.34).
                // Rounds 17-21: +0.02 per round again (was 1.44).
                // Rounds 22-26: +0.02 per round again (was 1.54).
                // Rounds 27-31: +0.02 per round again (was 1.64).
                // Rounds 32-36: +0.02 per round again (was 1.74).
                // Rounds 37-41 (per request): +0.02 per round again (was 1.84).
                // Natural-difficulty pass round 2 (see Hard): same bounded pace bump.
                // Round 3 (see Hard): +0.06 again so Expert clears the player's pace
                // by a real margin; still capped by physical feasibility.
                paceMultiplier = 2.05f,
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
