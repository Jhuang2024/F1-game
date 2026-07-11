using System.Collections;
using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager race-engineer + radio subsystem (partial). Engineer-message
    /// stacking, overtake/fastest-lap notifications, lap-gap radio, auto-pit
    /// prompts and the per-frame race-engineer warnings. Split out of the
    /// monolith verbatim (same class; behaviour, thresholds and call order
    /// unchanged). Includes shared helpers (DriverShortCode, gap formatting)
    /// used by other partials - resolved in-class.
    /// </summary>
    public partial class RaceManager
    {
        // Radio message stacking fix: every message that fires becomes its own
        // independently-timed stack entry (newest inserted at index 0, so
        // RaceHud's newest-on-top rendering just walks the list in order)
        // instead of one shared "now showing" slot with everything else stuck
        // waiting behind it. Settings.raceControlMessages / engineerMessageVerbosity
        // can still mute all of this without touching a single call site.
        void PostEngineerMessage(string message, bool priority)
        {
            PostEngineerMessage(message, priority, RaceAudioCue.None);
        }

        // cue plays a short race-control stinger alongside the text (throttled
        // by SimpleAudioManager's own cooldown, so a burst of several messages
        // in one frame - e.g. safety-car deployment - never stacks several
        // sounds on top of each other). Only the moments that actually warrant
        // an audio cue pass one; everything else keeps using the 2-arg
        // overload above and stays silent.
        void PostEngineerMessage(string message, bool priority, RaceAudioCue cue)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (Settings != null && Settings.Current.engineerMessageVerbosity <= 0)
            {
                return;
            }

            string formatted = "ENGINEER: " + message;
            for (int i = 0; i < activeEngineerMessages.Count; i++)
            {
                if (activeEngineerMessages[i].text == formatted)
                {
                    return;
                }
            }

            // Minimal verbosity: only priority (urgent) lines get through at all.
            if (!priority && Settings != null && Settings.Current.engineerMessageVerbosity == 1)
            {
                return;
            }

            // Only reached once the message has cleared every suppression check
            // above, so the cue and the text it accompanies always agree.
            SimpleAudioManager.PlayRaceControlCue(cue);

            if (activeEngineerMessages.Count >= MaxActiveEngineerMessages)
            {
                // Make room rather than silently dropping the new (just
                // triggered, presumably relevant right now) message. Only
                // ever evicts a non-priority entry - the one closest to
                // expiring anyway - so a still-fresh safety-car/pit/damage
                // call is never bumped by routine flavor chatter. If every
                // active slot happens to be priority, a new priority message
                // may still evict the priority entry closest to expiring;
                // a new non-priority message just waits for natural expiry.
                int evictIndex = -1;
                for (int i = 0; i < activeEngineerMessages.Count; i++)
                {
                    if (activeEngineerMessages[i].priority)
                    {
                        continue;
                    }

                    if (evictIndex < 0 || activeEngineerMessages[i].remaining < activeEngineerMessages[evictIndex].remaining)
                    {
                        evictIndex = i;
                    }
                }

                if (evictIndex < 0 && priority)
                {
                    for (int i = 0; i < activeEngineerMessages.Count; i++)
                    {
                        if (evictIndex < 0 || activeEngineerMessages[i].remaining < activeEngineerMessages[evictIndex].remaining)
                        {
                            evictIndex = i;
                        }
                    }
                }

                if (evictIndex >= 0)
                {
                    activeEngineerMessages.RemoveAt(evictIndex);
                }
                else
                {
                    return;
                }
            }

            // Radio priority pass: a red flag is the single most critical thing
            // race control can say - it gets noticeably longer screen time than
            // an ordinary priority message (which itself already outlasts and
            // out-survives-eviction over routine flavor chatter, see the
            // eviction loop above), instead of sharing the same flat duration
            // as "box this lap for softs".
            float duration = cue == RaceAudioCue.RedFlag ? PriorityEngineerMessageDuration * 1.6f
                : (priority ? PriorityEngineerMessageDuration : RoutineEngineerMessageDuration);
            activeEngineerMessages.Insert(0, new EngineerMessageEntry { text = formatted, remaining = duration, age = 0f, priority = priority });
        }

        // Settings.cameraShakeLevel: 0 Off, 1 Low, 2 Standard (matches the historic
        // default feel), 3 High.
        static float CameraShakeLevelMultiplier(int level)
        {
            if (level <= 0) return 0f;
            if (level == 1) return 0.55f;
            if (level == 2) return 1f;
            return 1.4f;
        }

        string OpeningEngineerMessage()
        {
            string weather = Track == null ? "dry" : WeatherStateLabel(Track.weather);
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                return "Weather is " + weather + ". Out lap first, then push when the tyres are ready.";
            }

            if (IsTimeTrial)
            {
                float best = PlayerRecordsStore.GetBestLap(EventData == null ? "" : EventData.trackId);
                return best > 0f
                    ? "Time trial. Track record to beat: " + UiFactory.FormatTime(best) + "."
                    : "Time trial. No local record yet, set a benchmark lap.";
            }

            string planLine = GetPlannedStopCount() >= 2
                ? "Two-stop plan. First window around lap " + GetPlannedPitLapForStop(1) + " for " + GetPlannedCompoundForStop(1) +
                  "s, second around lap " + GetPlannedPitLapForStop(2) + " for " + GetPlannedCompoundForStop(2) + "s."
                : "One-stop plan. Target window around lap " + GetPlannedPitLapForStop(1) + " for " + GetPlannedCompoundForStop(1) + "s.";
            return "Weather is " + weather + ". Mandatory stop is active. " + planLine;
        }

        string WeatherStateLabel(WeatherState weather)
        {
            if (weather == WeatherState.Cloudy)
            {
                return "cloudy";
            }

            if (weather == WeatherState.LightRain)
            {
                return "light rain";
            }

            if (weather == WeatherState.HeavyRain)
            {
                return "heavy rain";
            }

            return "dry";
        }

        // Part 1: player overtake/position-lost toasts+radio calls, and a
        // session-wide fastest-lap callout when the player sets the best lap of
        // the race so far (distinct from TrackPlayerBestLapRecord's personal-best/
        // all-time-local-record tracking). Throttled to a short interval - cheap
        // either way, but there's no reason to run the fastest-lap scan every frame.
        void UpdateOvertakeAndFastestLapNotifications()
        {
            if (PlayerParticipant == null || IsTimeTrial || CurrentSession == RaceWeekendSession.Qualifying)
            {
                return;
            }

            overtakeCheckTimer -= Time.deltaTime;
            if (overtakeCheckTimer > 0f)
            {
                return;
            }

            overtakeCheckTimer = 0.6f;

            int position = GetPosition(PlayerParticipant);
            bool eligibleForOvertakeCallout = playerLastPosition > 0 && position > 0 && position != playerLastPosition &&
                !PlayerParticipant.isPitting && PlayerParticipant.pitPhase == PitPhase.None &&
                StartCountdown <= 0f && RaceElapsed > 3f;
            if (eligibleForOvertakeCallout)
            {
                if (position < playerLastPosition)
                {
                    QueueHudToast("OVERTAKE! P" + position, ToastColorGreen);
                    PostEngineerMessage("Good overtake, you're up to P" + position + ".", false);
                }
                else
                {
                    QueueHudToast("POSITION LOST - P" + position, ToastColorAmber);
                    PostEngineerMessage("Position lost, now P" + position + ". Fight back.", false);
                }
            }

            if (position > 0)
            {
                playerLastPosition = position;
            }

            if (State != null)
            {
                for (int i = 0; i < State.Participants.Count; i++)
                {
                    RaceParticipant participant = State.Participants[i];
                    if (participant == null || participant.lapTracker == null)
                    {
                        continue;
                    }

                    float best = participant.lapTracker.BestLapTime;
                    if (best <= 0f || (sessionFastestLap > 0f && best >= sessionFastestLap - 0.001f))
                    {
                        continue;
                    }

                    sessionFastestLap = best;
                    sessionFastestLapDriverId = participant.driverId;
                    if (participant.isPlayer)
                    {
                        QueueHudToast("FASTEST LAP OF THE RACE", ToastColorPurple);
                        PostEngineerMessage("That's the fastest lap of the race so far.", true);
                    }
                }
            }
        }

        // Slipstream + dirty-air feed (UpdateSlipstreamEffects,
        // ComputeSlipstreamStrength, eligibility + constants) live in the
        // RaceManager.Slipstream.cs partial (same class; behaviour unchanged).

        // Lap-gap radio feature: at the end of each completed player lap, the
        // engineer reports how much time was gained or lost against the relevant
        // car - the car directly ahead if the player isn't leading, or P2 if they
        // are. Edge-detects a newly completed lap and arms a short delay
        // (PlayerGapRadioDelaySeconds) before actually reading gaps/order, so a
        // read taken the instant the line is crossed (when standings/distances can
        // be momentarily noisy) never drives the call.
        const float PlayerGapRadioDelaySeconds = 1.2f;

        void UpdatePlayerLapGapRadio()
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null || IsTimeTrial || CurrentSession == RaceWeekendSession.Qualifying)
            {
                return;
            }

            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            if (completedLaps != playerGapRadioLastSeenCompletedLaps)
            {
                playerGapRadioLastSeenCompletedLaps = completedLaps;
                playerGapRadioPendingLapNumber = completedLaps;
                playerGapRadioPendingTimer = PlayerGapRadioDelaySeconds;
            }

            if (playerGapRadioPendingTimer < 0f)
            {
                return;
            }

            playerGapRadioPendingTimer -= Time.deltaTime;
            if (playerGapRadioPendingTimer > 0f)
            {
                return;
            }

            playerGapRadioPendingTimer = -1f;
            EvaluatePlayerLapGapRadio(playerGapRadioPendingLapNumber);
        }

        // Gap in seconds from `participant` to whoever is directly ahead of it in
        // the running order (not the distance-radius-capped GetIntervalToAheadSeconds,
        // which is tuned for DRS-range checks) - or -1f if `participant` is the
        // leader, has no valid data, or the car ahead is a full lap or more up the
        // road (not a meaningful "seconds" gap for a pace call).
        float GetRunningOrderGapAheadSeconds(RaceParticipant participant)
        {
            if (participant == null || State == null || participant.lapTracker == null)
            {
                return -1f;
            }

            int index = State.SortedOrder.IndexOf(participant);
            if (index <= 0)
            {
                return -1f;
            }

            RaceParticipant ahead = State.SortedOrder[index - 1];
            if (ahead == null || ahead.lapTracker == null)
            {
                return -1f;
            }

            float aheadDistance = State.GetProgressDistance(ahead);
            float participantDistance = State.GetProgressDistance(participant);
            float deltaMeters = aheadDistance - participantDistance;
            if (Track != null && deltaMeters >= Track.length * 0.92f)
            {
                return -1f;
            }

            float speed = Mathf.Max(24f, participant.vehicle == null ? 36f : Mathf.Abs(participant.vehicle.CurrentSpeedKph) / 3.6f);
            return Mathf.Max(0f, deltaMeters) / speed;
        }

        // The actual comparison, run once the post-lap delay above has elapsed.
        // Every exit path that isn't a genuine, clean, single-lap comparison
        // returns without touching the persistent snapshot fields, so a dirty lap
        // (pit stop, safety car, retired rival, lapped gap) never contaminates the
        // baseline the next clean lap compares against - it just self-heals once
        // a fully clean lap comes around again, rather than ever speaking a
        // misleading number.
        void EvaluatePlayerLapGapRadio(int completedLapNumber)
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null ||
                PlayerParticipant.retired || PlayerParticipant.finished || State == null)
            {
                return;
            }

            // Player pit-stop-this-lap detection: compares the pit-stop COUNT at
            // this lap boundary against the count recorded at the previous one,
            // which catches a stop that both started and fully completed within
            // the same lap - a plain "is currently pitting" check taken only at
            // this one delayed instant could miss that entirely.
            bool playerPittedThisLap = playerPitStopsAtLastGapRadioBoundary >= 0 && PlayerParticipant.pitStops != playerPitStopsAtLastGapRadioBoundary;
            playerPitStopsAtLastGapRadioBoundary = PlayerParticipant.pitStops;

            if (completedLapNumber < 1)
            {
                // Lap 1 (out/formation lap) has no previous-lap reference - never a
                // real comparison. Establish nothing yet; the snapshot starts once
                // lap 1 itself completes cleanly.
                return;
            }

            SortRunningOrder();
            int playerIndex = State.SortedOrder.IndexOf(PlayerParticipant);
            if (playerIndex < 0)
            {
                return;
            }

            bool playerIsLeader = playerIndex == 0;
            int position = playerIndex + 1;
            RaceParticipant comparisonDriver;
            float currentGap;
            if (playerIsLeader)
            {
                comparisonDriver = State.SortedOrder.Count > 1 ? State.SortedOrder[1] : null;
                currentGap = comparisonDriver == null ? -1f : GetRunningOrderGapAheadSeconds(comparisonDriver);
            }
            else
            {
                comparisonDriver = State.SortedOrder[playerIndex - 1];
                currentGap = GetRunningOrderGapAheadSeconds(PlayerParticipant);
            }

            bool raceControlUnsettled = drsRestartCooldownTimer > 0f ||
                CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                CurrentRaceControlState == RaceControlState.SafetyCarInThisLap ||
                CurrentRaceControlState == RaceControlState.RedFlagged ||
                CurrentRaceControlState == RaceControlState.Restart;

            bool comparisonDriverDirty = comparisonDriver == null || comparisonDriver.lapTracker == null ||
                comparisonDriver.retired || comparisonDriver.finished ||
                comparisonDriver.isPitting || comparisonDriver.pitPhase != PitPhase.None;

            bool gapValid = currentGap >= 0f && currentGap < 40f;

            bool lapIsClean = !playerPittedThisLap && !raceControlUnsettled && !comparisonDriverDirty && gapValid &&
                !PlayerParticipant.isPitting && PlayerParticipant.pitPhase == PitPhase.None;

            if (!lapIsClean)
            {
                return;
            }

            string comparisonId = comparisonDriver.driverId;
            bool hasContinuousSnapshot = lastPlayerGapRadioLap == completedLapNumber - 1;
            bool playerPositionChanged = hasContinuousSnapshot && previousPlayerGapRadioPosition > 0 && position != previousPlayerGapRadioPosition;

            if (playerPositionChanged)
            {
                string name = DriverRadioName(comparisonDriver);
                string gapText = FormatGapSeconds(currentGap);
                if (position < previousPlayerGapRadioPosition)
                {
                    PostEngineerMessage(playerIsLeader
                        ? "Position gained. Leading now, " + name + " is " + gapText + " behind."
                        : "Position gained. Now chasing " + name + ", gap " + gapText + ".", false);
                }
                else
                {
                    PostEngineerMessage("Position lost. Car ahead is " + name + ", gap " + gapText + ".", false);
                }
            }
            else if (hasContinuousSnapshot && !string.IsNullOrEmpty(previousComparisonDriverId) && previousComparisonDriverId == comparisonId &&
                     previousPlayerWasLeader == playerIsLeader)
            {
                SpeakPlayerLapGapDelta(currentGap - previousPlayerComparisonGap, comparisonDriver, playerIsLeader);
            }

            // else: comparison target shuffled for reasons other than the player's
            // own position (the car ahead pitted out/retired and got replaced by
            // someone else, or this is the first clean lap after a gap in
            // coverage) - stay silent, just refresh the baseline below.
            previousPlayerGapRadioPosition = position;
            lastPlayerGapRadioLap = completedLapNumber;
            previousPlayerComparisonGap = currentGap;
            previousComparisonDriverId = comparisonId;
            previousPlayerWasLeader = playerIsLeader;
        }

        const float PlayerGapRadioStableThreshold = 0.15f;

        void SpeakPlayerLapGapDelta(float delta, RaceParticipant comparisonDriver, bool playerIsLeader)
        {
            string name = DriverRadioName(comparisonDriver);
            if (Mathf.Abs(delta) < PlayerGapRadioStableThreshold)
            {
                PostEngineerMessage(playerIsLeader ? "Gap to P2 is stable." : "Gap to " + name + " is stable.", false);
                return;
            }

            string amount = FormatGapDelta(Mathf.Abs(delta));
            if (playerIsLeader)
            {
                // delta > 0: the gap to P2 grew - player pulled away.
                // delta < 0: the gap shrank - P2 gained on the player.
                PostEngineerMessage(delta > 0f
                    ? "You pulled " + amount + " on " + name + " last lap."
                    : name + " gained " + amount + " on you last lap.", false);
            }
            else
            {
                // delta < 0: the gap to the car ahead shrank - player gained.
                // delta > 0: the gap grew - player lost time to them.
                PostEngineerMessage(delta < 0f
                    ? "Good lap. You gained " + amount + " on " + name + "."
                    : "You lost " + amount + " to " + name + " last lap.", false);
            }
        }

        static readonly string[] TenthsWords = { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        // Under a second: spoken as tenths ("seven tenths", "half a second" for
        // exactly five). A second or more: plain numeric text - spec explicitly
        // allows this fallback rather than a full number-to-words conversion.
        string FormatGapDelta(float absSeconds)
        {
            if (absSeconds < 0.95f)
            {
                int tenths = Mathf.Clamp(Mathf.RoundToInt(absSeconds * 10f), 1, 9);
                if (tenths == 5)
                {
                    return "half a second";
                }

                return tenths == 1 ? "one tenth" : TenthsWords[tenths] + " tenths";
            }

            return absSeconds.ToString("0.0") + " seconds";
        }

        string FormatGapSeconds(float seconds)
        {
            return Mathf.Max(0f, seconds).ToString("0.0") + " seconds";
        }

        // Compact 3-letter driver code for HUD/debug display (slipstream source,
        // etc.) - distinct from DriverRadioName below, which prefers a spoken last
        // name for engineer radio text.
        string DriverShortCode(RaceParticipant participant)
        {
            if (participant == null)
            {
                return "";
            }

            if (participant.driverData != null && !string.IsNullOrEmpty(participant.driverData.abbreviation))
            {
                return participant.driverData.abbreviation.ToUpperInvariant();
            }

            return DriverCode(participant.driverName);
        }

        // Radio name for a driver: last name if driverName resolves to one,
        // falling back to the driver's real abbreviation/code.
        string DriverRadioName(RaceParticipant participant)
        {
            if (participant == null)
            {
                return "the car";
            }

            if (!string.IsNullOrEmpty(participant.driverName))
            {
                string[] parts = participant.driverName.Trim().Split(' ');
                string lastName = parts[parts.Length - 1];
                if (!string.IsNullOrEmpty(lastName))
                {
                    return lastName;
                }
            }

            if (participant.driverData != null && !string.IsNullOrEmpty(participant.driverData.abbreviation))
            {
                return participant.driverData.abbreviation.ToUpperInvariant();
            }

            return DriverCode(participant.driverName);
        }

        // Automatic pit stop fix: if the player set a stop plan on the
        // strategy screen and hasn't manually called for a stop themselves,
        // the car boxes on its own once the planned lap is reached - the
        // plan previously only ever surfaced as an engineer reminder message
        // (see the "Box this lap for..." line in UpdateRaceEngineer below),
        // with nothing actually acting on it, so a player who missed or
        // ignored that message never pitted at all. Runs every tick,
        // independent of the engineer message queue (which has several
        // early-return branches for other one-shot messages), so a message
        // skipped on a busy frame can never also skip the actual stop. Any
        // manual request (the P key, on any lap) sets vehicle.PitRequested
        // itself and is always checked first here, so it always wins and is
        // never second-guessed or replaced by the plan.
        void UpdatePlayerAutoPitStrategy()
        {
            if (PlayerParticipant == null || PlayerParticipant.vehicle == null || PlayerParticipant.lapTracker == null ||
                CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return;
            }

            if (PlayerParticipant.isPitting || PlayerParticipant.pitPhase != PitPhase.None ||
                PlayerParticipant.vehicle.PitRequested || PlayerParticipant.retired || PlayerParticipant.finished ||
                PlayerParticipant.missedPitEntryThisLap)
            {
                return;
            }

            if (!ShouldPromptPlannedStop(PlayerParticipant))
            {
                return;
            }

            // Same currentLapNumber >= targetLap trigger point UpdateRaceEngineer's
            // "Box this lap for..." message already uses, so the automatic stop
            // and the message the player sees stay in lockstep instead of
            // introducing a second, slightly different lap-comparison convention.
            //
            // Off-by-one fix: targetLap (from GetPlannedPitLapForStop/
            // RecommendedPitLap) is a 1-based lap NUMBER - "pit on lap 6" means
            // while DisplayLap (CompletedLaps + 1) reads 6, not once
            // CompletedLaps itself reaches 6. CompletedLaps only becomes 6 after
            // lap 6 is actually finished (i.e. once already driving lap 7), so
            // comparing raw CompletedLaps against targetLap fired the stop a
            // full lap after the one the player planned for.
            int targetLap = NextPlannedPitLapFor(PlayerParticipant);
            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            int currentLapNumber = completedLaps + 1;
            if (currentLapNumber < targetLap)
            {
                return;
            }

            PlayerParticipant.vehicle.RequestPit();
            PlayerParticipant.pitAutoTriggered = true;
            // Source tracking: this is the pre-race plan firing itself, never a
            // manual override, so it is never cancellable through
            // CanCancelManualPitRequest/CancelManualPitRequest.
            PlayerParticipant.activePitRequestSource = PitRequestSource.PreRacePlan;
            PlayerParticipant.manualPitRequested = false;
            PlayerParticipant.manualPitCommitted = false;
            if (!PlayerParticipant.requestedPitCompoundSet)
            {
                PlayerParticipant.requestedPitCompound = NextPlannedPitCompoundFor(PlayerParticipant);
                PlayerParticipant.requestedPitCompoundSet = true;
            }

            SessionMessage = "Auto-pit: strategy plan (" + PlayerParticipant.requestedPitCompound + ")";
            GameLog.Info("[Pit] Auto-triggered planned stop for player at lap " + currentLapNumber + " (target " + targetLap + "), compound=" + PlayerParticipant.requestedPitCompound + ".");
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                // Distinct from the earlier "Box this lap for..." reminder in
                // UpdateRaceEngineer (a heads-up before the fact) - this
                // confirms the car has actually committed to the stop on its
                // own, since the player never pressed the pit key themselves.
                PostEngineerMessage("Boxing automatically per the strategy plan - " + PlayerParticipant.requestedPitCompound + "s fitted this stop.", true);
            }
        }

        void UpdateRaceEngineer()
        {
            if (PlayerParticipant == null || PlayerParticipant.vehicle == null || PlayerParticipant.lapTracker == null)
            {
                return;
            }

            VehicleController car = PlayerParticipant.vehicle;
            TrackPlayerBestLapRecord();
            if (!engineerWeatherSent)
            {
                engineerWeatherSent = true;
                PostEngineerMessage(OpeningEngineerMessage(), true);
                return;
            }

            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                if (car.Tyres.TemperatureStatus == "HOT" && !engineerTyreWarningSent)
                {
                    engineerTyreWarningSent = true;
                    PostEngineerMessage("Tyres are hot. Cool them before the next push lap.", false);
                }

                return;
            }

            if (IsTimeTrial)
            {
                return;
            }

            // Part C.1: Expert-only radio warning, once, early in the race - staged
            // behind a short RaceElapsed gate so it doesn't collide with the opening
            // weather/strategy message's own display window.
            if (!engineerExpertWarningSent && Settings != null && Settings.Difficulty == RaceDifficulty.Expert && RaceElapsed > 2f)
            {
                engineerExpertWarningSent = true;
                PostEngineerMessage("Expert AI on track. It won't back off - defend hard and pick your moments to attack.", false);
                return;
            }

            // Part C.1: Expert-only "car behind has DRS and is closing" warning,
            // repeatable on a cooldown rather than a one-shot flag since the threat
            // can come and go several times over a race.
            if (Settings != null && Settings.Difficulty == RaceDifficulty.Expert && engineerDrsWarningCooldown <= 0f)
            {
                RaceParticipant chaserForDrsWarning = FindCarBehind(PlayerParticipant, 60f);
                if (chaserForDrsWarning != null && IsDrsAvailable(chaserForDrsWarning))
                {
                    engineerDrsWarningCooldown = 15f;
                    PostEngineerMessage("Car behind has DRS and is closing. Defend the inside line.", false);
                    return;
                }
            }

            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            if (completedLaps == RaceLaps - 1 && !engineerFinalLapSent)
            {
                engineerFinalLapSent = true;
                PostEngineerMessage("Final lap. Bring it home, watch the tyres.", true, RaceAudioCue.FinalLap);
                return;
            }

            if (car.FuelStarved && !engineerFuelStarvationSent)
            {
                engineerFuelStarvationSent = true;
                PostEngineerMessage("We are out of fuel. The engine is starving.", true, RaceAudioCue.Damage);
                return;
            }

            // Fuel system pass: delta-based instead of the old flat "<7kg" check -
            // that threshold was tuned for a 35kg flat start and would fire almost
            // immediately on a legitimate 8kg target-fuel short race. Reads the
            // same projected-delta figure the HUD fuel pill shows.
            float fuelDeltaLaps = car.ProjectedFuelDeltaLaps;
            if (fuelDeltaLaps < -0.25f)
            {
                engineerFuelEverNegative = true;
                if (!engineerFuelWarningSent)
                {
                    engineerFuelWarningSent = true;
                    PostEngineerMessage("Fuel target is negative. Lift and coast into heavy braking zones.", false);
                    return;
                }
            }
            else if (engineerFuelEverNegative && fuelDeltaLaps >= -0.05f && fuelDeltaLaps < 0.4f && !engineerFuelRecoverySent)
            {
                engineerFuelRecoverySent = true;
                PostEngineerMessage("Good fuel saving. You're back on target.", false);
                return;
            }
            else if (engineerFuelEverNegative && fuelDeltaLaps >= 0.4f && !engineerFuelSafeToPushSent)
            {
                engineerFuelSafeToPushSent = true;
                PostEngineerMessage("Fuel is safe now. You can push.", false);
                return;
            }

            if (car.Damage.OverallPercent > 45f && !engineerDamageWarningSent)
            {
                engineerDamageWarningSent = true;
                PostEngineerMessage("We are seeing damage on the car. Consider a stop for repairs.", false, RaceAudioCue.Damage);
                return;
            }
            if (ShouldPromptPlannedStop(PlayerParticipant) && !PlayerParticipant.isPitting)
            {
                int targetLap = NextPlannedPitLapFor(PlayerParticipant);
                // Same 1-based DisplayLap comparison UpdatePlayerAutoPitStrategy
                // uses - see the off-by-one fix comment there. Keeping both in
                // the same completedLaps+1 convention is what keeps this
                // reminder and the actual auto-triggered stop on the same lap.
                int currentLapNumber = completedLaps + 1;
                bool mandatoryStopStillOwed = PlayerParticipant.pitStops == 0;

                // Smarter strategy planner: proactive undercut alert, a couple of
                // laps before the planned stop rather than only the "Box this lap"
                // call right when the window arrives - the AI now pits early to
                // undercut a car it's following (see RaceManager.ShouldAiPitForUndercut),
                // so the player needs the same early notice to react before a rival
                // jumps them, not just a report of what already happened.
                if (mandatoryStopStillOwed && targetLap > 0 && currentLapNumber == Mathf.Max(0, targetLap - 2) && lastEngineerPitLapPrompt != completedLaps)
                {
                    RaceParticipant rivalAheadForUndercut = FindCarAhead(PlayerParticipant, 40f);
                    float rivalGap = GetIntervalToAheadSeconds(PlayerParticipant);
                    if (rivalAheadForUndercut != null && rivalAheadForUndercut.pitStops == 0 && rivalGap > 0f && rivalGap < 2.2f)
                    {
                        lastEngineerPitLapPrompt = completedLaps;
                        PostEngineerMessage("The car ahead hasn't stopped yet. Box early and you could undercut them.", false);
                        return;
                    }
                }

                if (currentLapNumber >= targetLap && lastEngineerPitLapPrompt != completedLaps)
                {
                    lastEngineerPitLapPrompt = completedLaps;
                    TyreCompound plannedCompound = NextPlannedPitCompoundFor(PlayerParticipant);
                    float undercutGap = GetIntervalToAheadSeconds(PlayerParticipant);
                    string undercut = undercutGap > 0f && undercutGap < 2.5f ? " The undercut on the car ahead is live." : "";
                    string requirement = mandatoryStopStillOwed ? "Mandatory stop still required." : "Second stop window is here.";
                    // Recommendation reason: the requirement/undercut clauses above
                    // already cover "mandatory rule" and "undercut threat" - this
                    // adds the other two most common real reasons (tyre wear, rain
                    // crossover) whenever they're ALSO genuinely true this stop, so
                    // the call reads as "why", not just "when".
                    string extraReason = PitRecommendationReasonClause(PlayerParticipant);
                    PostEngineerMessage("Box this lap for " + plannedCompound + "s. " + requirement + undercut + extraReason, true);
                    return;
                }

                if (currentLapNumber == Mathf.Max(0, targetLap - 1) && lastEngineerPitLapPrompt != completedLaps)
                {
                    lastEngineerPitLapPrompt = completedLaps;
                    string label = mandatoryStopStillOwed ? "Pit window opens next lap. Think about the undercut." : "Second stop window opens next lap.";
                    PostEngineerMessage(label, false);
                    return;
                }
            }

            if (PlayerParticipant.trackLimitWarnings >= 2 && !engineerTrackLimitsSent)
            {
                engineerTrackLimitsSent = true;
                PostEngineerMessage("Careful with track limits. One more warning is a time penalty.", true, RaceAudioCue.Penalty);
                return;
            }

            if (!engineerRivalSent && IsCareerRace && Career != null && Career.Save != null && !string.IsNullOrEmpty(Career.Save.rivalDriverId))
            {
                RaceParticipant rivalAhead = FindCarAhead(PlayerParticipant, 70f);
                RaceParticipant rivalBehind = FindCarBehind(PlayerParticipant, 70f);
                if (rivalAhead != null && rivalAhead.driverId == Career.Save.rivalDriverId)
                {
                    engineerRivalSent = true;
                    PostEngineerMessage("That's your rival ahead." + RivalTraitHint(rivalAhead) + " Beat him and the team will notice.", false);
                    return;
                }

                if (rivalBehind != null && rivalBehind.driverId == Career.Save.rivalDriverId)
                {
                    engineerRivalSent = true;
                    PostEngineerMessage("Your rival is right behind." + RivalTraitHint(rivalBehind) + " Keep it clean, hold the position.", false);
                    return;
                }
            }

            // Teammate gap callout, on the off-parity laps so it never collides
            // with the every-2-laps pace report below.
            if (completedLaps >= 3 && completedLaps % 2 == 1 && lastTeammateGapReportLap != completedLaps && engineerCooldown <= 0f)
            {
                RaceParticipant teammate = FindTeammate(PlayerParticipant);
                if (teammate != null)
                {
                    lastTeammateGapReportLap = completedLaps;
                    float gapSeconds = GetGapBetweenSeconds(PlayerParticipant, teammate);
                    bool teammateAhead = GetPosition(teammate) < GetPosition(PlayerParticipant);
                    string gapText = Mathf.Abs(gapSeconds).ToString("0.0");
                    PostEngineerMessage(teammateAhead
                        ? "Teammate is " + gapText + " seconds ahead."
                        : "Teammate is " + gapText + " seconds behind.", false);
                    return;
                }
            }

            // Periodic pace report every couple of laps when nothing urgent is up.
            if (completedLaps >= 2 && completedLaps % 2 == 0 && lastGapReportLap != completedLaps && engineerCooldown <= 0f)
            {
                lastGapReportLap = completedLaps;
                if (GetPosition(PlayerParticipant) == 1)
                {
                    float gapBehind = 0f;
                    RaceParticipant chaser = FindCarBehind(PlayerParticipant, 400f);
                    if (chaser != null)
                    {
                        gapBehind = GetIntervalToAheadSeconds(chaser);
                    }

                    PostEngineerMessage(gapBehind > 0.05f
                        ? "You're leading, gap behind " + gapBehind.ToString("0.0") + "s. Manage the tyres."
                        : "You're leading. Manage the tyres and keep it clean.", false);
                    return;
                }

                float interval = GetIntervalToAheadSeconds(PlayerParticipant);
                if (interval > 0.05f && interval < 1.2f)
                {
                    PostEngineerMessage("Car ahead " + interval.ToString("0.0") + "s. You're in DRS range, go get him.", false);
                }
                else if (interval > 0.05f)
                {
                    PostEngineerMessage("Gap to the car ahead " + interval.ToString("0.0") + "s. Consistent laps now.", false);
                }

                return;
            }

            if (car.Tyres.WearPercent > 42f && !engineerTyreWarningSent)
            {
                engineerTyreWarningSent = true;
                PostEngineerMessage("Tyre wear is high. Avoid sliding and plan the stop.", false);
                return;
            }

            if (car.Tyres.TemperatureStatus == "HOT" && !engineerTyreWarningSent)
            {
                engineerTyreWarningSent = true;
                PostEngineerMessage("Tyres are overheating. Short-shift and reduce sliding.", false);
                return;
            }

            if (car.Tyres.FlatSpotLevel > 0.6f && !engineerFlatSpotWarningSent)
            {
                engineerFlatSpotWarningSent = true;
                PostEngineerMessage("Heavy flat spot. Consider boxing, it will vibrate through the braking zones.", true);
                return;
            }

            if (car.Tyres.LastLockupSeverity > 0.55f && !engineerLockupWarningSent)
            {
                engineerLockupWarningSent = true;
                PostEngineerMessage("Big lockup there. Ease the brake pressure into the next few corners.", false);
                return;
            }

            if (car.ErsBattery < 0.18f && !engineerBatteryWarningSent)
            {
                engineerBatteryWarningSent = true;
                PostEngineerMessage("Battery low. Harvest for a few corners.", false);
            }
        }
        public int ActiveEngineerMessageCount { get { return activeEngineerMessages.Count; } }

        public string GetActiveEngineerMessageText(int index)
        {
            return index >= 0 && index < activeEngineerMessages.Count ? activeEngineerMessages[index].text : "";
        }

        public bool GetActiveEngineerMessagePriority(int index)
        {
            return index >= 0 && index < activeEngineerMessages.Count && activeEngineerMessages[index].priority;
        }

        // 0-1 slide/fade progress for one stacked entry: rises over
        // EngineerMessageAnimInDuration after it first appears, holds at 1,
        // then falls over EngineerMessageAnimOutDuration right before it
        // expires and is removed.
        public float GetActiveEngineerMessageFade(int index)
        {
            if (index < 0 || index >= activeEngineerMessages.Count)
            {
                return 0f;
            }

            EngineerMessageEntry entry = activeEngineerMessages[index];
            float inProgress = Mathf.Clamp01(entry.age / EngineerMessageAnimInDuration);
            float outProgress = Mathf.Clamp01(entry.remaining / EngineerMessageAnimOutDuration);
            return Mathf.Min(inProgress, outProgress);
        }

        void ResetEngineerState()
        {
            activeEngineerMessages.Clear();
            engineerCooldown = 0f;
            lastEngineerPitLapPrompt = -1;
            engineerWeatherSent = false;
            engineerPitRequestConfirmed = false;
            engineerTyreWarningSent = false;
            engineerBatteryWarningSent = false;
            engineerFinalLapSent = false;
            engineerFuelWarningSent = false;
            engineerFuelEverNegative = false;
            engineerFuelRecoverySent = false;
            engineerFuelSafeToPushSent = false;
            engineerFuelStarvationSent = false;
            engineerDamageWarningSent = false;
            engineerRivalSent = false;
            engineerTrackLimitsSent = false;
            engineerExpertWarningSent = false;
            engineerDrsWarningCooldown = 0f;
            lastGapReportLap = -1;
            weatherTransitionDone = false;
            weatherSecondTransitionDone = false;
            trackEvolutionHalfwayMessageSent = false;
            playerLastPosition = -1;
            overtakeCheckTimer = 0f;
            sessionFastestLap = -1f;
            sessionFastestLapDriverId = "";
            engineerFlatSpotWarningSent = false;
            engineerLockupWarningSent = false;
            lastTeammateGapReportLap = -1;
            engineerPodiumMessageSent = false;
            hudToastQueue.Clear();
            playerGapRadioPendingTimer = -1f;
            playerGapRadioPendingLapNumber = -1;
            playerGapRadioLastSeenCompletedLaps = -1;
            playerPitStopsAtLastGapRadioBoundary = -1;
            lastPlayerGapRadioLap = -1;
            previousPlayerComparisonGap = -1f;
            previousComparisonDriverId = "";
            previousPlayerWasLeader = false;
            previousPlayerGapRadioPosition = -1;
            cachedPlannedPitLapStopOne = -1;
            cachedPlannedPitLapStopTwo = -1;
        }

        void TickEngineerTimers()
        {
            engineerCooldown = Mathf.Max(0f, engineerCooldown - Time.deltaTime);
            reactionDisplayTimer = Mathf.Max(0f, reactionDisplayTimer - Time.deltaTime);
            playerResetCooldown = Mathf.Max(0f, playerResetCooldown - Time.deltaTime);
            engineerDrsWarningCooldown = Mathf.Max(0f, engineerDrsWarningCooldown - Time.deltaTime);
            playerManualPitCancelMessageTimer = Mathf.Max(0f, playerManualPitCancelMessageTimer - Time.deltaTime);

            // Radio message stacking fix: every active entry ages/counts down
            // independently and is removed the instant it expires - there is
            // no single "current" message to advance from a queue any more,
            // each one lives and dies entirely on its own timer.
            for (int i = activeEngineerMessages.Count - 1; i >= 0; i--)
            {
                EngineerMessageEntry entry = activeEngineerMessages[i];
                entry.age += Time.deltaTime;
                entry.remaining -= Time.deltaTime;
                if (entry.remaining <= 0f)
                {
                    activeEngineerMessages.RemoveAt(i);
                    continue;
                }

                activeEngineerMessages[i] = entry;
            }
        }

    }
}
