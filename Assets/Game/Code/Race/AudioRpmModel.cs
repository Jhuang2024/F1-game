using System.Collections.Generic;

namespace F1Game.Race.Physics
{
    /// <summary>
    /// Audio-only normalized engine RPM reconstructed from the car's live speed and the
    /// authoritative auto-shift schedule (VehicleController.AutoShiftUpKph). It reports
    /// where the engine sits within the current gear's speed band - ~0 just after an
    /// upshift, ~1 at the top just before the next - so a rev-limiter cue and richer
    /// engine audio can key off a real per-gear RPM shape instead of a flat speed ramp.
    ///
    /// PRESENTATION ONLY. The shift schedule is read, never written; nothing here feeds
    /// powertrain, acceleration, gear changes, top speed or any race/RNG state, so
    /// handling and tuned feel are completely unchanged. Engine-free (no UnityEngine),
    /// pure functions + a deterministic gate, so it is unit-testable without the engine.
    /// </summary>
    public static class AudioRpmModel
    {
        // Rev-limiter cue gating (audio only).
        public const float LimiterEnter = 0.97f;        // rpm at/above -> sitting on the limiter
        public const float LimiterExit = 0.90f;         // rpm must fall below to re-arm (hysteresis)
        public const float LimiterDwellSeconds = 0.35f; // sustained time on the limiter before the cue fires

        /// <summary>
        /// Normalized RPM in [0,1] for a speed (kph) and 1-based <paramref name="gear"/>,
        /// using the per-gear upshift band from <paramref name="upshiftSchedule"/> (index
        /// g = the speed at which gear g upshifts; index 0 = 0). The top gear (no next
        /// entry) extrapolates one band width. Returns 0 on a missing/degenerate schedule
        /// so a caller can never divide by zero or read out of range.
        /// </summary>
        public static float NormalizedRpm(float speedKph, int gear, IReadOnlyList<float> upshiftSchedule)
        {
            if (upshiftSchedule == null || upshiftSchedule.Count < 2)
            {
                return 0f;
            }

            int count = upshiftSchedule.Count;
            if (gear < 1)
            {
                gear = 1;
            }

            int lowerIndex = gear - 1;
            if (lowerIndex > count - 1)
            {
                lowerIndex = count - 1;
            }

            float lower = upshiftSchedule[lowerIndex];
            float upper;
            if (gear < count)
            {
                upper = upshiftSchedule[gear];
            }
            else
            {
                // Top gear: no next threshold, so extrapolate one band beyond the last.
                float lastBand = upshiftSchedule[count - 1] - upshiftSchedule[count - 2];
                if (lastBand <= 0f)
                {
                    lastBand = upshiftSchedule[count - 1] * 0.15f + 1f;
                }

                upper = upshiftSchedule[count - 1] + lastBand;
            }

            if (upper <= lower)
            {
                return 0f;
            }

            float t = (speedKph - lower) / (upper - lower);
            if (t < 0f)
            {
                return 0f;
            }

            return t > 1f ? 1f : t;
        }
    }

    /// <summary>
    /// Edge-gated hysteresis+dwell rev-limiter detector for the audio cue. Fires exactly
    /// once per sustained engagement: RPM must sit at/above <see cref="AudioRpmModel.LimiterEnter"/>
    /// continuously for <see cref="AudioRpmModel.LimiterDwellSeconds"/> (so the momentary
    /// touch during a normal upshift never fires), and must fall below
    /// <see cref="AudioRpmModel.LimiterExit"/> to re-arm. Pure, deterministic in the
    /// (rpm, dt) sequence, engine-free.
    /// </summary>
    public sealed class RevLimiterGate
    {
        bool engaged;
        float dwell;

        /// <summary>True while the limiter is currently engaged (latched until RPM drops below exit).</summary>
        public bool Engaged => engaged;

        /// <summary>Advance the gate; returns true on the single frame the limiter engages.</summary>
        public bool Update(float rpm01, float deltaSeconds)
        {
            if (deltaSeconds < 0f)
            {
                deltaSeconds = 0f;
            }

            bool fire = false;
            if (rpm01 >= AudioRpmModel.LimiterEnter)
            {
                dwell += deltaSeconds;
                if (!engaged && dwell >= AudioRpmModel.LimiterDwellSeconds)
                {
                    engaged = true;
                    fire = true;
                }
            }
            else
            {
                // Any dip below the enter threshold breaks the sustained dwell.
                dwell = 0f;
                if (rpm01 <= AudioRpmModel.LimiterExit)
                {
                    engaged = false;
                }
            }

            return fire;
        }

        /// <summary>Clear all state (e.g. on session start / car swap).</summary>
        public void Reset()
        {
            engaged = false;
            dwell = 0f;
        }
    }
}
