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
        float ersDiagDeployThrottleSum;
        float ersDiagDeploySpeedSum;
        float ersDiagDeployCeilingSum;
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
                // Measure the ACTUAL physics deploy (ErsDeploying), not the
                // strategy vote: the strategy `deploy` is true on grip-limited
                // frames too (braking/tight corner), where the AI-side gate then
                // SUPPRESSES the real deploy - counting those dragged the "avg
                // throttle while deploying" down and misreported the deploy rate.
                // ErsDeploying is the ground truth of what the boost force saw.
                if (participant.vehicle.ErsDeploying)
                {
                    ersDiagDeploys++;
                    ersDiagDeployThrottleSum += participant.vehicle.EffectiveThrottle;
                    ersDiagDeploySpeedSum += Mathf.Abs(participant.vehicle.CurrentSpeedKph);
                    // The ERS-boosted top-speed ceiling the governor is allowing
                    // right now. Its gap over avg deploy speed is the headroom the
                    // car still has to climb into on a longer straight - proof the
                    // +ErsTopSpeedBonusKph is live in the ceiling, not absorbed.
                    ersDiagDeployCeilingSum += participant.vehicle.TargetTopSpeedKph;
                }

                ersDiagBatterySum += participant.vehicle.ErsBattery;
                if (Time.time - ersDiagLastLogTime > 45f && ersDiagSamples > 200)
                {
                    Debug.Log("[ErsDiag] AI ERS over the last " + (Time.time - ersDiagLastLogTime).ToString("0") + "s: actually deploying on " +
                              (100f * ersDiagDeploys / ersDiagSamples).ToString("0.0") + "% of AI-frames, average battery " +
                              (100f * ersDiagBatterySum / ersDiagSamples).ToString("0") + "%, while deploying avg throttle " +
                              (ersDiagDeploys > 0 ? (ersDiagDeployThrottleSum / ersDiagDeploys).ToString("0.00") : "n/a") +
                              " avg speed " + (ersDiagDeploys > 0 ? (ersDiagDeploySpeedSum / ersDiagDeploys).ToString("0") : "n/a") +
                              "kph toward a boosted ceiling of " + (ersDiagDeploys > 0 ? (ersDiagDeployCeilingSum / ersDiagDeploys).ToString("0") : "n/a") +
                              "kph (the ceiling sits +30 over the car's base top speed while deploying - that gap over avg speed is the pull the car climbs into on a straight).");
                    ersDiagLastLogTime = Time.time;
                    ersDiagSamples = 0;
                    ersDiagDeploys = 0;
                    ersDiagBatterySum = 0f;
                    ersDiagDeployThrottleSum = 0f;
                    ersDiagDeploySpeedSum = 0f;
                    ersDiagDeployCeilingSum = 0f;
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
            // Geometry gate widened 0.24 -> 0.62 (per report - AI never using ERS
            // in the many high/medium-speed corners taken flat out). The real
            // "is the car grip-limited here" test now lives at the AI call site
            // (gripLimitedForErs: braking / >30deg tight corner / actively
            // sliding), which correctly lets a flat fast/medium corner deploy.
            // This retained cut only short-circuits the strategy work for
            // genuinely slow corners (a hairpin still won't reach here), so the
            // decision below stays about STRATEGY, not a blanket corner veto.
            if (cornerSeverity > 0.62f || battery < 0.18f)
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
            // Defend-deploy round 2 (per request - "make it so that AI use ERS
            // when defending as well"): the closing-attacker detection was
            // Expert-ONLY, and the non-Expert battery threshold (0.32) meant
            // lower tiers mostly ignored a live attack unless the attacker had
            // DRS. Every tier now reacts to a genuinely closing car, with the
            // detection threshold scaled by ERS craft (a sharp driver responds
            // to a ~4kph closure, a weak one only notices a ~10kph rush), and
            // the defend battery bar is low enough that banked charge (see the
            // economy fix) actually gets spent on the defence it was banked
            // for. Skill still decides the final call via the ersQuality roll
            // below - Expert stays deterministic.
            bool closingFast = behind != null && behind.vehicle != null &&
                (Mathf.Abs(behind.vehicle.CurrentSpeedKph) - Mathf.Abs(participant.vehicle.CurrentSpeedKph)) > Mathf.Lerp(10f, 4f, ersQuality);
            float defendBatteryThreshold = isExpert ? 0.15f : 0.22f;
            bool defending = behind != null && (battery > defendBatteryThreshold || behindHasDrs || closingFast);

            if (!attacking && !defending)
            {
                // Bank-and-dump discretionary deploy ([ErsDiag] report - "i still
                // dont feel it": telemetry showed the field's battery pinned at
                // ~20%, right at the deploy floor, because the AI dribbled ERS
                // away on every straight and flat corner and never banked a real
                // reserve to spend decisively). When NOT in a fight, the AI now
                // HOLDS charge until it has a genuine reserve, then dumps it in a
                // burst on a real straight - so an alone car keeps a healthy
                // battery AND, crucially, still has a full charge to unload the
                // moment the player catches it (the attacking/defending branch
                // above deploys freely down to the floor and through flat
                // corners - that is the ERS the player actually races against
                // and feels). Hysteresis via the live deploy state: needs a 0.6
                // reserve to START a burst, then runs it down to 0.32; restricted
                // to genuine straights so the dump lands where it is visible.
                bool alreadyDeploying = participant.vehicle.ErsDeploying;
                float pushLapFloor = alreadyDeploying ? 0.32f : 0.6f;
                if (battery > pushLapFloor && cornerSeverity < 0.2f)
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
