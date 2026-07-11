using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager weather + track-evolution subsystem (partial). Split out of the
    /// RaceManager monolith verbatim - same class, same members, identical
    /// behaviour - so the mixed-forecast weather swing and the rubber/track-grip
    /// evolution read in one place. The pure decisions live in the engine-free
    /// WeatherRules; this partial owns the live state and the engine calls
    /// (Track weather, per-car SetWeather/grip, audio, fog, engineer radio).
    /// </summary>
    public partial class RaceManager
    {
        bool weatherTransitionDone;
        bool weatherSecondTransitionDone;
        bool trackEvolutionHalfwayMessageSent;

        // Simple dynamic weather: on mixed-forecast races the conditions flip once
        // past half distance — rain arrives on a dry track, or a wet track starts
        // drying. Grip, tyre wear, audio and lighting mood all follow.
        void UpdateWeatherTransition()
        {
            // Part 19: Weather Variability setting. Off locks the session to its
            // starting state (no transition at all); High allows a second, later
            // swing on top of the usual half-distance one for a mixed forecast.
            int variability = Settings == null ? 2 : Settings.Current.weatherVariability;
            if (variability <= 0 || IsTimeTrial || CurrentSession == RaceWeekendSession.Qualifying ||
                Track == null || EventData == null || string.IsNullOrEmpty(EventData.weatherProfile) ||
                !EventData.weatherProfile.ToLowerInvariant().Contains("mixed") ||
                PlayerParticipant == null || PlayerParticipant.lapTracker == null)
            {
                return;
            }

            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            WeatherRules.TransitionPhase phase = WeatherRules.EvaluateTransition(
                variability, completedLaps, RaceLaps, weatherTransitionDone, weatherSecondTransitionDone);
            if (phase == WeatherRules.TransitionPhase.None)
            {
                return;
            }

            if (phase == WeatherRules.TransitionPhase.First)
            {
                weatherTransitionDone = true;
            }
            else
            {
                weatherSecondTransitionDone = true;
            }

            bool wasRaining = Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain;
            WeatherState next = WeatherRules.NextIsRaining(wasRaining) ? WeatherState.LightRain : WeatherState.Cloudy;
            Track.weather = next;
            for (int i = 0; i < Participants.Count; i++)
            {
                if (Participants[i] != null && Participants[i].vehicle != null)
                {
                    Participants[i].vehicle.SetWeather(next);
                }
            }

            bool raining = next == WeatherState.LightRain || next == WeatherState.HeavyRain;
            SimpleAudioManager.SetRain(raining);
            RenderSettings.fogColor = raining ? new Color(0.28f, 0.34f, 0.36f) : RenderSettings.fogColor;
            RenderSettings.reflectionIntensity = raining ? 0.85f : 0.68f;
            PostEngineerMessage(raining
                ? "Rain is arriving. Grip is dropping, intermediates will come alive."
                : "The rain has stopped and the track is drying. Slicks will come to you.", true);
        }

        // Dynamic track evolution: session-wide grip gradually rises as rubber goes
        // down over green-flag running (the real "the track is coming in" effect),
        // instead of every lap of a race or qualifying session running on
        // identical grip regardless of how many laps have already been driven.
        // Heavy rain washes the rubber back off, since a soaked track keeps none
        // of the built-up line. A single TrackRuntime.RubberLevel (0-1) feeds a
        // small multiplicative bonus applied identically to every car via
        // VehicleController.SetTrackGripMultiplier (read inside
        // TyreState.GripMultiplier), plus a subtle darkening tween on the shared
        // road material for visual feedback. The ramp/grip maths live in
        // WeatherRules (engine-free, testable); RaceManager owns the live state.
        void UpdateTrackEvolution()
        {
            if (Track == null || Settings == null || !Settings.Current.trackEvolutionEnabled || IsTimeTrial)
            {
                return;
            }

            bool washedByRain = Track.weather == WeatherState.HeavyRain;
            Track.RubberLevel = WeatherRules.RampRubberLevel(Track.RubberLevel, washedByRain, Time.deltaTime);

            float gripMultiplier = WeatherRules.GripMultiplier(Track.RubberLevel);
            for (int i = 0; i < Participants.Count; i++)
            {
                if (Participants[i] != null && Participants[i].vehicle != null)
                {
                    Participants[i].vehicle.SetTrackGripMultiplier(gripMultiplier);
                }
            }

            Track.ApplyRubberEvolutionVisual(Track.RubberLevel);

            if (!trackEvolutionHalfwayMessageSent && !washedByRain && Track.RubberLevel > 0.5f && CurrentSession != RaceWeekendSession.Qualifying)
            {
                trackEvolutionHalfwayMessageSent = true;
                PostEngineerMessage("Track's rubbering in nicely, you should find a bit more grip now.", false);
            }
        }
    }
}
