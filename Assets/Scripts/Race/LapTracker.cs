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
        // Once-per-lap latches for the sector timing lines. CrossedSectorLine fires
        // on ANY forward transition across 1/3 or 2/3, and CompleteSector then
        // unconditionally restarts the sector clock and banks the elapsed time as a
        // candidate best. Backing over a sector line and crossing it again - a spin,
        // a recovery, a rejoin - is not by itself an invalidating event, so the
        // second crossing recorded a fraction-of-a-second "sector time" that went
        // straight into the session-best table and made every real sector after it
        // impossible to beat. The following sector was truncated too, so the three
        // splits stopped summing to the lap.
        bool sector1Timed;
        bool sector2Timed;
        bool sawSectorThree;
        bool awaitingRaceStartLine;

        // Virtual checkpoint validation: the track is split into normalized-progress bands.
        // A lap only counts when enough bands were crossed in forward sequence, which stops
        // teleports, resets, or reversing over the line from producing phantom laps.
        const int CheckpointCount = 16;
        const int MinimumCheckpointsForLap = 12;
        int lastCheckpointIndex;
        int checkpointsPassedThisLap;

        public int CheckpointsPassed { get { return checkpointsPassedThisLap; } }
        public int CurrentCheckpointIndex { get { return lastCheckpointIndex; } }

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
            lastCheckpointIndex = 0;
            checkpointsPassedThisLap = 0;
            sector1Timed = false;
            sector2Timed = false;
        }

        void ResetCheckpointsFromCurrentPosition()
        {
            lastCheckpointIndex = CheckpointIndexFor(CurrentProgress.normalized);
            checkpointsPassedThisLap = 0;
            sector1Timed = false;
            sector2Timed = false;
        }

        int CheckpointIndexFor(float normalized)
        {
            return Mathf.Clamp(Mathf.FloorToInt(normalized * CheckpointCount), 0, CheckpointCount - 1);
        }

        void UpdateCheckpoints()
        {
            int checkpoint = CheckpointIndexFor(CurrentProgress.normalized);
            if (checkpoint == lastCheckpointIndex)
            {
                return;
            }

            int forwardDelta = (checkpoint - lastCheckpointIndex + CheckpointCount) % CheckpointCount;
            // Pit-lane lap-counter fix: a pit-guided car can legitimately advance
            // a few checkpoint bands in one tick if its guide waypoint has to
            // correct a small discontinuity (a phase-transition snap within
            // RaceManager's pit-guidance tolerances, or a red-flag grid
            // teleport handled separately via ConfigureRaceGridStart) - none of
            // that should cost the lap. Only what's genuinely ambiguous with
            // reversing/a real teleport (more than half the checkpoint ring)
            // still resyncs without credit; every smaller forward step counts
            // in full so pit lane traversal can never under-count checkpoints.
            if (forwardDelta >= 1 && forwardDelta <= CheckpointCount / 2)
            {
                lastCheckpointIndex = checkpoint;
                checkpointsPassedThisLap += forwardDelta;
            }
            else
            {
                // Teleport, respawn, or reversing. Resync AND surrender the credit
                // for the bands given up, so the counter measures NET forward
                // progress rather than "bands crossed forwards at some point".
                //
                // It previously resynced without deducting, which made
                // checkpointsPassedThisLap monotonically non-decreasing regardless
                // of direction: a car could reverse from the line back to ~0.25,
                // re-drive those bands forwards, re-accumulate the 12 checkpoints
                // MinimumCheckpointsForLap asks for, and be credited with a lap it
                // never completed - gaining a full lap of race progress and jumping
                // the running order. That is exactly the exploit this checkpoint
                // system's own comment says it exists to prevent. (sawSectorTwo/
                // sawSectorThree don't catch it either: they are set by position,
                // not by a directional crossing.)
                int backwardDelta = CheckpointCount - forwardDelta;
                lastCheckpointIndex = checkpoint;
                checkpointsPassedThisLap = Mathf.Max(0, checkpointsPassedThisLap - backwardDelta);
            }
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
                ResetCheckpointsFromCurrentPosition();
            }
            else
            {
                progressDistanceOffset = 0f;
                awaitingRaceStartLine = false;
            }
        }

        // Pit-exit handoff fix: a pit-guided car's transform moves kinematically
        // for the entire stop, and Tick()'s own continuity-biased search
        // (GetProgressNear, referenceDistance) only self-corrects gradually -
        // a large kinematic jump right at handoff (a phase-transition snap, or
        // the recovery snap in RaceManager.UpdatePitExitMerge) could otherwise
        // leave LapTracker's own progress/checkpoint state disagreeing with
        // where the car definitively now is for several ticks. Called once,
        // right as pit guidance ends, with the known exit-merge distance the
        // RaceManager pit-exit state machine already trusts, so every
        // continuity reference here snaps into agreement immediately instead of
        // drifting into it. Does not award a lap or otherwise change
        // CompletedLaps/lap timing state - only the continuity references.
        public void ResyncToDistance(float distance, Vector3 worldPosition)
        {
            if (Track == null)
            {
                return;
            }

            float wrapped = Track.WrapDistance(distance);
            CurrentProgress = Track.GetProgressAtDistance(wrapped, worldPosition);
            CurrentSector = CurrentProgress.sector;
            referenceDistance = CurrentProgress.distance;
            previousNormalized = CurrentProgress.normalized;
            TotalProgressDistance = CompletedLaps * Track.length + CurrentProgress.distance + progressDistanceOffset;
            ResetCheckpointsFromCurrentPosition();
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
                ResetCheckpointsFromCurrentPosition();
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
                ResetCheckpointsFromCurrentPosition();
                return;
            }

            UpdateCheckpoints();

            if (!sector1Timed && CrossedSectorLine(0.333f))
            {
                sector1Timed = true;
                CompleteSector(1);
            }

            if (CurrentProgress.normalized > 0.34f)
            {
                sawSectorTwo = true;
            }

            if (!sector2Timed && CrossedSectorLine(0.666f))
            {
                sector2Timed = true;
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
                    checkpointsPassedThisLap = 0;
            sector1Timed = false;
            sector2Timed = false;
                }
                else if (sawSectorTwo && sawSectorThree &&
                         checkpointsPassedThisLap >= MinimumCheckpointsForLap &&
                         CurrentLapTime > MinimumValidLapTime())
                {
                    CompleteLap();
                }
                else
                {
                    // Rejected crossing (failed the checkpoint/sector/min-time
                    // guard, e.g. after a recovery or teleport). This used to fall
                    // through and do NOTHING, which corrupted two things at once:
                    //
                    // (a) TotalProgressDistance is CompletedLaps*length + distance
                    //     + offset. The car's distance has just wrapped from ~length
                    //     to ~0 while CompletedLaps did not increment, so total
                    //     progress dropped by a FULL LAP. SortRunningOrder sorts
                    //     purely on that value, so the car instantly fell to the
                    //     back as if lapped and never recovered for the rest of the
                    //     race. Advancing the offset keeps it continuous - the car
                    //     really did travel that distance; it just doesn't get a lap
                    //     credited for it.
                    //
                    // (b) The lap and sector clocks kept running across the line, so
                    //     the next completed lap posted roughly two laps' worth of
                    //     time and its sector 1 was measured from the PREVIOUS lap's
                    //     2/3 marker - the three splits no longer summed to the lap.
                    //
                    // The lap itself is still correctly not counted.
                    progressDistanceOffset += Track.length;
                    lapStartTime = Time.time;
                    sectorStartTime = Time.time;
                    sawSectorTwo = false;
                    sawSectorThree = false;
                    CurrentLapInvalidated = false;
                    checkpointsPassedThisLap = 0;
            sector1Timed = false;
            sector2Timed = false;
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
                checkpointsPassedThisLap = 0;
            sector1Timed = false;
            sector2Timed = false;
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
            checkpointsPassedThisLap = 0;
            sector1Timed = false;
            sector2Timed = false;

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
