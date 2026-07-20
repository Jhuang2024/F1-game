using System.IO;
using System.Linq;
using F1Game.Track;
using F1Game.Track.Dressing;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1Game.Editor.ArtPipeline
{
    /// <summary>
    /// Editor entry points for the procedural track-dressing system and art
    /// provenance validation. These are convenience wrappers over the runtime
    /// <see cref="TrackDressingBuilder"/> and do not alter gameplay data.
    ///
    /// NOTE: not yet compiled/run in Unity in the authoring environment.
    /// </summary>
    public static class TrackDressingTools
    {
        [MenuItem("F1 Game/Art/Dress Open Scene (reference circuit)")]
        public static void DressOpenScene()
        {
            var def = LoadFirst<TrackDefinitionAsset>();
            if (def == null)
            {
                Debug.LogError("[ArtPipeline] No TrackDefinitionAsset found. Generate the " +
                               "reference circuit first (F1 Game → Track → Generate Reference Circuit Asset).");
                return;
            }
            var profile = LoadFirst<CircuitVisualProfile>();
            if (profile == null)
            {
                Debug.LogError("[ArtPipeline] No CircuitVisualProfile asset found. Create one " +
                               "(Assets → Create → F1 Game → Track → Circuit Visual Profile) and " +
                               "assign the imported kit prefabs.");
                return;
            }

            var host = Object.FindObjectOfType<TrackDressing>();
            if (host == null)
            {
                var go = new GameObject("TrackDressing");
                host = go.AddComponent<TrackDressing>();
            }
            host.definition = def;
            host.profile = profile;
            host.Regenerate();
            EditorUtility.SetDirty(host);
        }

        [MenuItem("F1 Game/Art/Validate Art Provenance")]
        public static void ValidateProvenance()
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Art" });
            int prefabs = 0, missing = 0;
            foreach (string g in prefabGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!p.EndsWith(".prefab")) continue;
                prefabs++;
                string name = Path.GetFileNameWithoutExtension(p);
                string expected = Path.GetDirectoryName(p).Replace('\\', '/').Replace("/Prefabs", "/SourceMetadata")
                    + "/" + name + "_Source.asset";
                if (AssetDatabase.LoadAssetAtPath<SourceAssetMetadata>(expected) == null)
                {
                    missing++;
                    Debug.LogWarning("[ArtPipeline] Prefab without provenance: " + p);
                }
            }

            // Metadata whose asset is gone
            string[] metaGuids = AssetDatabase.FindAssets("t:SourceAssetMetadata", new[] { "Assets/Art" });
            int orphan = 0;
            foreach (string g in metaGuids)
            {
                var md = AssetDatabase.LoadAssetAtPath<SourceAssetMetadata>(AssetDatabase.GUIDToAssetPath(g));
                if (md == null) continue;
                if (!md.IsComplete())
                {
                    orphan++;
                    Debug.LogWarning("[ArtPipeline] Incomplete provenance record: " + md.name);
                }
            }

            Debug.Log($"[ArtPipeline] Provenance: {prefabs} prefabs, {missing} without metadata, " +
                      $"{orphan} incomplete records.");
        }

        static T LoadFirst<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
