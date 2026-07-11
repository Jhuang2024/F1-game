using System.Collections.Generic;

namespace F1Game.Race
{
    /// <summary>
    /// Engine-free replay/broadcast auto-director: given a recording's marker track
    /// and the current playhead, decides which car a replay or broadcast camera
    /// should focus. Picks the most relevant nearby event (an incident/overtake/pit
    /// stop close to the playhead, highest-priority and then nearest in time),
    /// otherwise falls back to a supplied default car (e.g. the player or the
    /// leader). Pure decision logic - the camera transform/cut lives in the engine
    /// layer and just follows this focus, so the director is unit-testable.
    /// </summary>
    public static class ReplayDirector
    {
        /// <summary>Default window (s) around the playhead within which a marker can pull focus.</summary>
        public const float DefaultWindowSeconds = 2.5f;

        /// <summary>
        /// The car index a camera should focus at <paramref name="playheadTime"/>.
        /// A car-tagged marker (incident &gt; overtake &gt; pit stop &gt; lap/flag)
        /// within <paramref name="windowSeconds"/> of the playhead wins, choosing by
        /// priority then nearest time; with none, returns <paramref name="defaultCarIndex"/>.
        /// </summary>
        public static int FocusCarIndex(ReplayRecording recording, float playheadTime, int defaultCarIndex,
            float windowSeconds = DefaultWindowSeconds)
        {
            if (recording == null)
            {
                return defaultCarIndex;
            }

            IReadOnlyList<ReplayRecording.Marker> markers = recording.Markers;
            if (markers == null || markers.Count == 0 || windowSeconds <= 0f)
            {
                return defaultCarIndex;
            }

            int carCount = recording.CarCount;
            int bestCar = -1;
            int bestPriority = int.MinValue;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < markers.Count; i++)
            {
                ReplayRecording.Marker m = markers[i];
                if (m.CarIndex < 0 || m.CarIndex >= carCount)
                {
                    continue; // field-wide marker (e.g. a flag) can't pull focus to a car
                }

                float distance = m.Time - playheadTime;
                if (distance < 0f)
                {
                    distance = -distance;
                }

                if (distance > windowSeconds)
                {
                    continue;
                }

                int priority = Priority(m.Kind);
                // Higher priority wins; within the same priority, the nearer marker.
                if (priority > bestPriority || (priority == bestPriority && distance < bestDistance))
                {
                    bestPriority = priority;
                    bestDistance = distance;
                    bestCar = m.CarIndex;
                }
            }

            return bestCar >= 0 ? bestCar : defaultCarIndex;
        }

        static int Priority(ReplayRecording.MarkerKind kind)
        {
            switch (kind)
            {
                case ReplayRecording.MarkerKind.Incident: return 4;
                case ReplayRecording.MarkerKind.Overtake: return 3;
                case ReplayRecording.MarkerKind.PitStop: return 2;
                case ReplayRecording.MarkerKind.LapComplete: return 1;
                default: return 0; // Flag / SessionStart / SessionEnd
            }
        }
    }
}
