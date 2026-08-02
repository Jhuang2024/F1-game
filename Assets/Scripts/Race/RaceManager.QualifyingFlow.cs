using System.Collections;
using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager qualifying-session flow subsystem (partial). Drives the
    /// segment-to-segment progression - completing a run, the advance/eliminated
    /// feedback, recording each phase, the Q1/Q2/Q3 elimination cuts (counts from
    /// the engine-free QualifyingProgression), building the final classification
    /// and the one-shot AI-balance qualifying diagnostics. Split out of the
    /// RaceManager monolith verbatim - same class, same members, identical
    /// behaviour, execution order and RNG use; the per-lap time model and the
    /// shared accessors live in RaceManager.Qualifying.cs, the sim nested types
    /// stay main-nested (resolve in-class).
    /// </summary>
    public partial class RaceManager
    {
        void CompleteQualifyingRun()
        {
            RecordQualifyingPhase();
            QualifyingSimEntry playerEntry = qualifyingEntries.Find(item => item.isPlayer);
            bool advances = playerEntry != null && string.IsNullOrEmpty(playerEntry.eliminatedIn) &&
                            !QualifyingProgression.IsFinalPhase(qualifyingPhase);
            BeginQualifyingFeedback(BuildQualifyingSegmentFeedback(playerEntry, qualifyingPhase, advances), advances);
        }

        void BeginQualifyingFeedback(string feedback, bool advances)
        {
            QualifyingFeedbackText = feedback;
            SessionMessage = feedback.Replace("\n", "  ");
            qualifyingTransitionPending = true;
            qualifyingTransitionFinish = !advances;
            qualifyingTransitionTimer = 4.2f;
        }

        string BuildQualifyingSegmentFeedback(QualifyingSimEntry playerEntry, int phase, bool advances)
        {
            int position = playerEntry == null ? 0 : GetQualifyingPhasePosition(playerEntry, phase);
            if (phase == 1)
            {
                return "You qualified P" + position.ToString("00") + " in Q1\n" + (advances ? "Advanced to Q2" : "Eliminated in Q1");
            }

            if (phase == 2)
            {
                return "You qualified P" + position.ToString("00") + " in Q2\n" + (advances ? "Advanced to Q3" : "Eliminated in Q2");
            }

            return "You qualified P" + position.ToString("00") + " overall\nQualifying complete";
        }

        int GetQualifyingPhasePosition(QualifyingSimEntry target, int phase)
        {
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(phase);
            // The caller is usually asking about a driver who has JUST been
            // eliminated in this phase - CompleteQualifyingRun records the phase (which
            // applies elimination and stamps eliminatedIn) and only then builds the
            // feedback card. ActiveQualifyingEntries filters on an EMPTY eliminatedIn
            // for phase >= 2, so the just-eliminated driver was not in the list, the
            // loop never matched, and it fell through to "active.Count" - the
            // survivor count, i.e. a flat 10 after a Q2 cut. The end-of-Q2 card
            // therefore always read "You qualified P10 in Q2 / Eliminated in Q2",
            // self-contradictory and identical whether the player was 11th or 16th.
            // Include anyone eliminated in THIS phase so they are ranked among the
            // field that actually ran it.
            string phaseLabel = "Q" + phase;
            for (int i = 0; i < qualifyingEntries.Count; i++)
            {
                QualifyingSimEntry entry = qualifyingEntries[i];
                if (entry != null && entry.eliminatedIn == phaseLabel && !active.Contains(entry))
                {
                    active.Add(entry);
                }
            }

            active.Sort((a, b) => GetQualifyingPhaseTime(a, phase).CompareTo(GetQualifyingPhaseTime(b, phase)));
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] == target || active[i].driverId == target.driverId)
                {
                    return i + 1;
                }
            }

            return Mathf.Max(1, active.Count);
        }

        void RecordQualifyingPhase()
        {
            if (qualifyingEntries.Count == 0)
            {
                BuildQualifyingField(PlayerParticipant == null ? "" : PlayerParticipant.teamId);
            }

            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            for (int i = 0; i < active.Count; i++)
            {
                QualifyingSimEntry entry = active[i];
                if (entry.isPlayer)
                {
                    int phaseIndex = Mathf.Clamp(qualifyingPhase, 1, 3) - 1;
                    float capturedTime = playerQualifyingBestTimes[phaseIndex];
                    bool hasCapturedValidLap = capturedTime > 0f && capturedTime < 9998f;
                    bool hasTrackerValidLap = PlayerParticipant != null && PlayerParticipant.lapTracker != null && PlayerParticipant.lapTracker.BestLapTime > 0f;
                    bool invalidated = !hasCapturedValidLap && !hasTrackerValidLap;
                    float time = invalidated ? InvalidQualifyingTime(qualifyingPhase) : (hasCapturedValidLap ? capturedTime : PlayerParticipant.lapTracker.BestLapTime);
                    entry.invalidated = invalidated;
                    entry.participant = PlayerParticipant;
                    SetQualifyingPhaseTime(entry, qualifyingPhase, time);
                    SetPlayerQualifyingSectors(entry, qualifyingPhase, time, invalidated);
                }
                else
                {
                    if (GetQualifyingPhaseTime(entry, qualifyingPhase) <= 0f)
                    {
                        // Prefer the lap the AI car ACTUALLY drove. The AI now run the
                        // session on track (see SpawnRaceGrid), so a real, physics-driven
                        // best lap exists for anyone who completed one - and it responds
                        // to traffic, tows, weather and mistakes the way the player's does.
                        // The simulated time is now only the fallback for a car that set
                        // no valid lap, and for the fully-simulated weekend path.
                        RaceParticipant car = entry.participant;
                        float drivenBest = car != null && car.lapTracker != null && car.lapTracker.ValidLapsCompleted > 0
                            ? car.lapTracker.BestLapTime
                            : 0f;

                        SetAiQualifyingPhaseTime(
                            entry,
                            qualifyingPhase,
                            drivenBest > 0f ? drivenBest : SimulateAiQualifyingTime(entry, qualifyingPhase));
                    }
                }
            }

            ApplyQualifyingElimination(active, qualifyingPhase);
        }

        void FinishQualifying()
        {
            IsRaceFinished = true;
            lastQualifyingResultWasSimulated = false;
            Time.timeScale = 1f;
            List<QualifyingResultEntry> results = BuildFinalQualifyingResults();
            lastQualifyingResults = results;
            if (IsCareerRace)
            {
                Career.ApplyQualifyingResults(EventData, results);
            }

            QualifyingResultEntry playerQualifying = results.Find(entry => entry.isPlayer);
            if (playerQualifying != null)
            {
                PlayerRecordsStore.RecordQualifyingResult(playerQualifying.position, EventData != null ? EventData.trackId : null);
            }

            LogAiQualifyingDiagnostics(results, playerQualifying);
            if (!ProductionSessionUi.TryShowQualifyingResults(results, IsCareerRace))
            {
                ProductionSessionUi.BeginResults();
                ui.ShowQualifyingResults(this, results, IsCareerRace);
            }
        }

        // Qualifying-side counterpart to LogAiDiagnostics: a one-shot internal log
        // comparing the player's actual/simulated time against the fastest AI and the
        // field median, so a balance pass can be checked from the log alone. Plain
        // GameLog only, never player-visible.
        void LogAiQualifyingDiagnostics(List<QualifyingResultEntry> results, QualifyingResultEntry playerQualifying)
        {
            if (results == null || results.Count == 0)
            {
                return;
            }

            List<float> aiTimes = new List<float>();
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].isPlayer && results[i].bestLapTime > 0f)
                {
                    aiTimes.Add(results[i].bestLapTime);
                }
            }

            aiTimes.Sort();
            float fastestAi = aiTimes.Count > 0 ? aiTimes[0] : 0f;
            float medianAi = aiTimes.Count > 0 ? aiTimes[aiTimes.Count / 2] : 0f;

            GameLog.Info("[AIQualifyingDiagnostics] difficulty=" + Settings.Difficulty +
                         " playerTime=" + (playerQualifying != null && playerQualifying.bestLapTime > 0f ? UiFactory.FormatTime(playerQualifying.bestLapTime) : "--") +
                         " playerPosition=" + (playerQualifying != null ? "P" + playerQualifying.position : "--") +
                         " aiFastest=" + (fastestAi > 0f ? UiFactory.FormatTime(fastestAi) : "--") +
                         " aiMedian=" + (medianAi > 0f ? UiFactory.FormatTime(medianAi) : "--") +
                         " fieldSize=" + results.Count);
        }

        List<QualifyingResultEntry> BuildFinalQualifyingResults()
        {
            EnsureQualifyingPhaseComplete(1);
            EnsureQualifyingPhaseComplete(2);
            EnsureQualifyingPhaseComplete(3);

            List<QualifyingResultEntry> results = new List<QualifyingResultEntry>();
            List<QualifyingSimEntry> q3 = qualifyingEntries.FindAll(item => string.IsNullOrEmpty(item.eliminatedIn));
            q3.Sort((a, b) => GetQualifyingPhaseTime(a, 3).CompareTo(GetQualifyingPhaseTime(b, 3)));
            AppendQualifyingResults(results, q3, "");

            List<QualifyingSimEntry> q2Eliminated = qualifyingEntries.FindAll(item => item.eliminatedIn == "Q2");
            q2Eliminated.Sort((a, b) => GetQualifyingPhaseTime(a, 2).CompareTo(GetQualifyingPhaseTime(b, 2)));
            AppendQualifyingResults(results, q2Eliminated, "Q2");

            List<QualifyingSimEntry> q1Eliminated = qualifyingEntries.FindAll(item => item.eliminatedIn == "Q1");
            q1Eliminated.Sort((a, b) => GetQualifyingPhaseTime(a, 1).CompareTo(GetQualifyingPhaseTime(b, 1)));
            AppendQualifyingResults(results, q1Eliminated, "Q1");

            for (int i = 0; i < results.Count; i++)
            {
                results[i].position = i + 1;
            }

            return results;
        }

        void EnsureQualifyingPhaseComplete(int phase)
        {
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(phase);
            if (active.Count == 0)
            {
                return;
            }

            for (int i = 0; i < active.Count; i++)
            {
                if (GetQualifyingPhaseTime(active[i], phase) <= 0f)
                {
                    if (active[i].isPlayer)
                    {
                        SetQualifyingPhaseTime(active[i], phase, InvalidQualifyingTime(phase));
                        active[i].invalidated = true;
                        SetPlayerQualifyingSectors(active[i], phase, GetQualifyingPhaseTime(active[i], phase), true);
                    }
                    else
                    {
                        SetAiQualifyingPhaseTime(active[i], phase, SimulateAiQualifyingTime(active[i], phase));
                    }
                }
            }

            ApplyQualifyingElimination(active, phase);
        }

        List<QualifyingSimEntry> ActiveQualifyingEntries(int phase)
        {
            if (phase == 1)
            {
                return new List<QualifyingSimEntry>(qualifyingEntries);
            }

            return qualifyingEntries.FindAll(item => string.IsNullOrEmpty(item.eliminatedIn) && GetQualifyingPhaseTime(item, phase - 1) > 0f);
        }

        void ApplyQualifyingElimination(List<QualifyingSimEntry> active, int phase)
        {
            active.Sort((a, b) => GetQualifyingPhaseTime(a, phase).CompareTo(GetQualifyingPhaseTime(b, phase)));
            string phaseLabel = "Q" + phase;
            for (int i = 0; i < active.Count; i++)
            {
                // Never overwrite a driver who was eliminated in a DIFFERENT phase.
                //
                // BuildFinalQualifyingResults calls EnsureQualifyingPhaseComplete(1)
                // first, and ActiveQualifyingEntries(1) returns a copy of EVERY
                // entry - including drivers already knocked out in Q2. This loop then
                // stamped session = "Q1" and finalTime = their Q1 time over the top.
                // The Q2 pass could not repair it either, because
                // ActiveQualifyingEntries(2) filters out anything already eliminated.
                // The published result took its TIME from Q1 while its ORDER came
                // from Q2, so P11-P16 showed times that were slower than they should
                // be, tagged "Q1", and not even in ascending order - and career
                // storage recorded the same wrong times.
                if (!string.IsNullOrEmpty(active[i].eliminatedIn) && active[i].eliminatedIn != phaseLabel)
                {
                    continue;
                }

                active[i].session = phaseLabel;
                active[i].finalTime = GetQualifyingPhaseTime(active[i], phase);
            }

            if (phase >= 3)
            {
                return;
            }

            int eliminateCount = QualifyingEliminationCount(phase, active.Count);
            if (eliminateCount <= 0)
            {
                return;
            }

            for (int i = active.Count - eliminateCount; i < active.Count; i++)
            {
                active[i].eliminatedIn = "Q" + phase;
            }
        }

        int QualifyingEliminationCount(int phase, int activeCount)
        {
            return QualifyingProgression.EliminationCount(phase, activeCount);
        }

        void AppendQualifyingResults(List<QualifyingResultEntry> results, List<QualifyingSimEntry> entries, string eliminatedIn)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                results.Add(new QualifyingResultEntry
                {
                    driverId = entries[i].driverId,
                    driverName = entries[i].driverName,
                    teamId = entries[i].teamId,
                    bestLapTime = entries[i].finalTime,
                    isPlayer = entries[i].isPlayer,
                    invalidated = entries[i].invalidated,
                    session = entries[i].session,
                    eliminatedIn = eliminatedIn
                });
            }
        }

        public void PrepareNewQualifyingWeekend()
        {
            CleanupRaceWorld();
            qualifyingPhase = 1;
            qualifyingEntries.Clear();
            preserveQualifyingState = false;
            qualifyingTransitionPending = false;
            qualifyingTransitionFinish = false;
            qualifyingTransitionTimer = 0f;
            QualifyingFeedbackText = "";
            lastQualifyingResultWasSimulated = false;
            SimQualifyingExplanation = "";
            ResetPlayerQualifyingCaptures();
            ResetQualifyingSectorState();
        }

    }
}
