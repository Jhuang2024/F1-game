using System.Collections.Generic;
using F1Game.Core;
using UnityEngine;

namespace F1Game.Rendering
{
    /// <summary>
    /// Project-owned procedural source textures for every <see cref="MaterialLibrary.Slot"/>.
    /// The authored placeholder materials under Assets/Game/Resources/BaseMaterials are
    /// flat URP/Lit colours with no maps (m_TexEnvs: []); this generator gives each slot
    /// a category-appropriate albedo texture plus sensible metallic/smoothness/emission,
    /// built once at runtime from the deterministic maths in
    /// <see cref="F1Game.Core.ProceduralPatterns"/>. Nothing here uses copyrighted art or
    /// third-party brand assets - the surfaces are noise, gradients, stripes, weaves and
    /// masks generated from first principles.
    ///
    /// Wiring: <see cref="MaterialLibrary.Get"/> calls <see cref="Enrich"/> once per slot.
    /// Enrich is a no-op when the material already carries a mainTexture, so dropping a
    /// real authored texture set into a slot's .mat transparently supersedes the
    /// procedural one - this is a placeholder, not a competing system. Every step is
    /// guarded; a generation failure leaves the flat placeholder untouched.
    ///
    /// Expected shader inputs (URP/Lit or Standard, resolved via <see cref="ShaderCompat"/>):
    ///   _BaseColor/_Color (albedo tint, via material.color) + _MainTex (this texture),
    ///   _Metallic (float), _Smoothness/_Glossiness (via ShaderCompat.SetSmoothness),
    ///   _EmissionColor + _EMISSION keyword (via ShaderCompat.SetEmission) for Emissive.
    /// </summary>
    public static class ProceduralSurfaceTextures
    {
        const int Size = 128;

        enum Kind { FbmSpeckle, Weave, Brushed, Stripes, Panels, Corrugation, Bands, Turf, ChainLink, Flat }

        struct Profile
        {
            public Kind kind;
            public Color a;       // base / low
            public Color b;       // detail / high (or the second stripe colour)
            public float metallic;
            public float smoothness;
            public int tiling;    // mainTextureScale (repeats across a unit)
            public int detail;    // octaves / cells / stripe count depending on kind
            public Color emission;
        }

        static readonly Dictionary<MaterialLibrary.Slot, Texture2D> textureCache =
            new Dictionary<MaterialLibrary.Slot, Texture2D>();
        static readonly HashSet<MaterialLibrary.Slot> enriched = new HashSet<MaterialLibrary.Slot>();

        static Color Gray(float v, float a = 1f) => new Color(v, v, v, a);

        static Profile ProfileFor(MaterialLibrary.Slot slot)
        {
            switch (slot)
            {
                // ---- Car ----
                case MaterialLibrary.Slot.CarPaint:
                    // Near-white base so per-instance livery tint (MaterialInstanceService) multiplies true.
                    return new Profile { kind = Kind.FbmSpeckle, a = Gray(0.92f), b = Gray(1f), metallic = 0.15f, smoothness = 0.78f, tiling = 2, detail = 4 };
                case MaterialLibrary.Slot.CarbonFibre:
                    return new Profile { kind = Kind.Weave, a = Gray(0.045f), b = Gray(0.16f), metallic = 0.25f, smoothness = 0.58f, tiling = 6, detail = 22 };
                case MaterialLibrary.Slot.Rubber:
                    return new Profile { kind = Kind.FbmSpeckle, a = Gray(0.015f), b = Gray(0.05f), metallic = 0f, smoothness = 0.22f, tiling = 3, detail = 4 };
                case MaterialLibrary.Slot.Metal:
                    return new Profile { kind = Kind.Brushed, a = Gray(0.55f), b = Gray(0.72f), metallic = 0.9f, smoothness = 0.7f, tiling = 2, detail = 3 };
                case MaterialLibrary.Slot.Glass:
                    return new Profile { kind = Kind.FbmSpeckle, a = new Color(0.06f, 0.08f, 0.10f), b = new Color(0.12f, 0.15f, 0.18f), metallic = 0f, smoothness = 0.95f, tiling = 1, detail = 3 };
                case MaterialLibrary.Slot.Decal:
                    return new Profile { kind = Kind.Flat, a = Gray(0.96f), b = Gray(1f), metallic = 0f, smoothness = 0.35f, tiling = 1, detail = 1 };
                case MaterialLibrary.Slot.Emissive:
                    return new Profile { kind = Kind.Flat, a = new Color(1f, 0.85f, 0.55f), b = new Color(1f, 0.9f, 0.6f), metallic = 0f, smoothness = 0.5f, tiling = 1, detail = 1, emission = new Color(1f, 0.75f, 0.4f) * 2f };

                // ---- Track surfaces ----
                case MaterialLibrary.Slot.Asphalt:
                    return new Profile { kind = Kind.FbmSpeckle, a = Gray(0.075f), b = Gray(0.16f), metallic = 0f, smoothness = 0.28f, tiling = 8, detail = 5 };
                case MaterialLibrary.Slot.RubberedLine:
                    return new Profile { kind = Kind.FbmSpeckle, a = Gray(0.045f), b = Gray(0.11f), metallic = 0f, smoothness = 0.42f, tiling = 8, detail = 4 };
                case MaterialLibrary.Slot.WetAsphalt:
                    return new Profile { kind = Kind.FbmSpeckle, a = Gray(0.045f), b = Gray(0.10f), metallic = 0f, smoothness = 0.82f, tiling = 8, detail = 5 };
                case MaterialLibrary.Slot.PaintedLine:
                    return new Profile { kind = Kind.FbmSpeckle, a = Gray(0.85f), b = Gray(0.98f), metallic = 0f, smoothness = 0.38f, tiling = 3, detail = 3 };
                case MaterialLibrary.Slot.Kerb:
                    return new Profile { kind = Kind.Stripes, a = new Color(0.72f, 0.09f, 0.10f), b = Gray(0.9f), metallic = 0f, smoothness = 0.35f, tiling = 1, detail = 6 };
                case MaterialLibrary.Slot.Concrete:
                    return new Profile { kind = Kind.Panels, a = Gray(0.52f), b = Gray(0.40f), metallic = 0f, smoothness = 0.22f, tiling = 3, detail = 3 };
                case MaterialLibrary.Slot.Gravel:
                    return new Profile { kind = Kind.FbmSpeckle, a = new Color(0.46f, 0.40f, 0.30f), b = new Color(0.60f, 0.53f, 0.40f), metallic = 0f, smoothness = 0.14f, tiling = 10, detail = 5 };
                case MaterialLibrary.Slot.Grass:
                    return new Profile { kind = Kind.FbmSpeckle, a = new Color(0.10f, 0.24f, 0.08f), b = new Color(0.20f, 0.42f, 0.14f), metallic = 0f, smoothness = 0.18f, tiling = 12, detail = 5 };
                case MaterialLibrary.Slot.ArtificialTurf:
                    return new Profile { kind = Kind.Turf, a = new Color(0.09f, 0.30f, 0.11f), b = new Color(0.16f, 0.44f, 0.18f), metallic = 0f, smoothness = 0.30f, tiling = 16, detail = 24 };
                case MaterialLibrary.Slot.MetalBarrier:
                    return new Profile { kind = Kind.Corrugation, a = Gray(0.60f), b = Gray(0.80f), metallic = 0.8f, smoothness = 0.6f, tiling = 4, detail = 10 };
                case MaterialLibrary.Slot.TyreWall:
                    return new Profile { kind = Kind.Bands, a = Gray(0.02f), b = Gray(0.09f), metallic = 0f, smoothness = 0.20f, tiling = 4, detail = 5 };
                case MaterialLibrary.Slot.Fencing:
                    return new Profile { kind = Kind.ChainLink, a = Gray(0.30f), b = Gray(0.62f), metallic = 0.6f, smoothness = 0.5f, tiling = 8, detail = 6 };
                case MaterialLibrary.Slot.PitConcrete:
                    return new Profile { kind = Kind.Panels, a = Gray(0.58f), b = Gray(0.46f), metallic = 0f, smoothness = 0.28f, tiling = 3, detail = 3 };
                case MaterialLibrary.Slot.GarageFloor:
                    return new Profile { kind = Kind.Panels, a = Gray(0.34f), b = Gray(0.26f), metallic = 0f, smoothness = 0.6f, tiling = 2, detail = 2 };
                default:
                    return new Profile { kind = Kind.FbmSpeckle, a = Gray(0.4f), b = Gray(0.6f), metallic = 0f, smoothness = 0.4f, tiling = 2, detail = 4 };
            }
        }

        static Color Sample(Profile p, int slotSeed, float u, float v)
        {
            switch (p.kind)
            {
                case Kind.Flat:
                {
                    float n = ProceduralPatterns.Fbm(u * 4f, v * 4f, slotSeed, 2);
                    return Color.Lerp(p.a, p.b, n * 0.35f);
                }
                case Kind.FbmSpeckle:
                {
                    float n = ProceduralPatterns.Fbm(u * 16f, v * 16f, slotSeed, p.detail);
                    return Color.Lerp(p.a, p.b, n);
                }
                case Kind.Weave:
                {
                    float w = ProceduralPatterns.Weave(u, v, p.detail);
                    float grime = ProceduralPatterns.Fbm(u * 8f, v * 8f, slotSeed, 3) * 0.15f;
                    return Color.Lerp(p.a, p.b, Mathf.Clamp01(w - grime));
                }
                case Kind.Brushed:
                {
                    // Horizontal streaks: high-frequency along v, smeared along u.
                    float streak = ProceduralPatterns.Fbm(u * 2f, v * 64f, slotSeed, p.detail);
                    return Color.Lerp(p.a, p.b, streak);
                }
                case Kind.Stripes:
                {
                    bool odd = (ProceduralPatterns.StripeIndex(v, p.detail) & 1) == 1;
                    Color baseC = odd ? p.b : p.a;
                    float wear = ProceduralPatterns.Fbm(u * 12f, v * 12f, slotSeed, 3) * 0.18f;
                    return Color.Lerp(baseC, Gray(0.15f), wear);
                }
                case Kind.Panels:
                {
                    float field = ProceduralPatterns.PanelSeam(u, v, p.detail, 0.06f);
                    float stain = ProceduralPatterns.Fbm(u * 6f, v * 6f, slotSeed, 4);
                    Color surf = Color.Lerp(p.b, p.a, stain);
                    return Color.Lerp(p.b * 0.55f, surf, field); // darken seams
                }
                case Kind.Corrugation:
                {
                    float rib = 0.5f + 0.5f * Mathf.Sin(u * p.detail * 2f * Mathf.PI);
                    float scuff = ProceduralPatterns.Fbm(u * 20f, v * 20f, slotSeed, 3) * 0.12f;
                    return Color.Lerp(p.a, p.b, Mathf.Clamp01(rib - scuff));
                }
                case Kind.Bands:
                {
                    // Stacked tyre rings: dark valleys between rounded band highlights.
                    float band = Mathf.Abs(Mathf.Sin(v * p.detail * Mathf.PI));
                    float wear = ProceduralPatterns.Fbm(u * 10f, v * 10f, slotSeed, 3) * 0.2f;
                    return Color.Lerp(p.a, p.b, Mathf.Clamp01(band - wear));
                }
                case Kind.Turf:
                {
                    // Regular short-pile stipple over a noise base.
                    float pile = ProceduralPatterns.StripePhase(u, p.detail) * ProceduralPatterns.StripePhase(v, p.detail);
                    float n = ProceduralPatterns.Fbm(u * 20f, v * 20f, slotSeed, 3);
                    return Color.Lerp(p.a, p.b, Mathf.Clamp01(0.4f * n + 0.6f * pile));
                }
                case Kind.ChainLink:
                {
                    float wire = ProceduralPatterns.ChainLink(u, v, p.detail, 0.16f);
                    float rust = ProceduralPatterns.Fbm(u * 14f, v * 14f, slotSeed, 3) * 0.2f;
                    return Color.Lerp(p.a, p.b, Mathf.Clamp01(wire - rust));
                }
                default:
                    return p.a;
            }
        }

        /// <summary>The cached procedural albedo texture for a slot (built on first request).</summary>
        public static Texture2D For(MaterialLibrary.Slot slot)
        {
            if (textureCache.TryGetValue(slot, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            Profile p = ProfileFor(slot);
            int seed = (int)slot * 101 + 17;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "T_" + slot + "_Procedural",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[Size * Size];
            float inv = 1f / Size;
            for (int y = 0; y < Size; y++)
            {
                float fv = (y + 0.5f) * inv;
                for (int x = 0; x < Size; x++)
                {
                    float fu = (x + 0.5f) * inv;
                    Color c = Sample(p, seed, fu, fv);
                    pixels[y * Size + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            textureCache[slot] = tex;
            return tex;
        }

        /// <summary>
        /// Give a slot's shared material its procedural albedo + PBR values. No-op if the
        /// material already carries a mainTexture (a real authored set wins) or on any
        /// failure. Called once per slot from <see cref="MaterialLibrary.Get"/>.
        /// </summary>
        public static void Enrich(Material material, MaterialLibrary.Slot slot)
        {
            if (material == null || enriched.Contains(slot))
            {
                return;
            }

            enriched.Add(slot);
            if (material.mainTexture != null)
            {
                return; // authored texture set present - leave it alone.
            }

            try
            {
                Profile p = ProfileFor(slot);
                material.mainTexture = For(slot);
                material.mainTextureScale = new Vector2(p.tiling, p.tiling);
                material.color = Color.white; // albedo comes from the texture; livery tint is per-instance.
                if (material.HasProperty("_Metallic"))
                {
                    material.SetFloat("_Metallic", p.metallic);
                }

                ShaderCompat.SetSmoothness(material, p.smoothness);
                if (p.emission.maxColorComponent > 0f)
                {
                    ShaderCompat.SetEmission(material, p.emission);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Rendering] Procedural texture enrichment failed for " + slot + ": " + e.Message);
            }
        }
    }
}
