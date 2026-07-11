using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager time-trial ghost + best-lap subsystem (partial). Records the
    /// player's best lap to the local records store, promotes/saves the ghost,
    /// samples and plays it back, and exposes the ghost delta
    /// (TryGetGhostDelta/GhostDeltaText - public, read by the relay/HUD). Split
    /// out of the monolith verbatim; behaviour and values unchanged.
    /// </summary>
    public partial class RaceManager
    {
        void TrackPlayerBestLapRecord()
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null || EventData == null)
            {
                return;
            }

            float best = PlayerParticipant.lapTracker.BestLapTime;
            if (best <= 0f || Mathf.Approximately(best, lastRecordedPlayerBestLap))
            {
                return;
            }

            lastRecordedPlayerBestLap = best;
            string context = IsTimeTrial ? "Time Trial" : (CurrentSession == RaceWeekendSession.Qualifying ? "Qualifying" : "Race");
            if (PlayerRecordsStore.TryRecordLap(EventData.trackId, best, context))
            {
                PostEngineerMessage("New local track record: " + UiFactory.FormatTime(best) + "!", true);
            }
            else if (IsTimeTrial)
            {
                PostEngineerMessage("Personal best this session: " + UiFactory.FormatTime(best) + ".", false);
            }

            PromoteGhostRecordingIfBest(best);
        }

        // Time-trial ghost: the buffer accumulated by RecordGhostSample IS the
        // lap that just produced this new best time (same-frame ordering -
        // both run from Update() after the same lapTracker.Tick() pass), so
        // it's promoted here rather than re-detecting the lap boundary a
        // second time. Session mode swaps the live ghost immediately so the
        // player is always chasing their own latest best; All-time mode only
        // ever touches the persistent store (loaded once at spawn) so the
        // on-track ghost stays a stable target for the whole session.
        void PromoteGhostRecordingIfBest(float lapTime)
        {
            if (!IsTimeTrial || EventData == null || ghostRecordingBuffer.Count < 2)
            {
                return;
            }

            GhostLapData candidate = new GhostLapData
            {
                trackId = EventData.trackId,
                lapTime = lapTime,
                samples = new List<GhostSample>(ghostRecordingBuffer)
            };

            int ghostMode = Settings != null ? Settings.Current.ghostMode : 0;
            if (ghostMode == 2)
            {
                TimeTrialGhostStore.TrySaveIfBest(EventData.trackId, candidate);
            }
            else if (ghostMode == 1 && ghostController != null)
            {
                ghostController.Initialize(candidate);
            }
        }

        // Called once per frame (not per participant - only the player is
        // recorded/played back) from Update(), gated on IsTimeTrial by both
        // callers below.
        void RecordGhostSample()
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null || PlayerParticipant.vehicle == null)
            {
                return;
            }

            int currentLap = PlayerParticipant.lapTracker.CompletedLaps;
            if (currentLap != ghostRecordedLapNumber)
            {
                ghostRecordedLapNumber = currentLap;
                ghostRecordingBuffer.Clear();
                ghostRecordTimer = 0f;
            }

            ghostRecordTimer -= Time.deltaTime;
            if (ghostRecordTimer > 0f || ghostRecordingBuffer.Count >= TimeTrialGhostStore.MaxSamplesPerGhost)
            {
                return;
            }

            ghostRecordTimer = GhostSampleInterval;
            ghostRecordingBuffer.Add(new GhostSample
            {
                elapsedSeconds = PlayerParticipant.lapTracker.CurrentLapTime,
                distanceAlongLap = PlayerParticipant.lapTracker.CurrentProgress.normalized,
                position = PlayerParticipant.transform.position,
                headingDegrees = PlayerParticipant.transform.eulerAngles.y,
                speedKph = PlayerParticipant.vehicle.CurrentSpeedKph
            });
        }

        void UpdateGhostPlayback()
        {
            if (ghostController == null || PlayerParticipant == null || PlayerParticipant.lapTracker == null)
            {
                return;
            }

            ghostController.UpdatePlayback(PlayerParticipant.lapTracker.CurrentLapTime);
        }

        // Spawned once, right after the player's own car, at Time Trial
        // session start - reuses CreateOpenWheelCar (the exact same geometry
        // every real car uses) so the ghost silhouette is instantly
        // recognisable, then strips it down to a purely visual, non-colliding
        // shell (kinematic Rigidbody, disabled Collider) and re-tints every
        // renderer through a single shared translucent material so it always
        // reads as a ghost regardless of team livery colours.
        void SpawnGhostIfAvailable()
        {
            if (!IsTimeTrial || Settings == null || Settings.Current.ghostMode == 0 || EventData == null)
            {
                return;
            }

            GhostLapData stored = Settings.Current.ghostMode == 2 ? TimeTrialGhostStore.GetBestGhost(EventData.trackId) : null;
            if (Settings.Current.ghostMode == 2 && stored == null)
            {
                return;
            }

            ghostCarObject = ProductionCarSpawner.SpawnCar("Ghost", new Color(0.3f, 0.62f, 1f), new Color(0.55f, 0.8f, 1f));
            ghostCarObject.name = "Ghost car";
            ghostCarObject.transform.SetParent(raceWorld.transform);
            Rigidbody body = ghostCarObject.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }

            Collider[] colliders = ghostCarObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            Material ghostMaterial = CreateTranslucentMaterial("Ghost car shell", new Color(0.3f, 0.62f, 1f), 0.4f);
            Renderer[] renderers = ghostCarObject.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = ghostMaterial;
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            ghostController = ghostCarObject.AddComponent<GhostCarController>();
            if (stored != null)
            {
                ghostController.Initialize(stored);
            }

            if (Track != null)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Track.SampleAtDistance(0f, out point, out forward, out right);
                ghostCarObject.transform.position = point + Vector3.up * 0.3f;
                ghostCarObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        // Standard shader in alpha-blended Fade mode - same recipe
        // TrackManager.CreateTranslucentMaterial already uses for the wet-
        // track sheen/mist banks, duplicated here rather than shared across
        // files since TrackManager and RaceManager don't otherwise share a
        // material-helper base class.
        Material CreateTranslucentMaterial(string materialName, Color color, float alpha)
        {
            Material material = F1Game.Rendering.ShaderCompat.CreateLitMaterial();
            material.name = materialName;
            material.color = new Color(color.r, color.g, color.b, alpha);
            F1Game.Rendering.ShaderCompat.MakeTransparentFade(material);
            return material;
        }

        // HUD delta readout for Time Trial, parallel to QualifyingDeltaText -
        // RaceHud's qualifying-card slot shows whichever of the two applies
        // (see RaceHud.UpdateTimingCard).
        // Numeric time-trial ghost delta (+ slower); the single source both the
        // legacy text readout and the production HUD delta module read.
        public bool TryGetGhostDelta(RaceParticipant participant, out float delta)
        {
            delta = 0f;
            if (!IsTimeTrial || ghostController == null || !ghostController.HasLap || participant == null || participant.lapTracker == null)
            {
                return false;
            }

            if (participant.lapTracker.OutLapActive)
            {
                return false;
            }

            float ghostTime = ghostController.GhostTimeAtDistance(participant.lapTracker.CurrentProgress.normalized);
            if (ghostTime < 0f)
            {
                return false;
            }

            delta = participant.lapTracker.CurrentLapTime - ghostTime;
            return true;
        }

        public string GhostDeltaText(RaceParticipant participant)
        {
            if (!TryGetGhostDelta(participant, out float delta))
            {
                return "--";
            }

            string color = delta <= 0f ? "#6CFF8D" : "#FF6C6C";
            return "<color=" + color + ">" + (delta >= 0f ? "+" : "") + delta.ToString("0.000") + "</color>";
        }
    }
}
