using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager DRS-eligibility subsystem (partial). Owns the detection-point
    /// gap evaluation (checked once as a car crosses the detection line, then held
    /// for the whole following zone), the DRS zone lookup, the availability gate
    /// and the HUD state text. The pure eligibility policy lives in the engine-free
    /// DrsRules; this partial owns the live per-frame evaluation and the track
    /// queries. Split out of the RaceManager monolith verbatim - same class, same
    /// members, identical behaviour and call order; IsDrsAvailable/DrsStateText
    /// stay public so external callers resolve in-class. The LocalHalfWidthAt
    /// geometry helper stays in the main partial (shared well beyond DRS).
    /// </summary>
    public partial class RaceManager
    {
        // DRS fix: the 1-second gap requirement is evaluated ONCE, at each zone's own
        // detection point (a short distance before the zone starts - see
        // TrackRuntime.drsDetectionOne/Two), not continuously through the whole
        // activation zone. Real DRS works the same way: the gap is checked as you
        // cross the detection line, and if you're within a second you get DRS for
        // the ENTIRE following zone even if the gap opens back up past a second
        // halfway down the straight. The old code called GetIntervalToAheadSeconds
        // every frame inside the zone, so DRS would flicker off the instant the live
        // gap crept past 1.0s - which is exactly why it "did not stay open long
        // enough". Runs unconditionally every frame per participant (not just while
        // DRS-eligible) so a car approaching a zone always gets evaluated the moment
        // it crosses the detection point, regardless of what it was doing before.
        void UpdateDrsEligibility(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || Track == null || participant.vehicle == null)
            {
                return;
            }

            TrackProgress progress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            float currentNormalized = progress.normalized;

            if (participant.retired || participant.finished || participant.isPitting || participant.pitPhase != PitPhase.None)
            {
                participant.drsEligibleZoneOne = false;
                participant.drsEligibleZoneTwo = false;
                participant.drsZoneEntryRacePosition = -1;
                participant.previousDrsProgressNormalized = currentNormalized;
                return;
            }

            if (participant.previousDrsProgressNormalized < 0f)
            {
                participant.previousDrsProgressNormalized = currentNormalized;
                return;
            }

            float previousNormalized = participant.previousDrsProgressNormalized;

            if (Track.CrossedDrsDetectionPoint(previousNormalized, currentNormalized, 1))
            {
                participant.drsEligibleZoneOne = EvaluateDrsDetectionGap(participant);
                participant.drsEligibilityLapZoneOne = participant.lapTracker.CompletedLaps;
            }

            if (Track.CrossedDrsDetectionPoint(previousNormalized, currentNormalized, 2))
            {
                participant.drsEligibleZoneTwo = EvaluateDrsDetectionGap(participant);
                participant.drsEligibilityLapZoneTwo = participant.lapTracker.CompletedLaps;
            }

            // Latch the race position held at the moment the car ENTERS a DRS
            // zone (passed-leader fix - see the gate below). Checked against
            // both zones with one flag since the zones never overlap.
            bool inAnyZoneNow = Track.IsInDrsZone(1, currentNormalized) || Track.IsInDrsZone(2, currentNormalized);
            bool inAnyZoneBefore = Track.IsInDrsZone(1, previousNormalized) || Track.IsInDrsZone(2, previousNormalized);
            if (inAnyZoneNow && !inAnyZoneBefore)
            {
                participant.drsZoneEntryRacePosition = GetPosition(participant);
            }
            else if (!inAnyZoneNow)
            {
                participant.drsZoneEntryRacePosition = -1;
            }

            // Live in-zone earning ("DRS is broken" fix): the detection-point
            // check above is a snapshot - a car that was 3-4s back at the line
            // but catches the car ahead mid-zone (routine here, where closing
            // speeds are far larger than real F1's) used to get nothing for the
            // whole zone, which in practice meant DRS almost never activated in
            // a race at all. A car inside a zone that has NOT yet earned it now
            // re-checks the same gap rule continuously and earns the zone the
            // moment it comes within the activation gap. Earning stays sticky
            // ("once earned it holds for the whole zone" is unchanged - the gap
            // opening back up never revokes it), and the zone-exit clearing
            // below still resets everything for the next pass.
            // Passed-leader gate (per report - "if I'm leading across the DRS
            // detection line and the AI behind me gets DRS and passes me on
            // that straight... I get DRS too???"): live earning exists for a
            // car CATCHING the one ahead, but a car that got PASSED inside the
            // zone also suddenly has a car ahead within the gap and used to
            // earn DRS retroactively. A car may only live-earn while it still
            // holds (or has improved on) the position it entered the zone with
            // - losing a place inside the zone forfeits live earning for the
            // rest of that zone, exactly like real DRS where the decision was
            // already made at the detection line. The detection-point latch
            // above is untouched.
            bool heldPositionSinceZoneEntry = participant.drsZoneEntryRacePosition < 0 ||
                GetPosition(participant) <= participant.drsZoneEntryRacePosition;
            if (Track.IsInDrsZone(1, currentNormalized) && !participant.drsEligibleZoneOne &&
                heldPositionSinceZoneEntry && EvaluateDrsDetectionGap(participant))
            {
                participant.drsEligibleZoneOne = true;
                participant.drsEligibilityLapZoneOne = participant.lapTracker.CompletedLaps;
            }

            if (Track.IsInDrsZone(2, currentNormalized) && !participant.drsEligibleZoneTwo &&
                heldPositionSinceZoneEntry && EvaluateDrsDetectionGap(participant))
            {
                participant.drsEligibleZoneTwo = true;
                participant.drsEligibilityLapZoneTwo = participant.lapTracker.CompletedLaps;
            }

            // Clear a zone's own eligibility once the car has actually left it, so a
            // stale "eligible" flag can never linger into some later, unrelated pass
            // through the same normalized band (e.g. after a forced reposition).
            if (Track.IsInDrsZone(1, previousNormalized) && !Track.IsInDrsZone(1, currentNormalized))
            {
                participant.drsEligibleZoneOne = false;
            }

            if (Track.IsInDrsZone(2, previousNormalized) && !Track.IsInDrsZone(2, currentNormalized))
            {
                participant.drsEligibleZoneTwo = false;
            }

            participant.previousDrsProgressNormalized = currentNormalized;
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

        // The actual 1-second-gap decision, made once at the detection point.
        // Qualifying/time trial never require a gap at all - every zone is always
        // available with no car-ahead requirement.
        bool EvaluateDrsDetectionGap(RaceParticipant participant)
        {
            // Qualifying/time trial earn every zone with no gap requirement, so the
            // heavier interval scan is only run on the race path (matching the
            // original short-circuit exactly).
            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return DrsRules.EarnsDetectionEligibility(true, participant.lapTracker.CompletedLaps, 0f);
            }

            return DrsRules.EarnsDetectionEligibility(
                false, participant.lapTracker.CompletedLaps, GetIntervalToAheadSeconds(participant));
        }

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

            // DRS permission under flags is FlagRules' call. FlagForParticipant
            // folds the sector-local yellow (and its near-incident fallback
            // window) into the global flag, and this is checked continuously
            // (every frame, not just at the detection point) so a yellow that
            // comes out AFTER a car already earned zone eligibility still shuts
            // DRS off immediately, matching the real rule. The restart cooldown
            // is race-layer state (real F1 rule: no DRS for a spell after a
            // restart) layered on top. The zone-eligibility ordering (wet/
            // cooldown/flag → in-zone → session → laps → earned) lives in
            // DrsRules; RaceManager resolves the live state here. The earned gap
            // itself is NOT re-checked against the live gap - once earned at the
            // detection point it holds for the whole zone.
            bool isWet = Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain;
            TrackProgress progress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            int zoneIndex = DrsZoneIndexAt(progress);
            bool qualiOrTt = CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial;
            return DrsRules.IsAvailable(
                isWet,
                drsRestartCooldownTimer > 0f,
                FlagRules.DrsAllowed(FlagForParticipant(participant)),
                zoneIndex,
                qualiOrTt,
                participant.lapTracker.CompletedLaps,
                participant.drsEligibleZoneOne,
                participant.drsEligibleZoneTwo);
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
