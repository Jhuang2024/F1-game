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
        float progressDistanceOffset;
        float lapStartTime;
        float sectorStartTime;
        bool initialized;
        bool sawSectorTwo;
        bool sawSectorThree;

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
        }

        public void ConfigureRaceGridStart()
        {
            if (Track != null)
            {
                CurrentProgress = Track.GetProgress(transform.position);
                CurrentSector = CurrentProgress.sector;
                
                // Fix "lap down" bug by ensuring progress distance offset is correctly set.
                // If we are on the grid, we are on Lap 0, but behind the start line.
                // TotalProgressDistance = CompletedLaps * length + distance + offset
                // On Lap 0, before crossing start line, distance is near length (e.g. 0.95 * length).
                // If offset is -length, TotalProgressDistance is roughly -0.05 * length, which is correct.
                progressDistanceOffset = -Track.length;
                TotalProgressDistance = CurrentProgress.distance + progressDistanceOffset;
                previousNormalized = CurrentProgress.normalized;
                initialized = true;
            }
            else
            {
                progressDistanceOffset = 0f;
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

            CurrentProgress = Track.GetProgress(transform.position);
            CurrentSector = CurrentProgress.sector;
            CurrentLapTime = OutLapActive || TimedLapStarted ? Time.time - lapStartTime : 0f;
            CurrentSectorTime = Time.time - sectorStartTime;

            if (!initialized)
            {
                TotalProgressDistance = CompletedLaps * Track.length + CurrentProgress.distance + progressDistanceOffset;
                previousNormalized = CurrentProgress.normalized;
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

            // More robust crossing detection
            bool crossedStart = previousNormalized > 0.80f && CurrentProgress.normalized < 0.20f;
            
            if (crossedStart)
            {
                if (progressDistanceOffset < -0.001f)
                {
                    // First crossing of start line after grid start
                    progressDistanceOffset = 0f;
                    // We don't call CompleteLap yet because the first crossing just starts Lap 1 (CompletedLaps 0 -> 1 happens at the END of Lap 1)
                    // Wait, if CompletedLaps is 0, and we cross start, we are still on Lap 1.
                    // Actually, the original logic calls CompleteLap() which increments CompletedLaps.
                    // If we start on the grid, we are on "Lap 1" but have 0 completed laps.
                    // Crossing the line for the FIRST time should NOT increment CompletedLaps if we started on the grid.
                    // But if we are in Qualifying OutLap, it should finish the outlap.
                    
                    if (OutLapActive)
                    {
                        CompleteLap();
                    }
                    else
                    {
                        // Reset lap timer for the start of the first full lap
                        lapStartTime = Time.time;
                        sectorStartTime = Time.time;
                        sawSectorTwo = false;
                        sawSectorThree = false;
                    }
                }
                else if (sawSectorTwo && sawSectorThree && CurrentLapTime > 10f)
                {
                    CompleteLap();
                }
            }

            TotalProgressDistance = CompletedLaps * Track.length + CurrentProgress.distance + progressDistanceOffset;
            previousNormalized = CurrentProgress.normalized;
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
