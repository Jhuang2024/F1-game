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

        /// <summary>
        /// topSpeed (a literal ~300-365 km/h value) mapped onto the same 45-125
        /// rating scale every other car stat uses, extracted verbatim from
        /// RaceManager.NormalizeTopSpeedToRating, so it can be weighted alongside
        /// the other ratings without a unit mismatch.
        /// </summary>
        public static float TopSpeedRating(float topSpeedKph)
        {
            return Lerp(45f, 125f, InverseLerp(300f, 365f, topSpeedKph));
        }

        /// <summary>
        /// The neutral circuit reference lap (seconds), extracted verbatim from
        /// RaceManager.CircuitReferenceLapTime: track length over the field's shared
        /// expected average speed (the field-average top speed x the circuit style
        /// factor), floored at 45 s. The caller supplies the live field average, the
        /// style factor and the (null-defaulted) track length.
        /// </summary>
        public static float ReferenceLapTime(float neutralTopSpeedKph, float styleFactor, float trackLengthMeters)
        {
            float referenceSpeedMps = (neutralTopSpeedKph / 3.6f) * styleFactor;
            return Max(45f, trackLengthMeters / referenceSpeedMps);
        }

        /// <summary>
        /// Per-circuit car-stat weighting (each set sums to 1.0), extracted verbatim
        /// from RaceManager.CarPerformanceWeights: power-sensitive circuits weight
        /// top speed/acceleration/engine, high-downforce circuits weight
        /// aero/cornering, technical/tight circuits weight cornering/braking/chassis,
        /// and everything else gets an even spread. The caller maps a null track to
        /// empty descriptors and a wide road half-width (so the tight-circuit test
        /// is false, matching the inline null short-circuit).
        /// </summary>
        public static void CarPerformanceWeights(string trackId, string styleName, float roadHalfWidth,
            out float wTopSpeed, out float wAcceleration, out float wCornering, out float wBraking,
            out float wAero, out float wChassis, out float wEngine)
        {
            string id = trackId ?? "";
            string style = (styleName ?? "").ToLowerInvariant();

            if (id.Contains("monza") || id.Contains("las_vegas") || id.Contains("baku") || id.Contains("jeddah"))
            {
                wTopSpeed = 0.28f; wAcceleration = 0.20f; wCornering = 0.10f; wBraking = 0.08f;
                wAero = 0.07f; wChassis = 0.05f; wEngine = 0.22f;
                return;
            }

            if (id.Contains("spa") || id.Contains("silverstone") || id.Contains("suzuka") || id.Contains("qatar"))
            {
                wTopSpeed = 0.10f; wAcceleration = 0.08f; wCornering = 0.26f; wBraking = 0.10f;
                wAero = 0.26f; wChassis = 0.14f; wEngine = 0.06f;
                return;
            }

            if (id.Contains("monaco") || id.Contains("hungary") || style.Contains("street") || roadHalfWidth < 12f)
            {
                wTopSpeed = 0.04f; wAcceleration = 0.14f; wCornering = 0.28f; wBraking = 0.24f;
                wAero = 0.06f; wChassis = 0.20f; wEngine = 0.04f;
                return;
            }

            wTopSpeed = 0.14f; wAcceleration = 0.14f; wCornering = 0.20f; wBraking = 0.16f;
            wAero = 0.16f; wChassis = 0.12f; wEngine = 0.08f;
        }

        /// <summary>
        /// The driver-skill contribution to a qualifying lap (seconds, signed),
        /// extracted verbatim from SimulateQualifyingRunDetailed. Each stat is
        /// measured against the FIELD's own average of that stat (so the whole grid
        /// isn't shifted by a strong/weak roster) and weighted by its coefficient:
        /// qualifying ability is primary, pace and confidence smaller secondary
        /// flavours. Positive means slower than the field average.
        /// </summary>
        public static float DriverEffect(float qualifying, float pace, float confidence,
            float avgQualifying, float avgPace, float avgConfidence,
            float qualifyingCoeff, float paceCoeff, float confidenceCoeff)
        {
            return (avgQualifying - qualifying) * qualifyingCoeff +
                   (avgPace - pace) * paceCoeff +
                   (avgConfidence - confidence) * confidenceCoeff;
        }

        /// <summary>
        /// The car contribution to a qualifying lap (seconds, signed), extracted
        /// verbatim from SimulateQualifyingRunDetailed: the car's composite rating
        /// gap to the field average times the per-point coefficient, clamped so an
        /// extreme outlier car can never dominate the result on its own. Positive
        /// means slower than the field average.
        /// </summary>
        public static float CarEffect(float carRating, float fieldAverageCarRating, float coeffPerPoint, float capSeconds)
        {
            float raw = (fieldAverageCarRating - carRating) * coeffPerPoint;
            return raw < -capSeconds ? -capSeconds : (raw > capSeconds ? capSeconds : raw);
        }

        // Engine-free UnityEngine.Mathf equivalents (Lerp/InverseLerp clamp t to 0-1).
        static float Lerp(float a, float b, float t)
        {
            float clamped = t < 0f ? 0f : (t > 1f ? 1f : t);
            return a + (b - a) * clamped;
        }

        static float InverseLerp(float a, float b, float v)
        {
            if (a == b)
            {
                return 0f;
            }

            float t = (v - a) / (b - a);
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }

        static float Max(float a, float b) => a > b ? a : b;
    }
}
