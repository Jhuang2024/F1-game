using UnityEngine;

namespace LocalFormulaRacing
{
    public class LapTracker : MonoBehaviour
    {
        public TrackRuntime Track { get; private set; }
        public int RaceLaps { get; private set; }
        public int CompletedLaps { get; private set; }
        public float LastLapTime { get; private set; }
        public float BestLapTime { get; private set; }
        public float CurrentLapTime { get; private set; }
        public float TotalProgressDistance { get; private set; }
        public TrackProgress CurrentProgress { get; private set; }
        public int CurrentSector { get; private set; }
        public bool CompletedRace { get; private set; }
        public bool CurrentLapInvalidated { get; private set; }
        public bool LastLapInvalidated { get; private set; }
        public int ValidLapsCompleted { get; private set; }
        public bool OutLapActive { get; private set; }
        public bool TimedLapStarted { get; private set; }
        public bool OutLapFinalCornerCut { get; private set; }
        public float CurrentSectorTime { get; private set; }
        public float LastSector1Time { get; private set; }
        public float LastSector2Time { get; private set; }
        public float LastSector3Time { get; private set; }
        public float BestSector1Time { get; private set; }
        public float BestSector2Time { get; private set; }
        public float BestSector3Time { get; private set; }

        float previousNormalized;
        float referenceDistance;
        float progressDistanceOffset;
        float lapStartTime;
        float sectorStartTime;
        bool initialized;
        bool sawSectorTwo;
        bool sawSectorThree;
        bool awaitingRaceStartLine;

        public int DisplayLap
        {
            get { return OutLapActive ? 0 : Mathf.Clamp(CompletedLaps + 1, 1, RaceLaps); }
        }

        public void Initialize(TrackRuntime track, int raceLaps)
        {
            Track = track;
            RaceLaps = Mathf.Max(1, raceLaps);
            CompletedLaps = 0;
            LastLapTime = 0f;
            BestLapTime = 0f;
            CurrentLapTime = 0f;
            lapStartTime = Time.time;
            sectorStartTime = Time.time;
            initialized = false;
            sawSectorTwo = false;
            sawSectorThree = false;
            awaitingRaceStartLine = false;
            CompletedRace = false;
            CurrentLapInvalidated = false;
            LastLapInvalidated = false;
            ValidLapsCompleted = 0;
            OutLapActive = false;
            TimedLapStarted = true;
            OutLapFinalCornerCut = false;
            CurrentSectorTime = 0f;
            LastSector1Time = 0f;
            LastSector2Time = 0f;
            LastSector3Time = 0f;
            BestSector1Time = 0f;
            BestSector2Time = 0f;
            BestSector3Time = 0f;
            progressDistanceOffset = 0f;
            referenceDistance = 0f;
        }

        public void ConfigureRaceGridStart()
        {
            float inferredDistance = Track == null ? 0f : Track.GetProgress(transform.position).distance;
            ConfigureRaceGridStart(inferredDistance);
        }

        public void ConfigureRaceGridStart(float gridDistance)
        {
            if (Track != null)
            {
                float wrappedGridDistance = Track.WrapDistance(gridDistance);
                CurrentProgress = Track.GetProgressAtDistance(wrappedGridDistance, transform.position);
                CurrentSector = CurrentProgress.sector;

                progressDistanceOffset = -Track.length;
                TotalProgressDistance = CurrentProgress.distance + progressDistanceOffset;
                previousNormalized = CurrentProgress.normalized;
                referenceDistance = CurrentProgress.distance;
                initialized = true;
                awaitingRaceStartLine = true;
                lapStartTime = Time.time;
                sectorStartTime = Time.time;
                sawSectorTwo = false;
                sawSectorThree = false;
            }
            else
            {
                progressDistanceOffset = 0f;
                awaitingRaceStartLine = false;
            }
        }

        public void ConfigureQualifyingOutLap()
        {
            OutLapActive = true;
            TimedLapStarted = false;
            OutLapFinalCornerCut = false;
            CurrentLapInvalidated = false;
            LastLapInvalidated = false;
            lapStartTime = Time.time;
            sectorStartTime = Time.time;
            if (Track != null)
            {
                CurrentProgress = Track.GetProgress(transform.position);
                CurrentSector = CurrentProgress.sector;
                previousNormalized = CurrentProgress.normalized;
                referenceDistance = CurrentProgress.distance;
                initialized = true;
            }
        }

        public void InvalidateCurrentLap()
        {
            if (OutLapActive)
            {
                if (CurrentProgress.normalized > 0.78f)
                {
                    OutLapFinalCornerCut = true;
                }

                return;
            }

            CurrentLapInvalidated = true;
        }

        public void Tick()
        {
            if (Track == null || CompletedRace)
            {
                return;
            }

            CurrentProgress = initialized ? Track.GetProgressNear(transform.position, referenceDistance) : Track.GetProgress(transform.position);
            CurrentSector = CurrentProgress.sector;
            CurrentLapTime = OutLapActive || TimedLapStarted ? Time.time - lapStartTime : 0f;
            CurrentSectorTime = Time.time - sectorStartTime;

            if (!initialized)
            {
                TotalProgressDistance = CompletedLaps * Track.length + CurrentProgress.distance + progressDistanceOffset;
                previousNormalized = CurrentProgress.normalized;
                referenceDistance = CurrentProgress.distance;
                initialized = true;
                return;
            }

            if (CrossedSectorLine(0.333f))
            {
                CompleteSector(1);
            }

            if (CurrentProgress.normalized > 0.34f)
            {
                sawSectorTwo = true;
            }

            if (CrossedSectorLine(0.666f))
            {
                CompleteSector(2);
            }

            if (CurrentProgress.normalized > 0.67f)
            {
                sawSectorThree = true;
            }

            bool crossedStart = previousNormalized > 0.80f && CurrentProgress.normalized < 0.20f;
            
            if (crossedStart)
            {
                if (OutLapActive)
                {
                    CompleteLap();
                }
                else if (awaitingRaceStartLine)
                {
                    awaitingRaceStartLine = false;
                    progressDistanceOffset = 0f;
                    lapStartTime = Time.time;
                    sectorStartTime = Time.time;
                    sawSectorTwo = false;
                    sawSectorThree = false;
                }
                else if (sawSectorTwo && sawSectorThree && CurrentLapTime > MinimumValidLapTime())
                {
                    CompleteLap();
                }
            }

            TotalProgressDistance = CompletedLaps * Track.length + CurrentProgress.distance + progressDistanceOffset;
            previousNormalized = CurrentProgress.normalized;
            referenceDistance = CurrentProgress.distance;
        }

        float MinimumValidLapTime()
        {
            if (Track == null)
            {
                return 10f;
            }

            return Mathf.Clamp(Track.length / 92f, 11f, 42f);
        }

        void CompleteLap()
        {
            if (OutLapActive)
            {
                OutLapActive = false;
                TimedLapStarted = true;
                LastLapTime = 0f;
                LastLapInvalidated = false;
                lapStartTime = Time.time;
                sectorStartTime = Time.time;
                sawSectorTwo = false;
                sawSectorThree = false;
                CurrentLapInvalidated = OutLapFinalCornerCut;
                referenceDistance = CurrentProgress.distance;
                return;
            }

            CompleteSector(3);
            CompletedLaps++;
            TotalProgressDistance = CompletedLaps * Track.length + CurrentProgress.distance + progressDistanceOffset;
            LastLapTime = CurrentLapTime;
            LastLapInvalidated = CurrentLapInvalidated;
            if (!CurrentLapInvalidated)
            {
                ValidLapsCompleted++;
                if (BestLapTime <= 0f || LastLapTime < BestLapTime)
                {
                    BestLapTime = LastLapTime;
                }
            }

            lapStartTime = Time.time;
            sectorStartTime = Time.time;
            sawSectorTwo = false;
            sawSectorThree = false;
            CurrentLapInvalidated = false;

            if (CompletedLaps >= RaceLaps)
            {
                CompletedRace = true;
                TotalProgressDistance = CompletedLaps * Track.length;
            }
        }

        bool CrossedSectorLine(float normalizedLine)
        {
            return previousNormalized < normalizedLine && CurrentProgress.normalized >= normalizedLine;
        }

        void CompleteSector(int sector)
        {
            float sectorTime = Time.time - sectorStartTime;
            sectorStartTime = Time.time;
            if (OutLapActive || !TimedLapStarted)
            {
                return;
            }

            if (sector == 1)
            {
                LastSector1Time = sectorTime;
                if (!CurrentLapInvalidated && (BestSector1Time <= 0f || sectorTime < BestSector1Time))
                {
                    BestSector1Time = sectorTime;
                }
            }
            else if (sector == 2)
            {
                LastSector2Time = sectorTime;
                if (!CurrentLapInvalidated && (BestSector2Time <= 0f || sectorTime < BestSector2Time))
                {
                    BestSector2Time = sectorTime;
                }
            }
            else
            {
                LastSector3Time = sectorTime;
                if (!CurrentLapInvalidated && (BestSector3Time <= 0f || sectorTime < BestSector3Time))
                {
                    BestSector3Time = sectorTime;
                }
            }
        }
    }
}
