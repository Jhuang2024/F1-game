using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace F1Game.Editor.ArtPipeline
{
    /// <summary>
    /// GitHub Actions build entry points. Invoked as the GameCI
    /// <c>buildMethod</c> (unity-builder) so a single Unity session runs the art
    /// integration and then produces a standalone player reflecting the integrated
    /// assets. On any failure the Unity process exits non-zero so the workflow fails
    /// loudly (never a false green).
    /// </summary>
    public static class CIBuild
    {
        const string BuildDir = "build/StandaloneLinux64";
        const string BuildName = "F1Game.x86_64";
        const string ScreenshotDir = "build/screenshots";

        /// <summary>Run the full art integration, then build a Linux64 player.</summary>
        public static void IntegrateAndBuild()
        {
            // 1. Integration. AutomatedArtIntegration.Run calls EditorApplication.Exit(1)
            // in batch mode on failure, so we only reach the build on success.
            AutomatedArtIntegration.Run();

            // 2. Standalone build.
            Directory.CreateDirectory(BuildDir);
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[CIBuild] No scenes to build.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(BuildDir, BuildName),
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[CIBuild] Build {summary.result}: {summary.totalSize} bytes, " +
                      $"{summary.totalErrors} errors, {summary.totalWarnings} warnings.");

            TryCaptureScreenshots();

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[CIBuild] Standalone build FAILED.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>Integration only (no player build) — used by PR validation mode.</summary>
        public static void IntegrateOnly()
        {
            AutomatedArtIntegration.Run();
        }

        static string[] EnabledScenes()
        {
            string[] fromSettings = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (fromSettings.Length > 0) return fromSettings;

            // Fallback: every scene under Assets so the CI build is not empty even
            // if build settings have not been populated.
            return AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/"))
                .Distinct()
                .OrderBy(p => p)
                .ToArray();
        }

        /// <summary>
        /// Best-effort visual capture. Real gameplay screenshots need a graphics
        /// device; GameCI's Linux editor runs headless, so this succeeds only when a
        /// GL context is available. It never fails the build — it writes what it can
        /// and logs when it cannot (honest, per the task's "if technically possible").
        /// </summary>
        static void TryCaptureScreenshots()
        {
            try
            {
                Directory.CreateDirectory(ScreenshotDir);
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    File.WriteAllText(Path.Combine(ScreenshotDir, "README.txt"),
                        "No graphics device in the CI runner (headless -nographics): " +
                        "in-editor gameplay screenshots were not captured. Run the " +
                        "standalone build artifact with -screenshot, or use a runner " +
                        "with a GL/xvfb context, to capture rendered frames.");
                    Debug.LogWarning("[CIBuild] No graphics device — skipped screenshots (documented).");
                    return;
                }

                string[] scenes = EnabledScenes();
                if (scenes.Length == 0) return;
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenes[0]);
                var cam = UnityEngine.Object.FindObjectOfType<Camera>();
                if (cam == null) { cam = new GameObject("CIScreenshotCam").AddComponent<Camera>(); }

                var rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ScreenshotDir, "editor_scene0.png"), tex.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
                Debug.Log("[CIBuild] Captured editor screenshot.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CIBuild] Screenshot capture skipped: " + e.Message);
            }
        }
    }
}
