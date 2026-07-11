using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager qualifying-simulation entry points (partial). The shared
    /// best-of-two attempt orchestration and the tyre/weather penalty, split
    /// out of the monolith verbatim (RNG order and all values unchanged). The
    /// deeper lap-time model (SimulateQualifyingRunDetailed and the field-average
    /// helpers) remains in the main file for now.
    /// </summary>
    public partial class RaceManager
    {
        // Qualifying rework: AI and player used to run two independently-written
        // "best of two laps" implementations with two different, uncalibrated
        // second-run-improvement models (the AI version could hand up to ~0.46s for
        // no reason beyond a coin flip; the player version up to ~0.34s). Both now
        // share this one helper - the second-run gain is a small, explicit,
        // reasoned term (see below) instead of raw per-path randomness, so AI and
        // player results are internally consistent with each other.
        QualifyingLapBreakdown SimulateBestOfTwoQualifyingAttempt(QualifyingSimEntry entry, int phase, TyreCompound? tyreChoice)
        {
            QualifyingLapBreakdown first = SimulateQualifyingRunDetailed(entry, phase, false);
            QualifyingLapBreakdown second = SimulateQualifyingRunDetailed(entry, phase, true);

            // Second-run improvement: a small baseline gain from track evolution,
            // better tyre prep and driver adaptation on a repeated lap - NOT a
            // random half-second lottery. A second run only gains meaningfully more
            // than that when the first lap was genuinely compromised by a mistake
            // (see QualifyingMistakePenalty) - recovering from a bad lap, not luck.
            float secondRunGain = Random.Range(0.03f, 0.10f);
            if (first.mistakePenalty > 0.05f)
            {
                secondRunGain += Mathf.Min(first.mistakePenalty * 0.5f, 0.5f);
            }

            second.variance -= secondRunGain;
            second.finalTime -= secondRunGain;

            if (tyreChoice.HasValue)
            {
                float tyrePenalty = PlayerQualifyingTyreWeatherPenalty(tyreChoice.Value);
                first.tyreChoicePenalty = tyrePenalty;
                first.finalTime += tyrePenalty;
                second.tyreChoicePenalty = tyrePenalty;
                second.finalTime += tyrePenalty;
            }

            return first.finalTime <= second.finalTime ? first : second;
        }

        float SimulateAiQualifyingTime(QualifyingSimEntry entry, int phase)
        {
            return SimulateBestOfTwoQualifyingAttempt(entry, phase, null).finalTime;
        }

        float SimulatePlayerQualifyingTime(QualifyingSimEntry entry, int phase)
        {
            TyreCompound compound = Settings == null ? TyreCompound.Medium : Settings.SelectedTyreCompound;
            QualifyingLapBreakdown best = SimulateBestOfTwoQualifyingAttempt(entry, phase, compound);
            best.finalTime = Mathf.Max(20f, best.finalTime);
            if (phase >= 1 && phase <= 3)
            {
                playerSimBreakdowns[phase - 1] = best;
            }

            return best.finalTime;
        }

        float PlayerQualifyingTyreWeatherPenalty(TyreCompound compound)
        {
            WeatherState weather = Track == null ? WeatherState.Clear : Track.weather;
            if (weather == WeatherState.HeavyRain)
            {
                if (compound == TyreCompound.Wet)
                {
                    return -0.12f;
                }

                if (compound == TyreCompound.Intermediate)
                {
                    return 1.45f;
                }

                return 5.4f;
            }

            if (weather == WeatherState.LightRain)
            {
                if (compound == TyreCompound.Intermediate)
                {
                    return -0.12f;
                }

                if (compound == TyreCompound.Wet)
                {
                    return 0.74f;
                }

                return 2.75f;
            }

            if (compound == TyreCompound.Soft)
            {
                return -0.18f;
            }

            if (compound == TyreCompound.Medium)
            {
                return 0.08f;
            }

            if (compound == TyreCompound.Hard)
            {
                return 0.34f;
            }

            return compound == TyreCompound.Intermediate ? 1.7f : 3.1f;
        }
    }
}
