using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager position-query subsystem (partial). The running-order position
    /// lookup and the nearest-car-ahead / nearest-car-behind track-distance
    /// searches used by the AI, the HUD and radio gap logic. Split out of the
    /// RaceManager monolith verbatim - same class, same members, identical
    /// ordering and distance maths; the public entry points stay public so external
    /// callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        public int GetPosition(RaceParticipant participant)
        {
            if (CurrentSession == RaceWeekendSession.Qualifying && participant == PlayerParticipant)
            {
                return GetQualifyingPositionEstimate();
            }

            if (State == null || State.SortedOrder.Count == 0)
            {
                SortRunningOrder();
            }

            if (State == null) return Participants.Count;
            int index = State.SortedOrder.IndexOf(participant);
            return index < 0 ? Participants.Count : index + 1;
        }

        public int DisplayedEntrantCount
        {
            get
            {
                if (CurrentSession == RaceWeekendSession.Qualifying && qualifyingEntries.Count > 0)
                {
                    return ActiveQualifyingEntries(qualifyingPhase).Count;
                }

                return Participants.Count;
            }
        }

        public RaceParticipant FindCarAhead(RaceParticipant participant, float maxMeters)
        {
            if (participant == null || participant.lapTracker == null)
            {
                return null;
            }

            float self = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float bestDelta = maxMeters;
            RaceParticipant best = null;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                // Retired and finished cars are NOT traffic. A retired car is
                // deactivated, so its lapTracker stops ticking and its
                // TotalProgressDistance is frozen at wherever it stopped - it stays
                // in this search forever as a permanently parked entry. These two
                // methods are the "who is next to me" primitive for the whole race
                // layer (DRS detection gaps, ERS deployment, engineer radio, AI
                // attack/defend states and AI pit strategy), so a ghost handed out
                // real DRS for closing on nothing, made AI fight a despawned car,
                // and produced "you're closing on the car ahead" with clear track.
                if (other == participant || other.lapTracker == null || other.retired || other.finished)
                {
                    continue;
                }

                float otherDistance = State == null ? other.lapTracker.TotalProgressDistance : State.GetProgressDistance(other);
                float delta = otherDistance - self;
                if (delta > 0f && delta < bestDelta)
                {
                    bestDelta = delta;
                    best = other;
                }
            }

            return best;
        }

        public RaceParticipant FindCarBehind(RaceParticipant participant, float maxMeters)
        {
            if (participant == null || participant.lapTracker == null)
            {
                return null;
            }

            float self = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float bestDelta = maxMeters;
            RaceParticipant best = null;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                // Retired and finished cars are NOT traffic. A retired car is
                // deactivated, so its lapTracker stops ticking and its
                // TotalProgressDistance is frozen at wherever it stopped - it stays
                // in this search forever as a permanently parked entry. These two
                // methods are the "who is next to me" primitive for the whole race
                // layer (DRS detection gaps, ERS deployment, engineer radio, AI
                // attack/defend states and AI pit strategy), so a ghost handed out
                // real DRS for closing on nothing, made AI fight a despawned car,
                // and produced "you're closing on the car ahead" with clear track.
                if (other == participant || other.lapTracker == null || other.retired || other.finished)
                {
                    continue;
                }

                float otherDistance = State == null ? other.lapTracker.TotalProgressDistance : State.GetProgressDistance(other);
                float delta = self - otherDistance;
                if (delta > 0f && delta < bestDelta)
                {
                    bestDelta = delta;
                    best = other;
                }
            }

            return best;
        }

    }
}
