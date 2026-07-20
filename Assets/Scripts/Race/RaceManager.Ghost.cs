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
            // Read the SNAPSHOT of the just-finished lap (ghostLastLapBuffer), not
            // the live ghostRecordingBuffer: RecordGhostSample already cleared the
            // live buffer for the new lap earlier this frame (it runs before this),
            // so the completed lap's samples live only in the snapshot now.
            if (!IsTimeTrial || EventData == null || ghostLastLapBuffer.Count < 2)
            {
                return;
            }

            // STORE the faithful lap - real (untrimmed) lapTime and samples - so the
            // "is this a new best?" comparison is always official-time vs
            // official-time. Trimming only happens for PLAYBACK (below and on load),
            // so a lap that merely lacks a standing start can never masquerade as
            // faster than a genuinely quicker lap that had one.
            GhostLapData storeCandidate = new GhostLapData
            {
                trackId = EventData.trackId,
                lapTime = lapTime,
                samples = new List<GhostSample>(ghostLastLapBuffer)
            };

            // PLAYBACK copy: standing start trimmed, times rebased so it launches at
            // t=0 (see TrimGhostStandingStart).
            List<GhostSample> playbackSamples = TrimGhostStandingStart(ghostLastLapBuffer);
            if (playbackSamples.Count < 2)
            {
                return;
            }

            GhostLapData playbackCandidate = new GhostLapData
            {
                trackId = EventData.trackId,
                lapTime = playbackSamples[playbackSamples.Count - 1].elapsedSeconds,
                samples = playbackSamples
            };

            int ghostMode = Settings != null ? Settings.Current.ghostMode : 0;
            if (ghostMode == 2)
            {
                // All-time mode: the on-track ghost should always BE the all-time
                // best (per report - "the ghost should be my all time best... and
                // it's not"). TrySaveIfBest returns true only when this lap beat the
                // stored best, i.e. it IS the new all-time best - so swap the live
                // ghost to it then. Also adopt when the ghost has no lap yet (first
                // lap on a fresh track). A slower lap leaves the better stored ghost
                // in place. This replaces the old "stable target, never swap"
                // behaviour, which left a stale lap on track after the player
                // improved.
                bool isNewAllTimeBest = TimeTrialGhostStore.TrySaveIfBest(EventData.trackId, storeCandidate);
                if (ghostController != null && (isNewAllTimeBest || !ghostController.HasLap))
                {
                    ghostController.Initialize(playbackCandidate);
                }
            }
            else if (ghostMode == 1 && ghostController != null)
            {
                ghostController.Initialize(playbackCandidate);
            }
        }

        // A time-trial lap can begin with the car sitting at the line while the
        // clock already runs (per report - "the ghost exists but doesn't move":
        // the recorded lap had ~9s of norm ~0.97 stationary at the start, so the
        // ghost faithfully parked at the line for the first 9s of every replay and
        // read as broken). Drop the leading samples up to the moment the car is
        // genuinely moving, then rebase every remaining sample's elapsed time so
        // the launch sits at t=0 - the ghost now pulls away from the line
        // immediately, and playback (driven by the player's own lap time, also
        // measured from the line) stays in sync. Falls back to the untrimmed lap
        // if trimming would leave too little to play.
        List<GhostSample> TrimGhostStandingStart(List<GhostSample> source)
        {
            const float MovingSpeedKph = 20f;
            int firstMoving = 0;
            while (firstMoving < source.Count && Mathf.Abs(source[firstMoving].speedKph) < MovingSpeedKph)
            {
                firstMoving++;
            }

            // Keep one sample of lead-in so the launch isn't clipped mid-motion,
            // and never trim away the whole lap.
            firstMoving = Mathf.Clamp(firstMoving - 1, 0, source.Count - 1);
            if (source.Count - firstMoving < 2)
            {
                firstMoving = 0;
            }

            float baseTime = source[firstMoving].elapsedSeconds;
            List<GhostSample> trimmed = new List<GhostSample>(source.Count - firstMoving);
            for (int i = firstMoving; i < source.Count; i++)
            {
                GhostSample s = source[i];
                trimmed.Add(new GhostSample
                {
                    elapsedSeconds = s.elapsedSeconds - baseTime,
                    distanceAlongLap = s.distanceAlongLap,
                    position = s.position,
                    headingDegrees = s.headingDegrees,
                    speedKph = s.speedKph
                });
            }

            return trimmed;
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
                // Snapshot the lap that just finished BEFORE clearing, so the
                // promotion pass (TrackPlayerBestLapRecord, which runs later this
                // same frame in Update) still has its samples. Without this the
                // clear here beat the promote and the ghost was never given a lap -
                // it spawned but never moved.
                ghostLastLapBuffer.Clear();
                ghostLastLapBuffer.AddRange(ghostRecordingBuffer);
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

            // Spawn the ghost SHELL for every enabled mode, even when all-time mode
            // has no saved ghost for this track yet (per report - "where is the ghost
            // car? it's not there"). The old early-return here meant a first visit to
            // a track in the DEFAULT all-time mode showed no ghost at all, and (since
            // all-time only ever loaded from the store at spawn) never showed one for
            // the whole session even after the player set laps. Now the shell always
            // spawns; if there's a stored ghost it plays back immediately, otherwise
            // it waits at the line and PromoteGhostRecordingIfBest adopts the player's
            // first lap live (see there).
            GhostLapData stored = Settings.Current.ghostMode == 2 ? TimeTrialGhostStore.GetBestGhost(EventData.trackId) : null;

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
            if (stored != null && stored.samples != null && stored.samples.Count > 1)
            {
                // Trim on load too: a ghost saved before the standing-start trim
                // existed (or any lap that began stationary) would otherwise sit at
                // the line for its first several seconds and "appear" only once it
                // started moving - the reported "it randomly spawns when I'm 10s in".
                List<GhostSample> trimmed = TrimGhostStandingStart(stored.samples);
                GhostLapData loaded = new GhostLapData
                {
                    trackId = stored.trackId,
                    lapTime = trimmed.Count > 1 ? trimmed[trimmed.Count - 1].elapsedSeconds : stored.lapTime,
                    samples = trimmed
                };
                ghostController.Initialize(loaded);
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
