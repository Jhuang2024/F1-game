using System.Collections.Generic;

namespace F1Game.Core.Diagnostics
{
    /// <summary>
    /// Turns a captured <see cref="TelemetryRecorder"/> trace into a compact,
    /// human-readable engineer debrief (top/avg speed, throttle/brake/coast time
    /// share, DRS/ERS usage, tyre wear over the trace). Pure over the samples so
    /// it can be unit-tested and shown in-game without touching the race path.
    /// </summary>
    public static class TelemetryDebrief
    {
        // A throttle reading at or above this counts as "full throttle"; a brake
        // reading at or above this counts as "on the brakes". Between the two the
        // driver is coasting. The gap avoids double-counting a light dab of both.
        const float FullThrottleThreshold = 0.95f;
        const float OnBrakeThreshold = 0.05f;
        const float OnThrottleThreshold = 0.05f;

        public struct Summary
        {
            public int SampleCount;
            public float DurationSeconds;
            public float DistanceMeters;
            public float TopSpeedKph;
            public float AverageSpeedKph;
            public float FullThrottlePercent;
            public float BrakingPercent;
            public float CoastingPercent;
            public float DrsPercent;
            public float ErsAverage01;
            public float TyreWearStart01;
            public float TyreWearEnd01;
            public int TopGear;

            public bool HasData => SampleCount > 0;
            /// <summary>Tyre wear accrued across the captured trace (end minus start, clamped ≥0).</summary>
            public float TyreWearDelta01 => TyreWearEnd01 > TyreWearStart01 ? TyreWearEnd01 - TyreWearStart01 : 0f;
        }

        public static Summary Build(TelemetryRecorder recorder)
        {
            return recorder != null ? Build(recorder.Samples) : default;
        }

        public static Summary Build(IReadOnlyList<TelemetryRecorder.Sample> samples)
        {
            var summary = new Summary();
            if (samples == null || samples.Count == 0)
            {
                return summary;
            }

            int count = samples.Count;
            summary.SampleCount = count;

            float minTime = samples[0].Time;
            float maxTime = samples[0].Time;
            float minDistance = samples[0].Distance;
            float maxDistance = samples[0].Distance;
            float speedSum = 0f;
            float ersSum = 0f;
            int fullThrottle = 0;
            int braking = 0;
            int coasting = 0;
            int drs = 0;

            for (int i = 0; i < count; i++)
            {
                TelemetryRecorder.Sample s = samples[i];

                if (s.Time < minTime) minTime = s.Time;
                if (s.Time > maxTime) maxTime = s.Time;
                if (s.Distance < minDistance) minDistance = s.Distance;
                if (s.Distance > maxDistance) maxDistance = s.Distance;
                if (s.SpeedKph > summary.TopSpeedKph) summary.TopSpeedKph = s.SpeedKph;
                if (s.Gear > summary.TopGear) summary.TopGear = s.Gear;

                speedSum += s.SpeedKph;
                ersSum += s.Ers01;

                bool onBrake = s.Brake >= OnBrakeThreshold;
                if (onBrake)
                {
                    braking++;
                }
                else if (s.Throttle >= FullThrottleThreshold)
                {
                    fullThrottle++;
                }
                else if (s.Throttle < OnThrottleThreshold)
                {
                    coasting++;
                }

                if (s.Drs) drs++;
            }

            summary.DurationSeconds = maxTime > minTime ? maxTime - minTime : 0f;
            summary.DistanceMeters = maxDistance > minDistance ? maxDistance - minDistance : 0f;
            summary.AverageSpeedKph = speedSum / count;
            summary.ErsAverage01 = ersSum / count;
            summary.FullThrottlePercent = 100f * fullThrottle / count;
            summary.BrakingPercent = 100f * braking / count;
            summary.CoastingPercent = 100f * coasting / count;
            summary.DrsPercent = 100f * drs / count;
            summary.TyreWearStart01 = samples[0].TyreWear01;
            summary.TyreWearEnd01 = samples[count - 1].TyreWear01;

            return summary;
        }
    }
}
