using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager quick-sim qualifying subsystem (partial). Runs a whole
    /// qualifying weekend in one pass without live driving (SimulateQualifyingWeekend),
    /// builds the human-readable explanation of the player's simulated result, and
    /// the Q1/Q2 cutoff-time and signed-seconds helpers. Split out of the RaceManager
    /// monolith verbatim - same class, same members, identical RNG call order and
    /// the sim model; the sector-colour / lap-cap consts and the sim nested types
    /// stay in main and resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        public void SimulateQualifyingWeekend(
            GameDataRepository repository,
            CareerManager career,
            GameSettingsStore settings,
            RuntimeUi runtimeUi,
            CalendarEventData eventData,
            string playerName,
            string playerTeamId,
            bool careerRace)
        {
            CleanupRaceWorld();
            Data = repository;
            Career = career;
            Settings = settings;
            ui = runtimeUi;
            EventData = eventData;
            IsCareerRace = careerRace;
            CurrentSession = RaceWeekendSession.Qualifying;
            IsRaceFinished = true;
            IsPaused = false;
            StartCountdown = 0f;
            raceStartTime = Time.time;
            qualifyingTransitionPending = false;
            qualifyingTransitionTimer = 0f;
            qualifyingTransitionFinish = true;
            QualifyingFeedbackText = "";
            SessionMessage = "Sim qualifying complete";
            lastQualifyingResultWasSimulated = true;
            qualifyingPhase = 1;
            qualifyingEntries.Clear();
            SimQualifyingExplanation = "";
            for (int i = 0; i < playerSimBreakdowns.Length; i++)
            {
                playerSimBreakdowns[i] = null;
            }

            ResetPlayerQualifyingCaptures();
            ResetQualifyingSectorState();

            raceWorld = new GameObject("Runtime simulated qualifying world");
            TrackManager trackManager = new GameObject("Track Manager").AddComponent<TrackManager>();
            trackManager.transform.SetParent(raceWorld.transform);
            Track = trackManager.Build(eventData, false);
            SimpleAudioManager.SetRain(Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain);

            BuildSimulatedQualifyingField(playerName, playerTeamId);

            // Deterministic seed: the same weekend simulated twice produces the same
            // session, so results are reproducible and debuggable rather than dice.
            Random.State previousRandomState = Random.state;
            int seasonPart = Career != null && Career.Save != null ? Career.Save.currentSeason * 8887 + Career.Save.currentRound * 331 : 17;
            int trackPart = eventData != null && !string.IsNullOrEmpty(eventData.trackId) ? eventData.trackId.GetHashCode() : 0;
            int teamPart = string.IsNullOrEmpty(playerTeamId) ? 0 : playerTeamId.GetHashCode();
            Random.InitState(seasonPart ^ (trackPart * 31) ^ teamPart);

            for (int phase = 1; phase <= 3; phase++)
            {
                qualifyingPhase = phase;
                ResetQualifyingSectorState();
                List<QualifyingSimEntry> active = ActiveQualifyingEntries(phase);
                for (int i = 0; i < active.Count; i++)
                {
                    if (GetQualifyingPhaseTime(active[i], phase) > 0f)
                    {
                        continue;
                    }

                    if (active[i].isPlayer)
                    {
                        SetSimulatedPlayerQualifyingPhaseTime(active[i], phase, SimulatePlayerQualifyingTime(active[i], phase));
                    }
                    else
                    {
                        SetAiQualifyingPhaseTime(active[i], phase, SimulateAiQualifyingTime(active[i], phase));
                    }
                }

                ApplyQualifyingElimination(active, phase);

                // Per-phase summary diagnostic ([QualiSim] set): the per-run
                // logs only ever show the PLAYER's own lap terms - a "how did I
                // end up P20" report also needs where the CUT was and whether
                // the player cleared it, in the same console stream.
                QualifyingSimEntry playerPhaseEntry = qualifyingEntries.Find(item => item.isPlayer);
                if (playerPhaseEntry != null)
                {
                    float playerPhaseTime = GetQualifyingPhaseTime(playerPhaseEntry, phase);
                    Debug.Log("[QualiSim] Q" + phase + " summary: playerTime=" +
                              (playerPhaseTime > 0f ? playerPhaseTime.ToString("0.000") : "none") +
                              " tyre=" + (Settings != null ? Settings.SelectedTyreCompound.ToString() : "?") +
                              " cutoff=" + QualifyingCutoffTime(phase).ToString("0.000") +
                              " eliminated=" + (string.IsNullOrEmpty(playerPhaseEntry.eliminatedIn) ? "no" : playerPhaseEntry.eliminatedIn));
                }
            }

            Random.state = previousRandomState;

            List<QualifyingResultEntry> results = BuildFinalQualifyingResults();
            lastQualifyingResults = results;
            SimQualifyingExplanation = BuildSimQualifyingExplanation(results);
            if (IsCareerRace && Career != null)
            {
                Career.ApplyQualifyingResults(EventData, results);
            }

            if (!ProductionSessionUi.TryShowQualifyingResults(results, IsCareerRace))
            {
                ProductionSessionUi.BeginResults();
                ui.ShowQualifyingResults(this, results, IsCareerRace);
            }
        }

        // Full transparency for the simulated player lap: every contribution to the
        // final time, plus the exact elimination reason if the player went out.
        public string SimQualifyingExplanation { get; private set; }

        string BuildSimQualifyingExplanation(List<QualifyingResultEntry> results)
        {
            QualifyingSimEntry player = qualifyingEntries.Find(item => item.isPlayer);
            if (player == null)
            {
                return "";
            }

            int decisivePhase = string.IsNullOrEmpty(player.eliminatedIn) ? 3 : int.Parse(player.eliminatedIn.Substring(1));
            QualifyingLapBreakdown breakdown = playerSimBreakdowns[Mathf.Clamp(decisivePhase, 1, 3) - 1];
            QualifyingResultEntry playerResult = results == null ? null : results.Find(entry => entry.isPlayer);
            int position = playerResult != null ? playerResult.position : 0;

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("YOUR ").Append("Q").Append(decisivePhase).Append(" LAP, ITEMIZED\n");
            if (breakdown != null)
            {
                text.Append("Circuit reference lap    ").Append(UiFactory.FormatTime(breakdown.baseLap)).Append("\n");
                text.Append("Car package              ").Append(SignedSeconds(breakdown.carEffect)).Append("\n");
                text.Append("Driver qualifying craft  ").Append(SignedSeconds(breakdown.driverEffect)).Append("\n");
                text.Append("AI difficulty setting    ").Append(SignedSeconds(breakdown.difficultyEffect)).Append("\n");
                text.Append("Track evolution (Q").Append(decisivePhase).Append(")     ").Append(SignedSeconds(breakdown.phaseEffect)).Append("\n");
                text.Append("Tyre preparation         ").Append(SignedSeconds(breakdown.tyrePrep)).Append("\n");
                text.Append("Tyre choice (").Append(Settings == null ? "Medium" : Settings.SelectedTyreCompound.ToString()).Append(")     ").Append(SignedSeconds(breakdown.tyreChoicePenalty)).Append("\n");
                text.Append("Weather                  ").Append(SignedSeconds(breakdown.weatherPenalty)).Append("\n");
                if (breakdown.mistakePenalty > 0.001f)
                {
                    string mistakeLabel = string.IsNullOrEmpty(breakdown.mistakeType) ? "mistake" : breakdown.mistakeType;
                    text.Append("Driver mistake           ").Append(SignedSeconds(breakdown.mistakePenalty)).Append("  (").Append(mistakeLabel).Append(")\n");
                }
                else
                {
                    text.Append("Driver mistake           clean lap\n");
                }

                text.Append("Natural variance         ").Append(SignedSeconds(breakdown.variance)).Append("\n");
                text.Append("FINAL LAP                ").Append(UiFactory.FormatTime(breakdown.finalTime)).Append("\n\n");
            }

            text.Append("Classified P").Append(position > 0 ? position.ToString() : "--");
            if (!string.IsNullOrEmpty(player.eliminatedIn))
            {
                float cutoff = QualifyingCutoffTime(decisivePhase);
                float playerTime = GetQualifyingPhaseTime(player, decisivePhase);
                text.Append("  |  ELIMINATED IN ").Append(player.eliminatedIn);
                if (cutoff > 0f && playerTime > 0f && playerTime < 9998f)
                {
                    text.Append("  (missed the cut by ").Append(Mathf.Max(0f, playerTime - cutoff).ToString("0.000")).Append("s)");
                }
                else if (playerTime >= 9998f)
                {
                    text.Append("  (no valid time set)");
                }
            }
            else
            {
                text.Append("  |  Advanced to the final shootout");
            }

            return text.ToString();
        }

        // Slowest surviving time in a phase: the reference a player had to beat.
        float QualifyingCutoffTime(int phase)
        {
            int survivors = phase == 1 ? Q1SurvivorCount : (phase == 2 ? Q2SurvivorCount : qualifyingEntries.Count);
            List<QualifyingSimEntry> ranked = new List<QualifyingSimEntry>();
            for (int i = 0; i < qualifyingEntries.Count; i++)
            {
                float time = GetQualifyingPhaseTime(qualifyingEntries[i], phase);
                if (time > 0f)
                {
                    ranked.Add(qualifyingEntries[i]);
                }
            }

            ranked.Sort((a, b) => GetQualifyingPhaseTime(a, phase).CompareTo(GetQualifyingPhaseTime(b, phase)));
            if (ranked.Count == 0 || survivors <= 0 || survivors > ranked.Count)
            {
                return 0f;
            }

            float cutoff = GetQualifyingPhaseTime(ranked[survivors - 1], phase);
            return cutoff >= 9998f ? 0f : cutoff;
        }

        static string SignedSeconds(float value)
        {
            return (value >= 0f ? "+" : "") + value.ToString("0.000") + "s";
        }

    }
}
