using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace F1Game.Editor
{
    /// <summary>
    /// Post-migration URP validation: confirms the pipeline assignment, colour
    /// space, quality-tier assets and renderer post-process data, and repairs
    /// the pieces that hand-authored YAML cannot guarantee (package-internal
    /// references). Run after the first editor open of the migration branch.
    /// </summary>
    public static class RenderPipelineValidator
    {
        [MenuItem("F1 Game/Rendering/Validate URP Setup")]
        public static void Validate()
        {
            int problems = 0;

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                Debug.LogError("[URP Validate] Colour space is not Linear.");
                problems++;
            }

            var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
            {
                Debug.LogError("[URP Validate] GraphicsSettings has no URP pipeline asset assigned.");
                problems++;
            }

            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                if (QualitySettings.GetRenderPipelineAssetAt(i) == null)
                {
                    Debug.LogError("[URP Validate] Quality level '" + QualitySettings.names[i] + "' has no pipeline asset.");
                    problems++;
                }
            }

            problems += RepairRendererData();

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[URP Validate] URP/Lit shader not found - is the URP package installed?");
                problems++;
            }

            Debug.Log(problems == 0
                ? "[URP Validate] All checks passed."
                : "[URP Validate] " + problems + " problem(s) found - see errors above.");
        }

        static int RepairRendererData()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/Game/Settings/Rendering/URP-Renderer.asset");
            if (rendererData == null)
            {
                Debug.LogError("[URP Validate] URP-Renderer.asset failed to load - the hand-authored asset may need recreating via Assets > Create > Rendering > URP Universal Renderer.");
                return 1;
            }

            if (rendererData.postProcessData == null)
            {
                var postData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                if (postData != null)
                {
                    rendererData.postProcessData = postData;
                    EditorUtility.SetDirty(rendererData);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[URP Validate] Repaired missing PostProcessData reference on URP-Renderer.");
                }
                else
                {
                    Debug.LogError("[URP Validate] Could not locate URP PostProcessData in the package.");
                    return 1;
                }
            }

            return 0;
        }
    }
}
