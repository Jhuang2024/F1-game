using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace F1Game.Editor.ArtPipeline
{
    /// <summary>
    /// Builds URP/Lit materials from the procedurally-generated PBR texture sets
    /// under Assets/Art/Materials/&lt;surface&gt;/ (BaseColor + Normal + MaskMap).
    ///
    /// The generated MaskMap packs R=Metallic, G=Occlusion, B=0, A=Smoothness —
    /// which lines up with URP/Lit's channel expectations: the same texture drives
    /// _MetallicGlossMap (metallic in R, smoothness in A) and _OcclusionMap
    /// (occlusion in G). Importer settings are fixed here too: normals flagged as
    /// NormalMap, MaskMap forced to linear (sRGB off).
    ///
    /// This is additive: it writes new M_&lt;surface&gt;.mat assets next to their
    /// textures. It does not touch the existing M_*_Placeholder library or the
    /// runtime MaterialLibrary; swapping the placeholders for these is a deliberate
    /// follow-up (see Docs/ArtPipeline/MATERIALS_AND_LIGHTING.md).
    ///
    /// NOTE: not compiled/run in Unity in the authoring environment — dev machine only.
    /// </summary>
    public static class UrpMaterialBuilder
    {
        const string MaterialsRoot = "Assets/Art/Materials";

        [MenuItem("F1 Game/Art/Build URP Materials From Generated Textures")]
        public static void BuildAll()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[ArtPipeline] URP/Lit shader not found — is URP active?");
                return;
            }

            int built = 0;
            foreach (string surfDir in AssetDatabase.GetSubFolders(MaterialsRoot))
            {
                string surf = Path.GetFileName(surfDir);
                string baseTexPath = surfDir + "/" + surf + "_BaseColor.png";
                string normalPath = surfDir + "/" + surf + "_Normal.png";
                string maskPath = surfDir + "/" + surf + "_MaskMap.png";
                if (!File.Exists(baseTexPath)) continue;

                ConfigureTextureImporter(normalPath, isNormal: true, linear: true);
                ConfigureTextureImporter(maskPath, isNormal: false, linear: true);

                var mat = new Material(lit) { name = "M_" + surf };
                var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(baseTexPath);
                var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                var maskTex = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);

                if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
                mat.SetColor("_BaseColor", Color.white);

                if (normalTex != null)
                {
                    mat.SetTexture("_BumpMap", normalTex);
                    mat.EnableKeyword("_NORMALMAP");
                    mat.SetFloat("_BumpScale", 1f);
                }
                if (maskTex != null)
                {
                    mat.SetTexture("_MetallicGlossMap", maskTex);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetFloat("_Metallic", 1f);   // map-driven
                    mat.SetFloat("_Smoothness", 1f); // map alpha-driven
                    mat.SetTexture("_OcclusionMap", maskTex);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                    mat.SetFloat("_OcclusionStrength", 1f);
                }
                mat.enableInstancing = true;
                // sensible world tiling for a ~2 m texel surface
                mat.SetTextureScale("_BaseMap", new Vector2(2f, 2f));

                string matPath = surfDir + "/M_" + surf + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (existing != null)
                {
                    existing.CopyPropertiesFromMaterial(mat);
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                built++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ArtPipeline] Built {built} URP materials under {MaterialsRoot}.");
        }

        static void ConfigureTextureImporter(string path, bool isNormal, bool linear)
        {
            if (!File.Exists(path)) return;
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            bool dirty = false;
            if (isNormal && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                dirty = true;
            }
            if (!isNormal && linear && ti.sRGBTexture)
            {
                ti.sRGBTexture = false; // mask maps are data, not colour
                dirty = true;
            }
            if (ti.mipmapEnabled != true) { ti.mipmapEnabled = true; dirty = true; }
            if (dirty)
            {
                ti.SaveAndReimport();
            }
        }
    }
}
