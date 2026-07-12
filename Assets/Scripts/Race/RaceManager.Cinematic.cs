using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager cinematic-camera session integration (partial). Wires the default-off
    /// CinematicDirector into the real race-session lifecycle: it is created as a child
    /// of raceWorld after the grid and player camera exist, so it is destroyed
    /// deterministically with the world on CleanupRaceWorld (no leaked cameras,
    /// listeners or capture state across repeated sessions). The director suppresses the
    /// gameplay CameraRig while a cinematic mode owns the camera and restores it on exit
    /// or teardown (exactly one camera writer at a time). Replay mode is FORBIDDEN during
    /// a live race (it would reposition the simulating cars) - only the camera-only
    /// spectator/broadcast/photo modes are reachable here. Feature-gated
    /// (f1game_cinematic_director, default off) with an automatic no-op fallback: any
    /// setup failure leaves the gameplay camera untouched.
    /// </summary>
    public partial class RaceManager
    {
        CinematicDirector cinematicDirector;

        // Called at the end of a session start, once the grid + player camera exist.
        void SetupCinematicDirector()
        {
            if (!CinematicDirector.FeatureEnabled || raceWorld == null || playerCameraRig == null)
            {
                return;
            }

            try
            {
                Camera outputCamera = playerCameraRig.Camera;
                if (outputCamera == null)
                {
                    return;
                }

                var cars = new List<Transform>(Participants.Count);
                int playerIndex = 0;
                for (int i = 0; i < Participants.Count; i++)
                {
                    RaceParticipant p = Participants[i];
                    cars.Add(p != null && p.vehicle != null ? p.vehicle.transform : null);
                    if (p != null && p == PlayerParticipant)
                    {
                        playerIndex = i;
                    }
                }

                var go = new GameObject("Cinematic director");
                go.transform.SetParent(raceWorld.transform);
                cinematicDirector = go.AddComponent<CinematicDirector>();
                // Live cars: only camera-only modes. No HUD canvas is passed, so the
                // spectator/broadcast/photo modes are driven by the director's keys.
                cinematicDirector.Configure(cars, outputCamera, playerIndex, ReplayRecording,
                    null, null, SetLiveCameraEnabled, null);
                cinematicDirector.SetReplayAllowed(false);
            }
            catch (System.Exception)
            {
                // Automatic fallback: any failure leaves the gameplay camera as-is.
                TeardownCinematicDirector();
            }
        }

        // The single camera-writer guarantee: the gameplay rig only drives the camera in
        // Live mode; a cinematic mode disables it, exit re-enables it.
        void SetLiveCameraEnabled(bool enabled)
        {
            if (playerCameraRig != null)
            {
                playerCameraRig.enabled = enabled;
            }
        }

        // Deterministic restore + no-leak teardown, called before raceWorld is destroyed.
        void TeardownCinematicDirector()
        {
            if (cinematicDirector != null)
            {
                cinematicDirector.ReturnToLive();
            }

            cinematicDirector = null;
        }
    }
}
