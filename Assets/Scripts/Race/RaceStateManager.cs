using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    public class RaceStateManager : MonoBehaviour
    {
        public List<RaceParticipant> Participants { get; private set; } = new List<RaceParticipant>();
        public List<RaceParticipant> SortedOrder { get; private set; } = new List<RaceParticipant>();

        private Dictionary<RaceParticipant, SectorSnapshot> sectorSnapshots = new Dictionary<RaceParticipant, SectorSnapshot>();
        private Dictionary<RaceParticipant, TimingSnapshot> timingSnapshots = new Dictionary<RaceParticipant, TimingSnapshot>();
        private float[] overallBestSectors = new float[3];
        
        public RaceWeekendSession CurrentSession { get; private set; }
        public int QualifyingPhase { get; private set; } = 1;
        public int FinishedCount { get; private set; } = 0;

        public void Initialize(RaceWeekendSession session, int qualifyingPhase = 1)
        {
            CurrentSession = session;
            QualifyingPhase = qualifyingPhase;
            Participants.Clear();
            SortedOrder.Clear();
            sectorSnapshots.Clear();
            timingSnapshots.Clear();
            FinishedCount = 0;
            for (int i = 0; i < 3; i++) overallBestSectors[i] = 0f;
        }

        public void RegisterParticipant(RaceParticipant participant)
        {
            if (!Participants.Contains(participant))
            {
                Participants.Add(participant);
                sectorSnapshots[participant] = new SectorSnapshot();
                timingSnapshots[participant] = BuildTimingSnapshot(participant);
            }
        }

        public void Tick()
        {
            RefreshTimingSnapshots();
            SortRunningOrder();
        }

        public void RefreshTimingSnapshots()
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null)
                {
                    continue;
                }

                timingSnapshots[participant] = BuildTimingSnapshot(participant);
            }
        }

        public void RefreshTimingSnapshot(RaceParticipant participant)
        {
            if (participant == null)
            {
                return;
            }

            timingSnapshots[participant] = BuildTimingSnapshot(participant);
        }

        private void SortRunningOrder()
        {
            SortedOrder.Clear();
            SortedOrder.AddRange(Participants);
            SortedOrder.Sort((a, b) =>
            {
                if (a.finished && b.finished)
                {
                    if (a.retired != b.retired)
                    {
                        return a.retired ? 1 : -1;
                    }

                    return a.finishingPosition.CompareTo(b.finishingPosition);
                }

                if (a.finished)
                {
                    return a.retired ? 1 : -1;
                }

                if (b.finished)
                {
                    return b.retired ? -1 : 1;
                }

                float aDistance = GetProgressDistance(a);
                float bDistance = GetProgressDistance(b);
                return bDistance.CompareTo(aDistance);
            });
        }

        public float GetProgressDistance(RaceParticipant participant)
        {
            TimingSnapshot snapshot;
            if (participant != null && timingSnapshots.TryGetValue(participant, out snapshot) && snapshot.valid)
            {
                return snapshot.totalProgressDistance;
            }

            return participant == null || participant.lapTracker == null ? 0f : participant.lapTracker.TotalProgressDistance;
        }

        public int GetCompletedLaps(RaceParticipant participant)
        {
            TimingSnapshot snapshot;
            if (participant != null && timingSnapshots.TryGetValue(participant, out snapshot) && snapshot.valid)
            {
                return snapshot.completedLaps;
            }

            return participant == null || participant.lapTracker == null ? 0 : participant.lapTracker.CompletedLaps;
        }

        public TrackProgress GetCurrentProgress(RaceParticipant participant)
        {
            TimingSnapshot snapshot;
            if (participant != null && timingSnapshots.TryGetValue(participant, out snapshot) && snapshot.valid)
            {
                return snapshot.progress;
            }

            return participant == null || participant.lapTracker == null ? new TrackProgress() : participant.lapTracker.CurrentProgress;
        }

        TimingSnapshot BuildTimingSnapshot(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null)
            {
                return new TimingSnapshot();
            }

            return new TimingSnapshot
            {
                valid = true,
                completedLaps = participant.lapTracker.CompletedLaps,
                totalProgressDistance = participant.lapTracker.TotalProgressDistance,
                progress = participant.lapTracker.CurrentProgress
            };
        }

        public void OnSectorComplete(RaceParticipant participant, int sector, float sectorTime, bool invalidated)
        {
            if (sector < 1 || sector > 3 || sectorTime <= 0f) return;

            if (!sectorSnapshots.ContainsKey(participant))
            {
                sectorSnapshots[participant] = new SectorSnapshot();
            }

            SectorSnapshot snapshot = sectorSnapshots[participant];
            float previous = sector == 1 ? snapshot.s1 : (sector == 2 ? snapshot.s2 : snapshot.s3);
            
            if (Mathf.Abs(previous - sectorTime) < 0.001f) return;

            if (sector == 1) snapshot.s1 = sectorTime;
            else if (sector == 2) snapshot.s2 = sectorTime;
            else snapshot.s3 = sectorTime;

            if (invalidated) return;

            int idx = sector - 1;
            if (overallBestSectors[idx] <= 0f || sectorTime < overallBestSectors[idx] - 0.001f)
            {
                overallBestSectors[idx] = sectorTime;
            }
        }

        public void OnParticipantFinished(RaceParticipant participant, float raceElapsed)
        {
            if (participant.finished) return;

            participant.finished = true;
            FinishedCount++;
            participant.finishingPosition = FinishedCount;
            participant.finishTime = raceElapsed;
        }

        public void ResetAllParticipants()
        {
            foreach (var participant in Participants)
            {
                if (participant.vehicle != null)
                {
                    // Logic to reset vehicle state if needed, 
                    // though currently RaceManager recreates them.
                }
                
                if (participant.lapTracker != null)
                {
                    if (CurrentSession == RaceWeekendSession.Qualifying)
                        participant.lapTracker.ConfigureQualifyingOutLap();
                    else
                        participant.lapTracker.ConfigureRaceGridStart();
                }

                timingSnapshots[participant] = BuildTimingSnapshot(participant);

                participant.finished = false;
                participant.retired = false;
                participant.finishingPosition = 0;
                participant.penaltiesSeconds = 0;
                participant.penaltyReason = "";
                participant.trackLimitWarnings = 0;
                participant.trackLimitEventLog.Clear();
            }
            FinishedCount = 0;
            SortRunningOrder();
        }

        public float GetOverallBestSector(int sector)
        {
            if (sector < 1 || sector > 3) return 0f;
            return overallBestSectors[sector - 1];
        }

        public bool IsPurpleSector(int sector, float time)
        {
            if (sector < 1 || sector > 3 || time <= 0f) return false;
            float best = overallBestSectors[sector - 1];
            return best > 0f && Mathf.Abs(best - time) < 0.001f;
        }

        class SectorSnapshot
        {
            public float s1;
            public float s2;
            public float s3;
        }

        struct TimingSnapshot
        {
            public bool valid;
            public int completedLaps;
            public float totalProgressDistance;
            public TrackProgress progress;
        }
    }
}
