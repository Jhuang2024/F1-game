using UnityEngine;

namespace F1Game.Core
{
    /// <summary>
    /// Engine-free layered-engine-audio mix maths, extracted verbatim from
    /// EngineAudioLayers.Tick so the RPM crossfade, off-throttle attenuation, volume
    /// and pitch curves are unit-testable without an AudioSource. The MonoBehaviour
    /// keeps owning the AudioSources and calls these to compute each layer's volume
    /// and pitch; the numbers are identical to the previous inline code (no retuning),
    /// this only makes the feel curve pinnable in tests.
    /// </summary>
    public static class EngineAudioMix
    {
        /// <summary>Off-throttle amount (0 at full throttle, 1 fully lifted).</summary>
        public static float OffThrottle(float throttle01) => 1f - Mathf.Clamp01(throttle01);

        /// <summary>
        /// Triangular crossfade weight for a layer whose band centre is
        /// <paramref name="rpmCenter"/>, given the current normalized RPM and the
        /// layer count (the fade width scales with the number of layers).
        /// </summary>
        public static float LayerWeight(float rpm01, float rpmCenter, int layerCount)
        {
            float distance = Mathf.Abs(Mathf.Clamp01(rpm01) - rpmCenter);
            return Mathf.Clamp01(1f - distance * (float)layerCount);
        }

        /// <summary>Final layer volume: crossfade weight, off-throttle attenuation and master gain.</summary>
        public static float LayerVolume(float weight, float offThrottle, float offThrottleAttenuation, float masterVolume)
        {
            float attenuation = 1f - offThrottle * offThrottleAttenuation;
            return weight * attenuation * masterVolume;
        }

        /// <summary>Layer pitch: 0.85..1.25 across the RPM range, scaled by an external factor.</summary>
        public static float LayerPitch(float rpm01, float pitchScale) =>
            Mathf.Lerp(0.85f, 1.25f, Mathf.Clamp01(rpm01)) * pitchScale;
    }
}
