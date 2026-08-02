using System.Collections.Generic;
using F1Game.Race;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Live full-session replay capture (Phase E service / Phase K "full-session
    /// recording"). Records per-frame car transforms into the engine-free
    /// <see cref="ReplayRecording"/> ring buffer at a fixed capture rate, plus
    /// markers (session start/end, flags, incidents, laps) for the timeline.
    /// Read-only over the live cars, bounded memory, and gated by a feature flag
    /// so it never affects the race path when disabled. RaceManager owns one and
    /// drives Begin/Tick/marker/End; a future replay playback system reads
    /// <see cref="Recording"/>.
    /// </summary>
    public sealed class ReplayCaptureService
    {
        const string EnabledKey = "f1game_replay_capture";
        const float CaptureHz = 20f;
        const float CaptureInterval = 1f / CaptureHz;
        // Bounded window: ~5 minutes at the capture rate; the oldest frames are
        // overwritten once full (a long race keeps the most recent action).
        const int CapacityFrames = 6000;

        ReplayRecording recording;
        ReplayRecording.CarFrame[] scratch;
        float nextCaptureTime;

        public ReplayRecording Recording => recording;
        public bool IsCapturing => recording != null;

        public static bool CaptureEnabled => PlayerPrefs.GetInt(EnabledKey, 1) == 1;

        public static void SetCaptureEnabled(bool value)
        {
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Start a fresh recording sized to the field; no-op when disabled.</summary>
        public void Begin(IReadOnlyList<RaceParticipant> participants, float raceElapsed)
        {
            recording = null;
            scratch = null;
            nextCaptureTime = 0f;

            if (!CaptureEnabled || participants == null || participants.Count == 0)
            {
                return;
            }

            int carCount = participants.Count;
            recording = new ReplayRecording(carCount, CapacityFrames);
            scratch = new ReplayRecording.CarFrame[carCount];
            recording.AddMarker(raceElapsed, ReplayRecording.MarkerKind.SessionStart, -1, "Session start");
        }

        /// <summary>Capture one frame if the interval has elapsed (call every tick while racing).</summary>
        public void Tick(IReadOnlyList<RaceParticipant> participants, float raceElapsed)
        {
            if (recording == null || participants == null || scratch == null)
            {
                return;
            }

            if (raceElapsed < nextCaptureTime)
            {
                return;
            }

            nextCaptureTime = raceElapsed + CaptureInterval;

            int carCount = scratch.Length;
            for (int i = 0; i < carCount; i++)
            {
                RaceParticipant p = i < participants.Count ? participants[i] : null;
                if (p == null || p.vehicle == null)
                {
                    scratch[i] = default;
                    continue;
                }

                Transform t = p.transform;
                Vector3 pos = t.position;
                Quaternion rot = t.rotation;
                scratch[i] = new ReplayRecording.CarFrame
                {
                    PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                    RotX = rot.x, RotY = rot.y, RotZ = rot.z, RotW = rot.w,
                    SpeedKph = p.vehicle.CurrentSpeedKph,
                };
            }

            recording.RecordFrame(raceElapsed, scratch);
        }

        public void AddFlagMarker(float raceElapsed, string label)
        {
            recording?.AddMarker(raceElapsed, ReplayRecording.MarkerKind.Flag, -1, label);
        }

        public void AddIncidentMarker(float raceElapsed, int carIndex, string label)
        {
            recording?.AddMarker(raceElapsed, ReplayRecording.MarkerKind.Incident, carIndex, label);
        }

        /// <summary>
        /// An overtake. Nothing ever created one of these, so the replay timeline's
        /// OvertakeCount was permanently zero - and that count is what the results
        /// screen prints, so every race reported "0 overtakes" directly above a
        /// highlight list of the passes that actually happened. It also made the
        /// auto-director's overtake priority unreachable.
        /// </summary>
        public void AddOvertakeMarker(float raceElapsed, int carIndex, string label)
        {
            recording?.AddMarker(raceElapsed, ReplayRecording.MarkerKind.Overtake, carIndex, label);
        }

        public void AddLapMarker(float raceElapsed, int carIndex, string label)
        {
            recording?.AddMarker(raceElapsed, ReplayRecording.MarkerKind.LapComplete, carIndex, label);
        }

        public void AddPitMarker(float raceElapsed, int carIndex, string label)
        {
            recording?.AddMarker(raceElapsed, ReplayRecording.MarkerKind.PitStop, carIndex, label);
        }

        public void End(float raceElapsed)
        {
            recording?.AddMarker(raceElapsed, ReplayRecording.MarkerKind.SessionEnd, -1, "Session end");
        }

        public void Clear()
        {
            recording = null;
            scratch = null;
        }
    }
}
