using System.IO;
using F1Game.UI;
using F1Game.UI.Theme;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace F1Game.Editor
{
    /// <summary>
    /// One-time UI production setup, run from the editor:
    ///
    ///  1. "Create TMP Font Assets" — builds TextMeshPro font assets from the
    ///     shipped Rajdhani OFL fonts and assigns them into UiTheme_Default.
    ///  2. "Bake Screen Prefabs" — runs the UiScreenFactory builders once and
    ///     saves the results as authored prefabs under Resources/UI/Screens.
    ///     After baking, the ScreenRouter loads prefabs and the factory is no
    ///     longer used at runtime.
    /// </summary>
    public static class UiSetupTools
    {
        const string ScreenPrefabFolder = "Assets/Game/Resources/UI/Screens";
        const string FontFolder = "Assets/Game/Art/Fonts";
        const string ThemeAssetPath = "Assets/Game/Resources/UI/UiTheme_Default.asset";
        const string TmpEssentialsPackage = "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage";
        const string PendingTmpImportKey = "F1Game.PendingTmpEssentialsImport";
        const string AutoTmpImportAttemptKey = "F1Game.AutoTmpEssentialsImportAttempted";

        [InitializeOnLoadMethod]
        static void ResumePendingTmpImport()
        {
            if (SessionState.GetBool(PendingTmpImportKey, false))
            {
                EditorApplication.delayCall += ImportTmpEssentialsWhenReady;
            }
        }

        /// <summary>
        /// Zero-manual-step text-pipeline bring-up, mirroring the URP auto-repair:
        /// the production UI is gated on TMP being able to render
        /// (UiShell.TextPipelineReady), but TMP Essentials and the theme's TMP font
        /// assets are not committed to the repo, so on a fresh clone the gate stayed
        /// false forever and the whole production frontend/HUD silently fell back to
        /// legacy. On editor load this (1) imports TMP Essentials when the TMP
        /// Settings resource is missing, then (2) builds the Rajdhani TMP font
        /// assets and assigns them into UiTheme_Default. Both steps are idempotent
        /// and deferred until the editor is idle and out of Play Mode.
        /// </summary>
        [InitializeOnLoadMethod]
        static void AutoSetupTextPipeline()
        {
            // TMP Essentials contain no scripts, so a non-interactive import does
            // not cause a domain reload; re-run the setup when the import lands so
            // the font step follows in the same editor session.
            AssetDatabase.importPackageCompleted += packageName =>
            {
                EditorApplication.delayCall += AutoSetupTextPipelineWhenReady;
            };

            EditorApplication.delayCall += AutoSetupTextPipelineWhenReady;
        }

        static void AutoSetupTextPipelineWhenReady()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoSetupTextPipelineWhenReady;
                return;
            }

            // Step 1: TMP Essentials (TMP Settings asset, default font, shaders).
            if (!TmpSettingsAvailable())
            {
                if (SessionState.GetBool(AutoTmpImportAttemptKey, false))
                {
                    return; // Already attempted this session; don't loop on a failing import.
                }

                string packagePath = Path.GetFullPath(TmpEssentialsPackage);
                if (!File.Exists(packagePath))
                {
                    Debug.LogError("[UI Setup] TMP Essentials package not found: " + packagePath);
                    return;
                }

                SessionState.SetBool(AutoTmpImportAttemptKey, true);
                Debug.Log("[UI Setup] TMP Essentials missing - importing automatically so the production UI can activate.");
                AssetDatabase.ImportPackage(packagePath, false);
                return;
            }

            // Step 2: theme fonts. CreateTmpFontAssets reuses existing font assets,
            // so this only ever does work while the theme is unassigned.
            UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemeAssetPath);
            if (theme != null && theme.typography != null &&
                (theme.typography.regular == null || theme.typography.semiBold == null || theme.typography.tabularNumeric == null))
            {
                Debug.Log("[UI Setup] UiTheme_Default has unassigned TMP fonts - creating and assigning them automatically.");
                CreateTmpFontAssets();
            }
        }

        static bool TmpSettingsAvailable()
        {
            // Probed via Resources.Load rather than TMP_Settings.instance, whose
            // getter pops the TMP importer window while the asset is missing (and
            // some members throw before the Essentials import).
            try
            {
                return Resources.Load<TMP_Settings>("TMP Settings") != null;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// AssetDatabase.ImportPackage is rejected while the editor is playing. This
        /// wrapper exits Play Mode first and uses SessionState so the pending import
        /// survives the domain reload caused by returning to Edit Mode.
        /// </summary>
        [MenuItem("F1 Game/UI/0. Import TMP Essentials Safely")]
        public static void ImportTmpEssentialsSafely()
        {
            SessionState.SetBool(PendingTmpImportKey, true);

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.Log("[UI Setup] Exiting Play Mode before importing TMP Essentials.");
                EditorApplication.isPlaying = false;
            }

            EditorApplication.delayCall -= ImportTmpEssentialsWhenReady;
            EditorApplication.delayCall += ImportTmpEssentialsWhenReady;
        }

        static void ImportTmpEssentialsWhenReady()
        {
            if (!SessionState.GetBool(PendingTmpImportKey, false))
            {
                return;
            }

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ImportTmpEssentialsWhenReady;
                return;
            }

            string packagePath = Path.GetFullPath(TmpEssentialsPackage);
            if (!File.Exists(packagePath))
            {
                SessionState.EraseBool(PendingTmpImportKey);
                Debug.LogError("[UI Setup] TMP Essentials package not found: " + packagePath);
                return;
            }

            SessionState.EraseBool(PendingTmpImportKey);
            AssetDatabase.ImportPackage(packagePath, true);
        }

        [MenuItem("F1 Game/UI/1. Create TMP Font Assets")]
        public static void CreateTmpFontAssets()
        {
            Directory.CreateDirectory(FontFolder);

            TMP_FontAsset regular = BuildFont("Assets/Resources/Fonts/Rajdhani-SemiBold.ttf", "Rajdhani-SemiBold SDF");
            TMP_FontAsset bold = BuildFont("Assets/Resources/Fonts/Rajdhani-Bold.ttf", "Rajdhani-Bold SDF");

            UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Game/Resources/UI/UiTheme_Default.asset");
            if (theme != null)
            {
                theme.typography.regular = regular;
                theme.typography.semiBold = bold;
                // Rajdhani's digits are effectively tabular; a dedicated numeric
                // font can replace this slot later without touching screens.
                theme.typography.tabularNumeric = bold;
                EditorUtility.SetDirty(theme);
                AssetDatabase.SaveAssets();
                Debug.Log("[UI Setup] TMP fonts created and assigned to UiTheme_Default.");
            }
            else
            {
                Debug.LogError("[UI Setup] UiTheme_Default.asset not found under Assets/Game/Resources/UI.");
            }
        }

        [MenuItem("F1 Game/UI/1. Create TMP Font Assets", true)]
        static bool CanCreateTmpFontAssets()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        static TMP_FontAsset BuildFont(string sourcePath, string assetName)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (source == null)
            {
                Debug.LogError("[UI Setup] Source font missing: " + sourcePath);
                return null;
            }

            string assetPath = FontFolder + "/" + assetName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(source, 90, 9,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            fontAsset.name = assetName;
            AssetDatabase.CreateAsset(fontAsset, assetPath);
            if (fontAsset.atlasTextures != null)
            {
                foreach (Texture2D atlas in fontAsset.atlasTextures)
                {
                    AssetDatabase.AddObjectToAsset(atlas, fontAsset);
                }
            }

            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        [MenuItem("F1 Game/UI/2. Bake Screen Prefabs")]
        public static void BakeScreenPrefabs()
        {
            Directory.CreateDirectory(ScreenPrefabFolder);

            var stagingRoot = new GameObject("~UiPrefabStaging").transform;
            try
            {
                Bake(UiScreenFactory.BuildMainMenu(stagingRoot).gameObject, "MainMenu");
                Bake(UiScreenFactory.BuildTrackSelect(stagingRoot).gameObject, "TrackSelect");
                Bake(UiScreenFactory.BuildStrategy(stagingRoot).gameObject, "PreRaceStrategy");
                Bake(UiScreenFactory.BuildHudShell(stagingRoot).gameObject, "RaceHudShell");
                AssetDatabase.SaveAssets();
                Debug.Log("[UI Setup] Screen prefabs baked to " + ScreenPrefabFolder + ".");
            }
            finally
            {
                Object.DestroyImmediate(stagingRoot.gameObject);
            }
        }

        [MenuItem("F1 Game/UI/2. Bake Screen Prefabs", true)]
        static bool CanBakeScreenPrefabs()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        static void Bake(GameObject built, string name)
        {
            string path = ScreenPrefabFolder + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(built, path);
            Debug.Log("[UI Setup] Baked " + path);
        }
    }
}
