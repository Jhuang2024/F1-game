using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace F1Game.Editor.ArtPipeline
{
    /// <summary>
    /// Assembles the procedural GLB kit (Assets/Art/**/*.glb) into game-ready
    /// prefabs: an LODGroup from the `_LOD0/_LOD1/_LOD2` meshes, a MeshCollider
    /// from the `_COL` proxy, correct static flags, GPU instancing on materials,
    /// and a <see cref="SourceAssetMetadata"/> provenance record per asset.
    ///
    /// Package-agnostic by design: it reads the *already-imported* GLB via
    /// AssetDatabase (com.unity.cloud.gltfast registers the ScriptedImporter that
    /// turns a .glb into a GameObject on import). It therefore compiles whether or
    /// not glTFast is currently resolved; if a GLB has not been imported it logs a
    /// clear message and skips, rather than failing the build.
    ///
    /// Nothing here mutates gameplay, physics, circuit geometry or existing scenes.
    /// It only writes new prefabs + metadata under Assets/Art/.../Prefabs.
    ///
    /// NOTE: not yet compiled/run in Unity in the authoring environment (Linux, no
    /// editor) — see Docs/ArtPipeline/SETUP_REPORT.md. Run on the dev machine.
    /// </summary>
    public static class KitAssetImporter
    {
        const string ArtRoot = "Assets/Art";
        const string PrefabSubfolder = "Prefabs";
        const string MetaSubfolder = "SourceMetadata";

        [MenuItem("F1 Game/Art/Import Kit GLBs → Prefabs")]
        public static void ImportAll()
        {
            string[] glbs = FindGlbs();
            if (glbs.Length == 0)
            {
                Debug.LogWarning("[ArtPipeline] No GLBs found under " + ArtRoot);
                return;
            }

            var report = new List<string>();
            int ok = 0, skipped = 0;
            foreach (string glbPath in glbs)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
                if (root == null)
                {
                    skipped++;
                    report.Add("SKIP (not imported — is glTFast installed?): " + glbPath);
                    continue;
                }
                try
                {
                    AssembleOne(glbPath, root, report);
                    ok++;
                }
                catch (System.Exception e)
                {
                    report.Add("FAIL " + glbPath + " : " + e.Message);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            WriteReport(report, ok, skipped, glbs.Length);
            Debug.Log($"[ArtPipeline] Import complete: {ok} prefabs, {skipped} skipped of {glbs.Length}.");
        }

        static string[] FindGlbs()
        {
            return AssetDatabase.FindAssets("t:GameObject", new[] { ArtRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(p => p)
                .ToArray();
        }

        static void AssembleOne(string glbPath, GameObject importedRoot, List<string> report)
        {
            string name = Path.GetFileNameWithoutExtension(glbPath);
            string dir = Path.GetDirectoryName(glbPath).Replace('\\', '/');
            string prefabDir = dir + "/" + PrefabSubfolder;
            string metaDir = dir + "/" + MetaSubfolder;
            EnsureFolder(prefabDir);
            EnsureFolder(metaDir);

            GameObject instance = Object.Instantiate(importedRoot);
            instance.name = name;

            // Collect renderers by LOD suffix and the collision proxy.
            var lodRenderers = new SortedDictionary<int, List<Renderer>>();
            MeshFilter colFilter = null;
            foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                string n = mf.gameObject.name;
                if (n.EndsWith("_COL"))
                {
                    colFilter = mf;
                    continue;
                }
                int lod = ParseLod(n);
                if (lod < 0) lod = 0;
                var r = mf.GetComponent<Renderer>();
                if (r == null) continue;
                if (!lodRenderers.TryGetValue(lod, out var list))
                {
                    list = new List<Renderer>();
                    lodRenderers[lod] = list;
                }
                list.Add(r);
                ConfigureMaterials(r);
            }

            if (lodRenderers.Count == 0)
            {
                report.Add("WARN no LOD renderers: " + glbPath);
                Object.DestroyImmediate(instance);
                return;
            }

            // Build LODGroup.
            var group = instance.GetComponent<LODGroup>();
            if (group == null) group = instance.AddComponent<LODGroup>();
            var lods = new List<LOD>();
            // Screen-relative transition heights: LOD0 -> 0.5, LOD1 -> 0.18, LOD2 -> 0.02
            float[] transitions = { 0.5f, 0.18f, 0.02f, 0.006f };
            int idx = 0;
            foreach (var kv in lodRenderers)
            {
                float t = idx < transitions.Length ? transitions[idx] : 0.01f;
                lods.Add(new LOD(t, kv.Value.ToArray()));
                idx++;
            }
            // last LOD must transition to 0 (cull) — use a small tail value already.
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();

            // Collider from the convex proxy (keep the mesh, drop its renderer).
            if (colFilter != null)
            {
                var colObj = colFilter.gameObject;
                var rend = colObj.GetComponent<Renderer>();
                if (rend != null) Object.DestroyImmediate(rend);
                var mc = colObj.GetComponent<MeshCollider>();
                if (mc == null) mc = colObj.AddComponent<MeshCollider>();
                mc.sharedMesh = colFilter.sharedMesh;
                mc.convex = true;
            }
            else
            {
                report.Add("WARN no _COL proxy (no collider): " + name);
            }

            // Static flags for batching/occlusion (non-moving trackside dressing).
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);
            }

            string prefabPath = prefabDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);

            CreateMetadata(name, glbPath, metaDir, lodRenderers.Count, colFilter != null);
            report.Add("OK " + name + " (LODs=" + lodRenderers.Count + ", collider=" +
                       (colFilter != null) + ") -> " + prefabPath);
        }

        static int ParseLod(string objName)
        {
            int i = objName.IndexOf("_LOD", System.StringComparison.Ordinal);
            if (i < 0) return -1;
            string tail = objName.Substring(i + 4);
            int end = 0;
            while (end < tail.Length && char.IsDigit(tail[end])) end++;
            if (end == 0) return -1;
            return int.Parse(tail.Substring(0, end));
        }

        static void ConfigureMaterials(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                m.enableInstancing = true;
            }
        }

        static void CreateMetadata(string name, string glbPath, string metaDir,
                                   int lodCount, bool hasCollider)
        {
            var md = ScriptableObject.CreateInstance<SourceAssetMetadata>();
            md.assetId = "gen_" + name;
            md.assetName = name;
            md.provider = "generated-blender";
            md.creator = "F1-game art pipeline (procedural Blender factory)";
            md.sourcePage = "Tools/ArtPipeline/blender/build_kit.py";
            md.license = "CC0-1.0 (project-owned original)";
            md.attributionRequired = false;
            md.commercialUse = true;
            md.sourceFormat = "GLB (glTF 2.0)";
            md.generatedLods = lodCount;
            md.unityDestination = glbPath;
            md.aiGenerated = false;
            md.modifications = "Assembled to prefab: LODGroup + convex MeshCollider + static flags.";
            md.sha256 = Sha256(glbPath);
            AssetDatabase.CreateAsset(md, metaDir + "/" + name + "_Source.asset");
        }

        static string Sha256(string assetPath)
        {
            string full = Path.GetFullPath(assetPath);
            if (!File.Exists(full)) return "";
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(full);
            var hash = sha.ComputeHash(fs);
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void WriteReport(List<string> lines, int ok, int skipped, int total)
        {
            string dir = "Docs/ArtPipeline";
            Directory.CreateDirectory(dir);
            var header = new[]
            {
                "# Kit import report",
                "",
                $"Prefabs built: {ok}  |  Skipped: {skipped}  |  Total GLBs: {total}",
                "",
            };
            File.WriteAllLines(dir + "/KIT_IMPORT_REPORT.md", header.Concat(lines));
        }
    }
}
