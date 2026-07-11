using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager gap/interval text subsystem (partial). Formats the radio/HUD
    /// timing strings - the gap and time-interval to the car ahead, the gap between
    /// any two cars, the gap to the leader, the interval ahead and the gap behind.
    /// Split out of the RaceManager monolith verbatim - same class, same members,
    /// identical formatting and maths; the public entry points stay public so the
    /// HUD/radio callers resolve in-class (GetIntervalToAheadSeconds is also read by
    /// the DRS partial).
    /// </summary>
    public partial class RaceManager
    {
        public string GapAheadText(RaceParticipant participant)
        {
            RaceParticipant ahead = FindCarAhead(participant, 9999f);
            if (ahead == null || participant == null || participant.lapTracker == null)
            {
                return "--";
            }

            float aheadDistance = State == null ? ahead.lapTracker.TotalProgressDistance : State.GetProgressDistance(ahead);
            float selfDistance = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float deltaMeters = aheadDistance - selfDistance;
            float speed = Mathf.Max(18f, participant.vehicle == null ? 32f : participant.vehicle.CurrentSpeedKph / 3.6f);
            return (deltaMeters / speed).ToString("0.0") + "s";
        }

        public float GetIntervalToAheadSeconds(RaceParticipant participant)
        {
            RaceParticipant ahead = FindCarAhead(participant, 220f);
            if (ahead == null || participant == null || participant.lapTracker == null)
            {
                return 999f;
            }

            float aheadDistance = State == null ? ahead.lapTracker.TotalProgressDistance : State.GetProgressDistance(ahead);
            float participantDistance = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float deltaMeters = aheadDistance - participantDistance;
            float speed = Mathf.Max(24f, participant.vehicle == null ? 36f : Mathf.Abs(participant.vehicle.CurrentSpeedKph) / 3.6f);
            return Mathf.Max(0f, deltaMeters / speed);
        }

        // Generic gap-in-seconds between any two participants (not necessarily
        // adjacent on track), used for teammate/rival callouts where the pair
        // could be several cars apart. Positive when `a` is ahead of `b`.
        public float GetGapBetweenSeconds(RaceParticipant a, RaceParticipant b)
        {
            if (a == null || b == null || a.lapTracker == null || b.lapTracker == null)
            {
                return 0f;
            }

            float aDistance = State == null ? a.lapTracker.TotalProgressDistance : State.GetProgressDistance(a);
            float bDistance = State == null ? b.lapTracker.TotalProgressDistance : State.GetProgressDistance(b);
            float deltaMeters = aDistance - bDistance;
            float refSpeed = Mathf.Max(24f, a.vehicle == null ? 36f : Mathf.Abs(a.vehicle.CurrentSpeedKph) / 3.6f);
            return deltaMeters / refSpeed;
        }

        public string GapToLeaderText(RaceParticipant participant)
        {
            SortRunningOrder();
            if (participant == null || State == null || State.SortedOrder.Count == 0 || State.SortedOrder[0] == participant)
            {
                return "LEADER";
            }

            RaceParticipant leader = State.SortedOrder[0];
            float leaderDistance = State.GetProgressDistance(leader);
            float participantDistance = State.GetProgressDistance(participant);
            float deltaMeters = leaderDistance - participantDistance;
            if (Track != null && deltaMeters >= Track.length * 0.92f)
            {
                int laps = Mathf.Max(1, Mathf.RoundToInt(deltaMeters / Mathf.Max(1f, Track.length)));
                return "+" + laps + "L";
            }

            float speed = Mathf.Max(24f, participant.vehicle == null ? 36f : Mathf.Abs(participant.vehicle.CurrentSpeedKph) / 3.6f);
            return "+" + (Mathf.Max(0f, deltaMeters) / speed).ToString("0.0") + "s";
        }

        public string IntervalAheadText(RaceParticipant participant)
        {
            SortRunningOrder();
            if (State == null) return "--";
            int index = State.SortedOrder.IndexOf(participant);
            if (index <= 0)
            {
                return "--";
            }

            RaceParticipant ahead = State.SortedOrder[index - 1];
            float aheadDistance = State.GetProgressDistance(ahead);
            float participantDistance = State.GetProgressDistance(participant);
            float deltaMeters = aheadDistance - participantDistance;
            if (Track != null && deltaMeters >= Track.length * 0.92f)
            {
                int laps = Mathf.Max(1, Mathf.RoundToInt(deltaMeters / Mathf.Max(1f, Track.length)));
                return "+" + laps + "L";
            }

            float speed = Mathf.Max(24f, participant.vehicle == null ? 36f : Mathf.Abs(participant.vehicle.CurrentSpeedKph) / 3.6f);
            return (Mathf.Max(0f, deltaMeters) / speed).ToString("0.0") + "s";
        }

        public string GapBehindText(RaceParticipant participant)
        {
            RaceParticipant behind = FindCarBehind(participant, 9999f);
            if (behind == null || participant == null || participant.lapTracker == null)
            {
                return "--";
            }

            float participantDistance = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float behindDistance = State == null ? behind.lapTracker.TotalProgressDistance : State.GetProgressDistance(behind);
            float deltaMeters = participantDistance - behindDistance;
            float speed = Mathf.Max(18f, behind.vehicle == null ? 32f : behind.vehicle.CurrentSpeedKph / 3.6f);
            return (deltaMeters / speed).ToString("0.0") + "s";
        }

        // Structured qualifying timing tower row so RaceHud renders directly into
        // real Text cells instead of parsing a hand-padded string back apart.
        public struct QualifyingTowerRow
        {
            public int position;
            public string driverCode;
            public string bestTimeText;
            public string gapText;
            public bool isPlayer;
        }

    }
}
