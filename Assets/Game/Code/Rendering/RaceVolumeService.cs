using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace F1Game.Rendering
{
    /// <summary>
    /// URP global Volume stack for the race world. Replaces the removed
    /// CameraPostFx OnRenderImage chain (bloom + tonemap + grade + vignette)
    /// with pipeline post-processing: ACES tonemapping, colour adjustments,
    /// bloom, vignette, plus mood-driven fog via RenderSettings.
    ///
    /// The numbers come from authored <see cref="LightingMoodProfile"/> assets
    /// (Resources/LightingMoods). The service keeps the old static entry points
    /// (GlobalEnabled, ConfigureMood) so RaceManager's lighting builder calls the
    /// same shape of API it always did.
    /// </summary>
    public static class RaceVolumeService
    {
        const string MoodResourceFolder = "LightingMoods/";

        static Volume volume;
        static Bloom bloom;
        static Tonemapping tonemapping;
        static ColorAdjustments colorAdjustments;
        static Vignette vignette;

        static bool globalEnabled = true;
        static bool userBloomEnabled = true;
        static bool userVignetteEnabled = true;

        public static LightingMoodProfile ActiveMood { get; private set; }

        /// <summary>Quality gate, mirrors the old CameraPostFx.GlobalEnabled (quality 0 turns post off).</summary>
        public static bool GlobalEnabled
        {
            get => globalEnabled;
            set
            {
                globalEnabled = value;
                if (volume != null)
                {
                    volume.enabled = value;
                }
            }
        }

        /// <summary>User-facing effect toggles (settings screen hooks).</summary>
        public static void SetUserToggles(bool bloomEnabled, bool vignetteEnabled)
        {
            userBloomEnabled = bloomEnabled;
            userVignetteEnabled = vignetteEnabled;
            if (ActiveMood != null)
            {
                ApplyMood(ActiveMood);
            }
        }

        /// <summary>
        /// Mood selection with the same signature the old post chain used, so the
        /// lighting builder keeps one call site. Prefers authored profiles;
        /// missing assets fall back to built-in defaults that mirror the old grade.
        /// </summary>
        public static void ConfigureMood(bool night, bool rainThreat, bool twilight)
        {
            string moodName = night ? "Mood_Night" : (rainThreat ? "Mood_Rain" : (twilight ? "Mood_GoldenHour" : "Mood_Clear"));
            LightingMoodProfile profile = Resources.Load<LightingMoodProfile>(MoodResourceFolder + moodName);
            if (profile == null)
            {
                Debug.LogWarning("[Rendering] Lighting mood asset missing: " + moodName + " - using neutral defaults.");
                profile = ScriptableObject.CreateInstance<LightingMoodProfile>();
                profile.name = moodName + " (runtime fallback)";
            }

            ApplyMood(profile);
        }

        public static void ApplyMood(LightingMoodProfile mood)
        {
            if (mood == null)
            {
                return;
            }

            ActiveMood = mood;
            EnsureVolume();

            colorAdjustments.postExposure.Override(mood.postExposureEv);
            colorAdjustments.saturation.Override(mood.saturation);
            colorAdjustments.contrast.Override(mood.contrast);

            bloom.active = userBloomEnabled;
            bloom.intensity.Override(userBloomEnabled ? mood.bloomIntensity : 0f);
            bloom.threshold.Override(mood.bloomThreshold);

            vignette.active = userVignetteEnabled;
            vignette.intensity.Override(userVignetteEnabled ? mood.vignetteIntensity : 0f);

            RenderSettings.fog = mood.fogEnabled;
            if (mood.fogEnabled)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = mood.fogColor;
                // Scenery-visibility clamp (same as LightingMoodApplier): the
                // old 0.004-class densities fogged out everything past ~500m.
                RenderSettings.fogDensity = Mathf.Min(mood.fogDensity, 0.0007f);
            }
        }

        /// <summary>Creates the persistent global volume on first use.</summary>
        public static void EnsureVolume()
        {
            if (volume != null)
            {
                return;
            }

            var go = new GameObject("Global Race Volume (URP)");
            Object.DontDestroyOnLoad(go);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;

            // The profile object is assembled once here from authored mood data;
            // it is configuration, not per-frame generation.
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = profile;

            tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            colorAdjustments = profile.Add<ColorAdjustments>(true);
            bloom = profile.Add<Bloom>(true);
            bloom.scatter.Override(0.6f);
            // High-quality prefiltering: URP's default bloom downsample can
            // flicker/sparkle on small bright sources (rain lights, floodlight
            // specular hits); the HQ path adds the anti-flicker prefilter.
            bloom.highQualityFiltering.Override(true);
            vignette = profile.Add<Vignette>(true);
            vignette.smoothness.Override(0.42f);

            volume.enabled = globalEnabled;
        }
    }
}
