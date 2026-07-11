using F1Game.Race.Physics;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager slipstream + dirty-air subsystem (partial). Split out of the
    /// RaceManager monolith verbatim - same class, same members, identical
    /// behaviour. Computes the automatic tow (and the nearest-ahead gap that
    /// feeds VehicleController's default-off dirty-air penalty) for every car
    /// each frame. DriverShortCode stays in the main file (shared).
    /// </summary>
    public partial class RaceManager
    {
        // Slipstream: automatic, physics-based tow from running behind another car
        // on a straight - distinct from DRS (button/AI-commanded, race-eligibility
        // gated, much bigger effect). Computed for every participant every frame
        // and pushed straight into VehicleController.SetSlipstream, which smooths
        // and applies it identically for the player and every AI car - nothing
        // here is player-only.
        const float SlipstreamMinDistance = 8f;
        // Speed-rebalance pass: straights are now ~25% longer, giving a following
        // car more real room to build/hold a tow before the braking zone - nudged
        // up from 85f rather than left to feel weak on the longer straights.
        // Round 2: stacked another 25% (95f -> 119f) to match the further track
        // length increase.
        // Round 3: increased range further (119f -> 150f) so a following car can
        // pick up a tow from noticeably further back.
        const float SlipstreamMaxDistance = 150f;
        const float SlipstreamFullLateralWidth = 3.5f;
        const float SlipstreamMaxLateralWidth = 7.5f;
        const float SlipstreamMinSpeedKph = 130f;
        // Dirty-air feed (consumed by VehicleController behind its default-off
        // switch): a nominal car length, the lateral band within which a leading
        // car's wake reaches the follower, and the "clear air" sentinel gap.
        const float CarLengthMeters = 5f;
        const float DirtyAirLateralMeters = 4f;
        const float DirtyAirClearCarLengths = 999f;

        void UpdateSlipstreamEffects()
        {
            if (Track == null || Participants == null)
            {
                return;
            }

            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (!IsSlipstreamEligible(participant) || Mathf.Abs(participant.vehicle.CurrentSpeedKph) < SlipstreamMinSpeedKph)
                {
                    if (participant != null && participant.vehicle != null)
                    {
                        participant.vehicle.SetSlipstream(0f, "");
                        participant.vehicle.SetDirtyAirGap(DirtyAirClearCarLengths);
                    }

                    continue;
                }

                TrackProgress followerProgress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
                float bestStrength = 0f;
                string bestSource = "";
                // Nearest car directly ahead (any track section), for the dirty-air
                // cornering penalty - independent of the straight-weighted tow.
                float nearestAheadMeters = DirtyAirClearCarLengths * CarLengthMeters;

                for (int j = 0; j < Participants.Count; j++)
                {
                    RaceParticipant other = Participants[j];
                    if (other == participant || !IsSlipstreamEligible(other))
                    {
                        continue;
                    }

                    TrackProgress otherProgress = State == null ? other.lapTracker.CurrentProgress : State.GetCurrentProgress(other);
                    float ahead = SlipstreamForwardDistance(followerProgress.distance, otherProgress.distance);
                    if (ahead > 0.5f && ahead < nearestAheadMeters &&
                        Mathf.Abs(followerProgress.lateralDistance - otherProgress.lateralDistance) < DirtyAirLateralMeters)
                    {
                        nearestAheadMeters = ahead;
                    }

                    float strength = ComputeSlipstreamStrength(followerProgress, otherProgress);
                    if (strength > bestStrength)
                    {
                        bestStrength = strength;
                        bestSource = DriverShortCode(other);
                    }
                }

                participant.vehicle.SetSlipstream(bestStrength, bestSource);
                participant.vehicle.SetDirtyAirGap(nearestAheadMeters / CarLengthMeters);
            }
        }

        // Shared eligibility for BOTH roles - a car being towed and a car creating
        // the wake. No slipstream for anything not genuinely racing on the live
        // track surface right now.
        bool IsSlipstreamEligible(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null)
            {
                return false;
            }

            if (participant.retired || participant.finished)
            {
                return false;
            }

            if (participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return false;
            }

            if (participant.vehicle.PitLimiterActive || participant.vehicle.IsPitGuided || !participant.vehicle.IsOnRoad)
            {
                return false;
            }

            if (participant.isRaceControlAutopilot || participant.vehicle.RaceControlSpeedCapKph < 900f)
            {
                return false;
            }

            if (CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                CurrentRaceControlState == RaceControlState.SafetyCarInThisLap ||
                CurrentRaceControlState == RaceControlState.RedFlagged ||
                CurrentRaceControlState == RaceControlState.Restart)
            {
                return false;
            }

            return true;
        }

        // Forward-only wrapped distance from one track distance to another, always
        // >= 0, going the direction of travel around the lap.
        float SlipstreamForwardDistance(float fromDistance, float toDistance)
        {
            float delta = toDistance - fromDistance;
            if (delta < 0f)
            {
                delta += Track.length;
            }

            return delta;
        }

        // Best tow around 18-35m behind, weaker further back or more offset,
        // scaled down off a straight and to zero in a proper corner. Deliberately
        // generous laterally ("somewhat behind", not laser-aligned) - full strength
        // inside SlipstreamFullLateralWidth, fading out to zero by
        // SlipstreamMaxLateralWidth.
        float ComputeSlipstreamStrength(TrackProgress followerProgress, TrackProgress leaderProgress)
        {
            float aheadDistance = SlipstreamForwardDistance(followerProgress.distance, leaderProgress.distance);
            if (aheadDistance < SlipstreamMinDistance || aheadDistance > SlipstreamMaxDistance)
            {
                return 0f;
            }

            float lateralDiff = Mathf.Abs(followerProgress.lateralDistance - leaderProgress.lateralDistance);
            if (lateralDiff > SlipstreamMaxLateralWidth)
            {
                return 0f;
            }

            // Peak-at-18m / fade-to-150m distance curve and the full-width->fade
            // lateral curve are the engine-free AeroModel.SlipstreamTowStrength; the
            // in-range gates above and the tuned distances/widths stay owned here.
            float strength = AeroModel.SlipstreamTowStrength(
                aheadDistance, lateralDiff, SlipstreamMaxDistance, 18f, SlipstreamFullLateralWidth, SlipstreamMaxLateralWidth);

            return strength * SlipstreamStraightSectionStrength(followerProgress.distance);
        }

        // Mild bends still get a partial tow (full below 9 degrees of heading
        // change over the sampled span, fading to none by 22) rather than a hard
        // corner/straight cutoff - a slipstream doesn't vanish the instant a
        // straight has the faintest kink in it. Widened from 6/16 so a following
        // car keeps (at least partial) tow through gentler, longer-radius bends
        // too, not just near-dead-straight sections.
        float SlipstreamStraightSectionStrength(float distance)
        {
            Vector3 point1;
            Vector3 forward1;
            Vector3 right1;
            Track.SampleAtDistance(distance + 10f, out point1, out forward1, out right1);
            Vector3 point2;
            Vector3 forward2;
            Vector3 right2;
            Track.SampleAtDistance(distance + 55f, out point2, out forward2, out right2);
            // Track sampling + heading angle stay here; the full-below-9deg /
            // none-above-22deg fade is the engine-free AeroModel.SlipstreamStraightFactor.
            float angle = Vector3.Angle(forward1, forward2);
            return AeroModel.SlipstreamStraightFactor(angle, 9f, 22f);
        }
    }
}
