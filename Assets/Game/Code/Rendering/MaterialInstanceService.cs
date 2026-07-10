using UnityEngine;

namespace F1Game.Rendering
{
    /// <summary>
    /// Central per-renderer material customization via MaterialPropertyBlock, so
    /// per-car / per-team colour, emission, brake glow and wetness never clone
    /// shared materials at runtime (each clone is a draw-call-breaking allocation
    /// and a leak risk). Every runtime material tweak should route through here.
    /// </summary>
    public static class MaterialInstanceService
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        static MaterialPropertyBlock shared;

        static MaterialPropertyBlock Block => shared ??= new MaterialPropertyBlock();

        /// <summary>Sets the base colour on a single renderer without cloning its material.</summary>
        public static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetPropertyBlock(Block);
            Block.SetColor(BaseColorId, color);
            Block.SetColor(ColorId, color); // built-in fallback
            renderer.SetPropertyBlock(Block);
        }

        public static void SetEmission(Renderer renderer, Color emission)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetPropertyBlock(Block);
            Block.SetColor(EmissionColorId, emission);
            renderer.SetPropertyBlock(Block);
        }

        /// <summary>Applies a livery colour to every renderer under a car root.</summary>
        public static void ApplyLivery(GameObject carRoot, Color primary)
        {
            if (carRoot == null)
            {
                return;
            }

            foreach (Renderer renderer in carRoot.GetComponentsInChildren<Renderer>())
            {
                SetColor(renderer, primary);
            }
        }

        /// <summary>Brake-disc emissive glow driven by disc temperature (0..1).</summary>
        public static void SetBrakeGlow(Renderer disc, float temperature01)
        {
            if (disc == null)
            {
                return;
            }

            float t = Mathf.Clamp01(temperature01);
            Color glow = Color.Lerp(Color.black, new Color(1.4f, 0.25f, 0.05f), t * t);
            SetEmission(disc, glow);
        }
    }
}
