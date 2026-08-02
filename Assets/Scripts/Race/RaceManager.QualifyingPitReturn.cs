using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager qualifying pit-return subsystem (partial). Animates the field
    /// back into the pits between qualifying segments - snapping each car to its
    /// pit service pose and posting the player's "Qn complete: car in pits" status.
    /// Split out of the RaceManager monolith verbatim - same class, same members,
    /// identical poses and call order; callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        void AnimateQualifyingReturnToPits()
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null || participant.vehicle == null)
                {
                    continue;
                }

                if (participant.pitPhase != PitPhase.QualifyingReturn)
                {
                    BeginQualifyingPitReturn(participant);
                }

                UpdateQualifyingPitReturn(participant);
            }
        }

        void BeginQualifyingPitReturn(RaceParticipant participant)
        {
            participant.pitPhase = PitPhase.QualifyingReturn;
            participant.isPitting = true;
            participant.pitLimiterUntilExit = false;
            participant.vehicle.ClearPitRequest();
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitGuidance(true);
            if (participant.isPlayer)
            {
                SessionMessage = "Q" + qualifyingPhase + " complete: returning to pits";
                PostEngineerMessage("Good, bring it back to the pits. We will reset for the next segment.", true);
            }
        }

        void UpdateQualifyingPitReturn(RaceParticipant participant)
        {
            // Each car returns to its TEAM's garage box. Boxes are shared by both of a
            // team's cars (pitBoxIndex is a constructor index, not a grid slot), so
            // the second car parks back down the lane rather than being driven into
            // its teammate - the same offset SpawnParticipant applies when it places
            // the player's car in the box at the start of a segment.
            Vector3 servicePosition;
            Quaternion serviceRotation;
            Track.GetPitServicePose(participant.pitBoxIndex, out servicePosition, out serviceRotation);
            if (IsSecondCarOfTeam(participant.driverId, participant.teamId))
            {
                servicePosition -= serviceRotation * Vector3.forward * 6f;
            }

            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitGuidance(true);
            float distance = participant.vehicle.GuideToPitPose(servicePosition, serviceRotation, 22f, 220f);
            if (distance <= 0.45f)
            {
                participant.vehicle.SnapToPitPose(servicePosition, serviceRotation);
                if (participant.isPlayer)
                {
                    SessionMessage = "Q" + qualifyingPhase + " complete: car in pits";
                }
            }
        }

    }
}
