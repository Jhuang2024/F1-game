using System;
using System.Collections.Generic;
using F1Game.Race;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Session integration + lifecycle owner for the non-live camera modes (replay,
    /// spectator, broadcast, photo). It attaches the individual controllers, holds the
    /// single active mode, and guarantees exactly one of them drives the output camera
    /// at a time - entering a mode activates its controller and deactivates the rest,
    /// and while any cinematic mode is active the live camera rig is suppressed via a
    /// host callback so there is never a second writer on the camera transform.
    /// Returning to <see cref="Mode.Live"/> hands ownership straight back.
    ///
    /// Default-off (<c>f1game_cinematic_director</c>) and inert until Configure().
    /// It creates no state in the race loop and mutates nothing but its own camera,
    /// so the live path is unchanged. VISUAL VALIDATION PENDING (in-editor run).
    /// </summary>
    public sealed class CinematicDirector : MonoBehaviour
    {
        public const string EnabledKey = "f1game_cinematic_director";

        public static bool FeatureEnabled => PlayerPrefs.GetInt(EnabledKey, 0) == 1;

        public static void SetFeatureEnabled(bool value)
        {
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public enum Mode
        {
            Live,
            Replay,
            Spectator,
            Broadcast,
            Photo,
        }

        ReplayCameraController replay;
        SpectatorCameraController spectator;
        BroadcastCameraController broadcast;
        PhotoModeController photo;
        ReplayScrubberUi scrubber;
        CinematicHud hud;

        Transform hudCanvas;
        Action<bool> liveCameraEnabledSink;

        Mode current = Mode.Live;
        bool configured;
        // Replay repositions the car transforms to recorded poses, which would corrupt
        // an actively-simulating race - so a live-race host disables it and only the
        // camera-only modes (spectator/broadcast/photo) are reachable. Default on for
        // dedicated replay-viewing hosts.
        bool replayAllowed = true;

        // Keys that cycle modes / return to live when no HUD bar is present.
        [SerializeField] KeyCode cycleKey = KeyCode.F8;
        [SerializeField] KeyCode liveKey = KeyCode.F7;

        public Mode CurrentMode => current;
        public bool IsCinematic => current != Mode.Live;

        /// <summary>Allow/forbid replay mode. A live race forbids it (replay moves cars).</summary>
        public void SetReplayAllowed(bool allowed)
        {
            replayAllowed = allowed;
            if (!allowed && current == Mode.Replay)
            {
                ReturnToLive();
            }
        }

        /// <summary>
        /// Wire the director to a session. <paramref name="cars"/> are the per-index car
        /// Transforms; <paramref name="recording"/> feeds replay + the broadcast director;
        /// <paramref name="broadcastCameras"/> are optional authored trackside cameras;
        /// <paramref name="liveCameraSink"/> lets the host disable the live camera rig
        /// while a cinematic mode owns the camera; <paramref name="hudSink"/> hides the
        /// HUD for photo mode. Safe to call with nulls (stays inert).
        /// </summary>
        public void Configure(
            IReadOnlyList<Transform> cars,
            Camera camera,
            int defaultCarIndex,
            ReplayRecording recording,
            IReadOnlyList<Transform> broadcastCameras = null,
            Transform hudCanvasRoot = null,
            Action<bool> liveCameraSink = null,
            Action<bool> hudSink = null)
        {
            hudCanvas = hudCanvasRoot;
            liveCameraEnabledSink = liveCameraSink;

            replay = GetOrAdd(ref replay);
            spectator = GetOrAdd(ref spectator);
            broadcast = GetOrAdd(ref broadcast);
            photo = GetOrAdd(ref photo);

            replay.Configure(recording, cars, camera, defaultCarIndex);
            spectator.Configure(cars, camera, defaultCarIndex);
            broadcast.Configure(cars, camera, defaultCarIndex, broadcastCameras, recording);
            photo.Configure(camera, hudSink);

            current = Mode.Live;
            configured = true;

            // The always-on control bar (mode buttons). Null when no HUD canvas or the
            // feature is off, in which case modes are driven programmatically only.
            if (hud == null && hudCanvas != null)
            {
                hud = CinematicHud.Create(hudCanvas, this);
            }
        }

        T GetOrAdd<T>(ref T field) where T : Component
        {
            if (field == null)
            {
                field = gameObject.AddComponent<T>();
            }

            return field;
        }

        /// <summary>Switch modes: deactivate the current owner, activate the new one.</summary>
        public void SetMode(Mode mode)
        {
            if (!configured || mode == current)
            {
                return;
            }

            // Replay is refused while disallowed (live race) so it can never reposition
            // simulating cars.
            if (mode == Mode.Replay && !replayAllowed)
            {
                return;
            }

            Exit(current);
            current = mode;
            Enter(mode);
        }

        public void ReturnToLive() => SetMode(Mode.Live);

        /// <summary>Advance to the next available mode (skips replay when it is disallowed).</summary>
        public void CycleMode()
        {
            if (!configured)
            {
                return;
            }

            int next = (int)current;
            for (int step = 0; step < 5; step++)
            {
                next = (next + 1) % 5;
                var candidate = (Mode)next;
                if (candidate == Mode.Replay && !replayAllowed)
                {
                    continue;
                }

                SetMode(candidate);
                return;
            }
        }

        void Update()
        {
            // Keyboard entry when no HUD control bar is wired. Only switches the camera
            // owner - it never touches race state.
            if (!configured)
            {
                return;
            }

            if (Input.GetKeyDown(cycleKey))
            {
                CycleMode();
            }
            else if (Input.GetKeyDown(liveKey))
            {
                ReturnToLive();
            }
        }

        void Enter(Mode mode)
        {
            // The live rig only owns the camera in Live mode.
            liveCameraEnabledSink?.Invoke(mode == Mode.Live);

            switch (mode)
            {
                case Mode.Replay:
                    replay.Activate();
                    EnsureScrubber();
                    break;
                case Mode.Spectator:
                    spectator.Activate();
                    break;
                case Mode.Broadcast:
                    broadcast.Activate();
                    break;
                case Mode.Photo:
                    photo.Enter();
                    break;
            }
        }

        void Exit(Mode mode)
        {
            switch (mode)
            {
                case Mode.Replay:
                    replay.Deactivate();
                    break;
                case Mode.Spectator:
                    spectator.Deactivate();
                    break;
                case Mode.Broadcast:
                    broadcast.Deactivate();
                    break;
                case Mode.Photo:
                    photo.Exit();
                    break;
            }
        }

        void EnsureScrubber()
        {
            if (scrubber == null && hudCanvas != null)
            {
                scrubber = ReplayScrubberUi.Create(hudCanvas, replay);
            }
        }

        void OnDisable()
        {
            // Tearing down mid-cinematic returns camera + HUD ownership to the live path.
            if (current != Mode.Live)
            {
                Exit(current);
                current = Mode.Live;
                liveCameraEnabledSink?.Invoke(true);
            }
        }
    }
}
