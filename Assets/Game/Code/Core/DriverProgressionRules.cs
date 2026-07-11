using UnityEngine;

namespace F1Game.Core
{
    /// <summary>
    /// Pure driver season-progression maths, extracted verbatim from
    /// CareerManager.ScoreToDelta / ClampRating so the per-subrating rating change
    /// (and the realistic 40-99 clamp it feeds) are stated and tested in one place.
    /// The career layer owns the per-driver scoring, the accumulated deltas and when
    /// this runs; it asks these rules for the deterministic bounded number. Menu/
    /// progression maths, not in-race feel - relocation, not a retune.
    /// </summary>
    public static class DriverProgressionRules
    {
        /// <summary>The realistic in-game rating band all driver stats are held within.</summary>
        public const int MinRating = 40;
        public const int MaxRating = 99;

        /// <summary>Clamp a rating to the realistic <see cref="MinRating"/>-<see cref="MaxRating"/> band.</summary>
        public static int ClampRating(int value)
        {
            return Mathf.Clamp(value, MinRating, MaxRating);
        }

        /// <summary>
        /// One subrating's season change (-5..+5), extracted verbatim from
        /// CareerManager.ScoreToDelta: a near-zero score is no change; otherwise the
        /// score scales by the stat's max magnitude and a potential factor (higher
        /// potential develops faster), poor form (negative score) hits veterans
        /// harder, gains near the 94-99 ceiling get diminishing returns, then the
        /// season confidence scale applies before rounding and clamping to +/-5.
        /// </summary>
        public static int RatingDelta(float score, int currentEffectiveRating, int developmentPotential,
            int experience, float confidenceScale, float maxMagnitude)
        {
            if (Mathf.Abs(score) < 0.02f)
            {
                return 0;
            }

            float potentialFactor = Mathf.Lerp(0.65f, 1.3f, Mathf.InverseLerp(40f, 95f, developmentPotential));
            float raw = score * maxMagnitude * potentialFactor;

            if (score < 0f)
            {
                // Veterans regress a little faster on poor form; young/high-
                // potential drivers are cushioned somewhat.
                raw *= Mathf.Lerp(0.85f, 1.25f, Mathf.InverseLerp(40f, 99f, experience));
            }

            if (score > 0f && currentEffectiveRating >= 94)
            {
                raw *= Mathf.Lerp(1f, 0.2f, Mathf.InverseLerp(94f, 99f, currentEffectiveRating));
            }

            raw *= confidenceScale;
            return Mathf.Clamp(Mathf.RoundToInt(raw), -5, 5);
        }
    }
}
