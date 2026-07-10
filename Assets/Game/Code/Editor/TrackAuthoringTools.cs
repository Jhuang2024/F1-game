using System.IO;
using F1Game.Track;
using UnityEditor;
using UnityEngine;

namespace F1Game.Editor
{
    /// <summary>
    /// Editor tooling for the authored-track pipeline: generates the reference
    /// circuit asset deterministically, validates any TrackDefinitionAsset, and
    /// builds a live preview of the authored-track runtime in the scene.
    /// </summary>
    public static class TrackAuthoringTools
    {
        const string OutputFolder = "Assets/Game/ScriptableObjects/Tracks";

        [MenuItem("F1 Game/Track/Generate Reference Circuit Asset")]
        public static void GenerateReferenceCircuit()
        {
            Directory.CreateDirectory(OutputFolder);
            TrackDefinitionAsset asset = ReferenceTrackGenerator.Generate();
            string path = OutputFolder + "/Track_AuroraPark_Reference.asset";

            TrackDefinitionAsset existing = AssetDatabase.LoadAssetAtPath<TrackDefinitionAsset>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                Object.DestroyImmediate(asset);
                asset = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(asset, path);
            }

            AssetDatabase.SaveAssets();
            var problems = asset.Validate();
            Debug.Log($"[Track] Reference circuit written to {path}. Length ~{asset.ComputeLength():0} m. " +
                      (problems.Count == 0 ? "Validation clean." : "Validation: " + string.Join(" | ", problems)));
        }

        [MenuItem("F1 Game/Track/Build Authored Track Preview")]
        public static void BuildPreview()
        {
            var selected = Selection.activeObject as TrackDefinitionAsset;
            if (selected == null)
            {
                selected = ReferenceTrackGenerator.Generate();
                Debug.Log("[Track] No TrackDefinitionAsset selected - previewing the generated reference circuit.");
            }

            TrackRuntimeBuilder.BuildResult result = TrackRuntimeBuilder.Build(selected);
            Debug.Log("[Track] " + result.Report.Summarize() +
                      (result.Runtime != null ? $" Length {result.Runtime.Length:0} m, {result.Runtime.Grid.SlotCount} grid slots, {result.Runtime.Drs.ZoneCount} DRS zones." : ""));
            if (result.Root != null)
            {
                Selection.activeGameObject = result.Root;
            }
        }
    }
}
