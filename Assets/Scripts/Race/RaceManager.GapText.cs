using F1Game.Race.Rules;
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
        /// <summary>
        /// Time gap in seconds between two cars: how long ago the car AHEAD passed
        /// the point the car BEHIND is standing on now. Positive when
        /// <paramref name="ahead"/> is genuinely ahead; 0 when it is not.
        ///
        /// This replaces the old "metres / one car's instantaneous speed" form used
        /// by every gap in the game. That form is not a time gap at all: speed
        /// varies 4-5x around a lap, so a constant 60m gap read anywhere between
        /// ~0.7s and ~2.7s purely from where the reference car was, and since each
        /// call site chose a different reference car (the participant here, the car
        /// behind in GapBehindText, the leader in the production timing tower) the
        /// same pair of cars was given different gaps at the same instant. The whole
        /// tower pulsed every time the leader braked, and DRS - a 1.0s test - turned
        /// on whether the detection point sat before or after a braking zone.
        /// </summary>
        public float GapSecondsBetween(RaceParticipant ahead, RaceParticipant behind)
        {
            if (ahead == null || behind == null || ahead == behind ||
                ahead.lapTracker == null || behind.lapTracker == null)
            {
                return 0f;
            }

            float aheadDistance = State == null ? ahead.lapTracker.TotalProgressDistance : State.GetProgressDistance(ahead);
            float behindDistance = State == null ? behind.lapTracker.TotalProgressDistance : State.GetProgressDistance(behind);
            float deltaMeters = aheadDistance - behindDistance;
            if (deltaMeters <= 0f)
            {
                return 0f;
            }

            // Preferred: when did the car ahead actually pass this point?
            float passedAt;
            if (State != null && State.ProgressHistory != null &&
                State.ProgressHistory.TryGetTimeAtDistance(ahead, behindDistance, out passedAt))
            {
                return Mathf.Max(0f, Time.time - passedAt);
            }

            // Fallback for the opening seconds of a session, a car that has just
            // rejoined, or a gap deeper than the retained history. Uses the MEAN of
            // both cars' speeds so at least it is symmetric - the same answer
            // whichever car asks - rather than depending on one car's corner phase.
            float aheadSpeed = ahead.vehicle == null ? 0f : Mathf.Abs(ahead.vehicle.CurrentSpeedKph) / 3.6f;
            float behindSpeed = behind.vehicle == null ? 0f : Mathf.Abs(behind.vehicle.CurrentSpeedKph) / 3.6f;
            float referenceSpeed = Mathf.Max(24f, (aheadSpeed + behindSpeed) * 0.5f);
            return deltaMeters / referenceSpeed;
        }

        public string GapAheadText(RaceParticipant participant)
        {
            RaceParticipant ahead = FindCarAhead(participant, 9999f);
            if (ahead == null || participant == null || participant.lapTracker == null)
            {
                return "--";
            }

            return GapSecondsBetween(ahead, participant).ToString("0.0") + "s";
        }

        public float GetIntervalToAheadSeconds(RaceParticipant participant)
        {
            RaceParticipant ahead = FindCarAhead(participant, 220f);
            if (ahead == null || participant == null || participant.lapTracker == null)
            {
                return 999f;
            }

            return GapSecondsBetween(ahead, participant);
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
            // Signed, and symmetric: negating the argument order negates the result.
            return aDistance >= bDistance ? GapSecondsBetween(a, b) : -GapSecondsBetween(b, a);
        }

        /// <summary>
        /// How many laps <paramref name="behind"/> is down on <paramref name="ahead"/>,
        /// or 0 if on the same lap.
        ///
        /// Lapped status is a LAP-COUNT question, and the code used to answer it with
        /// a distance threshold (GapMath.IsLapDownGap: raw gap >= 92% of a lap). Those
        /// are different quantities and it was wrong in both directions - a car on the
        /// lead lap but 0.93 laps adrift was shown "+1L", while a car genuinely a lap
        /// down but running physically AHEAD of the leader showed a plain "+42.9s".
        /// RaceStateManager.GetCompletedLaps has the exact answer.
        /// </summary>
        int LapsDownBetween(RaceParticipant ahead, RaceParticipant behind)
        {
            if (State == null || ahead == null || behind == null)
            {
                return 0;
            }

            return Mathf.Max(0, State.GetCompletedLaps(ahead) - State.GetCompletedLaps(behind));
        }

        public string GapToLeaderText(RaceParticipant participant)
        {
            SortRunningOrder();
            if (participant == null || State == null || State.SortedOrder.Count == 0 || State.SortedOrder[0] == participant)
            {
                return "LEADER";
            }

            RaceParticipant leader = State.SortedOrder[0];
            int lapsDown = LapsDownBetween(leader, participant);
            if (lapsDown > 0)
            {
                return "+" + lapsDown + "L";
            }

            return "+" + GapSecondsBetween(leader, participant).ToString("0.0") + "s";
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
            int lapsDown = LapsDownBetween(ahead, participant);
            if (lapsDown > 0)
            {
                return "+" + lapsDown + "L";
            }

            return GapSecondsBetween(ahead, participant).ToString("0.0") + "s";
        }

        public string GapBehindText(RaceParticipant participant)
        {
            RaceParticipant behind = FindCarBehind(participant, 9999f);
            if (behind == null || participant == null || participant.lapTracker == null)
            {
                return "--";
            }

            return GapSecondsBetween(participant, behind).ToString("0.0") + "s";
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
