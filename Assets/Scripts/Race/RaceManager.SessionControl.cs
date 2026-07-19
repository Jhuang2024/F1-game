using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager session-control subsystem (partial). Pause/resume, restart the
    /// current race, and tear down the race world (CleanupRaceWorld - destroys the
    /// spawned field/lighting and resets audio). Split out of the RaceManager
    /// monolith verbatim - same class, same members, identical teardown order; the
    /// public entry points stay public so the pause menu and GameBootstrap resolve
    /// in-class.
    /// </summary>
    public partial class RaceManager
    {
        public void TogglePause()
        {
            if (IsRaceFinished)
            {
                return;
            }

            IsPaused = !IsPaused;
            Time.timeScale = IsPaused ? 0f : 1f;
            // Production HUD (if active) hides while paused so the legacy pause
            // menu is visible/interactable; restored on resume.
            ProductionSessionUi.SetPaused(this, IsPaused);
            ui.SetPauseVisible(IsPaused);
        }

        public void Resume()
        {
            IsPaused = false;
            Time.timeScale = 1f;
            ProductionSessionUi.SetPaused(this, false);
            ui.SetPauseVisible(false);
        }

        public void RestartRace()
        {
            if (State != null)
            {
                State.ResetAllParticipants();
            }
            pendingTimeTrial = IsTimeTrial;
            StartSession(Data, Career, Settings, ui, EventData, Career.Save.playerDriverName, Career.Save.playerTeamId, IsCareerRace, CurrentSession);
        }

        public void CleanupRaceWorld()
        {
            // Restore the gameplay camera + any frozen time BEFORE the world is torn
            // down (the director is a child of raceWorld and is destroyed with it).
            TeardownCinematicDirector();
            TrackQueryProvider.Clear();
            Time.timeScale = 1f;
            IsPaused = false;
            IsRaceFinished = true;
            IsTimeTrial = false;
            State = null;
            PlayerParticipant = null;
            if (raceWorld != null)
            {
                Destroy(raceWorld);
                raceWorld = null;
            }

            // Ghost car is parented under raceWorld so Destroy above already
            // removes it from the scene - just drop the now-stale references.
            ghostCarObject = null;
            ghostController = null;
            ghostRecordingBuffer.Clear();
            ghostLastLapBuffer.Clear();
            ghostRecordedLapNumber = -1;
            playerCameraRig = null;
            ActivePracticeProgramId = null;

            SimpleAudioManager.SetRain(false);
            SimpleAudioManager.SetRaceAmbience(false);
        }

    }
}
