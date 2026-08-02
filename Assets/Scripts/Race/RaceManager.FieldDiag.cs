using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager field-health diagnostics (partial). One compact line per lap
    /// answering the question a play report cannot: when the AI is a long way back,
    /// is the field SLOW or is it BROKEN?
    ///
    /// The existing recorders each answer a slice - [PaceDiag] lap times, [CornerDiag]
    /// where time goes, [WallDiag] individual impacts, [PitStopDiag] each stop - so
    /// diagnosing "the AI are way too easy" meant correlating four logs by hand. This
    /// prints the whole field's state on one line, every lap: pace against the player,
    /// how much damage the field is carrying, how many cars are hitting things, how
    /// many stops have been taken and how much tyre life is left.
    ///
    /// Reading it:
    ///   - big gap, damage near zero, few stops  -> genuinely slow. Look at pace.
    ///   - big gap, damage climbing, wall hits up -> crashing. Look at [WallDiag].
    ///   - big gap, stops climbing every lap      -> a strategy/tyre-life problem.
    /// </summary>
    public partial class RaceManager
    {
        int fieldDiagLastLap = -1;

        void LogFieldHealthDiagnostics()
        {
            if (!IsScoredRaceSession || PlayerParticipant == null || PlayerParticipant.lapTracker == null)
            {
                return;
            }

            int lap = PlayerParticipant.lapTracker.CompletedLaps;
            if (lap == fieldDiagLastLap || lap < 1)
            {
                return;
            }

            fieldDiagLastLap = lap;

            int aiCount = 0;
            int retired = 0;
            int damaged = 0;
            int flagged = 0;
            int stops = 0;
            int offTrack = 0;
            int pacing = 0;
            int limited = 0;
            int inPit = 0;
            int lapSamples = 0;
            float damageSum = 0f;
            float wearSum = 0f;
            float suspensionSum = 0f;
            float speedSum = 0f;
            float lapSum = 0f;
            float worstGapSeconds = 0f;

            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant p = Participants[i];
                if (p == null || p.isPlayer)
                {
                    continue;
                }

                aiCount++;
                stops += p.pitStops;
                if (p.retired)
                {
                    retired++;
                    continue;
                }

                if (p.blackOrangeShown)
                {
                    flagged++;
                }

                if (p.vehicle != null && p.vehicle.Damage != null)
                {
                    float percent = p.vehicle.Damage.OverallPercent;
                    damageSum += percent;
                    suspensionSum += p.vehicle.Damage.suspension;
                    if (percent > 20f)
                    {
                        damaged++;
                    }
                }

                if (p.vehicle != null && p.vehicle.Tyres != null)
                {
                    wearSum += p.vehicle.Tyres.Wear;
                }

                if (p.vehicle != null)
                {
                    speedSum += Mathf.Abs(p.vehicle.CurrentSpeedKph);
                    if (p.vehicle.IsOffTrackSlowdown)
                    {
                        offTrack++;
                    }

                    if (p.vehicle.PitLimiterActive)
                    {
                        limited++;
                    }
                }

                if (p.isRaceControlAutopilot)
                {
                    pacing++;
                }

                if (p.pitPhase != PitPhase.None || p.isPitting)
                {
                    inPit++;
                }

                if (p.lapTracker != null && p.lapTracker.LastLapTime > 0f)
                {
                    lapSum += p.lapTracker.LastLapTime;
                    lapSamples++;
                }

                worstGapSeconds = Mathf.Max(worstGapSeconds, GapSecondsBetween(PlayerParticipant, p));
            }

            if (aiCount == 0)
            {
                return;
            }

            int running = Mathf.Max(1, aiCount - retired);
            float playerLap = PlayerParticipant.lapTracker.LastLapTime;
            float aiLap = lapSamples > 0 ? lapSum / lapSamples : 0f;
            Debug.LogWarning(
                "[FieldDiag] lap " + lap + "/" + RaceLaps +
                // PACE - the first question. A big deficit here with everything else
                // clean means the field is genuinely slow, not broken.
                " playerLap=" + playerLap.ToString("0.00") +
                " aiAvgLap=" + aiLap.ToString("0.00") +
                " deficit=" + (aiLap > 0f && playerLap > 0f ? (aiLap - playerLap).ToString("0.00") : "n/a") + "s" +
                " aiAvgSpeed=" + (speedSum / running).ToString("0") + "kph" +
                // STATE - the second question. Any of these being non-zero for more
                // than a moment is a car not racing, and explains a deficit without
                // any of it being about pace.
                " offTrack=" + offTrack +
                " limiterOn=" + limited +
                " rcPacing=" + pacing +
                " inPit=" + inPit +
                " retired=" + retired +
                // CONDITION - the third question.
                " ai=" + aiCount +
                " blackOrange=" + flagged +
                " stops=" + stops +
                " avgDamage=" + (damageSum / running).ToString("0.0") + "%" +
                " damagedOver20%=" + damaged +
                " avgSuspension=" + (suspensionSum / running).ToString("0.00") +
                " avgTyreLifeLeft=" + (wearSum / running * 100f).ToString("0") + "%" +
                " worstGapToPlayer=" + worstGapSeconds.ToString("0.0") + "s");
        }
    }
}
