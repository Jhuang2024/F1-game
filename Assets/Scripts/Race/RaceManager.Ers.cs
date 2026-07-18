using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager AI ERS-deployment subsystem (partial). Decides when the AI
    /// deploys ERS based on corner severity, the per-tier decision-quality profile
    /// and the situational context. Split out of the RaceManager monolith verbatim
    /// - same class, same members, identical thresholds and RNG call order; the
    /// AiDifficultyProfile struct stays in the AiProfiles partial (same class), and
    /// the public entry point stays public so AI callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        // [ErsDiag] rolling sample (per report - "im convinced the ai either
        // dont have ERS at all or arent using it"): every AI asks this
        // function every frame, so sampling here gives a complete picture of
        // real deployment. One visible summary line every ~45s: what fraction
        // of AI-frames actually deployed, and the field's average battery.
        // Near-zero deploy % or a battery pinned at 100% would prove the
        // report; healthy numbers prove the system and point the perception
        // gap elsewhere.
        int ersDiagSamples;
        int ersDiagDeploys;
        float ersDiagBatterySum;
        float ersDiagLastLogTime;

        public bool ShouldAiUseErs(RaceParticipant participant, float cornerSeverity)
        {
            bool deploy = ShouldAiUseErsInternal(participant, cornerSeverity);
            if (participant != null && participant.vehicle != null)
            {
                // Strategy-dial parity (see VehicleController's AI mode block):
                // run the same Harvest/Balanced/Attack dial the player has,
                // with the policy a competent player uses - Attack in a live
                // fight or the closing stages (harder punch when it matters),
                // Harvest when the battery is low with nothing on (bank
                // charge), Balanced otherwise. Same multiplier values as the
                // player's settings dial.
                float modeBattery = participant.vehicle.ErsBattery;
                bool fight = GetIntervalToAheadSeconds(participant) < 1.6f || FindCarBehind(participant, 70f) != null;
                bool closingStagesMode = CurrentSession != RaceWeekendSession.Qualifying && participant.lapTracker != null &&
                                         participant.lapTracker.CompletedLaps >= RaceLaps - 2;
                if (fight || closingStagesMode)
                {
                    participant.vehicle.SetAiErsMode(0.8f, 1.2f);
                }
                else if (modeBattery < 0.35f)
                {
                    participant.vehicle.SetAiErsMode(1.9f, 0.82f);
                }
                else
                {
                    participant.vehicle.SetAiErsMode(1f, 1f);
                }
            }

            if (participant != null && participant.vehicle != null && CurrentSession != RaceWeekendSession.Qualifying)
            {
                ersDiagSamples++;
                if (deploy)
                {
                    ersDiagDeploys++;
                }

                ersDiagBatterySum += participant.vehicle.ErsBattery;
                if (Time.time - ersDiagLastLogTime > 45f && ersDiagSamples > 200)
                {
                    Debug.Log("[ErsDiag] AI ERS over the last " + (Time.time - ersDiagLastLogTime).ToString("0") + "s: deploying on " +
                              (100f * ersDiagDeploys / ersDiagSamples).ToString("0.0") + "% of AI-frames, average battery " +
                              (100f * ersDiagBatterySum / ersDiagSamples).ToString("0") + "%.");
                    ersDiagLastLogTime = Time.time;
                    ersDiagSamples = 0;
                    ersDiagDeploys = 0;
                    ersDiagBatterySum = 0f;
                }
            }

            return deploy;
        }

        bool ShouldAiUseErsInternal(RaceParticipant participant, float cornerSeverity)
        {
            if (participant == null || participant.vehicle == null)
            {
                return false;
            }

            float battery = participant.vehicle.ErsBattery;
            if (cornerSeverity > 0.24f || battery < 0.18f)
            {
                return false;
            }

            // ERS deployment is disabled while this car runs under any caution -
            // nobody is racing for position, so there is nothing to spend it on -
            // and it comes back the moment the green flag flies, where a strong
            // launch matters. FlagRules owns the flag consequence; the second
            // test adds the sector-wide local-yellow scope the passing ban uses.
            if (!FlagRules.OvertakingAllowed(FlagForParticipant(participant)) ||
                IsOvertakingRestrictedForParticipant(participant))
            {
                return false;
            }

            AiDifficultyProfile profile = GetAiDifficultyProfile();
            int awareness = participant.driverData == null ? 78 : participant.driverData.awareness;
            // Awareness-modulated racecraft deploy quality is the engine-free
            // AiErsRules; the live reads and the Random roll below stay here.
            float ersQuality = AiErsRules.RacecraftDeployQuality(profile.ersDeploymentQuality, awareness);

            bool finalLap = CurrentSession != RaceWeekendSession.Qualifying && participant.lapTracker != null && participant.lapTracker.CompletedLaps >= RaceLaps - 1;
            float normalized = participant.lapTracker == null ? 0f : (State == null ? participant.lapTracker.CurrentProgress.normalized : State.GetCurrentProgress(participant).normalized);
            // Battery-economy fix ([ErsDiag] report - AI deploying 17-48% of
            // frames yet average battery pinned at ~19%, right at the deploy
            // floor): "always spend it coming home" was meant for the END OF
            // THE RACE, but this flag tested only lap position - so it fired
            // across the last THIRD of EVERY lap, unconditionally draining the
            // battery to the floor once per lap. The AI lived hand-to-mouth
            // with nothing banked for attacks or defence - "their ERS isn't as
            // good as mine" was exactly right: same hardware, bankrupt
            // management. The coming-home burn now starts at the final sector
            // of the PENULTIMATE lap (flowing into the finalLap bypass), so
            // for the rest of the race deployment is strategic (attacking /
            // defending / push-lap with >50% banked / near-full battery) and
            // the battery actually cycles.
            bool closingStages = CurrentSession != RaceWeekendSession.Qualifying && participant.lapTracker != null &&
                                 participant.lapTracker.CompletedLaps >= RaceLaps - 2 && normalized > 0.68f;
            bool batteryHigh = battery > 0.85f;

            // Never hoard a near-full battery, and always spend it coming home - these
            // are decisions even a weak driver gets right, so they bypass the quality
            // gate entirely.
            if (batteryHigh || finalLap || closingStages)
            {
                return true;
            }

            float aheadInterval = GetIntervalToAheadSeconds(participant);
            RaceParticipant behind = FindCarBehind(participant, 70f);
            bool attacking = aheadInterval < 1.6f;
            bool behindHasDrs = behind != null && IsDrsAvailable(behind);
            bool isExpert = IsExpertDifficulty;

            // Part A.4: Expert's defend trigger is far more sensitive - a chasing car
            // with DRS, a healthily-charged battery, or simply closing fast all count
            // as a real threat, not only a comfortably-charged battery alone.
            bool closingFast = isExpert && behind != null && behind.vehicle != null &&
                (Mathf.Abs(behind.vehicle.CurrentSpeedKph) - Mathf.Abs(participant.vehicle.CurrentSpeedKph)) > 6f;
            float defendBatteryThreshold = isExpert ? 0.15f : 0.32f;
            bool defending = behind != null && (battery > defendBatteryThreshold || behindHasDrs || closingFast);

            if (!attacking && !defending)
            {
                // Push-lap deploy: a real driver spends ERS on a clear straight with
                // battery to spare generally, not only while directly racing someone.
                // Kept modest and scaled by difficulty so it never becomes constant spam.
                if (battery > 0.5f)
                {
                    // Part A.2: Expert-only, deterministic - a push-lap deploy with
                    // battery to spare is an obvious call, not a coin flip.
                    return isExpert || Random.value < AiErsRules.PushLapDeployChance(profile.ersDeploymentQuality);
                }

                return false;
            }

            // Racecraft calls (attack/defend timing) are where difficulty and driver
            // awareness actually show up: Expert nails them almost every time, Easy
            // fluffs a meaningful share. Part A.2: Expert is fully deterministic here
            // - once attacking/defending is true the condition itself is the decision,
            // not a dice roll on top of it.
            return isExpert || Random.value < ersQuality;
        }

    }
}
