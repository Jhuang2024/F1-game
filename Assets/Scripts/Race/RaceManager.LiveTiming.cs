using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager live-timing subsystem (partial). The qualifying timing tower,
    /// pole/delta references, the per-sector capture and records (ReportSectorToState,
    /// UpdateSectorRecords, CheckCompletedSector, SampleCorneringTelemetry), the
    /// player sector/live text, the qualifying best-lap captures and resets, and the
    /// display-time / position-estimate helpers. Split out of the RaceManager
    /// monolith verbatim - same class, same members, identical behaviour, capture
    /// order and RNG-free maths; the sim/tower nested types stay main-nested and
    /// resolve in-class, and the public tower/display entry points stay public so
    /// the HUD callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        public List<QualifyingTowerRow> BuildQualifyingTowerRows(int maxRows)
        {
            List<QualifyingTowerRow> rows = new List<QualifyingTowerRow>();
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            active.Sort((a, b) => GetDisplayQualifyingTime(a).CompareTo(GetDisplayQualifyingTime(b)));
            float pole = GetQualifyingPoleReferenceTime();
            int count = Mathf.Min(maxRows, active.Count);
            for (int i = 0; i < count; i++)
            {
                QualifyingSimEntry entry = active[i];
                float time = GetDisplayQualifyingTime(entry);
                string best = time >= 9998f ? "--:--.---" : UiFactory.FormatTime(time);
                string gap = time >= 9998f || pole <= 0f ? "--" : (Mathf.Abs(time - pole) < 0.001f ? "P1" : "+" + (time - pole).ToString("0.000"));
                rows.Add(new QualifyingTowerRow
                {
                    position = i + 1,
                    driverCode = GetDisplayDriverCode(entry.driverData, entry.driverName),
                    bestTimeText = best,
                    gapText = gap,
                    isPlayer = entry.isPlayer
                });
            }

            return rows;
        }

        public float GetQualifyingPoleReferenceTime()
        {
            if (CurrentSession != RaceWeekendSession.Qualifying)
            {
                return 0f;
            }

            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            float best = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                float time = GetDisplayQualifyingTime(active[i]);
                if (time > 0f && time < 9998f && time < best)
                {
                    best = time;
                }
            }

            return best == float.MaxValue ? 0f : best;
        }

        public void ReportSectorToState(RaceParticipant participant, int sector, float sectorTime, bool invalidated)
        {
            if (State != null)
            {
                State.OnSectorComplete(participant, sector, sectorTime, invalidated);
            }
        }

        // Numeric qualifying delta vs the pole reference (+ slower); the single
        // source both the legacy text readout and the production HUD read.
        public bool TryGetQualifyingDelta(RaceParticipant participant, out float delta)
        {
            delta = 0f;
            if (CurrentSession != RaceWeekendSession.Qualifying || participant == null || participant.lapTracker == null)
            {
                return false;
            }

            float pole = GetQualifyingPoleReferenceTime();
            if (pole <= 0f || participant.lapTracker.OutLapActive)
            {
                return false;
            }

            TrackProgress currentProgress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            float progress = Mathf.Clamp(currentProgress.normalized, 0.02f, 0.995f);
            float reference = pole * progress;
            delta = participant.lapTracker.CurrentLapTime - reference;
            return true;
        }

        public string QualifyingDeltaText(RaceParticipant participant)
        {
            if (!TryGetQualifyingDelta(participant, out float delta))
            {
                return "--";
            }

            string color = delta <= 0f ? "#6CFF8D" : "#FF6C6C";
            return "<color=" + color + ">" + (delta >= 0f ? "+" : "") + delta.ToString("0.000") + "</color>";
        }

        public string PlayerSectorText(int sector, float time)
        {
            if (time <= 0f)
            {
                return "--.---";
            }

            string formatted = UiFactory.FormatTime(time);
            if (CurrentSession != RaceWeekendSession.Qualifying || sector < 1 || sector > 3)
            {
                return formatted;
            }

            string color = playerSectorColors[sector - 1];
            return string.IsNullOrEmpty(color) ? formatted : "<color=" + color + ">" + formatted + "</color>";
        }

        public string LiveSectorText(float time)
        {
            if (time <= 0f)
            {
                return "--.---";
            }

            string formatted = UiFactory.FormatTime(time);
            return CurrentSession == RaceWeekendSession.Qualifying ? "<color=" + SectorYellow + ">" + formatted + "</color>" : formatted;
        }

        void ResetQualifyingSectorState()
        {
            if (State != null) State.Initialize(CurrentSession, qualifyingPhase);
            for (int i = 0; i < 3; i++)
            {
                playerSectorColors[i] = "";
            }
        }

        void ResetPlayerQualifyingCaptures()
        {
            for (int phase = 0; phase < playerQualifyingBestTimes.Length; phase++)
            {
                playerQualifyingBestTimes[phase] = 0f;
                for (int sector = 0; sector < 3; sector++)
                {
                    playerQualifyingBestSectors[phase, sector] = 0f;
                }
            }

            recordedPlayerValidLapCount = 0;
        }

        void ResetPlayerQualifyingPhaseCapture(int phase)
        {
            int index = Mathf.Clamp(phase, 1, 3) - 1;
            playerQualifyingBestTimes[index] = 0f;
            for (int sector = 0; sector < 3; sector++)
            {
                playerQualifyingBestSectors[index, sector] = 0f;
            }

            recordedPlayerValidLapCount = 0;
        }

        void CapturePlayerQualifyingBestLap(LapTracker lap)
        {
            if (lap == null || CurrentSession != RaceWeekendSession.Qualifying || qualifyingPhase < 1 || qualifyingPhase > 3)
            {
                return;
            }

            if (lap.ValidLapsCompleted <= recordedPlayerValidLapCount)
            {
                return;
            }

            recordedPlayerValidLapCount = lap.ValidLapsCompleted;
            if (lap.LastLapInvalidated || lap.LastLapTime <= 0f)
            {
                return;
            }

            int index = qualifyingPhase - 1;
            if (playerQualifyingBestTimes[index] <= 0f || lap.LastLapTime < playerQualifyingBestTimes[index])
            {
                playerQualifyingBestTimes[index] = lap.LastLapTime;
                playerQualifyingBestSectors[index, 0] = lap.LastSector1Time;
                playerQualifyingBestSectors[index, 1] = lap.LastSector2Time;
                playerQualifyingBestSectors[index, 2] = lap.LastSector3Time;
            }
        }

        bool ShouldCompleteQualifyingRun()
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null)
            {
                return false;
            }

            LapTracker lap = PlayerParticipant.lapTracker;
            // Time budget widened alongside QualifyingSessionLapCap (was 360s for a
            // flat 2-lap session) so the extra flying-lap attempts actually fit on
            // longer circuits instead of being cut off by the clock before the lap
            // cap is ever reached.
            if (RaceElapsed > 480f)
            {
                return true;
            }

            if (lap.ValidLapsCompleted > 0 && PlayerHoldsCurrentQualifyingPole())
            {
                return true;
            }

            return lap.CompletedLaps >= QualifyingSessionLapCap;
        }

        bool PlayerHoldsCurrentQualifyingPole()
        {
            if (qualifyingPhase < 1 || qualifyingPhase > 3)
            {
                return false;
            }

            int index = qualifyingPhase - 1;
            float playerTime = playerQualifyingBestTimes[index];
            if (playerTime <= 0f && PlayerParticipant != null && PlayerParticipant.lapTracker != null)
            {
                playerTime = PlayerParticipant.lapTracker.BestLapTime;
            }

            if (playerTime <= 0f)
            {
                return false;
            }

            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].isPlayer)
                {
                    continue;
                }

                float rivalTime = GetQualifyingPhaseTime(active[i], qualifyingPhase);
                if (rivalTime <= 0f)
                {
                    rivalTime = SimulateAiQualifyingTime(active[i], qualifyingPhase);
                    SetAiQualifyingPhaseTime(active[i], qualifyingPhase, rivalTime);
                }

                if (rivalTime > 0f && rivalTime < 9998f && rivalTime < playerTime - 0.001f)
                {
                    return false;
                }
            }

            return true;
        }

        void UpdateSectorRecords(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || State == null)
            {
                return;
            }

            LapTracker lap = participant.lapTracker;
            CheckCompletedSector(participant, 1, lap.LastSector1Time, lap.BestSector1Time, lap.CurrentLapInvalidated);
            CheckCompletedSector(participant, 2, lap.LastSector2Time, lap.BestSector2Time, lap.CurrentLapInvalidated);
            CheckCompletedSector(participant, 3, lap.LastSector3Time, lap.BestSector3Time, lap.LastLapInvalidated);
        }

        // Cornering performance telemetry: samples this car's actual speed the
        // moment it passes each classified corner's peak (see
        // TrackRuntime.ClassifyCorners, cached once as telemetryCorners at
        // track build time) against a simple reference speed for that corner's
        // risk tier, so a post-race/post-session report can show where time
        // was actually gained or lost by corner type. Deliberately a live
        // apex-speed-vs-reference comparison rather than a full ghost-lap
        // system - much cheaper to build and still answers the real question
        // ("am I carrying enough speed through high-speed corners specifically,
        // or hairpins specifically") without needing lap-to-lap replay data.
        const float TelemetryCornerCaptureRadius = 12f;

        void SampleCorneringTelemetry(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || telemetryCorners == null || telemetryCorners.Count == 0 || Track == null)
            {
                return;
            }

            if (participant.retired || participant.finished || participant.isPitting || participant.pitPhase != PitPhase.None)
            {
                return;
            }

            float distance = participant.lapTracker != null ? participant.lapTracker.CurrentProgress.distance : 0f;
            for (int i = 0; i < telemetryCorners.Count; i++)
            {
                TrackRuntime.CornerRiskInfo corner = telemetryCorners[i];
                float delta = Mathf.Abs(Track.WrapDistance(distance - corner.distance));
                float wrapped = Mathf.Min(delta, Track.length - delta);
                if (wrapped > TelemetryCornerCaptureRadius)
                {
                    continue;
                }

                // Only once per approach - a car crawling/recovering right at
                // a corner apex for several ticks (traffic, a spin) must not
                // flood the same sample in over and over.
                if (Mathf.Abs(participant.LastTelemetryCornerDistance - corner.distance) < 1f)
                {
                    continue;
                }

                participant.LastTelemetryCornerDistance = corner.distance;

                float carTopSpeed = participant.vehicle.CarData == null || participant.vehicle.CarData.topSpeed <= 0 ? 337f : participant.vehicle.CarData.topSpeed;
                // Rough real-world fraction of top speed a well-driven car
                // carries through each risk tier - deliberately simple and
                // clearly a reference/approximation, not a claim of physical
                // precision.
                float referenceFraction = corner.risk == TrackRuntime.CornerRisk.Low ? 0.90f : (corner.risk == TrackRuntime.CornerRisk.Medium ? 0.68f : 0.45f);
                float referenceSpeedKph = carTopSpeed * referenceFraction;
                float actualSpeedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);

                int index = (int)corner.risk;
                participant.cornerSpeedSumByRisk[index] += actualSpeedKph;
                participant.cornerReferenceSumByRisk[index] += referenceSpeedKph;
                participant.cornerSampleCountByRisk[index]++;
            }
        }

        void CheckCompletedSector(RaceParticipant participant, int sector, float sectorTime, float personalBest, bool invalidated)
        {
            if (sectorTime <= 0f || sector < 1 || sector > 3 || State == null)
            {
                return;
            }

            State.OnSectorComplete(participant, sector, sectorTime, invalidated);

            if (CurrentSession != RaceWeekendSession.Qualifying || invalidated)
            {
                return;
            }

            bool purple = State.IsPurpleSector(sector, sectorTime);
            if (participant.isPlayer)
            {
                bool personalBestSector = personalBest > 0f && Mathf.Abs(personalBest - sectorTime) < 0.002f;
                playerSectorColors[sector - 1] = purple ? SectorPurple : (personalBestSector ? SectorGreen : SectorYellow);
            }
        }


        float GetDisplayQualifyingTime(QualifyingSimEntry entry)
        {
            if (entry == null)
            {
                return 9999f;
            }

            if (entry.isPlayer && qualifyingPhase >= 1 && qualifyingPhase <= 3 && playerQualifyingBestTimes[qualifyingPhase - 1] > 0f)
            {
                return playerQualifyingBestTimes[qualifyingPhase - 1];
            }

            if (entry.isPlayer && PlayerParticipant != null && PlayerParticipant.lapTracker != null && PlayerParticipant.lapTracker.BestLapTime > 0f)
            {
                return PlayerParticipant.lapTracker.BestLapTime;
            }

            float time = GetQualifyingPhaseTime(entry, qualifyingPhase);
            return time > 0f ? time : 9999f;
        }

        int GetQualifyingPositionEstimate()
        {
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            active.Sort((a, b) => GetDisplayQualifyingTime(a).CompareTo(GetDisplayQualifyingTime(b)));
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].isPlayer)
                {
                    return i + 1;
                }
            }

            return Mathf.Max(1, active.Count);
        }

    }
}
