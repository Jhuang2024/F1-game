using System.Collections.Generic;
using F1Game.Core.Events;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Interim adapter that turns RaceManager state into typed bus events
    /// (flags, weather, player laps, session phase) by observing public state
    /// once per frame — a zero-risk bridge that feeds the event-driven HUD and
    /// audio layers without touching race-control logic. As the monolith split
    /// progresses, each publisher moves to the owning service and the
    /// corresponding watcher here is deleted (see Docs/REFACTOR_MAP.md).
    /// </summary>
    public class RaceEventRelay : MonoBehaviour
    {
        RaceManager race;

        FlagState lastFlag = FlagState.Green;
        WeatherState lastWeather = WeatherState.Clear;
        int lastPlayerLaps;
        bool lastFinished;
        bool primed;
        bool loggedFault;

        public void Attach(RaceManager raceManager)
        {
            race = raceManager;
            // Register the interactive HUD command handlers (UI → race). The
            // eligibility gate still lives in CanCancelManualPitRequest, so a
            // click that arrives after the request became uncancellable is a
            // safe no-op.
            F1Game.Core.HudCommands.CancelPitRequest = raceManager != null ? raceManager.CancelManualPitRequest : (System.Action)null;
        }

        void Update()
        {
            if (race == null)
            {
                return;
            }

            // Session teardown between races: reset the watermarks so the next
            // session starts clean instead of diffing against the previous one.
            if (race.PlayerParticipant == null && race.Track == null)
            {
                primed = false;
                return;
            }

            // Interim per-frame observer bridge: it only reads state and publishes
            // events, and must never throw into the game loop. A null slipping
            // through a guard here would spam the console every frame; guard the
            // whole body and stop after the first fault instead.
            try
            {
                Observe();
            }
            catch (System.Exception exception)
            {
                if (!loggedFault)
                {
                    loggedFault = true;
                    Debug.LogWarning("[RaceEventRelay] disabled after fault (event bridge only): " + exception);
                }

                enabled = false;
            }
        }

        void Observe()
        {
            FlagState flag = MapFlag();
            WeatherState weather = race.Track != null ? race.Track.weather : WeatherState.Clear;
            int playerLaps = race.PlayerParticipant != null && race.PlayerParticipant.lapTracker != null
                ? race.PlayerParticipant.lapTracker.CompletedLaps
                : 0;

            if (!primed)
            {
                // First frame of a live session: seed without publishing.
                primed = true;
                lastFlag = flag;
                lastWeather = weather;
                lastPlayerLaps = playerLaps;
                lastFinished = race.IsRaceFinished;
                return;
            }

            if (flag != lastFlag)
            {
                GameEvents.Publish(new FlagChangedEvent(lastFlag, flag));
                lastFlag = flag;
            }

            if (weather != lastWeather)
            {
                GameEvents.Publish(new WeatherChangedEvent(MapWeather(lastWeather), MapWeather(weather), WetnessFor(weather)));
                lastWeather = weather;
            }

            if (playerLaps > lastPlayerLaps)
            {
                LapTracker tracker = race.PlayerParticipant.lapTracker;
                GameEvents.Publish(new LapCompletedEvent(
                    race.PlayerParticipant.driverId,
                    playerLaps,
                    tracker.LastLapTime,
                    tracker.LastLapTime > 0f && tracker.LastLapTime <= tracker.BestLapTime + 0.0005f,
                    false));
                lastPlayerLaps = playerLaps;
            }
            else if (playerLaps < lastPlayerLaps)
            {
                lastPlayerLaps = playerLaps;
            }

            if (race.IsRaceFinished != lastFinished)
            {
                lastFinished = race.IsRaceFinished;
                if (lastFinished)
                {
                    GameEvents.Publish(new SessionStateChangedEvent(MapSession(), SessionPhase.Green, SessionPhase.Finished));
                }
            }

            PublishTelemetry();
        }

        void PublishTelemetry()
        {
            RaceParticipant player = race.PlayerParticipant;
            if (player == null || player.vehicle == null)
            {
                F1Game.Core.HudTelemetry.Clear();
                F1Game.Core.HudRaceOrder.Clear();
                if (mappedTrack != null)
                {
                    mappedTrack = null;
                    F1Game.Core.HudTrackMap.Clear();
                }

                return;
            }

            VehicleController vehicle = player.vehicle;
            float ers = vehicle.ErsBattery;
            if (ers > 1.5f)
            {
                ers /= 100f; // normalise if stored as a percentage
            }

            float speed01 = Mathf.Clamp01(Mathf.Abs(vehicle.CurrentSpeedKph) / Mathf.Max(200f, vehicle.TargetTopSpeedKph));
            // TyreState.Wear is REMAINING life (1 fresh); the snapshot field is
            // the worn fraction (0 fresh .. 1 worn) - publishing the raw value
            // rendered the production wear bar inverted.
            float wornFraction = vehicle.Tyres != null ? 1f - Mathf.Clamp01(vehicle.Tyres.Wear) : 0f;
            int compound = vehicle.Tyres != null ? (int)vehicle.Tyres.Compound : 1;

            // Relative gaps: the interval helpers report 999 when nobody is
            // inside the search radius.
            float gapAhead = race.GetIntervalToAheadSeconds(player);
            RaceParticipant behind = race.FindCarBehind(player, 220f);
            float gapBehind = behind == null ? 999f : race.GetGapBetweenSeconds(player, behind);

            // Session delta: the ghost delta in time trial, else the pole delta
            // in qualifying (both from the same numeric source the legacy text
            // readouts now use).
            bool hasSessionDelta = race.TryGetGhostDelta(player, out float sessionDelta) ||
                                   race.TryGetQualifyingDelta(player, out sessionDelta);

            string pitStatusText = BuildPitStatus(vehicle, out F1Game.Core.Events.PitEmphasis pitEmphasis);

            var snapshot = new F1Game.Core.HudTelemetrySnapshot
            {
                Valid = true,
                Position = race.GetPosition(player),
                FieldSize = race.Participants != null ? race.Participants.Count : 0,
                Lap = player.lapTracker != null ? player.lapTracker.CompletedLaps + 1 : 1,
                TotalLaps = race.RaceLaps,
                SessionClockSeconds = race.RaceElapsed,
                SpeedKph = Mathf.Abs(vehicle.CurrentSpeedKph),
                UseMphUnits = race.Settings != null && race.Settings.Current.useMphUnits,
                CompactHud = race.Settings != null && race.Settings.Current.compactHud,
                Gear = vehicle.CurrentGear,
                Rpm01 = Mathf.Clamp01(speed01 * 0.9f + (vehicle.CurrentGear > 0 ? 0.1f : 0f)),
                Ers01 = Mathf.Clamp01(ers),
                DrsActive = vehicle.DrsActive,
                DrsAvailable = race.IsDrsAvailable(player),
                Fuel01 = vehicle.StartFuelKg > 0.01f ? Mathf.Clamp01(vehicle.FuelKg / vehicle.StartFuelKg) : 0f,
                FuelLapsRemaining = vehicle.FuelPerLapEstimateKg > 0.01f ? vehicle.FuelKg / vehicle.FuelPerLapEstimateKg : 0f,
                TyreCompound = compound,
                TyreWear01 = wornFraction,
                BrakeTemp01 = 0f,
                TyreTempStatus = vehicle.Tyres != null ? vehicle.Tyres.TemperatureStatus : "OPT",
                LockupSeverity = vehicle.Tyres != null ? vehicle.Tyres.LockupSeverity : 0f,
                Throttle01 = Mathf.Clamp01(vehicle.EffectiveThrottle),
                Brake01 = Mathf.Clamp01(vehicle.EffectiveBrake),
                DeltaSeconds = sessionDelta,
                HasDelta = hasSessionDelta,
                SlipstreamStrength = vehicle.SlipstreamStrength,
                SlipstreamBonusKph = vehicle.SlipstreamBonusKph,
                SlipstreamSourceCode = vehicle.SlipstreamSourceCode ?? "",
                GapAheadSeconds = gapAhead,
                HasGapAhead = gapAhead < 900f,
                GapBehindSeconds = gapBehind,
                HasGapBehind = gapBehind < 900f,
                Flag = MapFlag(),
                BlueFlag = race.IsShownBlueFlag(player),
                PitLimiterActive = vehicle.PitLimiterActive,
                IsPitting = player.isPitting,
                PenaltySeconds = player.penaltiesSeconds,
                StartLightCount = race.RaceStartLightCount,
                StartLightsVisible = race.RaceStartLightsVisible,
                CurrentLapSeconds = player.lapTracker != null ? player.lapTracker.CurrentLapTime : 0f,
                LastLapSeconds = player.lapTracker != null ? player.lapTracker.LastLapTime : 0f,
                LastLapInvalidated = player.lapTracker != null && player.lapTracker.LastLapInvalidated,
                BestLapSeconds = player.lapTracker != null ? player.lapTracker.BestLapTime : 0f,
                SessionBestSeconds = race.SessionFastestLap,
                Weather = MapWeather(race.Track != null ? race.Track.weather : WeatherState.Clear),
                Session = MapSession(),
                EventName = race.EventData != null ? race.EventData.displayName : "",
                SessionMessage = race.SessionMessage ?? "",
                QualifyingFeedback = race.QualifyingFeedbackText ?? "",
                CheckpointsPassed = player.lapTracker != null ? player.lapTracker.CheckpointsPassed : 0,
                Damage01 = vehicle.Damage != null ? Mathf.Clamp01(vehicle.Damage.OverallPercent / 100f) : 0f,
                FuelStarved = vehicle.FuelStarved,
                PitStopProgress01 = race.PitStopProgress01(player),
                PaceLimited = race.IsRaceControlPaceLimited,
                PaceCapKph = race.RaceControlSpeedCapKphFor(player),
                RestartCountdownSeconds = race.RestartCountdownSeconds,
                RaceControlDetail = BuildRaceControlDetail(),
                PitStatusText = pitStatusText,
                PitStatusEmphasis = pitEmphasis,
                TrackLimitWarnings = player.trackLimitWarnings,
                CanCancelPit = race.CanCancelManualPitRequest(),
                PitRequested = vehicle.PitRequested,
                NextPlannedPitLap = race.NextPlannedPitLapFor(player),
                NextPlannedPitCompound = (int)race.NextPlannedPitCompoundFor(player),
                BoxThisLap = race.NextPlannedPitLapFor(player) > 0 && player.lapTracker != null &&
                             player.lapTracker.CompletedLaps + 1 >= race.NextPlannedPitLapFor(player),
                ScPitWindowOpen = race.RecommendedPitUnderSafetyCar(player),
            };

            if (race.State != null)
            {
                float s1, s2, s3;
                race.State.GetSectorTimes(player, out s1, out s2, out s3);
                snapshot.Sector1Seconds = s1;
                snapshot.Sector2Seconds = s2;
                snapshot.Sector3Seconds = s3;
                snapshot.BestSector1Seconds = race.State.GetOverallBestSector(1);
                snapshot.BestSector2Seconds = race.State.GetOverallBestSector(2);
                snapshot.BestSector3Seconds = race.State.GetOverallBestSector(3);
            }

            F1Game.Core.HudTelemetry.Publish(snapshot);
            PublishTrackMap();

            raceOrderRefreshTimer -= Time.deltaTime;
            if (raceOrderRefreshTimer <= 0f)
            {
                raceOrderRefreshTimer = 0.5f;
                PublishRaceOrder();
            }
        }

        // Minimap: the track outline (published once per track) and the live
        // car dots (every frame), both normalized 0..1 into a shared XZ box so
        // the UI maps into its own rect without knowing world scale.
        TrackRuntime mappedTrack;
        float mapMinX, mapMinZ, mapRangeX, mapRangeZ;
        readonly Vector2[] outlineScratch = new Vector2[F1Game.Core.HudTrackMap.MaxOutline];

        void PublishTrackMap()
        {
            if (race.Track == null || race.Track.centerLine == null || race.Track.centerLine.Count < 4)
            {
                if (mappedTrack != null)
                {
                    mappedTrack = null;
                    F1Game.Core.HudTrackMap.Clear();
                }

                return;
            }

            if (!ReferenceEquals(race.Track, mappedTrack))
            {
                BuildTrackOutline();
                mappedTrack = race.Track;
            }

            if (mapRangeX <= 0.0001f || mapRangeZ <= 0.0001f)
            {
                return;
            }

            var participants = race.Participants;
            int dotCount = Mathf.Min(participants.Count, F1Game.Core.HudTrackMap.MaxDots);
            for (int i = 0; i < dotCount; i++)
            {
                RaceParticipant p = participants[i];
                Vector3 pos = p != null && p.vehicle != null ? p.transform.position
                    : (p != null ? p.transform.position : Vector3.zero);
                F1Game.Core.HudTrackMap.Dots[i] = new F1Game.Core.HudMapDot
                {
                    X = Mathf.Clamp01((pos.x - mapMinX) / mapRangeX),
                    Y = Mathf.Clamp01((pos.z - mapMinZ) / mapRangeZ),
                    IsPlayer = p != null && p.isPlayer,
                    Retired = p != null && p.retired,
                    InPit = IsEnteringOrInPit(p),
                };
            }

            F1Game.Core.HudTrackMap.DotCount = dotCount;
        }

        void BuildTrackOutline()
        {
            var line = race.Track.centerLine;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 p = line[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }

            // Small margin so cars near the edge stay inside the map rect.
            float margin = 0.04f;
            mapRangeX = Mathf.Max(1f, maxX - minX);
            mapRangeZ = Mathf.Max(1f, maxZ - minZ);
            mapMinX = minX - mapRangeX * margin;
            mapMinZ = minZ - mapRangeZ * margin;
            mapRangeX *= 1f + margin * 2f;
            mapRangeZ *= 1f + margin * 2f;

            // Subsample the centerline down to the outline budget.
            // Truncation fix (per report - wrong map shape): step used integer
            // FLOOR division (count/budget), so a subdivided ~730-point line
            // with a 128-point budget produced 146 candidate samples, hit the
            // budget cap early, and silently DROPPED the last ~12% of the lap
            // - the published outline was missing its whole final sector and
            // the UI closed the hole with a straight chord. Ceil guarantees
            // the stride always spans the entire loop within budget.
            int budget = Mathf.Min(line.Count, F1Game.Core.HudTrackMap.MaxOutline);
            int step = Mathf.Max(1, Mathf.CeilToInt(line.Count / (float)budget));
            int n = 0;
            for (int i = 0; i < line.Count && n < F1Game.Core.HudTrackMap.MaxOutline; i += step)
            {
                Vector3 p = line[i];
                outlineScratch[n++] = new Vector2(
                    (p.x - mapMinX) / mapRangeX,
                    (p.z - mapMinZ) / mapRangeZ);
            }

            F1Game.Core.HudTrackMap.PublishOutline(outlineScratch, n);

            // [MapDiag] - relocated to the map that is ACTUALLY displayed: the
            // production UI draws this relay-published outline, not
            // RaceHud.BuildTrackMap (whose earlier [MapDiag] therefore never
            // printed). States exactly what was published, from which runtime.
            Debug.LogWarning("[MapDiag] published outline for '" + race.Track.displayName + "' (id=" + race.Track.trackId +
                             "): centreline=" + line.Count + " points, outline=" + n + " samples (step " + step +
                             "), boundsXZ=(" + mapMinX.ToString("0") + "," + mapMinZ.ToString("0") +
                             ") range=(" + mapRangeX.ToString("0") + "," + mapRangeZ.ToString("0") + ")");
        }

        // Second line of the production race-control banner: the red-flag
        // reason while suspended, or the player's safety-car queue slot while
        // one is out; empty under ordinary running.
        string BuildRaceControlDetail()
        {
            if (race.IsRedFlagged && !string.IsNullOrEmpty(race.RedFlagReason))
            {
                return race.RedFlagReason;
            }

            if (race.IsSafetyCarConvoyActive)
            {
                int queue = race.PlayerSafetyCarQueuePosition;
                float gap = race.PlayerGapToSafetyCarMeters();
                if (queue > 0)
                {
                    return "Queue P" + queue + (gap > 0f ? " · +" + gap.ToString("0") + "m" : "");
                }
            }

            return "";
        }

        // Composes the pit-status headline + emphasis exactly as the legacy pit
        // card does (RaceHud.UpdatePitCard): a just-cancelled confirmation wins,
        // then the standalone limiter readout (entry 80 / fast-exit 108 cap)
        // while outside the pit phase machine, otherwise the race layer's own
        // PitStatusText with the queued-request tint. Empty means "hide the line"
        // (qualifying / idle time trial). player is the local resolved above.
        string BuildPitStatus(VehicleController vehicle, out F1Game.Core.Events.PitEmphasis emphasis)
        {
            emphasis = F1Game.Core.Events.PitEmphasis.Neutral;
            RaceParticipant player = race.PlayerParticipant;
            if (player == null || vehicle == null)
            {
                return "";
            }

            if (race.PlayerManualPitCancelMessageActive)
            {
                emphasis = F1Game.Core.Events.PitEmphasis.Cancelled;
                return "MANUAL PIT STOP CANCELLED";
            }

            bool limiterOnly = vehicle.PitLimiterActive && player.pitPhase == PitPhase.None;
            if (limiterOnly)
            {
                // Player entry limiter is 105 (player pit-entry buff); this line
                // only ever renders for the player.
                int limiterCapKph = vehicle.PitExitFastLimiter ? 108 : 105;
                return (player.pitLimiterUntilExit ? "PIT EXIT  LIMITER " : "PIT APPROACH  LIMITER ") + limiterCapKph;
            }

            bool requestQueued = vehicle.PitRequested && player.pitPhase == PitPhase.None && !player.isPitting;
            if (requestQueued)
            {
                emphasis = player.pitAutoTriggered ? F1Game.Core.Events.PitEmphasis.Auto : F1Game.Core.Events.PitEmphasis.Manual;
            }

            return race.PitStatusText(player);
        }

        float raceOrderRefreshTimer;

        // Static HUD snapshots must not outlive the session that produced them
        // - a destroyed relay means no more publishes, so a stale "valid"
        // snapshot would freeze the production HUD on old data.
        void OnDestroy()
        {
            F1Game.Core.HudTelemetry.Clear();
            F1Game.Core.HudRaceOrder.Clear();
            F1Game.Core.HudCommands.Clear();
            F1Game.Core.HudTrackMap.Clear();
        }

        // Running-order snapshot for the production timing tower: the sorted
        // order the race already maintains, reduced to display rows in a fixed
        // buffer (no allocation beyond the code strings Unity interns anyway).
        void PublishRaceOrder()
        {
            if (race.State == null || race.State.SortedOrder == null)
            {
                F1Game.Core.HudRaceOrder.Clear();
                return;
            }

            List<RaceParticipant> order = race.State.SortedOrder;
            RaceParticipant leader = order.Count > 0 ? order[0] : null;
            int count = Mathf.Min(order.Count, F1Game.Core.HudRaceOrder.MaxEntries);
            for (int i = 0; i < count; i++)
            {
                RaceParticipant p = order[i];
                RaceParticipant ahead = i > 0 ? order[i - 1] : null;
                string code = p == null ? "---"
                    : (p.driverData != null && !string.IsNullOrEmpty(p.driverData.abbreviation) ? p.driverData.abbreviation
                    : (string.IsNullOrEmpty(p.driverName) ? "---" : p.driverName));
                F1Game.Core.HudRaceOrder.Entries[i] = new F1Game.Core.HudRaceOrderEntry
                {
                    Position = i + 1,
                    Code = code,
                    GapToLeaderSeconds = p == null || p == leader ? 0f : Mathf.Max(0f, race.GetGapBetweenSeconds(leader, p)),
                    IntervalSeconds = p == null || ahead == null ? 0f : Mathf.Max(0f, race.GetGapBetweenSeconds(ahead, p)),
                    Compound = p != null && p.vehicle != null && p.vehicle.Tyres != null ? (int)p.vehicle.Tyres.Compound : 1,
                    DrsActive = p != null && p.vehicle != null && p.vehicle.DrsActive,
                    IsPlayer = p != null && p.isPlayer,
                    InPit = IsEnteringOrInPit(p),
                    Retired = p != null && p.retired,
                };
            }

            F1Game.Core.HudRaceOrder.Count = count;
        }

        // Leaderboard/track-map pit flag. The car is shown as pitting not only
        // once it's physically committed to the ramp (pitPhase != None / isPitting,
        // set at BeginPitEntry) but the instant it starts entering: a latched pit
        // request while in the pit approach window (the same 0.78->corridor-start
        // span the AI's committingToPit and the player's pit-entry assist use) -
        // so the badge appears as soon as a car peels off toward the pits rather
        // than only once it's already on the ramp. missedPitEntryThisLap guards a
        // request that ran past the opening without committing.
        bool IsEnteringOrInPit(RaceParticipant p)
        {
            if (p == null)
            {
                return false;
            }

            if (p.isPitting || p.pitPhase != PitPhase.None)
            {
                return true;
            }

            if (p.vehicle == null || !p.vehicle.PitRequested || p.missedPitEntryThisLap ||
                race.Track == null || p.lapTracker == null)
            {
                return false;
            }

            return race.Track.IsInPitApproach(p.lapTracker.CurrentProgress.normalized);
        }

        FlagState MapFlag()
        {
            if (race.IsRaceFinished)
            {
                return FlagState.Chequered;
            }

            switch (race.CurrentRaceControlState)
            {
                case RaceManager.RaceControlState.YellowSector: return FlagState.Yellow;
                case RaceManager.RaceControlState.VirtualSafetyCar: return FlagState.VirtualSafetyCar;
                case RaceManager.RaceControlState.SafetyCarDeploying:
                case RaceManager.RaceControlState.SafetyCarActive:
                case RaceManager.RaceControlState.SafetyCarInThisLap: return FlagState.SafetyCar;
                case RaceManager.RaceControlState.RedFlagged: return FlagState.Red;
                default: return FlagState.Green;
            }
        }

        SessionKind MapSession()
        {
            if (race.IsTimeTrial)
            {
                return SessionKind.TimeTrial;
            }

            switch (race.CurrentSession)
            {
                case RaceWeekendSession.Qualifying: return SessionKind.Qualifying;
                case RaceWeekendSession.Practice: return SessionKind.Practice;
                default: return SessionKind.Race;
            }
        }

        static WeatherKind MapWeather(WeatherState state)
        {
            switch (state)
            {
                case WeatherState.Cloudy: return WeatherKind.Overcast;
                case WeatherState.LightRain: return WeatherKind.LightRain;
                case WeatherState.HeavyRain: return WeatherKind.HeavyRain;
                default: return WeatherKind.Clear;
            }
        }

        static float WetnessFor(WeatherState state)
        {
            switch (state)
            {
                case WeatherState.LightRain: return 0.45f;
                case WeatherState.HeavyRain: return 1f;
                default: return 0f;
            }
        }
    }
}
