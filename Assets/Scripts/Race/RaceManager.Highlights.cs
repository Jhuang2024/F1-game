using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager overtake-highlights subsystem (partial). Records every
    /// genuine on-track pass during the race and rates it on several factors
    /// (position taken, speed, how close the fight was, corner vs straight,
    /// re-pass battles, late-race stakes, player involvement); the post-race
    /// report shows the best 20 (RuntimeUi.BuildOvertakeHighlightsSection).
    /// Detection rides the same running-order snapshot cadence the
    /// illegal-overtake check already uses, so "a pass happened" means exactly
    /// the same thing here as it does for penalties and the player's
    /// overtakes-completed counter.
    /// </summary>
    public partial class RaceManager
    {
        public struct OvertakeHighlight
        {
            public float raceTime;
            public int lap;
            public string attackerName;
            public string defenderName;
            public bool attackerIsPlayer;
            public bool defenderIsPlayer;
            /// <summary>Running-order position (1-based) the attacker took.</summary>
            public int positionTaken;
            /// <summary>0-100 composite score; see BuildRatedOvertake.</summary>
            public float rating;
            /// <summary>Human-readable factor tags, e.g. "for the lead · wheel-to-wheel".</summary>
            public string factors;
        }

        readonly List<OvertakeHighlight> recordedOvertakes = new List<OvertakeHighlight>();

        // "attacker>defender" -> race time of the last recorded pass. Serves
        // two jobs: the same ordered pair re-recording within a few seconds is
        // snapshot jitter (two cars trading noses over one braking zone), and
        // the REVERSE pair within a longer window is a genuine re-pass battle
        // worth a rating bonus.
        readonly Dictionary<string, float> overtakeHighlightPairTimes = new Dictionary<string, float>();

        const float OvertakeRecordJitterWindow = 12f;
        const float OvertakeBattleRepassWindow = 90f;

        public IReadOnlyList<OvertakeHighlight> OvertakeHighlights => recordedOvertakes;

        void ResetOvertakeHighlights()
        {
            recordedOvertakes.Clear();
            overtakeHighlightPairTimes.Clear();
        }

        // Called from CheckIllegalOvertakesUnderYellow on its snapshot cadence,
        // BEFORE the previous-order snapshot is overwritten (same contract as
        // TrackPlayerOvertakesCompleted).
        void RecordOvertakeHighlights(List<RaceParticipant> currentOrder, bool restrictionActiveNow)
        {
            if (raceControlOrderSnapshot.Count <= 1 || currentOrder.Count <= 1)
            {
                return;
            }

            for (int c = 0; c < currentOrder.Count; c++)
            {
                RaceParticipant mover = currentOrder[c];
                int moverPreviousIndex = raceControlOrderSnapshot.IndexOf(mover);
                if (moverPreviousIndex < 0 || mover.vehicle == null)
                {
                    continue;
                }

                for (int d = c + 1; d < currentOrder.Count; d++)
                {
                    RaceParticipant passed = currentOrder[d];
                    int passedPreviousIndex = raceControlOrderSnapshot.IndexOf(passed);
                    if (passedPreviousIndex < 0 || passedPreviousIndex >= moverPreviousIndex || passed.vehicle == null)
                    {
                        continue;
                    }

                    // Passing a car that isn't really racing is order
                    // correction, and a pass under caution is a penalty case -
                    // neither is a highlight.
                    if (IsPositionCorrectionAllowed(mover, passed))
                    {
                        continue;
                    }

                    if (restrictionActiveNow &&
                        (IsOvertakingRestrictedForParticipant(mover) || IsOvertakingRestrictedForParticipant(passed)))
                    {
                        continue;
                    }

                    // Neither car at racing speed = a shuffle, not an overtake.
                    if (Mathf.Abs(mover.vehicle.CurrentSpeedKph) < 60f)
                    {
                        continue;
                    }

                    string pairKey = mover.driverId + ">" + passed.driverId;
                    float lastSamePairTime;
                    if (overtakeHighlightPairTimes.TryGetValue(pairKey, out lastSamePairTime) &&
                        RaceElapsed - lastSamePairTime < OvertakeRecordJitterWindow)
                    {
                        continue;
                    }

                    float lastReversePairTime;
                    bool battleRepass = overtakeHighlightPairTimes.TryGetValue(passed.driverId + ">" + mover.driverId, out lastReversePairTime) &&
                                        RaceElapsed - lastReversePairTime < OvertakeBattleRepassWindow;
                    overtakeHighlightPairTimes[pairKey] = RaceElapsed;

                    recordedOvertakes.Add(BuildRatedOvertake(mover, passed, c + 1, currentOrder.Count, battleRepass));
                    // Feed the replay timeline too - this is the only place a genuine
                    // pass is detected, and OvertakeCount (printed on the results
                    // screen) is derived from these markers.
                    replayCapture.AddOvertakeMarker(RaceElapsed, ReplayCarIndex(mover),
                        mover.driverName + " on " + passed.driverName);
                }
            }
        }

        // The rating: a 40-point base plus weighted factors, clamped 0-100.
        // Deliberately several independent factors (per request) so the top-20
        // list surfaces varied drama - a last-lap lead change, a wheel-to-wheel
        // midfield scrap, a flat-out re-pass - not just twenty copies of
        // whoever passed the most backmarkers.
        OvertakeHighlight BuildRatedOvertake(RaceParticipant mover, RaceParticipant passed, int positionTaken, int fieldSize, bool battleRepass)
        {
            float moverSpeed = Mathf.Abs(mover.vehicle.CurrentSpeedKph);
            float passedSpeed = Mathf.Abs(passed.vehicle.CurrentSpeedKph);
            float speedDelta = Mathf.Abs(moverSpeed - passedSpeed);
            float steer = Mathf.Abs(mover.vehicle.CurrentCommand.steer);
            float brake = mover.vehicle.CurrentCommand.brake;
            int lap = mover.lapTracker != null ? mover.lapTracker.CompletedLaps + 1 : 1;

            List<string> tags = new List<string>();
            float score = 40f;

            // Factor 1: how far up the order the pass happened. Taking the
            // lead is the headline; a podium place is close behind.
            float positionValue = fieldSize > 1 ? 1f - (positionTaken - 1) / (float)(fieldSize - 1) : 1f;
            score += positionValue * 20f;
            if (positionTaken == 1)
            {
                score += 10f;
                tags.Add("for the lead");
            }
            else if (positionTaken <= 3)
            {
                score += 5f;
                tags.Add("for the podium");
            }

            // Factor 2: commitment - speed at the moment the pass completed.
            if (moverSpeed > 240f)
            {
                score += 10f;
                tags.Add("flat-out");
            }
            else
            {
                score += Mathf.Clamp01((moverSpeed - 120f) / 120f) * 6f;
            }

            // Factor 3: how close the fight was. Near-equal speeds mean a real
            // wheel-to-wheel move; a huge closing delta is a DRS breeze-past.
            if (speedDelta < 12f)
            {
                score += 10f;
                tags.Add("wheel-to-wheel");
            }
            else if (speedDelta > 45f)
            {
                score -= 8f;
            }

            // Factor 4: where the move was made - out-braking or around a
            // corner beats a straight-line pass.
            if (brake > 0.45f)
            {
                score += 8f;
                tags.Add("under braking");
            }
            else if (steer > 0.25f)
            {
                score += 6f;
                tags.Add("mid-corner");
            }

            // Factor 5: a re-pass inside the battle window - they traded
            // places and the fight came back.
            if (battleRepass)
            {
                score += 9f;
                tags.Add("re-pass");
            }

            // Factor 6: stakes rise late in the race; the last lap most of all.
            if (RaceLaps > 0 && lap >= RaceLaps)
            {
                score += 6f;
                tags.Add("last lap");
            }
            else if (RaceLaps > 0)
            {
                score += Mathf.Clamp01(lap / (float)RaceLaps) * 5f;
            }

            // Factor 7: the player being involved on either side of the move.
            if (mover.isPlayer)
            {
                score += 6f;
                tags.Add("by you");
            }
            else if (passed.isPlayer)
            {
                score += 4f;
                tags.Add("on you");
            }

            return new OvertakeHighlight
            {
                raceTime = RaceElapsed,
                lap = lap,
                attackerName = mover.driverName,
                defenderName = passed.driverName,
                attackerIsPlayer = mover.isPlayer,
                defenderIsPlayer = passed.isPlayer,
                positionTaken = positionTaken,
                rating = Mathf.Clamp(score, 0f, 100f),
                factors = tags.Count > 0 ? string.Join(" · ", tags) : "clean pass",
            };
        }

        /// <summary>
        /// The race's best overtakes, highest-rated first, capped at
        /// <paramref name="maxCount"/> (the post-race report asks for 20).
        /// </summary>
        public List<OvertakeHighlight> GetTopOvertakeHighlights(int maxCount)
        {
            List<OvertakeHighlight> sorted = new List<OvertakeHighlight>(recordedOvertakes);
            sorted.Sort((a, b) => b.rating.CompareTo(a.rating));
            if (sorted.Count > maxCount)
            {
                sorted.RemoveRange(maxCount, sorted.Count - maxCount);
            }

            return sorted;
        }
    }
}
