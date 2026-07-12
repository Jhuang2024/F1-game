using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace F1Game.Editor
{
    /// <summary>
    /// Creates and wires the project's URP assets. The repair is intentionally
    /// idempotent so it can be used both after a clean import and from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class RenderPipelineValidator
    {
        const string RenderingFolder = "Assets/Game/Settings/Rendering";
        const string RendererPath = RenderingFolder + "/URP-Renderer.asset";
        const string LowPath = RenderingFolder + "/URP-Low.asset";
        const string MediumPath = RenderingFolder + "/URP-Medium.asset";
        const string HighPath = RenderingFolder + "/URP-High.asset";
        const string AutoRepairSessionKey = "F1Game.URP.AutoRepairAttempted";

        static readonly string[] QualityAssetPaths =
        {
            LowPath, LowPath, MediumPath, MediumPath, HighPath, HighPath
        };

        static readonly string[] RequiredQualityNames =
        {
            "Very Low", "Low", "Medium", "High", "Very High", "Ultra"
        };

        static RenderPipelineValidator()
        {
            EditorApplication.delayCall += AutoRepairAfterImport;
        }

        static void AutoRepairAfterImport()
        {
            string sessionKey = AutoRepairSessionKey + "." + Application.dataPath.GetHashCode();
            if (SessionState.GetBool(sessionKey, false) || !NeedsRepair())
                return;

            SessionState.SetBool(sessionKey, true);
            Debug.Log("[URP Validate] Missing or invalid URP setup detected; repairing after import.");
            RepairAndValidate();
        }

        [MenuItem("F1 Game/Rendering/Repair and Validate URP Setup")]
        public static void RepairAndValidate()
        {
            EnsureFolderExists();

            var renderer = LoadOrCreateRenderer();
            var low = LoadOrCreatePipeline(LowPath, "URP-Low", renderer);
            var medium = LoadOrCreatePipeline(MediumPath, "URP-Medium", renderer);
            var high = LoadOrCreatePipeline(HighPath, "URP-High", renderer);

            WireRenderer(low, renderer);
            WireRenderer(medium, renderer);
            WireRenderer(high, renderer);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            GraphicsSettings.defaultRenderPipeline = high;

            var pipelines = new RenderPipelineAsset[] { low, low, medium, medium, high, high };
            if (QualitySettings.names.Length != pipelines.Length)
                Debug.LogError("[URP Validate] Expected six quality levels, but found " + QualitySettings.names.Length + ".");

            int originalQualityLevel = QualitySettings.GetQualityLevel();
            for (int i = 0; i < Math.Min(QualitySettings.names.Length, pipelines.Length); i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipelines[i];
            }
            QualitySettings.SetQualityLevel(originalQualityLevel, false);

            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(low);
            EditorUtility.SetDirty(medium);
            EditorUtility.SetDirty(high);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Validate();
        }

        [MenuItem("F1 Game/Rendering/Validate URP Setup")]
        public static void Validate()
        {
            int problems = 0;
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var low = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(LowPath);
            var medium = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MediumPath);
            var high = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HighPath);

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                Debug.LogError("[URP Validate] Colour space is not Linear.");
                problems++;
            }

            if (renderer == null)
            {
                Debug.LogError("[URP Validate] URP-Renderer.asset failed to load.");
                problems++;
            }
            else if (renderer.postProcessData == null)
            {
                Debug.LogError("[URP Validate] URP-Renderer has no PostProcessData.");
                problems++;
            }

            problems += ValidatePipeline(LowPath, low, renderer);
            problems += ValidatePipeline(MediumPath, medium, renderer);
            problems += ValidatePipeline(HighPath, high, renderer);

            if (GraphicsSettings.defaultRenderPipeline != high)
            {
                Debug.LogError("[URP Validate] GraphicsSettings does not use URP-High.");
                problems++;
            }

            var expected = new RenderPipelineAsset[] { low, low, medium, medium, high, high };
            if (QualitySettings.names.Length != expected.Length)
            {
                Debug.LogError("[URP Validate] Expected six quality levels, but found " + QualitySettings.names.Length + ".");
                problems++;
            }

            for (int i = 0; i < Math.Min(QualitySettings.names.Length, expected.Length); i++)
            {
                if (QualitySettings.names[i] != RequiredQualityNames[i])
                {
                    Debug.LogError("[URP Validate] Quality level " + i + " is '" + QualitySettings.names[i] +
                                   "', expected '" + RequiredQualityNames[i] + "'.");
                    problems++;
                }

                if (QualitySettings.GetRenderPipelineAssetAt(i) != expected[i])
                {
                    Debug.LogError("[URP Validate] Quality level '" + QualitySettings.names[i] + "' has the wrong pipeline asset.");
                    problems++;
                }
            }

            if (Shader.Find("Universal Render Pipeline/Lit") == null)
            {
                Debug.LogError("[URP Validate] URP/Lit shader not found - is the URP package installed?");
                problems++;
            }

            Debug.Log(problems == 0
                ? "[URP Validate] All checks passed."
                : "[URP Validate] " + problems + " problem(s) found - see errors above.");
        }

        static bool NeedsRepair()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var low = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(LowPath);
            var medium = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MediumPath);
            var high = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HighPath);

            if (renderer == null || renderer.postProcessData == null ||
                !PipelineUsesRenderer(low, renderer) ||
                !PipelineUsesRenderer(medium, renderer) ||
                !PipelineUsesRenderer(high, renderer) ||
                PlayerSettings.colorSpace != ColorSpace.Linear ||
                GraphicsSettings.defaultRenderPipeline != high ||
                QualitySettings.names.Length != QualityAssetPaths.Length)
                return true;

            for (int i = 0; i < QualityAssetPaths.Length; i++)
            {
                var expected = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QualityAssetPaths[i]);
                if (QualitySettings.GetRenderPipelineAssetAt(i) != expected)
                    return true;
            }

            return false;
        }

        static UniversalRendererData LoadOrCreateRenderer()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                DeleteInvalidAsset(RendererPath);
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "URP-Renderer";
                AssetDatabase.CreateAsset(renderer, RendererPath);
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                Debug.Log("[URP Validate] Created URP-Renderer through the Unity asset API.");
            }

            if (renderer.postProcessData == null)
            {
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            }

            ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
            EditorUtility.SetDirty(renderer);
            return renderer;
        }

        static UniversalRenderPipelineAsset LoadOrCreatePipeline(
            string path, string assetName, UniversalRendererData renderer)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipeline != null)
                return pipeline;

            DeleteInvalidAsset(path);
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.name = assetName;
            AssetDatabase.CreateAsset(pipeline, path);
            Debug.Log("[URP Validate] Created " + assetName + " through the Unity asset API.");
            return pipeline;
        }

        static void WireRenderer(UniversalRenderPipelineAsset pipeline, UniversalRendererData renderer)
        {
            var serialized = new SerializedObject(pipeline);
            var rendererList = serialized.FindProperty("m_RendererDataList");
            rendererList.arraySize = 1;
            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static int ValidatePipeline(
            string path, UniversalRenderPipelineAsset pipeline, UniversalRendererData renderer)
        {
            if (pipeline == null)
            {
                Debug.LogError("[URP Validate] " + path + " failed to load.");
                return 1;
            }

            if (!PipelineUsesRenderer(pipeline, renderer))
            {
                Debug.LogError("[URP Validate] " + path + " does not use URP-Renderer at index 0.");
                return 1;
            }

            return 0;
        }

        static bool PipelineUsesRenderer(UniversalRenderPipelineAsset pipeline, UniversalRendererData renderer)
        {
            if (pipeline == null || renderer == null)
                return false;

            var serialized = new SerializedObject(pipeline);
            var rendererList = serialized.FindProperty("m_RendererDataList");
            var defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            return rendererList != null && rendererList.arraySize > 0 &&
                   rendererList.GetArrayElementAtIndex(0).objectReferenceValue == renderer &&
                   defaultIndex != null && defaultIndex.intValue == 0;
        }

        static void DeleteInvalidAsset(string path)
        {
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                AssetDatabase.DeleteAsset(path);
        }

        static void EnsureFolderExists()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Settings"))
                AssetDatabase.CreateFolder("Assets/Game", "Settings");
            if (!AssetDatabase.IsValidFolder(RenderingFolder))
                AssetDatabase.CreateFolder("Assets/Game/Settings", "Rendering");
        }
    }
}
