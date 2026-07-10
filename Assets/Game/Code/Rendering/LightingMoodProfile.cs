using UnityEngine;

namespace F1Game.Rendering
{
    /// <summary>
    /// Authored lighting/grade profile for one weather-lighting mood
    /// (clear / overcast / rain / night / golden hour). The authored assets live
    /// in Assets/Game/Resources/LightingMoods so RaceVolumeService can load them
    /// in the runtime-built race world. This replaces the hard-coded numbers in
    /// the removed CameraPostFx.ConfigureMood.
    /// </summary>
    [CreateAssetMenu(menuName = "F1 Game/Rendering/Lighting Mood Profile", fileName = "Mood_New")]
    public class LightingMoodProfile : ScriptableObject
    {
        [Header("Colour grade")]
        [Tooltip("Post exposure in EV (0 = neutral).")]
        public float postExposureEv;
        [Range(-100f, 100f)] public float saturation = 12f;
        [Range(-100f, 100f)] public float contrast = 6f;

        [Header("Bloom")]
        [Min(0f)] public float bloomIntensity = 0.85f;
        [Min(0f)] public float bloomThreshold = 1.05f;

        [Header("Vignette")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.32f;

        [Header("Fog")]
        public bool fogEnabled;
        public Color fogColor = new Color(0.65f, 0.7f, 0.78f);
        [Min(0f)] public float fogDensity = 0.004f;

        [Header("Scene light shaping (applied by the race lighting builder)")]
        [Tooltip("Multiplier on the sun/main directional light intensity.")]
        public float sunIntensityMultiplier = 1f;
        public Color ambientTint = Color.white;
    }
}
