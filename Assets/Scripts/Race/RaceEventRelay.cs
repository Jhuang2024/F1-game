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

        public void Attach(RaceManager raceManager)
        {
            race = raceManager;
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
                return;
            }

            VehicleController vehicle = player.vehicle;
            float ers = vehicle.ErsBattery;
            if (ers > 1.5f)
            {
                ers /= 100f; // normalise if stored as a percentage
            }

            float speed01 = Mathf.Clamp01(Mathf.Abs(vehicle.CurrentSpeedKph) / Mathf.Max(200f, vehicle.TargetTopSpeedKph));
            float wear = vehicle.Tyres != null ? Mathf.Clamp01(vehicle.Tyres.Wear) : 0f;
            int compound = vehicle.Tyres != null ? (int)vehicle.Tyres.Compound : 1;

            var snapshot = new F1Game.Core.HudTelemetrySnapshot
            {
                Valid = true,
                Position = race.GetPosition(player),
                FieldSize = 22,
                Lap = player.lapTracker != null ? player.lapTracker.CompletedLaps + 1 : 1,
                TotalLaps = race.RaceLaps,
                SessionClockSeconds = race.RaceElapsed,
                SpeedKph = Mathf.Abs(vehicle.CurrentSpeedKph),
                Gear = vehicle.CurrentGear,
                Rpm01 = Mathf.Clamp01(speed01 * 0.9f + (vehicle.CurrentGear > 0 ? 0.1f : 0f)),
                Ers01 = Mathf.Clamp01(ers),
                DrsActive = vehicle.DrsActive,
                DrsAvailable = race.IsDrsAvailable(player),
                Fuel01 = 0f,
                FuelLapsRemaining = vehicle.FuelPerLapEstimateKg > 0.01f ? vehicle.FuelKg / vehicle.FuelPerLapEstimateKg : 0f,
                TyreCompound = compound,
                TyreWear01 = wear,
                BrakeTemp01 = 0f,
                DeltaSeconds = 0f,
                HasDelta = false,
            };

            F1Game.Core.HudTelemetry.Publish(snapshot);
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
