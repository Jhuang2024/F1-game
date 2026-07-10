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

        static void Bake(GameObject built, string name)
        {
            string path = ScreenPrefabFolder + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(built, path);
            Debug.Log("[UI Setup] Baked " + path);
        }
    }
}
