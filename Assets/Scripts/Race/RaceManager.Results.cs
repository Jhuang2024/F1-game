using System.Collections;
using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager race-end results subsystem (partial). Classifies the final
    /// finishing order, applies career results, records the player's stats, logs
    /// the AI-balance diagnostics, and stages the optional cinematic podium
    /// presentation before handing off to the results screen. Split out of the
    /// RaceManager monolith verbatim - same class, same members, identical
    /// behaviour and execution order; the classification maths live in the
    /// engine-free RaceClassifier, this partial owns the live state and the
    /// engine-side presentation.
    /// </summary>
    public partial class RaceManager
    {
        void FinishRace()
        {
            IsRaceFinished = true;
            Time.timeScale = 1f;
            replayCapture.End(RaceElapsed);
            SortRunningOrder();
            List<RaceResultEntry> results = new List<RaceResultEntry>();
            if (State == null) return;
            for (int i = 0; i < State.SortedOrder.Count; i++)
            {
                RaceParticipant participant = State.SortedOrder[i];
                // The two-compound rule only applies to cars that reach the flag;
                // ShouldDisqualifyForTwoCompoundRule takes `retired` and bails, but
                // keep the guard explicit here too.
                // A sprint has no mandatory pit stop and no two-compound requirement -
                // that is one of the things that makes it a sprint - so applying the
                // rule there would disqualify the entire field.
                if (!participant.retired && !IsSprintRace)
                {
                    ApplyTwoCompoundRule(participant);
                }

                participant.finishingPosition = i + 1;
                RaceResultEntry entry = participant.ToResultEntry();
                entry.finishingPosition = i + 1;
                if (participant.retired)
                {
                    entry.totalTime = participant.finishTime;
                    results.Add(entry);
                    continue;
                }

                if (!participant.finished && participant.lapTracker != null)
                {
                    // Finishing-order fix: this used an INTEGER laps-remaining
                    // (RaceLaps - CompletedLaps), which discards where each car
                    // actually is on its current lap - so every car with the same
                    // whole number of laps left tied on totalTime and the sort
                    // below fell back to arbitrary order, and a lapped car sitting
                    // near the start/finish line could be classified ahead of a
                    // lead-lap car that had only just crossed it (the "3 closest to
                    // the line, sometimes a lapped car" bug). Use FRACTIONAL laps
                    // remaining (including current-lap normalized progress, the same
                    // form the retired-time estimate already uses) so the estimated
                    // finish time is strictly monotonic with real race distance:
                    // more of the lap done -> less time left, and a lapped car
                    // always has at least a full extra lap of time on top.
                    entry.totalTime = RaceClassifier.EstimateUnfinishedTotalTime(
                        RaceElapsed,
                        RaceLaps,
                        participant.lapTracker.CompletedLaps + participant.lapTracker.CurrentProgress.normalized,
                        Track.length);
                }
                results.Add(entry);
            }

            // Disqualified cars sort behind everything else.
            RaceClassifier.AssignFinishingOrder(
                results,
                entry => entry.totalTime,
                entry => entry.penaltiesSeconds,
                entry => entry.disqualified,
                (entry, position) => entry.finishingPosition = position);

            // The 90% rule: a car must complete at least 90% of the winner's laps to
            // be classified. Unclassified cars and DSQs score no points.
            int winnerLaps = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].disqualified && results[i].completedLaps > winnerLaps)
                {
                    winnerLaps = results[i].completedLaps;
                }
            }

            for (int i = 0; i < results.Count; i++)
            {
                results[i].classified = !results[i].disqualified &&
                    RaceClassifier.IsClassified(results[i].completedLaps, winnerLaps);
            }

            if (IsCareerRace)
            {
                // Was calling the 2-arg overload, which defaults incident/safety-car/AI-overtake
                // counts to -1 ("no data") - that silently suppressed every race-control news
                // article (GenerateRaceControlNews bails out on safetyCarDeployments < 0) even
                // though this race tracked all three. Pass the real counts through.
                //
                // 188-incidents fix: this used to pass the raw internal IncidentCount, which
                // counts every single minor scrape/spin/stranded-car detection tick (routinely
                // in the hundreds over a full race) - CareerManager's team-news narrative and
                // "wasChaotic" classification read that number directly, so a perfectly normal
                // race could generate a "188 recorded incidents, utter chaos" story. Passing
                // RaceControlIncidentCount instead (genuine yellow/VSC/SC/red-flag actions only)
                // makes the narrative match what race control actually did.
                Career.ApplyRaceResults(EventData, results, RaceControlIncidentCount, SafetyCarDeploymentCount,
                    AiOvertakesCompletedCount, RedFlagCount, RedFlagReason, BuildRaceScoring());
            }
            else if (IsLegendsRace && Legends != null)
            {
                // Legends Championship: points + standings land in the isolated
                // legends save, never the career save.
                Legends.ApplyRaceResults(EventData, results);
            }

            RecordPlayerRaceStats(results);
            LogAiDiagnostics(results);
            LogTelemetryDebrief();
            LogReplaySummary();
            SimpleAudioManager.SetRaceAmbience(false);
            SimpleAudioManager.PlayResultsFlourish();

            // Podium/parc fermé presentation (#25): only at the Cinematic
            // presentation tier, and only when there's an actual camera/field
            // to stage - everything else (Minimal/Standard, or a degenerate
            // 0-1 car field) goes straight to the existing 2D results screen
            // exactly as before, unchanged.
            bool cinematic = Settings != null && Settings.Current.racePresentation >= 2;
            if (cinematic && playerCameraRig != null && results.Count >= 1)
            {
                StartCoroutine(PodiumPresentationSequence(results));
            }
            else
            {
                // Production results screen when the production UI owns the
                // frontend; the legacy screen is the compatibility fallback.
                if (!ProductionSessionUi.TryShowResults(results, IsCareerRace, RaceDebriefLine()))
                {
                    ProductionSessionUi.BeginResults();
                    ui.ShowResults(this, results, IsCareerRace);
                }
            }
        }

        // Generated runtime podium: repositions the top-3 finishers' own
        // already-alive car GameObjects (the race world isn't torn down until
        // the player navigates away from results - see CleanupRaceWorld's
        // call sites) onto three stepped blocks, hijacks the player's own
        // camera rig for a brief pan, then hands off to the normal 2D results
        // screen. Every step is defensively guarded so any missing piece
        // (no top-3 car found, no track reference) just skips that piece
        // rather than aborting the whole sequence - it always ends by calling
        // ShowResults no matter what happened above it.
        IEnumerator PodiumPresentationSequence(List<RaceResultEntry> results)
        {
            List<Transform> podiumCars = new List<Transform>();
            int topCount = Mathf.Min(3, results.Count);
            for (int i = 0; i < topCount; i++)
            {
                RaceParticipant found = null;
                for (int p = 0; p < Participants.Count; p++)
                {
                    if (Participants[p] != null && Participants[p].driverId == results[i].driverId)
                    {
                        found = Participants[p];
                        break;
                    }
                }

                podiumCars.Add(found != null ? found.transform : null);
            }

            GameObject podiumRoot = new GameObject("Podium presentation");
            if (raceWorld != null)
            {
                podiumRoot.transform.SetParent(raceWorld.transform);
            }

            Vector3 podiumCenter = Vector3.zero;
            Vector3 podiumForward = Vector3.forward;
            if (Track != null)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Track.SampleAtDistance(0f, out point, out forward, out right);
                // Well clear of the track/pit lane on either side - this is a
                // purely cosmetic stage, not something a car ever drives past,
                // so it only needs to be visually clear, not lane-accurate.
                podiumCenter = point + right * (Track.roadHalfWidth + 40f);
                podiumForward = -forward;
            }

            // Three stepped blocks: P1 centre and tallest, P2/P3 lower to either
            // side - the same silhouette every real podium reads as.
            Vector3 podiumRight = Vector3.Cross(Vector3.up, podiumForward).normalized;
            float[] blockHeights = { 1.1f, 0.75f, 0.5f };
            Vector3[] blockOffsets = { Vector3.zero, podiumRight * -3.2f, podiumRight * 3.2f };
            Color[] blockColors =
            {
                new Color(0.85f, 0.68f, 0.15f),
                new Color(0.75f, 0.76f, 0.78f),
                new Color(0.62f, 0.42f, 0.22f)
            };

            for (int i = 0; i < topCount; i++)
            {
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Podium block P" + (i + 1);
                block.transform.SetParent(podiumRoot.transform);
                Vector3 blockCenter = podiumCenter + blockOffsets[i] + Vector3.up * (blockHeights[i] * 0.5f);
                block.transform.position = blockCenter;
                block.transform.rotation = Quaternion.LookRotation(podiumForward, Vector3.up);
                block.transform.localScale = new Vector3(2.6f, blockHeights[i], 2.6f);
                Renderer blockRenderer = block.GetComponent<Renderer>();
                if (blockRenderer != null)
                {
                    blockRenderer.sharedMaterial = CarVisualFactory.CreateMaterial("Podium block P" + (i + 1), blockColors[i], 0.2f, 0.55f);
                }

                if (podiumCars[i] != null)
                {
                    // Freeze the car exactly where it's placed - a still-alive
                    // AiVehicleController/VehicleController would otherwise keep
                    // trying to drive it the instant it's teleported.
                    VehicleController vc = podiumCars[i].GetComponent<VehicleController>();
                    if (vc != null)
                    {
                        vc.enabled = false;
                    }

                    AiVehicleController ai = podiumCars[i].GetComponent<AiVehicleController>();
                    if (ai != null)
                    {
                        ai.enabled = false;
                    }

                    PlayerVehicleInput playerInput = podiumCars[i].GetComponent<PlayerVehicleInput>();
                    if (playerInput != null)
                    {
                        playerInput.enabled = false;
                    }

                    Rigidbody carBody = podiumCars[i].GetComponent<Rigidbody>();
                    if (carBody != null)
                    {
                        carBody.isKinematic = true;
                        carBody.velocity = Vector3.zero;
                        carBody.angularVelocity = Vector3.zero;
                    }

                    podiumCars[i].position = blockCenter + Vector3.up * (blockHeights[i] * 0.5f + 0.3f);
                    podiumCars[i].rotation = Quaternion.LookRotation(-podiumForward, Vector3.up);
                }
            }

            // Simple confetti: short burst of coloured particles above the
            // podium, gated the same way every other optional effect in this
            // codebase is (Settings.Current.particlesEnabled).
            if (Settings != null && Settings.Current.particlesEnabled && topCount > 0)
            {
                GameObject confettiObject = new GameObject("Podium confetti");
                confettiObject.transform.SetParent(podiumRoot.transform);
                confettiObject.transform.position = podiumCenter + Vector3.up * 4f;
                ParticleSystem confetti = confettiObject.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = confetti.main;
                main.startLifetime = 2.2f;
                main.startSpeed = 3.5f;
                main.startSize = 0.12f;
                main.maxParticles = 200;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.2f), new Color(0.3f, 0.6f, 1f));
                ParticleSystem.EmissionModule emission = confetti.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 120) });
                ParticleSystem.ShapeModule shape = confetti.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 25f;
                shape.radius = 1.5f;
                ParticleSystemRenderer confettiRenderer = confettiObject.GetComponent<ParticleSystemRenderer>();
                if (confettiRenderer != null)
                {
                    confettiRenderer.material = CarVisualFactory.CreateMaterial("Confetti particle", Color.white, 0f, 0.2f);
                }

                confetti.Play();
            }

            // Camera: hand-drive the player's own rig instead of leaving
            // CameraRig's own per-frame follow logic fighting this - it gets
            // re-enabled implicitly by never being needed again (the race
            // world, camera included, is destroyed the moment the player
            // navigates away from the results screen that follows this).
            playerCameraRig.enabled = false;
            Vector3 camStart = podiumCenter + podiumForward * 14f + podiumRight * 6f + Vector3.up * 3.2f;
            Vector3 camEnd = podiumCenter + podiumForward * 12f - podiumRight * 6f + Vector3.up * 4.4f;
            Transform camTransform = playerCameraRig.transform;

            float duration = 4f;
            float elapsed = 0f;
            while (elapsed < duration && !Input.anyKeyDown)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                camTransform.position = Vector3.Lerp(camStart, camEnd, t);
                camTransform.rotation = Quaternion.LookRotation((podiumCenter + Vector3.up * 1.5f) - camTransform.position, Vector3.up);
                yield return null;
            }

            // Same production-first results path as the non-cinematic branch.
            if (!ProductionSessionUi.TryShowResults(results, IsCareerRace, RaceDebriefLine()))
            {
                ProductionSessionUi.BeginResults();
                ui.ShowResults(this, results, IsCareerRace);
            }
        }

        // One-shot post-race summary so an Expert AI balance pass can be checked
        // from the log instead of only from playtesting feel.
        void LogAiDiagnostics(List<RaceResultEntry> results)
        {
            if (PlayerParticipant == null || results == null || results.Count == 0)
            {
                return;
            }

            float playerBest = PlayerParticipant.lapTracker == null ? 0f : PlayerParticipant.lapTracker.BestLapTime;
            List<float> aiBests = new List<float>();
            int ersFrameTotal = 0;
            int drsFrameTotal = 0;
            int aiCount = 0;
            int aiOvertakesTotal = 0;
            int aiLockupsTotal = 0;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant p = Participants[i];
                if (p == null || p.isPlayer)
                {
                    continue;
                }

                if (p.lapTracker != null && p.lapTracker.BestLapTime > 0f)
                {
                    aiBests.Add(p.lapTracker.BestLapTime);
                }

                ersFrameTotal += p.ersDeployFrameCount;
                drsFrameTotal += p.drsActiveFrameCount;
                aiOvertakesTotal += p.overtakesCompleted;
                aiLockupsTotal += p.vehicle == null || p.vehicle.Tyres == null ? 0 : p.vehicle.Tyres.TotalLockups;
                aiCount++;
            }

            aiBests.Sort();
            float fastestAi = aiBests.Count > 0 ? aiBests[0] : 0f;
            float medianAi = aiBests.Count > 0 ? aiBests[aiBests.Count / 2] : 0f;
            float slowestAi = aiBests.Count > 0 ? aiBests[aiBests.Count - 1] : 0f;

            RaceResultEntry playerResult = results.Find(entry => entry.isPlayer);
            RaceResultEntry winner = results[0];
            float playerGapToWinner = playerResult != null
                ? (playerResult.totalTime + playerResult.penaltiesSeconds) - (winner.totalTime + winner.penaltiesSeconds)
                : 0f;

            GameLog.Info("[AIDiagnostics] difficulty=" + Settings.Difficulty +
                         " playerBestLap=" + (playerBest > 0f ? UiFactory.FormatTime(playerBest) : "--") +
                         " aiFastestLap=" + (fastestAi > 0f ? UiFactory.FormatTime(fastestAi) : "--") +
                         " aiMedianLap=" + (medianAi > 0f ? UiFactory.FormatTime(medianAi) : "--") +
                         " aiSlowestLap=" + (slowestAi > 0f ? UiFactory.FormatTime(slowestAi) : "--") +
                         " playerFinish=P" + (playerResult != null ? playerResult.finishingPosition.ToString() : "--") +
                         " winner=" + winner.driverName +
                         " playerGapToWinner=" + playerGapToWinner.ToString("0.0") + "s" +
                         " aiAvgErsDeployFrames=" + (aiCount > 0 ? (ersFrameTotal / (float)aiCount).ToString("0") : "0") +
                         " aiAvgDrsActiveFrames=" + (aiCount > 0 ? (drsFrameTotal / (float)aiCount).ToString("0") : "0") +
                         " aiTotalDrsActiveFrames=" + drsFrameTotal +
                         " aiTotalErsDeployFrames=" + ersFrameTotal +
                         " aiTotalOvertakesCompleted=" + aiOvertakesTotal +
                         " aiTotalLockups=" + aiLockupsTotal +
                         " rawIncidentDetections=" + IncidentCount +
                         " raceControlIncidents=" + RaceControlIncidentCount +
                         " safetyCarDeployments=" + SafetyCarDeploymentCount);

            if (Settings.Difficulty == RaceDifficulty.Expert && playerBest > 0f && fastestAi > 0f && fastestAi - playerBest > 10f)
            {
                GameLog.Info("[AIDiagnostics] Expert AI too slow: investigate corner speed/braking/traffic.");
            }
        }

        /// <summary>
        /// How this session's championship points are scored: the full table for a
        /// grand prix run to the flag, the 8-7-6-5-4-3-2-1 sprint table for a sprint,
        /// and the FIA's sliding scale for a race suspended and never resumed.
        ///
        /// Both of the latter two were unreachable: the sprint table and the
        /// suspended-race tables existed in ChampionshipPoints with no caller at all,
        /// so a red-flagged race that never restarted still paid a full 25 for the
        /// win, and there was no sprint to pay the sprint table.
        /// </summary>
        CareerManager.RaceScoring BuildRaceScoring()
        {
            if (IsSprintRace)
            {
                return CareerManager.RaceScoring.Sprint;
            }

            if (!RaceAbandonedBeforeDistance)
            {
                return CareerManager.RaceScoring.FullGrandPrix;
            }

            int leaderLaps = 0;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant != null && participant.lapTracker != null)
                {
                    leaderLaps = Mathf.Max(leaderLaps, participant.lapTracker.CompletedLaps);
                }
            }

            float fraction = RaceLaps > 0 ? Mathf.Clamp01(leaderLaps / (float)RaceLaps) : 1f;
            return CareerManager.RaceScoring.Suspended(fraction, leaderLaps);
        }

        void RecordPlayerRaceStats(List<RaceResultEntry> results)
        {
            RaceResultEntry playerResult = results == null ? null : results.Find(entry => entry.isPlayer);
            if (playerResult == null)
            {
                return;
            }

            // A sprint win is not a grand prix win. Career records track grands prix,
            // so a sprint scores its championship points and nothing else - otherwise
            // a sprint weekend would inflate the career win/podium totals by a whole
            // extra race.
            if (IsSprintRace)
            {
                return;
            }

            RaceResultEntry fastest = null;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].bestLapTime > 0f && (fastest == null || results[i].bestLapTime < fastest.bestLapTime))
                {
                    fastest = results[i];
                }
            }

            bool fastestLap = fastest != null && fastest.isPlayer;
            int trackLimitWarnings = PlayerParticipant != null ? PlayerParticipant.trackLimitWarnings : 0;
            bool cleanRace = playerResult.penaltiesSeconds <= 0.01f &&
                             trackLimitWarnings == 0 &&
                             PlayerParticipant != null &&
                             PlayerParticipant.vehicle != null &&
                             PlayerParticipant.vehicle.Damage.OverallPercent < 20f;
            bool wetRace = Track != null && (Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain);
            string trackIdForRecords = EventData != null ? EventData.trackId : null;
            PlayerRecordsStore.RecordRaceFinish(playerResult.finishingPosition, playerResult.points, fastestLap, cleanRace, trackLimitWarnings,
                trackIdForRecords, playerResult.gridPosition, playerResult.overtakesMade, wetRace);
        }

    }
}
