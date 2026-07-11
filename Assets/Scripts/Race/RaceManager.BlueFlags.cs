using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager blue-flag subsystem (partial). Owns the detection - who is
    /// being lapped by whom, and the linger/hold bookkeeping - while the
    /// consequence (the shown car must yield) stays in the engine-free
    /// FlagRules.MustYield consumed by the AI and HUD, and the compliance penalty
    /// tariff stays in PenaltyRules. Split out of the RaceManager monolith
    /// verbatim - same class, same members, identical behaviour, linger window and
    /// call order; IsShownBlueFlag stays public so AI/HUD callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        // ---- Blue flags ----------------------------------------------------
        // Detection lives here (who is being lapped by whom); the consequence -
        // the shown car must yield - is FlagRules.MustYield, consumed by the AI
        // (attack suppression + straight-line concession) and the HUD banner.
        // The compliance penalty tariff lives in PenaltyRules.

        const float BlueFlagLingerSeconds = 0.75f;

        public bool IsShownBlueFlag(RaceParticipant participant)
        {
            return participant != null && participant.blueFlagShown && FlagRules.MustYield(RaceFlag.Blue);
        }

        void ClearBlueFlagState(RaceParticipant participant)
        {
            participant.blueFlagShown = false;
            participant.blueFlagHeldSeconds = 0f;
            participant.blueFlagLingerTimer = 0f;
            participant.blueFlagPenaltyApplied = false;
        }

        void UpdateBlueFlags()
        {
            // Blue flags only exist in green-flag racing: under any caution the
            // field is paced (no lapping traffic to wave through), and neither
            // qualifying nor time trial laps anybody.
            bool blueFlagRacing = State != null && Track != null && !IsRaceFinished && StartCountdown <= 0f &&
                                  CurrentSession != RaceWeekendSession.Qualifying && !IsTimeTrial &&
                                  GlobalRaceFlag == RaceFlag.Green;

            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant backmarker = Participants[i];
                if (backmarker == null)
                {
                    continue;
                }

                bool eligible = blueFlagRacing && !backmarker.retired && !backmarker.finished &&
                                backmarker.vehicle != null && !backmarker.isPitting &&
                                backmarker.pitPhase == PitPhase.None;
                if (!eligible)
                {
                    ClearBlueFlagState(backmarker);
                    continue;
                }

                bool lappingCarClose = FindCloseLappingCar(backmarker) != null;
                if (lappingCarClose)
                {
                    if (!backmarker.blueFlagShown && backmarker.isPlayer)
                    {
                        PostEngineerMessage("Blue flag - the car behind is lapping you, let them through.", true, RaceAudioCue.Yellow);
                    }

                    backmarker.blueFlagShown = true;
                    backmarker.blueFlagLingerTimer = BlueFlagLingerSeconds;
                    backmarker.blueFlagHeldSeconds += Time.deltaTime;
                    if (PenaltyRules.ShouldPenaliseIgnoredBlueFlag(backmarker.blueFlagHeldSeconds, backmarker.blueFlagPenaltyApplied))
                    {
                        backmarker.blueFlagPenaltyApplied = true;
                        AddPenalty(backmarker, PenaltyRules.IgnoredBlueFlagPenaltySeconds, PenaltyRules.IgnoredBlueFlagReason);
                        if (backmarker.isPlayer)
                        {
                            PostEngineerMessage("Penalty for ignoring blue flags - you have to let the leaders by.", true, RaceAudioCue.Penalty);
                        }
                    }
                }
                else if (backmarker.blueFlagShown)
                {
                    // Short linger so the flag doesn't flicker while the pair
                    // trade tenths around the detection gap.
                    backmarker.blueFlagLingerTimer -= Time.deltaTime;
                    if (backmarker.blueFlagLingerTimer <= 0f)
                    {
                        ClearBlueFlagState(backmarker);
                    }
                }
            }
        }

        // A car at least a lap of total progress ahead of `backmarker` and
        // physically within the detection gap behind it on the road.
        RaceParticipant FindCloseLappingCar(RaceParticipant backmarker)
        {
            float backTotalProgress = State.GetProgressDistance(backmarker);
            float backTrackDistance = State.GetCurrentProgress(backmarker).distance;
            float lapAheadThreshold = Track.length * FlagRules.BlueFlagLapProgressFraction;

            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant lapping = Participants[i];
                if (lapping == null || lapping == backmarker || lapping.retired || lapping.finished ||
                    lapping.vehicle == null || lapping.isPitting || lapping.pitPhase != PitPhase.None)
                {
                    continue;
                }

                if (State.GetProgressDistance(lapping) - backTotalProgress < lapAheadThreshold)
                {
                    continue;
                }

                float gapMeters = Track.WrapDistance(backTrackDistance - State.GetCurrentProgress(lapping).distance);
                float lappingSpeed = Mathf.Max(24f, Mathf.Abs(lapping.vehicle.CurrentSpeedKph) / 3.6f);
                if (gapMeters / lappingSpeed <= FlagRules.BlueFlagGapSeconds)
                {
                    return lapping;
                }
            }

            return null;
        }

    }
}
