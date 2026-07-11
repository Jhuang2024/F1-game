using F1Game.Core.Events;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager retirement + fuel-state subsystem (partial). Retires a car
    /// (reason, event publish, timeline, HUD) and runs the per-frame fuel-state
    /// tick that drains the tank and triggers a fuel-starvation retirement once the
    /// grace timer elapses. Split out of the RaceManager monolith verbatim - same
    /// class, same members, identical drain/grace timing and call order; the public
    /// RetireParticipant stays public so external callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        public void RetireParticipant(RaceParticipant participant, string reason)
        {
            if (participant == null || participant.retired || participant.finished || CurrentSession == RaceWeekendSession.Qualifying || State == null)
            {
                return;
            }

            participant.retired = true;
            participant.retirementReason = string.IsNullOrEmpty(reason) ? "Damage" : reason;
            GameEvents.Publish(new RetirementEvent(participant.driverId, participant.retirementReason));
            float retiredTime = RaceElapsed + 9999f + Mathf.Max(0f, RaceLaps - (participant.lapTracker == null ? 0 : participant.lapTracker.CompletedLaps)) * 120f;
            State.OnParticipantFinished(participant, retiredTime);
            if (string.IsNullOrEmpty(participant.penaltyReason))
            {
                participant.penaltyReason = "DNF " + participant.retirementReason;
            }
            else if (!participant.penaltyReason.Contains("DNF"))
            {
                participant.penaltyReason += ", DNF " + participant.retirementReason;
            }

            if (participant.vehicle != null)
            {
                participant.vehicle.SetCommand(new VehicleCommand { brake = 1f });
                participant.vehicle.SetGridHold(true);
            }

            // A retired car can never move again, so it must be dropped off
            // the pit rail immediately - IsRailRolling/FindBayReleaseBlocker
            // ignore phase-None cars, so nothing behind it can queue on it.
            participant.pitPhase = PitPhase.None;
            participant.hasPitGuideState = false;
            participant.pitAwaitingRelease = false;
            participant.pitLaneHeldByOccupancy = false;

            participant.gameObject.SetActive(false);
            if (participant.isPlayer)
            {
                SessionMessage = "Retired: " + participant.retirementReason;
            }
        }

        // Fuel system pass: keeps VehicleController's fuel projection current
        // (remaining laps, including the fractional lap in progress, so the HUD/AI
        // read a live "will this fuel actually make it" figure) and handles the
        // running-out-of-fuel DNF. Deliberately does NOT retire the instant fuel
        // hits zero - VehicleController.FuelStarved gives a short grace period
        // (FuelStarvedGraceSeconds) of crawling on starvation power first, so the
        // player feels the consequence before the car actually parks.
        const float FuelStarvedGraceSeconds = 11f;

        void UpdateFuelState(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null)
            {
                return;
            }

            float remainingLaps = Mathf.Max(0f, RaceLaps - (participant.lapTracker.CompletedLaps + participant.lapTracker.CurrentProgress.normalized));
            participant.vehicle.UpdateFuelProjection(remainingLaps);

            if (!participant.vehicle.FuelStarved || participant.retired || participant.finished || participant.fuelStarvationRetirementApplied)
            {
                return;
            }

            if (participant.vehicle.FuelStarvedTimer >= FuelStarvedGraceSeconds)
            {
                participant.fuelStarvationRetirementApplied = true;
                RetireParticipant(participant, "Fuel starvation");
            }
        }

    }
}
