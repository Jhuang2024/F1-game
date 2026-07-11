using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager practice-session subsystem (partial). Scores a just-driven
    /// Practice session against the selected practice program from the real
    /// telemetry captured during the session (EvaluatePracticeSession, called
    /// before CleanupRaceWorld while the player car is still live) and the
    /// best-AI-lap helper. Split out of the RaceManager monolith verbatim - same
    /// class, same members, identical scoring criteria; the PracticeSessionResult
    /// nested type stays in main, and the public entry point stays public so the
    /// practice UI resolves in-class.
    /// </summary>
    public partial class RaceManager
    {
        // Playable practice programs: scores the just-driven Practice session
        // against the criteria for whichever program (ActivePracticeProgramId)
        // the player picked in RuntimeUi.ShowPracticePrograms, from real telemetry
        // captured during the session rather than an unconditional click reward.
        // Call this BEFORE CleanupRaceWorld() while PlayerParticipant is still live.
        public PracticeSessionResult EvaluatePracticeSession()
        {
            PracticeSessionResult result = new PracticeSessionResult { programId = ActivePracticeProgramId };
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null || PlayerParticipant.vehicle == null)
            {
                result.title = "Practice Session";
                result.passed = false;
                result.metricSummary = "No valid lap data was recorded.";
                return result;
            }

            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            float bestLap = PlayerParticipant.lapTracker.BestLapTime;
            float tyreWear = PlayerParticipant.vehicle.Tyres == null ? 1f : PlayerParticipant.vehicle.Tyres.Wear;
            float ersBattery = PlayerParticipant.vehicle.ErsBattery;
            int pitStops = PlayerParticipant.pitStops;

            switch (ActivePracticeProgramId)
            {
                case "acclimatisation":
                    result.title = "Track Acclimatisation";
                    result.passed = completedLaps >= 3;
                    result.metricSummary = completedLaps + " lap(s) completed (need 3).";
                    break;

                case "tyreManagement":
                    result.title = "Tyre Management";
                    result.passed = completedLaps >= 5 && tyreWear > 0.4f;
                    result.metricSummary = completedLaps + " lap(s) completed, tyres at " + Mathf.RoundToInt(tyreWear * 100f) + "% life (need 5 laps and above 40% life).";
                    break;

                case "ersManagement":
                    result.title = "ERS Management";
                    result.passed = completedLaps >= 3 && ersBattery > 0.5f;
                    result.metricSummary = completedLaps + " lap(s) completed, battery at " + Mathf.RoundToInt(ersBattery * 100f) + "% (need 3 laps and above 50%).";
                    break;

                case "qualifyingPace":
                {
                    result.title = "Qualifying Pace";
                    float bestAiLap = BestAiLapTimeThisSession();
                    bool haveBenchmark = bestAiLap > 0f;
                    result.passed = bestLap > 0f && haveBenchmark && bestLap <= bestAiLap * 1.03f;
                    result.metricSummary = bestLap > 0f
                        ? ("Best lap " + UiFactory.FormatTime(bestLap) + (haveBenchmark ? " vs field best " + UiFactory.FormatTime(bestAiLap) + " (need within 3%)." : "."))
                        : "No valid lap was set.";
                    break;
                }

                case "racePace":
                    result.title = "Race Pace";
                    result.passed = completedLaps >= 8 && pitStops >= 1;
                    result.metricSummary = completedLaps + " lap(s) completed, " + pitStops + " pit stop(s) (need 8 laps and 1 stop).";
                    break;

                default:
                    result.title = "Practice Session";
                    result.passed = completedLaps >= 1;
                    result.metricSummary = completedLaps + " lap(s) completed.";
                    break;
            }

            return result;
        }

        float BestAiLapTimeThisSession()
        {
            float best = -1f;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null || participant.isPlayer || participant.lapTracker == null)
                {
                    continue;
                }

                float lap = participant.lapTracker.BestLapTime;
                if (lap > 0f && (best < 0f || lap < best))
                {
                    best = lap;
                }
            }

            return best;
        }

    }
}
