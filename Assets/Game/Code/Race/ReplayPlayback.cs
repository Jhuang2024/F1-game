namespace F1Game.Race
{
    /// <summary>
    /// Engine-free replay playback sampling over a <see cref="ReplayRecording"/>:
    /// the interpolated car transform at an arbitrary scrub time, plus the
    /// normalized-playhead conversions a scrub bar needs. The recording owns the
    /// raw frames and the frame seek; this owns the between-frames interpolation
    /// (position/speed linear, rotation normalized-lerp along the shortest arc) so a
    /// future replay camera / scrub UI renders smooth motion without re-deriving it.
    /// Pure data maths, no engine dependency - unit-testable in isolation.
    /// </summary>
    public static class ReplayPlayback
    {
        /// <summary>
        /// The car's transform at <paramref name="time"/>, interpolated between the
        /// two recorded frames that bracket it. Time is clamped to the recording's
        /// [StartTime, EndTime]; an empty recording or an out-of-range car returns
        /// default. Position and speed lerp; rotation uses a shortest-arc
        /// normalized-lerp (nlerp) so the quaternion stays unit-length.
        /// </summary>
        public static ReplayRecording.CarFrame SampleCar(ReplayRecording recording, float time, int carIndex)
        {
            if (recording == null || recording.FrameCount == 0 || carIndex < 0 || carIndex >= recording.CarCount)
            {
                return default;
            }

            float start = recording.StartTime;
            float end = recording.EndTime;
            if (time <= start)
            {
                time = start;
            }
            else if (time >= end)
            {
                time = end;
            }

            int lowerIndex = recording.SeekToTime(time);
            if (!recording.TryGetFrame(lowerIndex, out ReplayRecording.FrameHeader lowerHeader))
            {
                return default;
            }

            ReplayRecording.CarFrame lower = recording.GetCar(lowerHeader, carIndex);

            // Last frame (or no later frame): nothing to interpolate toward.
            if (lowerIndex + 1 >= recording.FrameCount ||
                !recording.TryGetFrame(lowerIndex + 1, out ReplayRecording.FrameHeader upperHeader))
            {
                return lower;
            }

            float span = upperHeader.Time - lowerHeader.Time;
            if (span <= 0.0000001f)
            {
                return lower;
            }

            float t = (time - lowerHeader.Time) / span;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);

            ReplayRecording.CarFrame upper = recording.GetCar(upperHeader, carIndex);
            return Interpolate(lower, upper, t);
        }

        /// <summary>Where <paramref name="time"/> sits on a 0-1 scrub bar across the recording.</summary>
        public static float NormalizedTime(ReplayRecording recording, float time)
        {
            if (recording == null || recording.FrameCount == 0)
            {
                return 0f;
            }

            float start = recording.StartTime;
            float duration = recording.EndTime - start;
            if (duration <= 0.0000001f)
            {
                return 0f;
            }

            float t = (time - start) / duration;
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }

        /// <summary>The absolute time for a 0-1 scrub position across the recording.</summary>
        public static float TimeForNormalized(ReplayRecording recording, float normalized)
        {
            if (recording == null || recording.FrameCount == 0)
            {
                return 0f;
            }

            float clamped = normalized < 0f ? 0f : (normalized > 1f ? 1f : normalized);
            float start = recording.StartTime;
            return start + clamped * (recording.EndTime - start);
        }

        static ReplayRecording.CarFrame Interpolate(in ReplayRecording.CarFrame a, in ReplayRecording.CarFrame b, float t)
        {
            ReplayRecording.CarFrame result;
            result.PosX = a.PosX + (b.PosX - a.PosX) * t;
            result.PosY = a.PosY + (b.PosY - a.PosY) * t;
            result.PosZ = a.PosZ + (b.PosZ - a.PosZ) * t;
            result.SpeedKph = a.SpeedKph + (b.SpeedKph - a.SpeedKph) * t;

            // Shortest-arc normalized-lerp for the rotation quaternion: take the
            // shorter of the two arcs (negate one end if the dot is negative), lerp
            // the components, then renormalize so the result stays unit-length.
            float bx = b.RotX, by = b.RotY, bz = b.RotZ, bw = b.RotW;
            float dot = a.RotX * bx + a.RotY * by + a.RotZ * bz + a.RotW * bw;
            if (dot < 0f)
            {
                bx = -bx; by = -by; bz = -bz; bw = -bw;
            }

            float rx = a.RotX + (bx - a.RotX) * t;
            float ry = a.RotY + (by - a.RotY) * t;
            float rz = a.RotZ + (bz - a.RotZ) * t;
            float rw = a.RotW + (bw - a.RotW) * t;

            float mag = (float)System.Math.Sqrt(rx * rx + ry * ry + rz * rz + rw * rw);
            if (mag > 0.0000001f)
            {
                float inv = 1f / mag;
                rx *= inv; ry *= inv; rz *= inv; rw *= inv;
            }
            else
            {
                // Degenerate (opposite quaternions cancelling): fall back to the
                // start orientation rather than emitting a zero quaternion.
                rx = a.RotX; ry = a.RotY; rz = a.RotZ; rw = a.RotW;
            }

            result.RotX = rx; result.RotY = ry; result.RotZ = rz; result.RotW = rw;
            return result;
        }
    }
}
