using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace F1Game.Art
{
    /// <summary>
    /// Builds URP/Lit materials in code from the committed StreamingAssets PBR
    /// textures — no editor-generated .mat files. Materials and their textures are
    /// cached so a surface is created once and shared by every object that uses it.
    ///
    /// The generated MaskMap packs R=Metallic, G=Occlusion, A=Smoothness, which maps
    /// onto URP/Lit's _MetallicGlossMap (R,A) and _OcclusionMap (G) with one texture.
    /// GPU instancing is enabled. "Wet" variants lower roughness + darken albedo
    /// (visual only — no grip/physics change).
    /// </summary>
    public static class RuntimeMaterialFactory
    {
        static readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();
        static Shader litShader;

        static Shader Lit
        {
            get
            {
                if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
                return litShader;
            }
        }

        static Texture2D LoadTexture(string path, bool linear)
        {
            string key = path + (linear ? "#l" : "#s");
            if (textureCache.TryGetValue(key, out Texture2D cached) && cached != null) return cached;
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                Object.Destroy(tex);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.anisoLevel = 4;
            textureCache[key] = tex;
            return tex;
        }

        /// <summary>Shared URP material for a surface (folder name under Materials/).</summary>
        public static Material GetSurface(string surface, float tiling = 2f, bool wet = false)
        {
            string key = surface + "@" + tiling.ToString("0.0") + (wet ? "#wet" : "");
            if (materialCache.TryGetValue(key, out Material m) && m != null) return m;

            Shader shader = Lit;
            Material mat = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
            mat.name = "RT_" + key;

            Texture2D baseTex = LoadTexture(RuntimeArtLibrary.SurfaceMap(surface, "BaseColor"), linear: false);
            Texture2D normalTex = LoadTexture(RuntimeArtLibrary.SurfaceMap(surface, "Normal"), linear: true);
            Texture2D maskTex = LoadTexture(RuntimeArtLibrary.SurfaceMap(surface, "MaskMap"), linear: true);

            if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", wet ? new Color(0.55f, 0.55f, 0.57f) : Color.white);
            if (normalTex != null)
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (maskTex != null)
            {
                mat.SetTexture("_MetallicGlossMap", maskTex);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetTexture("_OcclusionMap", maskTex);
                mat.EnableKeyword("_OCCLUSIONMAP");
                mat.SetFloat("_Metallic", 1f);
            }
            mat.SetFloat("_Smoothness", wet ? 0.8f : 0.35f);
            mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            mat.enableInstancing = true;

            materialCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// Swap the visible road mesh material to the runtime asphalt. Visual only —
        /// the MeshCollider, spline and geometry are untouched.
        /// </summary>
        public static bool ApplyRoadSurface(Transform trackRoot, bool wet)
        {
            if (trackRoot == null) return false;
            Material asphalt = GetSurface(RuntimeArtLibrary.SurfAsphaltWorn, tiling: 8f, wet: wet);
            if (asphalt == null) return false;
            int applied = 0;
            foreach (MeshRenderer r in trackRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.gameObject.name.StartsWith("Procedural road"))
                {
                    r.sharedMaterial = asphalt;
                    applied++;
                }
            }
            return applied > 0;
        }
    }
}
