using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Per-car rolling history of (race time, total progress distance), so a gap
    /// between two cars can be answered the way a real timing screen answers it:
    /// "how long ago was the car ahead standing where I am now?"
    ///
    /// Why this exists. Every gap in the game used to be computed as
    /// <c>metres / oneCarsInstantaneousSpeed</c>. That is not a time gap. Speed
    /// varies roughly 4-5x around a lap (about 22 m/s in a hairpin against 83 m/s
    /// on a straight), so a physically constant 60m gap was reported as anything
    /// between ~0.7s and ~2.7s depending only on where the REFERENCE car happened
    /// to be at that instant. And because each call site picked a different
    /// reference car - the participant, the car behind, or the leader - the same
    /// pair of cars was given contradictory gaps simultaneously.
    ///
    /// The visible symptoms were: the whole timing tower's gap column pulsing every
    /// time the leader braked for a corner (every gap jumping ~3x and snapping back
    /// on the next straight, with no position changes); the HUD's "interval ahead"
    /// disagreeing with the tower's interval for the same two cars; and DRS
    /// eligibility - which is a 1.0s test through GetIntervalToAheadSeconds -
    /// depending on whether the detection point happened to sit before or after a
    /// braking zone.
    ///
    /// A time-based gap is stable, symmetric, and independent of where either car
    /// is on the lap.
    /// </summary>
    public sealed class RaceProgressHistory
    {
        // ~0.08s sampling over a 120s window: enough resolution for a sub-tenth
        // gap and enough depth to still resolve a car more than a lap down.
        const float SampleIntervalSeconds = 0.08f;
        const float HistoryWindowSeconds = 120f;
        // 120s / 0.08s, plus a little headroom. Spelled out rather than computed
        // so it stays an unambiguous integer constant.
        const int Capacity = 1508;

        sealed class CarHistory
        {
            public readonly float[] times = new float[Capacity];
            public readonly float[] distances = new float[Capacity];
            public int count;
            public int head = -1;      // index of the newest sample
            public float lastSampleTime = float.NegativeInfinity;

            public int Oldest => count < Capacity ? 0 : (head + 1) % Capacity;

            public void Add(float time, float distance)
            {
                head = count == 0 ? 0 : (head + 1) % Capacity;
                times[head] = time;
                distances[head] = distance;
                if (count < Capacity)
                {
                    count++;
                }
            }

            public void Clear()
            {
                count = 0;
                head = -1;
                lastSampleTime = float.NegativeInfinity;
            }
        }

        readonly Dictionary<RaceParticipant, CarHistory> tracks = new Dictionary<RaceParticipant, CarHistory>();

        public void Clear()
        {
            foreach (KeyValuePair<RaceParticipant, CarHistory> pair in tracks)
            {
                pair.Value.Clear();
            }

            tracks.Clear();
        }

        public void Sample(RaceParticipant participant, float raceTime, float totalProgressDistance)
        {
            if (participant == null)
            {
                return;
            }

            CarHistory track;
            if (!tracks.TryGetValue(participant, out track))
            {
                track = new CarHistory();
                tracks[participant] = track;
            }

            if (raceTime - track.lastSampleTime < SampleIntervalSeconds)
            {
                return;
            }

            track.lastSampleTime = raceTime;
            track.Add(raceTime, totalProgressDistance);
        }

        /// <summary>
        /// The race time at which <paramref name="participant"/> passed
        /// <paramref name="distance"/>, if that lies inside the retained history.
        /// Total progress distance is monotonically non-decreasing, so a simple
        /// scan with linear interpolation between the bracketing samples is exact
        /// to the sampling interval.
        /// </summary>
        public bool TryGetTimeAtDistance(RaceParticipant participant, float distance, out float time)
        {
            time = 0f;
            CarHistory track;
            if (participant == null || !tracks.TryGetValue(participant, out track) || track.count < 2)
            {
                return false;
            }

            int oldest = track.Oldest;
            float oldestDistance = track.distances[oldest];
            float newestDistance = track.distances[track.head];

            // Outside the retained window in either direction: the caller must fall
            // back rather than extrapolate.
            if (distance < oldestDistance || distance > newestDistance)
            {
                return false;
            }

            // Walk newest -> oldest and stop at the first sample at or before the
            // target distance; gaps of interest are almost always recent, so this
            // terminates after a handful of steps in practice.
            for (int step = 0; step < track.count - 1; step++)
            {
                int newer = (track.head - step + Capacity) % Capacity;
                int older = (newer - 1 + Capacity) % Capacity;
                if (track.distances[older] <= distance && distance <= track.distances[newer])
                {
                    float span = track.distances[newer] - track.distances[older];
                    float t = span > 0.0001f ? (distance - track.distances[older]) / span : 0f;
                    time = Mathf.Lerp(track.times[older], track.times[newer], t);
                    return true;
                }
            }

            return false;
        }
    }
}
