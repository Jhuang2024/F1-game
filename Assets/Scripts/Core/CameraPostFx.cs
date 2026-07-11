using UnityEngine;

namespace LocalFormulaRacing
{
    // Premium visual pass: full-screen post chain for the race camera -
    // bloom (bright-pass + separable gaussian pyramid), filmic tonemapping,
    // a light saturation/contrast grade, and a subtle vignette, all through
    // one dependency-free shader (Hidden/RacePostUber, see
    // Assets/Shaders/RacePostUber.shader). The camera already rendered HDR;
    // without a tonemapper those >1.0 values simply clipped, which is a big
    // part of why the old image read as flat and dated.
    //
    // Attached by CameraRig when the race camera is created; tuned/enabled by
    // RaceManager.CreateLighting via the static mood setters below so the
    // grade tracks the same night/rain/desert mood system the lighting uses.
    // Degrades to a straight blit (and finally to no-op) if the shader is
    // missing or the platform can't run it - never a black screen.
    [RequireComponent(typeof(Camera))]
    public class CameraPostFx : MonoBehaviour
    {
        // Global toggle: quality 0 ("Low") turns the whole chain off.
        public static bool GlobalEnabled = true;

        // Grade parameters, set per race mood by RaceManager.CreateLighting.
        public static float SceneExposure = 1.05f;
        public static float SceneBloomIntensity = 0.85f;
        public static float SceneBloomThreshold = 1.05f;
        public static float SceneSaturation = 1.12f;
        public static float SceneContrast = 1.06f;
        public static float SceneVignette = 0.32f;

        Material material;
        bool shaderMissingLogged;

        public static void ConfigureMood(bool night, bool rainThreat, bool twilight)
        {
            SceneExposure = night ? 1.25f : (twilight ? 1.1f : (rainThreat ? 1.0f : 1.05f));
            SceneBloomIntensity = night ? 1.35f : (twilight ? 1.05f : 0.85f);
            SceneBloomThreshold = night ? 0.8f : 1.05f;
            SceneSaturation = rainThreat ? 1.02f : (night ? 1.08f : 1.12f);
            SceneContrast = rainThreat ? 1.03f : 1.06f;
            SceneVignette = night ? 0.4f : 0.32f;
        }

        Material PostMaterial
        {
            get
            {
                if (material != null)
                {
                    return material;
                }

                Shader shader = Shader.Find("Hidden/RacePostUber");
                if (shader == null || !shader.isSupported)
                {
                    if (!shaderMissingLogged)
                    {
                        shaderMissingLogged = true;
                        GameLog.Warn("[PostFx] Hidden/RacePostUber unavailable - post chain disabled.");
                    }

                    return null;
                }

                material = new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;
                return material;
            }
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            Material post = GlobalEnabled ? PostMaterial : null;
            if (post == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            post.SetFloat("_BloomThreshold", SceneBloomThreshold);
            post.SetFloat("_BloomIntensity", SceneBloomIntensity);
            post.SetFloat("_Exposure", SceneExposure);
            post.SetFloat("_Saturation", SceneSaturation);
            post.SetFloat("_Contrast", SceneContrast);
            post.SetFloat("_VignetteStrength", SceneVignette);

            // Bloom pyramid: bright-pass at quarter res, blur there, then one
            // more half-size blur level for a wider, softer halo. All
            // temporary targets come from the shared RT pool.
            int quarterWidth = Mathf.Max(1, source.width / 4);
            int quarterHeight = Mathf.Max(1, source.height / 4);
            RenderTexture bright = RenderTexture.GetTemporary(quarterWidth, quarterHeight, 0, source.format);
            Graphics.Blit(source, bright, post, 0);

            RenderTexture blurA = RenderTexture.GetTemporary(quarterWidth, quarterHeight, 0, source.format);
            post.SetVector("_BlurDir", new Vector2(1f, 0f));
            Graphics.Blit(bright, blurA, post, 1);
            post.SetVector("_BlurDir", new Vector2(0f, 1f));
            Graphics.Blit(blurA, bright, post, 1);

            int eighthWidth = Mathf.Max(1, quarterWidth / 2);
            int eighthHeight = Mathf.Max(1, quarterHeight / 2);
            RenderTexture wide = RenderTexture.GetTemporary(eighthWidth, eighthHeight, 0, source.format);
            Graphics.Blit(bright, wide);
            RenderTexture wideBlur = RenderTexture.GetTemporary(eighthWidth, eighthHeight, 0, source.format);
            post.SetVector("_BlurDir", new Vector2(1f, 0f));
            Graphics.Blit(wide, wideBlur, post, 1);
            post.SetVector("_BlurDir", new Vector2(0f, 1f));
            Graphics.Blit(wideBlur, wide, post, 1);

            // Fold the wide level back into the quarter-res bloom.
            Graphics.Blit(wide, blurA);
            post.SetVector("_BlurDir", new Vector2(0.5f, 0.5f));
            Graphics.Blit(blurA, bright, post, 1);

            post.SetTexture("_BloomTex", bright);
            Graphics.Blit(source, destination, post, 2);

            RenderTexture.ReleaseTemporary(bright);
            RenderTexture.ReleaseTemporary(blurA);
            RenderTexture.ReleaseTemporary(wide);
            RenderTexture.ReleaseTemporary(wideBlur);
        }

        void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
