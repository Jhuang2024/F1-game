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
        // 0 = bone dry, 1 = fully soaked; -1 = not yet initialised for this
        // session (snaps straight to the starting weather's level so a wet
        // race begins on a wet track). See UpdateTrackWetness.
        float trackWetness01 = -1f;

        // Gradual track soaking/drying (per report - "everything goes to shit
        // when the weather goes from dry to wet"): the weather STATE flips in
        // one frame - and strategy (pit calls, compound picks, engineer radio)
        // reacting instantly to that flip is correct - but physical grip used
        // to flip with it: slicks lost 60-84% of their grip between two frames
        // under a full field at race commitment, which is exactly the
        // whole-field chaos reported. The track now physically SOAKS over ~90
        // seconds of rain (and dries over ~150), and TyreState blends every
        // weather-driven grip/offset/lockup term by this wetness - so when
        // rain arrives the field gets a realistic, driveable crossover window
        // to reach the pits on slicks while the track wets up, instead of a
        // same-frame ice rink.
        void UpdateTrackWetness()
        {
            if (Track == null)
            {
                return;
            }

            bool raining = Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain;
            float target = raining ? 1f : 0f;
            if (trackWetness01 < 0f)
            {
                trackWetness01 = target;
            }

            float rate = target > trackWetness01 ? 1f / 90f : 1f / 150f;
            trackWetness01 = Mathf.MoveTowards(trackWetness01, target, rate * Time.deltaTime);
            TyreState.TrackWetness01 = trackWetness01;

            // Lap length for the distance-normalized wear model (TyreState).
            // Kept fresh here (cheap, once per tick, same shared-static
            // pattern as the wetness line above) rather than at one session-
            // start site, so every session type - race, quali, practice, time
            // trial, quick race - is covered without hunting down each
            // initialization path.
            TyreState.TrackLengthMeters = Mathf.Max(500f, Track.length);
        }

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
