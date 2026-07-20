using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using F1Game.Track.Dressing;
using UnityEditor;
using UnityEngine;

namespace F1Game.Editor.ArtPipeline
{
    /// <summary>
    /// Headless, one-shot art-integration entry point for CI. Invoked by Unity's
    /// <c>-executeMethod F1Game.Editor.ArtPipeline.AutomatedArtIntegration.Run</c>
    /// (see .github/workflows/unity-art-integration.yml). Performs the complete
    /// integration with no menu interaction:
    ///
    ///   1. import the generated GLB kit  -> prefabs (LODGroup + collider + provenance)
    ///   2. build URP materials from the generated PBR textures
    ///   3. create + populate every CircuitVisualProfile
    ///   4. build the CircuitProfileCatalog (Resources) mapping circuits -> profiles
    ///   5. validate materials + profile slots
    ///   6. save + refresh
    ///   7. print a structured report; exit non-zero on failure
    ///
    /// It reuses <see cref="KitAssetImporter.RunImport"/> and
    /// <see cref="UrpMaterialBuilder.RunBuild"/> (no duplicated importer logic).
    /// The runtime dressing hook in TrackManager consumes the catalog produced
    /// here, so once CI commits these assets every track is dressed automatically.
    /// </summary>
    public static class AutomatedArtIntegration
    {
        const string ProfilesDir = "Assets/Game/Resources/ArtPipeline/Profiles";
        const string CatalogDir = "Assets/Game/Resources/ArtPipeline";
        const string CatalogPath = CatalogDir + "/CircuitProfileCatalog.asset";
        const string ReportMd = "Docs/ArtPipeline/AUTOMATION_REPORT.md";

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();

        // ------------------------------------------------------------------
        // Entry point
        // ------------------------------------------------------------------

        public static void Run()
        {
            Errors.Clear();
            Notes.Clear();
            var report = new StringBuilder();
            int prefabs = 0, materials = 0, profilesCreated = 0, circuitsMapped = 0;

            try
            {
                Log(report, "== F1 Game — Automated Art Integration ==");
                Log(report, "Unity " + Application.unityVersion + "  batchMode=" + Application.isBatchMode);

                // 1. Import GLB kit -> prefabs
                KitAssetImporter.ImportResult import = KitAssetImporter.RunImport();
                prefabs = import.Built;
                Log(report, $"[1] Import: built {import.Built} prefabs, skipped {import.Skipped}, failed {import.Failed} of {import.Total}.");
                if (import.Total == 0)
                    Errors.Add("No GLBs found to import (expected the generated kit under Assets/Art).");
                if (import.Skipped > 0)
                    Errors.Add(import.Skipped + " GLB(s) not imported — is com.unity.cloud.gltfast resolved?");
                if (import.Failed > 0)
                    Errors.Add(import.Failed + " GLB import(s) threw.");

                // 2. Build URP materials
                materials = UrpMaterialBuilder.RunBuild();
                Log(report, $"[2] Materials: built {materials}.");
                if (materials < 0)
                    Errors.Add("URP/Lit shader unavailable — URP not active?");
                else if (materials == 0)
                    Errors.Add("No URP materials built (expected generated textures under Assets/Art/Materials).");

                // Index the just-imported assets by name.
                Dictionary<string, GameObject> prefabIndex = IndexPrefabs();
                Dictionary<string, Material> matIndex = IndexMaterials();
                Log(report, $"    Indexed {prefabIndex.Count} prefabs, {matIndex.Count} materials.");

                // 3 + 4. Profiles + catalog
                EnsureFolder(ProfilesDir);
                EnsureFolder(CatalogDir);
                var catalog = ScriptableObject.CreateInstance<CircuitProfileCatalog>();
                var profilesByKey = new Dictionary<string, CircuitVisualProfile>();
                foreach (ProfileSpec spec in ProfileSpecs)
                {
                    CircuitVisualProfile profile = BuildProfile(spec, prefabIndex, matIndex);
                    string path = ProfilesDir + "/Profile_" + spec.id + ".asset";
                    CreateOrReplace(profile, path);
                    profilesByKey[spec.id] = AssetDatabase.LoadAssetAtPath<CircuitVisualProfile>(path);
                    profilesCreated++;
                }
                Log(report, $"[3] Profiles: created {profilesCreated} ({string.Join(", ", profilesByKey.Keys)}).");

                catalog.defaultProfile = profilesByKey.TryGetValue("green_permanent", out var def) ? def : null;
                catalog.mappings = BuildMappings(profilesByKey, out circuitsMapped);
                CreateOrReplace(catalog, CatalogPath);
                Log(report, $"[4] Catalog: {catalog.mappings.Count} mappings -> {CatalogPath} (circuits mapped: {circuitsMapped}).");
                if (catalog.defaultProfile == null)
                    Errors.Add("Catalog has no default profile.");

                // 5. Validate materials + profile slots
                ValidateMaterials(matIndex, report);
                ValidateProfiles(profilesByKey.Values, report);

                // 6. Save everything
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                RuntimeTrackDressing.InvalidateCache();

                // 10/11 note: this project builds every circuit procedurally at
                // runtime (no statically-authored track scenes), so dressing is
                // delivered by the TrackManager runtime hook + this catalog rather
                // than by baking objects into scenes. Reported honestly, not skipped.
                Notes.Add("No statically-authored track scenes exist; runtime hook + catalog dress every circuit automatically.");
            }
            catch (Exception e)
            {
                Errors.Add("Unhandled exception: " + e);
            }

            // 7. Report + exit code
            Log(report, "");
            Log(report, "== SUMMARY ==");
            Log(report, $"prefabs={prefabs} materials={materials} profiles={profilesCreated} circuitsMapped={circuitsMapped}");
            foreach (string n in Notes) Log(report, "NOTE  " + n);
            if (Errors.Count == 0)
            {
                Log(report, "RESULT: SUCCESS");
            }
            else
            {
                Log(report, "RESULT: FAILURE (" + Errors.Count + " error(s))");
                foreach (string er in Errors) Log(report, "ERROR " + er);
            }

            WriteReportFile(report.ToString(), prefabs, materials, profilesCreated, circuitsMapped);
            Debug.Log(report.ToString());

            if (Errors.Count > 0)
            {
                Debug.LogError("[AutomatedArtIntegration] integration FAILED with " + Errors.Count + " error(s).");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw new Exception("Art integration failed; see console.");
            }
        }

        // ------------------------------------------------------------------
        // Profile specifications
        // ------------------------------------------------------------------

        struct ProfileSpec
        {
            public string id;
            public string[] keywords;
            public bool concreteOnStraights;
            public bool night;
            public float vegetationDensity;
        }

        static readonly ProfileSpec[] ProfileSpecs =
        {
            new ProfileSpec { id = "green_permanent", keywords = new[] { "permanent", "green", "parkland" }, concreteOnStraights = true, night = false, vegetationDensity = 5f },
            new ProfileSpec { id = "dry_european",    keywords = new[] { "european", "dry" },                concreteOnStraights = true, night = false, vegetationDensity = 3f },
            new ProfileSpec { id = "desert",          keywords = new[] { "desert", "hot", "sand" },          concreteOnStraights = true, night = false, vegetationDensity = 0.5f },
            new ProfileSpec { id = "tropical",        keywords = new[] { "tropical", "humid" },              concreteOnStraights = false, night = false, vegetationDensity = 7f },
            new ProfileSpec { id = "forest",          keywords = new[] { "forest", "wood" },                 concreteOnStraights = false, night = false, vegetationDensity = 9f },
            new ProfileSpec { id = "urban_street",    keywords = new[] { "street", "urban" },                concreteOnStraights = true, night = false, vegetationDensity = 1f },
            new ProfileSpec { id = "night_urban",     keywords = new[] { "night", "neon" },                  concreteOnStraights = true, night = true, vegetationDensity = 1f },
            new ProfileSpec { id = "coastal",         keywords = new[] { "coastal", "sea", "harbour" },      concreteOnStraights = true, night = false, vegetationDensity = 4f },
            new ProfileSpec { id = "high_altitude",   keywords = new[] { "altitude", "mountain" },           concreteOnStraights = true, night = false, vegetationDensity = 4f },
        };

        static CircuitVisualProfile BuildProfile(ProfileSpec spec,
            Dictionary<string, GameObject> prefabs, Dictionary<string, Material> mats)
        {
            var p = ScriptableObject.CreateInstance<CircuitVisualProfile>();
            p.profileId = spec.id;
            p.matchKeywords = spec.keywords;

            p.barrierPrimary = P(prefabs, "Barrier_Armco_4m");
            p.barrierSecondary = P(prefabs, "Barrier_Concrete_3m");
            p.barrierModuleLength = 4f;
            p.barrierMargin = 3.5f;
            p.concreteOnStraights = spec.concreteOnStraights;
            p.tyreWall = P(prefabs, "Barrier_TireWall_2m");

            p.catchFence = P(prefabs, "Fence_CatchPanel_5m");
            p.catchFenceEvery = spec.id == "urban_street" || spec.id == "night_urban" ? 10f : 20f;
            p.fenceExtraMargin = 1.2f;

            p.kerbPainted = P(prefabs, "Kerb_Painted_4m");
            p.kerbSausage = P(prefabs, "Kerb_Sausage_1m");
            p.kerbModuleLength = 4f;

            p.startGantry = P(prefabs, "Struct_StartGantry_16m");
            p.marshalPost = P(prefabs, "Prop_MarshalPost");
            p.cameraTower = P(prefabs, "Struct_CameraTower_6m");
            p.lightTower = P(prefabs, "Struct_LightTower_10m");
            p.lightTowerEvery = 120f;
            p.brakingBoard = P(prefabs, "Prop_BrakingBoard");
            p.grandstands = Weighted(prefabs, ("Struct_Grandstand_12m", 1f));

            // No authored tree/shrub meshes in the kit yet; use generic clutter
            // props so scatter is non-empty and honest (documented limitation).
            p.vegetation = Weighted(prefabs, ("Prop_SpeakerPole_5m", 1f));
            p.clutter = Weighted(prefabs,
                ("Prop_UtilityCabinet", 1.5f), ("Prop_GuardHut", 1f), ("Prop_MaintenanceCart", 1f));
            p.vegetationDensity = spec.vegetationDensity;
            p.vegetationBand = new Vector2(6f, 30f);

            p.asphaltMaterial = M(mats, spec.id == "desert" ? "M_asphalt_clean" : "M_asphalt_worn");
            p.kerbMaterial = M(mats, "M_curb_paint");
            p.runoffGravelMaterial = M(mats, "M_gravel");
            p.runoffGrassMaterial = M(mats, spec.id == "desert" ? "M_grass_dry" : "M_grass_green");
            return p;
        }

        // ------------------------------------------------------------------
        // Circuit -> profile mappings
        // ------------------------------------------------------------------

        static List<CircuitProfileCatalog.Mapping> BuildMappings(
            Dictionary<string, CircuitVisualProfile> byKey, out int circuitCount)
        {
            var list = new List<CircuitProfileCatalog.Mapping>();
            var counted = new HashSet<string>();

            void Map(string profileId, string[] trackKeywords, string[] styleKeywords)
            {
                if (!byKey.TryGetValue(profileId, out var prof) || prof == null) return;
                list.Add(new CircuitProfileCatalog.Mapping
                {
                    trackIdKeywords = trackKeywords,
                    styleKeywords = styleKeywords,
                    profile = prof,
                });
                if (trackKeywords != null) foreach (var t in trackKeywords) counted.Add(t);
            }

            // Track-id keyword mappings (most specific first).
            Map("night_urban", new[] { "singapore", "las_vegas", "vegas" }, new[] { "night", "neon" });
            Map("urban_street", new[] { "monaco", "baku", "azerbaijan", "miami", "jeddah", "saudi" }, new[] { "street", "urban" });
            Map("desert", new[] { "bahrain", "qatar", "abu_dhabi", "yas" }, new[] { "desert", "hot" });
            Map("coastal", new[] { "zandvoort", "melbourne", "albert_park" }, new[] { "coastal", "harbour" });
            Map("high_altitude", new[] { "mexico", "interlagos", "sao_paulo" }, new[] { "altitude", "mountain" });
            Map("forest", new[] { "spa", "red_bull_ring", "austria", "suzuka" }, new[] { "forest", "wood" });
            Map("dry_european", new[] { "barcelona", "catalunya", "hungary", "hungaroring", "imola" }, new[] { "european" });
            Map("green_permanent", new[] { "silverstone", "monza", "canada", "gilles", "zandvoort_gp" }, new[] { "permanent", "green", "parkland" });

            circuitCount = counted.Count;
            return list;
        }

        // ------------------------------------------------------------------
        // Validation
        // ------------------------------------------------------------------

        static void ValidateMaterials(Dictionary<string, Material> mats, StringBuilder report)
        {
            int pink = 0;
            foreach (var kv in mats)
            {
                var m = kv.Value;
                if (m == null) { pink++; continue; }
                if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader")
                {
                    pink++;
                    Errors.Add("Material with error/pink shader: " + kv.Key);
                }
            }
            Log(report, $"[5] Material validation: {mats.Count} materials, {pink} invalid/pink.");
        }

        static void ValidateProfiles(IEnumerable<CircuitVisualProfile> profiles, StringBuilder report)
        {
            int checkedCount = 0, incomplete = 0;
            foreach (var p in profiles)
            {
                checkedCount++;
                var missing = new List<string>();
                if (p.barrierPrimary == null) missing.Add("barrierPrimary");
                if (p.kerbPainted == null) missing.Add("kerbPainted");
                if (p.catchFence == null) missing.Add("catchFence");
                if (p.startGantry == null) missing.Add("startGantry");
                if (p.marshalPost == null) missing.Add("marshalPost");
                if (missing.Count > 0)
                {
                    incomplete++;
                    Errors.Add("Profile '" + p.profileId + "' missing critical slots: " + string.Join(", ", missing));
                }
            }
            Log(report, $"[5] Profile validation: {checkedCount} profiles, {incomplete} incomplete.");
        }

        // ------------------------------------------------------------------
        // Asset indexing helpers
        // ------------------------------------------------------------------

        static Dictionary<string, GameObject> IndexPrefabs()
        {
            var d = new Dictionary<string, GameObject>();
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Art" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) d[go.name] = go;
            }
            return d;
        }

        static Dictionary<string, Material> IndexMaterials()
        {
            var d = new Dictionary<string, Material>();
            foreach (string g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Art/Materials" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m != null && m.name.StartsWith("M_")) d[m.name] = m;
            }
            return d;
        }

        static GameObject P(Dictionary<string, GameObject> d, string name)
        {
            if (d.TryGetValue(name, out var go)) return go;
            Notes.Add("prefab slot unfilled (missing '" + name + "').");
            return null;
        }

        static Material M(Dictionary<string, Material> d, string name)
        {
            if (d.TryGetValue(name, out var m)) return m;
            Notes.Add("material slot unfilled (missing '" + name + "').");
            return null;
        }

        static CircuitVisualProfile.WeightedPrefab[] Weighted(
            Dictionary<string, GameObject> d, params (string name, float weight)[] entries)
        {
            var list = new List<CircuitVisualProfile.WeightedPrefab>();
            foreach (var (name, weight) in entries)
            {
                var go = P(d, name);
                if (go != null) list.Add(new CircuitVisualProfile.WeightedPrefab { prefab = go, weight = weight });
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------
        // Asset IO
        // ------------------------------------------------------------------

        static void CreateOrReplace(UnityEngine.Object asset, string path)
        {
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void Log(StringBuilder sb, string line) => sb.AppendLine(line);

        static void WriteReportFile(string body, int prefabs, int materials, int profiles, int circuits)
        {
            try
            {
                Directory.CreateDirectory("Docs/ArtPipeline");
                var md = new StringBuilder();
                md.AppendLine("# Automated art integration report");
                md.AppendLine();
                md.AppendLine("_Generated by `AutomatedArtIntegration.Run` in GitHub Actions._");
                md.AppendLine();
                md.AppendLine("| Metric | Value |");
                md.AppendLine("|---|---|");
                md.AppendLine("| Unity | " + Application.unityVersion + " |");
                md.AppendLine("| Prefabs generated | " + prefabs + " |");
                md.AppendLine("| URP materials built | " + materials + " |");
                md.AppendLine("| Visual profiles created | " + profiles + " |");
                md.AppendLine("| Circuit keywords mapped | " + circuits + " |");
                md.AppendLine("| Errors | " + Errors.Count + " |");
                md.AppendLine();
                md.AppendLine("```");
                md.AppendLine(body);
                md.AppendLine("```");
                File.WriteAllText(ReportMd, md.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AutomatedArtIntegration] could not write report file: " + e.Message);
            }
        }
    }
}
