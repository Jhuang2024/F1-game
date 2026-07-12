using F1Game.Core.Diagnostics;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Live player telemetry capture (Phase K "engineer debrief" / CSV export).
    /// Records the player car's input and state channels into the engine-free
    /// <see cref="TelemetryRecorder"/> at a fixed capture rate, so a lap or a full
    /// session can be exported and reviewed offline. Read-only over the player's
    /// vehicle/lap tracker, bounded by the recorder's sample cap, and gated by a
    /// feature flag so it never touches the race path when disabled. RaceManager
    /// owns one and drives Begin/Sample/ExportCsv.
    /// </summary>
    public sealed class TelemetryCaptureService
    {
        const string EnabledKey = "f1game_telemetry_capture";
        const float CaptureHz = 20f;
        const float CaptureInterval = 1f / CaptureHz;
        // ~15 minutes of player telemetry at the capture rate before the cap is
        // reached; the recorder stops appending past the cap (it does not wrap),
        // which keeps a clean lap-by-lap trace from the session start.
        const int CapacityFrames = 18000;

        TelemetryRecorder recorder;
        float nextCaptureTime;
        // Running brake-disc temperature estimate (structural BrakeThermalModel),
        // stepped at each capture. Diagnostic/telemetry channel only - it is never read
        // back into the live brake torque or glow, so tuned feel is untouched.
        float brakeDiscTempC;

        public TelemetryRecorder Recorder => recorder;
        public bool IsCapturing => recorder != null;
        public int SampleCount => recorder != null ? recorder.Count : 0;

        public static bool CaptureEnabled => PlayerPrefs.GetInt(EnabledKey, 1) == 1;

        public static void SetCaptureEnabled(bool value)
        {
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Start a fresh recording; no-op when disabled.</summary>
        public void Begin()
        {
            recorder = null;
            nextCaptureTime = 0f;
            brakeDiscTempC = F1Game.Race.Physics.BrakeThermalModel.AmbientC;

            if (!CaptureEnabled)
            {
                return;
            }

            recorder = new TelemetryRecorder(CapacityFrames);
        }

        /// <summary>
        /// Capture one player sample if the interval has elapsed. Call every racing
        /// tick; <paramref name="deltaSeconds"/> is the live HUD delta (qualifying
        /// or ghost) when one applies, otherwise 0.
        /// </summary>
        public void Sample(RaceParticipant player, float raceElapsed, float deltaSeconds)
        {
            if (recorder == null || player == null || player.vehicle == null || player.lapTracker == null)
            {
                return;
            }

            if (raceElapsed < nextCaptureTime)
            {
                return;
            }

            nextCaptureTime = raceElapsed + CaptureInterval;

            VehicleController vehicle = player.vehicle;
            float speedKph = Mathf.Abs(vehicle.CurrentSpeedKph);
            float topSpeed = vehicle.TargetTopSpeedKph;
            float rpm01 = topSpeed > 1f ? Mathf.Clamp01(speedKph / topSpeed) : 0f;
            float tyreWear01 = vehicle.Tyres != null ? Mathf.Clamp01(1f - vehicle.Tyres.Wear) : 0f;

            // Advance the structural brake-thermal estimate at the fixed capture interval
            // (the per-frame brake trace isn't captured, so the capture rate is the model
            // dt). Presentation/diagnostic only - the result is recorded, never fed back
            // into the live brake model or glow.
            brakeDiscTempC = F1Game.Race.Physics.BrakeThermalModel.Step(
                brakeDiscTempC, vehicle.EffectiveBrake, speedKph, CaptureInterval);

            var sample = new TelemetryRecorder.Sample
            {
                Time = raceElapsed,
                Distance = player.lapTracker.CurrentProgress.distance,
                SpeedKph = speedKph,
                Throttle = vehicle.EffectiveThrottle,
                Brake = vehicle.EffectiveBrake,
                Steer = vehicle.LastSteerInput,
                Gear = vehicle.CurrentGear,
                Rpm01 = rpm01,
                Ers01 = Mathf.Clamp01(vehicle.ErsBattery),
                Drs = vehicle.DrsActive,
                TyreWear01 = tyreWear01,
                DeltaSeconds = deltaSeconds,
                BrakeDiscTempC = brakeDiscTempC,
            };

            recorder.Record(in sample);
        }

        /// <summary>Export the captured trace to persistentDataPath; returns the path or null.</summary>
        public string ExportCsv(string fileName)
        {
            return recorder != null ? recorder.ExportCsv(fileName) : null;
        }

        public void Clear()
        {
            recorder = null;
        }
    }
}
