namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure, RNG-free pieces of the qualifying lap-time model, extracted verbatim
    /// from RaceManager.Qualifying so the track-speed character, the wet-weather
    /// penalty, the mistake probability and the invalid-time sentinel are stated and
    /// tested in one place. No engine dependency: weather is the small int code that
    /// matches the live WeatherState enum ordering (Clear 0, Cloudy 1, LightRain 2,
    /// HeavyRain 3 - see <see cref="Weather"/>), and every <c>Random</c> roll that
    /// turns these into an actual outcome stays in RaceManager. Feel-sensitive
    /// numbers are unchanged - this is a relocation, not a retune.
    /// </summary>
    public static class QualifyingModel
    {
        /// <summary>Weather codes matching the live WeatherState enum values.</summary>
        public static class Weather
        {
            public const int Clear = 0;
            public const int Cloudy = 1;
            public const int LightRain = 2;
            public const int HeavyRain = 3;
        }

        /// <summary>
        /// A circuit's average-speed character (relative lap-time scaler), extracted
        /// verbatim from RaceManager.TrackAverageSpeedFactor. The caller keeps the
        /// null-track guard (which returns 0.83 before any of these reads);
        /// <paramref name="trackId"/> is matched case-sensitively exactly as the
        /// inline code did, while <paramref name="styleName"/> is lower-cased here.
        /// </summary>
        public static float TrackSpeedFactor(string trackId, string styleName, float roadHalfWidth)
        {
            string id = trackId ?? "";
            string style = (styleName ?? "").ToLowerInvariant();
            if (id.Contains("monaco"))
            {
                return 0.65f;
            }

            if (id.Contains("spa") || id.Contains("monza") || id.Contains("silverstone") ||
                id.Contains("baku") || id.Contains("jeddah") || id.Contains("las_vegas") ||
                id.Contains("suzuka") || id.Contains("qatar"))
            {
                return 1.02f;
            }

            if (id.Contains("hungary"))
            {
                return 0.71f;
            }

            if (style.Contains("street") || roadHalfWidth < 12f)
            {
                return 0.76f;
            }

            return 0.92f;
        }

        /// <summary>
        /// The wet-weather qualifying penalty (seconds), extracted verbatim from
        /// RaceManager.WeatherQualifyingPenalty: a shared baseline per condition
        /// (Clear 0, Cloudy 0.04, light/heavy rain 1.25/2.65) with wetSkill creating
        /// a controlled 1.1x-0.6x spread around the rain baseline. The caller maps
        /// its live Track.weather to a code and supplies the driver's wetSkill.
        /// </summary>
        public static float WeatherPenalty(int weatherCode, float wetSkill)
        {
            if (weatherCode == Weather.Clear)
            {
                return 0f;
            }

            if (weatherCode == Weather.Cloudy)
            {
                return 0.04f;
            }

            float basePenalty = weatherCode == Weather.HeavyRain ? 2.65f : 1.25f;
            return basePenalty * Lerp(1.1f, 0.6f, wetSkill / 100f);
        }

        /// <summary>
        /// The probability (0-1) a qualifying lap picks up a mistake, extracted
        /// verbatim from RaceManager.QualifyingMistakePenalty's chance build-up:
        /// consistency drives the base rate, rain raises it, and Q3 nudges it up.
        /// The caller rolls against this and produces the mistake type/magnitude
        /// (all RNG stays there).
        /// </summary>
        public static float MistakeChance(float consistency, int weatherCode, int phase)
        {
            float chance = Lerp(0.075f, 0.012f, consistency / 100f);
            if (weatherCode == Weather.LightRain)
            {
                chance += 0.025f;
            }
            else if (weatherCode == Weather.HeavyRain)
            {
                chance += 0.045f;
            }

            if (phase == 3)
            {
                chance += 0.008f;
            }

            return chance;
        }

        /// <summary>
        /// The invalid/no-time sentinel for a phase, extracted verbatim from
        /// RaceManager.InvalidQualifyingTime: a large value nudged per phase so
        /// sorts stay stable across Q1/Q2/Q3.
        /// </summary>
        public static float InvalidTime(int phase)
        {
            int clamped = phase < 1 ? 1 : (phase > 3 ? 3 : phase);
            return 9998f + clamped * 0.1f;
        }

        // Engine-free UnityEngine.Mathf.Lerp equivalent (clamps t to 0-1).
        static float Lerp(float a, float b, float t)
        {
            float clamped = t < 0f ? 0f : (t > 1f ? 1f : t);
            return a + (b - a) * clamped;
        }
    }
}
