using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager track-limits subsystem (partial). Detects repeated off-track
    /// excursions, logs each sector event, escalates the warning count and applies
    /// the deletion/penalty tariff, surfacing the "n/3" status to the HUD. Split out
    /// of the RaceManager monolith verbatim - same class, same members, identical
    /// thresholds and call order; callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        void HandleTrackLimits(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || Track == null || participant.finished || participant.isPitting || participant.pitLimiterUntilExit || participant.pitPhase != PitPhase.None)
            {
                return;
            }

            TrackProgress progress = participant.lapTracker.CurrentProgress;
            float lateral = Mathf.Abs(progress.lateralDistance);
            // Track-limits penalties must key off the actual (possibly hairpin-
            // widened) drivable surface, not the flat field - otherwise a car using
            // the extra tarmac a widened hairpin exists to provide would rack up
            // false track-limits warnings and penalties for it.
            float localHalfWidth = LocalHalfWidthAt(progress.distance);
            // Barrier-flush fix: these used to allow +2.2m/+5.2m of "legal"
            // space beyond the paved edge before even a warning - fine when
            // the nearest barrier's own inner face was 1.5m+ further out
            // still, but now that barriers sit flush against the edge
            // (EdgeBarrierClearance, TrackManager.cs) a car "legally" using
            // that old leniency would be driving straight through solid
            // barrier geometry. Tightened to match where the wall actually
            // is - AI never even approaches this (LegalOffsetLimit keeps it
            // 1.8m+ inside the edge), so this only changes how forgiving the
            // player's own track-limits margin is, not AI behaviour.
            bool outsideWhiteLine = lateral > localHalfWidth + 0.5f;
            bool gainedTime = lateral > localHalfWidth + 1.0f && participant.vehicle != null && Mathf.Abs(participant.vehicle.CurrentSpeedKph) > 70f;
            // Stewarding depth: capture whether the lap was already invalidated
            // BEFORE this call, so a "lap deleted" moment only fires once per
            // lap (the very first excursion) instead of every single frame the
            // car stays outside the line for the rest of that lap.
            bool alreadyInvalidated = participant.lapTracker.CurrentLapInvalidated;
            if (outsideWhiteLine)
            {
                participant.lapTracker.InvalidateCurrentLap();
                participant.offTrackTimer += Time.deltaTime;
                if (!alreadyInvalidated && participant.lapTracker.CurrentLapInvalidated && participant.isPlayer &&
                    CurrentSession == RaceWeekendSession.Qualifying)
                {
                    QueueHudToast("LAP DELETED - Sector " + progress.sector, ToastColorAmber);
                    PostEngineerMessage("Lap deleted, track limits in sector " + progress.sector + ". Push again on the next one.", false);
                }
            }
            else
            {
                participant.offTrackTimer = Mathf.Max(0f, participant.offTrackTimer - Time.deltaTime * 2.5f);
            }

            if (gainedTime && participant.offTrackTimer > 0.75f)
            {
                participant.trackLimitWarnings++;
                participant.offTrackTimer = -1.6f;
                // Stewarding depth: log the individual event (lap/sector) rather
                // than only the running count - capped so a persistent offender
                // over a long race can't grow this unbounded.
                int displayLap = participant.lapTracker.CompletedLaps + 1;
                participant.trackLimitEventLog.Add("Lap " + displayLap + " - Sector " + progress.sector);
                const int maxTrackLimitEventLog = 8;
                if (participant.trackLimitEventLog.Count > maxTrackLimitEventLog)
                {
                    participant.trackLimitEventLog.RemoveAt(0);
                }

                if (participant.trackLimitWarnings >= 3)
                {
                    participant.trackLimitWarnings = 0;
                    AddPenalty(participant, 5f, "Track limits");
                    if (participant.isPlayer)
                    {
                        SessionMessage = "Track limits: +5s";
                        QueueHudToast("5S PENALTY - TRACK LIMITS", ToastColorAmber);
                    }
                }
                else if (participant.isPlayer)
                {
                    // RaceHud already watches player.trackLimitWarnings itself and
                    // raises its own "TRACK LIMITS WARNING n/3" toast (see
                    // RaceHud.UpdateTopAccentFlash) - only SessionMessage (the small
                    // top status line) is set here, not a second QueueHudToast, to
                    // avoid showing the same warning twice. RaceHud reads the sector
                    // detail straight off trackLimitEventLog above.
                    SessionMessage = "Track limits warning " + participant.trackLimitWarnings + "/3";
                }
            }
        }

    }
}
