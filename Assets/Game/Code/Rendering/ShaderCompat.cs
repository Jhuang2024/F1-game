using UnityEngine;
using UnityEngine.Rendering;

namespace F1Game.Rendering
{
    /// <summary>
    /// Pipeline-aware shader and material helpers used by the legacy runtime
    /// world builders during the URP migration.
    ///
    /// The monoliths used to hard-code <c>Shader.Find("Standard")</c> and
    /// Standard-shader conventions (<c>_Glossiness</c>, <c>_Mode</c> transparency).
    /// Under URP those materials render magenta. Every runtime material creation
    /// now routes through here, which targets URP/Lit when a scriptable pipeline
    /// is active and falls back to Standard when it is not — so the project keeps
    /// rendering during the transition in either configuration.
    ///
    /// NOTE: runtime material creation itself is migration scaffolding. The end
    /// state is the authored PBR library under Assets/Game/Resources/BaseMaterials
    /// (see MaterialLibrary), not code-built materials.
    /// </summary>
    public static class ShaderCompat
    {
        const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        const string BuiltInLitShaderName = "Standard";

        static Shader litShader;

        public static bool UrpActive => GraphicsSettings.currentRenderPipeline != null;

        public static Shader LitShader
        {
            get
            {
                if (litShader == null)
                {
                    litShader = UrpActive ? Shader.Find(UrpLitShaderName) : null;
                    if (litShader == null)
                    {
                        litShader = Shader.Find(BuiltInLitShaderName);
                    }
                }

                return litShader;
            }
        }

        /// <summary>Replacement for <c>new Material(Shader.Find("Standard"))</c>.</summary>
        public static Material CreateLitMaterial()
        {
            var material = new Material(LitShader);
            material.enableInstancing = true;
            return material;
        }

        /// <summary>Smoothness: URP/Lit uses _Smoothness, Standard uses _Glossiness.</summary>
        public static void SetSmoothness(Material material, float smoothness)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }
        }

        public static void SetEmission(Material material, Color emissionColor)
        {
            if (material == null)
            {
                return;
            }

            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emissionColor);
        }

        /// <summary>
        /// Alpha-blended ("Fade") transparency for the current pipeline. Mirrors
        /// the monoliths' Standard _Mode=3 block; on URP it drives _Surface/_Blend.
        /// </summary>
        public static void MakeTransparentFade(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (UrpActive && material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }

            material.renderQueue = 3000;
        }

        /// <summary>
        /// Alpha-tested (cutout) transparency for the current pipeline. Mirrors the
        /// monoliths' Standard _Mode=1 block; on URP it drives _AlphaClip.
        /// </summary>
        public static void MakeCutout(Material material, float cutoff)
        {
            if (material == null)
            {
                return;
            }

            if (UrpActive && material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetFloat("_Cutoff", cutoff);
            }
            else
            {
                material.SetFloat("_Mode", 1f);
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.SetFloat("_Cutoff", cutoff);
            }

            material.renderQueue = 2450;
        }
    }
}
