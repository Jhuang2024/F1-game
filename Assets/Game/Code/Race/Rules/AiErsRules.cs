namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure AI ERS decision-quality maths, extracted verbatim from
    /// RaceManager.ShouldAiUseErs so how well an AI judges an ERS deployment (and
    /// how likely it is to take a push-lap deploy) is stated and tested in one
    /// place. No engine dependency. These produce the probability an AI gets a
    /// racecraft/push-lap call right; the caller keeps every live state read (corner
    /// severity, battery, flags, gaps) and the Random roll that turns the
    /// probability into a decision. Feel-sensitive numbers unchanged - relocation,
    /// not a retune.
    /// </summary>
    public static class AiErsRules
    {
        /// <summary>
        /// The chance (0-1) an AI nails an attack/defend ERS deployment: the tier's
        /// base deployment quality modulated by driver awareness (0.8x at awareness
        /// 0, 1.08x at 100), clamped to 0-1. Expert bypasses the roll entirely
        /// (handled in the caller); this is the probability the rest of the field
        /// gets it right.
        /// </summary>
        public static float RacecraftDeployQuality(float baseQuality, float awareness)
        {
            float quality = baseQuality * Lerp(0.8f, 1.08f, awareness / 100f);
            return quality < 0f ? 0f : (quality > 1f ? 1f : quality);
        }

        /// <summary>
        /// The chance (0-1) an AI takes a push-lap ERS deploy on a clear straight
        /// with battery to spare: half the tier's base deployment quality, kept
        /// modest so it never becomes spam. Expert takes it deterministically
        /// (handled in the caller).
        /// </summary>
        public static float PushLapDeployChance(float baseQuality)
        {
            return baseQuality * 0.5f;
        }

        // Engine-free UnityEngine.Mathf.Lerp equivalent (clamps t to 0-1).
        static float Lerp(float a, float b, float t)
        {
            float clamped = t < 0f ? 0f : (t > 1f ? 1f : t);
            return a + (b - a) * clamped;
        }
    }
}
