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
        public bool ShouldAiUseErs(RaceParticipant participant, float cornerSeverity)
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
            float ersQuality = Mathf.Clamp01(profile.ersDeploymentQuality * Mathf.Lerp(0.8f, 1.08f, awareness / 100f));

            bool finalLap = CurrentSession != RaceWeekendSession.Qualifying && participant.lapTracker != null && participant.lapTracker.CompletedLaps >= RaceLaps - 1;
            float normalized = participant.lapTracker == null ? 0f : (State == null ? participant.lapTracker.CurrentProgress.normalized : State.GetCurrentProgress(participant).normalized);
            bool finalSector = normalized > 0.68f;
            bool batteryHigh = battery > 0.85f;

            // Never hoard a near-full battery, and always spend it coming home - these
            // are decisions even a weak driver gets right, so they bypass the quality
            // gate entirely.
            if (batteryHigh || finalLap || finalSector)
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
                    return isExpert || Random.value < profile.ersDeploymentQuality * 0.5f;
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
