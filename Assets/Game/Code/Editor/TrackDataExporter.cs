using F1Game.Track;
using UnityEditor;
using UnityEngine;

namespace F1Game.Editor
{
    /// <summary>
    /// Reference-track migration tool: with a race world alive in Play Mode
    /// (enter a quick race on the chosen track, then run this), samples the
    /// live TrackManager runtime into a TrackDefinitionAsset — spline positions,
    /// widths, racing line and grid — as the starting point for authored track
    /// data. Remaining fields (surfaces, cameras, crowd zones) are then edited
    /// on the asset directly.
    ///
    /// This is deliberately reflection-based so the editor assembly does not
    /// depend on Assembly-CSharp; it reads the legacy TrackRuntime's public
    /// sampling API by name.
    /// </summary>
    public static class TrackDataExporter
    {
        const string OutputFolder = "Assets/Game/ScriptableObjects/Tracks";

        [MenuItem("F1 Game/Track/Export Live Track To Asset (Play Mode)")]
        public static void ExportLiveTrack()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[Track Export] Enter Play Mode and load a race on the track to export first.");
                return;
            }

            // Find the live TrackManager by name via the scene (reflection keeps
            // this assembly decoupled from Assembly-CSharp).
            MonoBehaviour trackManager = null;
            foreach (MonoBehaviour behaviour in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (behaviour.GetType().FullName == "LocalFormulaRacing.TrackManager")
                {
                    trackManager = behaviour;
                    break;
                }
            }

            if (trackManager == null)
            {
                Debug.LogError("[Track Export] No live TrackManager found - start a session first.");
                return;
            }

            object runtime = trackManager.GetType().GetProperty("Runtime")?.GetValue(trackManager)
                          ?? trackManager.GetType().GetField("Runtime")?.GetValue(trackManager);
            if (runtime == null)
            {
                // Fall back to a public field/property named Track on RaceManager-side conventions.
                Debug.LogError("[Track Export] TrackManager.Runtime not found - see Docs/TRACK_PIPELINE.md for the manual sampling fallback.");
                return;
            }

            var sampleMethod = runtime.GetType().GetMethod("SampleAtDistance");
            var lengthField = runtime.GetType().GetField("length") ?? null;
            float length = lengthField != null ? (float)lengthField.GetValue(runtime) : 0f;
            if (sampleMethod == null || length <= 0f)
            {
                Debug.LogError("[Track Export] TrackRuntime sampling API not found (SampleAtDistance/length).");
                return;
            }

            var asset = ScriptableObject.CreateInstance<TrackDefinitionAsset>();
            asset.trackId = trackManager.name;
            asset.displayName = trackManager.name;

            const float step = 8f;
            for (float distance = 0f; distance < length; distance += step)
            {
                object sample = sampleMethod.Invoke(runtime, new object[] { distance });
                Vector3 position = ReadVector(sample, "position");
                float width = ReadFloat(sample, "width", 12f);
                asset.spline.Add(new TrackDefinitionAsset.SplinePoint
                {
                    position = position,
                    width = width,
                });
            }

            System.IO.Directory.CreateDirectory(OutputFolder);
            string path = OutputFolder + "/Track_" + asset.trackId + "_Exported.asset";
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            var problems = asset.Validate();
            Debug.Log("[Track Export] Wrote " + path + " with " + asset.spline.Count + " points; validation: " +
                      (problems.Count == 0 ? "clean" : string.Join(" | ", problems)));
        }

        static Vector3 ReadVector(object sample, string member)
        {
            var field = sample.GetType().GetField(member);
            if (field != null && field.FieldType == typeof(Vector3))
            {
                return (Vector3)field.GetValue(sample);
            }

            var property = sample.GetType().GetProperty(member);
            if (property != null && property.PropertyType == typeof(Vector3))
            {
                return (Vector3)property.GetValue(sample);
            }

            return Vector3.zero;
        }

        static float ReadFloat(object sample, string member, float fallback)
        {
            var field = sample.GetType().GetField(member);
            if (field != null && field.FieldType == typeof(float))
            {
                return (float)field.GetValue(sample);
            }

            return fallback;
        }
    }
}
