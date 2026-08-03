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
        /// <summary>
        /// True once the race has run in wet conditions at any point. The real
        /// two-compound rule is voided in a wet race, so this is what exempts a
        /// driver from it (alongside actually having fitted a wet-weather tyre).
        /// </summary>
        public bool RaceDeclaredWet { get; private set; }

        // Baseline dry track temperature for this event, captured at build time.
        float dryBaseTrackTemperatureC = -1f;

        /// <summary>
        /// Track temperature follows conditions. It used to be written once at track
        /// build and never touched again - so when rain arrived mid-race the tyres
        /// kept degrading at the dry-track rate, and the wet crossover the wetness
        /// model exists for was only half modelled. A real shower drops track temp
        /// 10-20 C within minutes.
        /// </summary>
        void UpdateTrackTemperature()
        {
            if (Track == null)
            {
                return;
            }

            if (dryBaseTrackTemperatureC < 0f)
            {
                dryBaseTrackTemperatureC = Track.trackTemperatureC;
            }

            float target = dryBaseTrackTemperatureC - 14f * Mathf.Clamp01(trackWetness01);
            Track.trackTemperatureC = Mathf.MoveTowards(Track.trackTemperatureC, target, Time.deltaTime * 1.2f);
        }

        void NoteWeatherForRuleExemptions()
        {
            if (Track != null && (Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain))
            {
                RaceDeclaredWet = true;
            }
        }

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

            NoteWeatherForRuleExemptions();
            UpdateTrackTemperature();
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
            // Tyre life is a distance, so every lap-based figure - the AI's plan, the
            // wear model, the tyre screen - has to know how long a lap of THIS circuit
            // is. Same shared-static pattern, same tick.
            TyreStrategyRules.SessionLapLengthMeters = Mathf.Max(500f, Track.length);
            // Tyre life is compressed to the fraction of a real grand prix this race
            // actually is, so a 5-lap race still has stints, a pit window and a
            // decision instead of tyres that outlast it several times over.
            TyreStrategyRules.SetRaceDistance(
                RaceLaps,
                EventData != null && EventData.lapsFull > 0 ? EventData.lapsFull : RaceLaps);
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
            // An arriving shower can be HEAVY. This used to be hard-coded to
            // LightRain, so HeavyRain was unreachable from any mid-race change and a
            // dry race could never be hit by a downpour.
            WeatherState next = WeatherRules.NextIsRaining(wasRaining)
                ? (WeatherRules.ArrivingRainIsHeavy(Random.value, variability) ? WeatherState.HeavyRain : WeatherState.LightRain)
                : WeatherState.Cloudy;
            // [WeatherDiag] (companion to the [PitStopDiag] recorders): every
            // mid-race weather flip is a mass pit-crossover generator, so the
            // timeline must be visible in the same log the stop records land
            // in - a "2 stops for no reason" race with two of these lines is
            // a double weather transition, not a strategy bug.
            Debug.LogWarning("[WeatherDiag] Weather transition " + Track.weather + " -> " + next +
                " on lap " + (completedLaps + 1) + "/" + RaceLaps + " (phase=" + phase + ") - expect a field-wide tyre crossover wave.");
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
