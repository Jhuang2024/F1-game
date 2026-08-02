using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager movable-aero subsystem (partial) for the 2026 regulations. Owns
    /// the activation-zone lookup, the wing (X-mode) availability gate, the Override
    /// Mode gap evaluation and energy budget, and the HUD state text. The pure policy
    /// lives in the engine-free ActiveAeroRules; this partial owns the live per-frame
    /// evaluation and the track queries.
    ///
    /// This used to be the DRS subsystem, with a whole detection-point eligibility
    /// machine behind it: cross a detection line, check you are within a second of the
    /// car ahead, and only then may the flap open, for that zone only. Under the 2026
    /// rules the movable wings are not an overtaking aid at all - the entire field has
    /// them in the activation zones - and the overtaking aid is Override Mode, which
    /// is extra electrical deployment and is not tied to a zone. The two are now
    /// modelled as the two separate things they are.
    /// </summary>
    public partial class RaceManager
    {
        /// <summary>
        /// Per-frame Override Mode bookkeeping: arms it when the car is within a
        /// second of the car ahead, drains its budget while it is being used, and
        /// refills the budget each lap. Replaces UpdateDrsEligibility, whose whole
        /// job - earning a zone at a detection point and holding that decision for
        /// the length of the zone - is a rule that no longer exists.
        /// </summary>
        void UpdateDrsEligibility(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || Track == null || participant.vehicle == null)
            {
                return;
            }

            if (participant.retired || participant.finished || participant.isPitting || participant.pitPhase != PitPhase.None)
            {
                participant.overrideArmed = false;
                return;
            }

            // Budget refills at the start of each lap, like the per-lap deployment
            // allowance it represents.
            int lap = participant.lapTracker.CompletedLaps;
            if (participant.overrideBudgetLap != lap)
            {
                participant.overrideBudgetLap = lap;
                participant.overrideEnergy01 = 1f;
            }

            bool isWet = Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain;
            bool qualiOrTt = CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial;
            float interval = qualiOrTt ? 999f : GetIntervalToAheadSeconds(participant);
            participant.overrideArmed = ActiveAeroRules.OverrideAvailable(
                isWet,
                drsRestartCooldownTimer > 0f,
                FlagRules.DrsAllowed(FlagForParticipant(participant)),
                qualiOrTt,
                lap,
                interval,
                participant.overrideEnergy01);

            // Deploying costs budget. The driver (or the AI) has to choose when to
            // spend it - held down all lap, there is nothing left for the move.
            if (participant.overrideArmed && participant.vehicle.ErsDeploying)
            {
                participant.overrideEnergy01 = Mathf.Max(
                    0f,
                    participant.overrideEnergy01 - ActiveAeroRules.OverrideDrainPerSecond01 * Time.deltaTime);
            }
        }

        /// <summary>
        /// True while this car is close enough to the car ahead to use Override Mode
        /// and still has budget for it. Read by VehicleController for the extra
        /// deployment and by the AI when deciding whether an attack is on.
        /// </summary>
        public bool IsOverrideAvailable(RaceParticipant participant)
        {
            return participant != null && participant.overrideArmed && CanDrive;
        }

        /// <summary>Override Mode budget left this lap, 0..1. HUD-facing.</summary>
        public float OverrideEnergyRemaining01(RaceParticipant participant)
        {
            return participant == null ? 0f : Mathf.Clamp01(participant.overrideEnergy01);
        }

        // ---- Track-query seam (Phase C consumer migration) ----------------
        // These consumers read the active ITrackQuery so an authored backend
        // can drop in per circuit once track construction itself is authored;
        // today the legacy adapter answers identically over the live
        // TrackRuntime. Null-safe: falls back to the direct runtime before the
        // provider is selected (e.g. qualifying warm-up before StartSession's
        // Select call, or if selection failed).

        int DrsZoneIndexAt(TrackProgress progress)
        {
            F1Game.Track.ITrackQuery query = TrackQueryProvider.Active;
            if (query != null)
            {
                int zone = query.DrsZoneAt(progress.distance);
                // The interface reports -1 for "no zone"; legacy call sites
                // treat 0 as "no zone" and 1/2 as the zone index.
                return zone < 0 ? 0 : zone;
            }

            return Track.GetDrsZoneIndex(progress.normalized);
        }

        /// <summary>
        /// Whether this car's movable wings may go to low-drag mode right now. Kept
        /// under the old name because every caller (HUD, AI, player input) is asking
        /// exactly this question, but the RULE is the 2026 one: no car-ahead gap, no
        /// earned zone eligibility, just "am I in an activation zone and is race
        /// control allowing movable aero".
        /// </summary>
        public bool IsDrsAvailable(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || Track == null)
            {
                return false;
            }

            if (!CanDrive)
            {
                return false;
            }

            // Movable-aero permission under flags is FlagRules' call. FlagForParticipant
            // folds the sector-local yellow (and its near-incident fallback window) into
            // the global flag, so a yellow shuts the wings immediately. The restart
            // cooldown is race-layer state (no movable aero for a spell after a restart)
            // layered on top.
            bool isWet = Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain;
            TrackProgress progress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            return ActiveAeroRules.WingModeAvailable(
                isWet,
                drsRestartCooldownTimer > 0f,
                FlagRules.DrsAllowed(FlagForParticipant(participant)),
                DrsZoneIndexAt(progress));
        }

        public string DrsStateText(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null || Track == null)
            {
                return "UNAVAILABLE";
            }

            // Flicker fix: this early-out used to ask Track.IsInDrsZone(normalized)
            // while IsDrsAvailable below asks DrsZoneIndexAt (the ITrackQuery seam,
            // distance-based). Two geometry sources answering "am I in a zone?"
            // can disagree for a frame at zone boundaries, which read as the pill
            // stuttering between states. One source (the same one availability
            // uses) now answers both.
            TrackProgress stateProgress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            if (DrsZoneIndexAt(stateProgress) == 0)
            {
                return "UNAVAILABLE";
            }

            // Show ACTIVE whenever the wing is genuinely open (DrsActive: button
            // latched, speed > 90, no brake). The boost is now strictly tied to
            // the open wing (see VehicleController - the old 15s post-activation
            // boost window is gone), so DrsActive alone is the whole truth.
            if (participant.vehicle.DrsActive)
            {
                return "ACTIVE";
            }

            return IsDrsAvailable(participant) ? "AVAILABLE" : "UNAVAILABLE";
        }

    }
}
