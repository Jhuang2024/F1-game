using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using F1Game.Track;
using F1Game.Track.Dressing;
using UnityEngine;

namespace F1Game.Art
{
    /// <summary>
    /// The live runtime art activator. Called once from <c>TrackManager.Build</c>
    /// after the gameplay track is built. It:
    ///
    ///   1. immediately swaps the visible road mesh to the runtime asphalt material
    ///      and applies a light URP lighting nudge (both synchronous, visual-only);
    ///   2. asynchronously streams the committed GLB kit via glTFast into cached
    ///      templates, then reuses the existing <see cref="TrackDressingBuilder"/>
    ///      placement logic — with the empty prefab slots filled by those templates —
    ///      to spawn barriers, kerbs, fencing, gantries, marshal/camera/light towers,
    ///      grandstands, braking boards, vegetation and pit buildings;
    ///   3. keeps everything under one root and rebuilds it safely on track reload;
    ///   4. disables the dressing's colliders (visual only) so nothing can block the
    ///      racing line, create an invisible wall, or affect pit/physics.
    ///
    /// No catalog asset, no editor-generated prefabs/materials, no menu commands.
    /// </summary>
    public sealed class RuntimeTrackArt : MonoBehaviour
    {
        public const string RootName = "Track Art (runtime)";

        TrackDefinitionAsset definition;
        string trackId;
        bool nightSession;
        CancellationTokenSource cts;

        /// <summary>Entry point from TrackManager. Dedupes, then activates.</summary>
        public static void Activate(Transform trackRoot, TrackDefinitionAsset definition,
            string trackId, bool nightSession)
        {
            if (trackRoot == null) return;
            try
            {
                // Dedupe: remove a prior runtime-art root (race restart / reload).
                Transform existing = trackRoot.Find(RootName);
                if (existing != null) Destroy(existing.gameObject);

                var go = new GameObject(RootName);
                go.transform.SetParent(trackRoot, false);
                var art = go.AddComponent<RuntimeTrackArt>();
                art.definition = definition;
                art.trackId = trackId ?? "";
                art.nightSession = nightSession;
                art.Begin();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RuntimeArt] Activate failed (race continues): " + e);
            }
        }

        void Begin()
        {
            // Synchronous, immediate visual wins (no async needed).
            bool wet = nightSession && false; // wetness handled by weather system; default dry
            RuntimeMaterialFactory.ApplyRoadSurface(transform.parent, wet);
            ApplyLightingNudge();

            if (!RuntimeArtLibrary.Available())
            {
                Debug.LogWarning("[RuntimeArt] StreamingAssets/Art missing — dressing skipped (road material still applied).");
                return;
            }

            cts = new CancellationTokenSource();
            _ = BuildDressingAsync(cts.Token);
        }

        void OnDestroy()
        {
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
                cts = null;
            }
        }

        async Task BuildDressingAsync(CancellationToken ct)
        {
            try
            {
                if (definition == null || definition.spline == null || definition.spline.Count < 8)
                {
                    return;
                }

                RuntimeCircuitProfile rp = RuntimeProfileLibrary.Select(trackId, definition.environmentStyle);

                // Load every needed template (once, cached, in parallel).
                var need = new List<string>
                {
                    rp.primaryBarrier, rp.secondaryBarrier, RuntimeArtLibrary.TireWall,
                    RuntimeArtLibrary.CatchFence, rp.curb, RuntimeArtLibrary.KerbSausage,
                    RuntimeArtLibrary.StartGantry, RuntimeArtLibrary.MarshalPost,
                    RuntimeArtLibrary.CameraTower, RuntimeArtLibrary.LightTower,
                    RuntimeArtLibrary.BrakingBoard, RuntimeArtLibrary.Grandstand,
                    RuntimeArtLibrary.SpeakerPole, RuntimeArtLibrary.UtilityCabinet,
                    RuntimeArtLibrary.GuardHut, RuntimeArtLibrary.MaintenanceCart,
                    RuntimeArtLibrary.PitGarageBay, RuntimeArtLibrary.PitWall,
                };
                var templates = new Dictionary<string, GameObject>();
                var keys = new List<string>();
                foreach (string key in need)
                {
                    if (templates.ContainsKey(key)) continue;
                    templates[key] = null;
                    keys.Add(key);
                }
                var loadTasks = new List<Task<GameObject>>();
                foreach (string key in keys)
                {
                    loadTasks.Add(RuntimeGltfLoader.GetTemplateAsync(RuntimeArtLibrary.Glb(key), ct));
                }
                GameObject[] loaded = await Task.WhenAll(loadTasks);
                if (ct.IsCancellationRequested || this == null) return;
                for (int i = 0; i < keys.Count; i++)
                {
                    templates[keys[i]] = loaded[i];
                }

                GameObject T(string k) => templates.TryGetValue(k, out var g) ? g : null;

                // Build an in-memory profile with the loaded templates in the slots,
                // then hand it to the existing placement logic.
                var profile = ScriptableObject.CreateInstance<CircuitVisualProfile>();
                profile.profileId = rp.id;
                profile.barrierPrimary = T(rp.primaryBarrier);
                profile.barrierSecondary = T(rp.secondaryBarrier);
                profile.barrierModuleLength = 4f;
                profile.barrierMargin = rp.barrierMargin;
                profile.concreteOnStraights = rp.concreteOnStraights;
                profile.tyreWall = rp.tyreStacks ? T(RuntimeArtLibrary.TireWall) : null;
                profile.catchFence = T(RuntimeArtLibrary.CatchFence);
                profile.catchFenceEvery = 25f;
                profile.fenceExtraMargin = 1.2f;
                profile.kerbPainted = T(rp.curb);
                profile.kerbSausage = rp.sausageKerbs ? T(RuntimeArtLibrary.KerbSausage) : null;
                profile.kerbModuleLength = 4f;
                profile.startGantry = T(RuntimeArtLibrary.StartGantry);
                profile.marshalPost = T(RuntimeArtLibrary.MarshalPost);
                profile.cameraTower = T(RuntimeArtLibrary.CameraTower);
                profile.lightTower = rp.lightTowers ? T(RuntimeArtLibrary.LightTower) : null;
                profile.lightTowerEvery = 140f;
                profile.brakingBoard = T(RuntimeArtLibrary.BrakingBoard);
                profile.grandstands = rp.grandstands
                    ? Weighted((T(RuntimeArtLibrary.Grandstand), 1f))
                    : new CircuitVisualProfile.WeightedPrefab[0];
                profile.vegetation = Weighted((T(RuntimeArtLibrary.SpeakerPole), 1f));
                profile.clutter = Weighted(
                    (T(RuntimeArtLibrary.UtilityCabinet), 1.5f),
                    (T(RuntimeArtLibrary.GuardHut), 1f),
                    (T(RuntimeArtLibrary.MaintenanceCart), 1f));
                profile.vegetationDensity = rp.vegetationDensity;
                profile.vegetationBand = new Vector2(6f, 30f);

                int seed = 20240720 + rp.seedOffset;
                TrackDressingBuilder.Result result =
                    TrackDressingBuilder.Build(definition, profile, transform, seed);

                if (result.Root != null)
                {
                    ActivateClones(result.Root);
                    DisableColliders(result.Root);
                }

                PlacePitBuildings(T(RuntimeArtLibrary.PitGarageBay));

                Debug.Log($"[RuntimeArt] {trackId} -> profile '{rp.id}': dressed with {result.Placed} " +
                          $"objects (barriers {result.Barriers}, kerbs {result.Kerbs}, fences {result.Fences}, " +
                          $"structures {result.Structures}, veg {result.Vegetation}).");
            }
            catch (System.OperationCanceledException) { /* track changed mid-load */ }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RuntimeArt] dressing failed (race continues): " + e);
            }
        }

        static CircuitVisualProfile.WeightedPrefab[] Weighted(params (GameObject go, float w)[] items)
        {
            var list = new List<CircuitVisualProfile.WeightedPrefab>();
            foreach (var (go, w) in items)
            {
                if (go != null) list.Add(new CircuitVisualProfile.WeightedPrefab { prefab = go, weight = w });
            }
            return list.ToArray();
        }

        // Templates are parked inactive; wake every spawned clone.
        static void ActivateClones(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            }
        }

        // Dressing is presentation only — no gameplay collision.
        static void DisableColliders(GameObject root)
        {
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
            {
                c.enabled = false;
            }
        }

        /// <summary>
        /// Place garage bays safely OUTSIDE the pit stalls (pushed away from the
        /// track along the centreline→stall direction), so they never intrude on the
        /// pit path or racing surface.
        /// </summary>
        void PlacePitBuildings(GameObject garageTemplate)
        {
            if (garageTemplate == null || definition.pitLane.stallPositions == null) return;
            Vector3[] stalls = definition.pitLane.stallPositions;
            if (stalls.Length == 0) return;

            var sampler = new TrackSplineSampler();
            sampler.Build(definition.spline, definition.closedLoop);
            var samples = sampler.Samples;
            if (samples.Count < 2) return;

            var pit = new GameObject("PitBuildings").transform;
            pit.SetParent(transform, false);

            // one garage every ~2 stalls to keep the count sane
            for (int i = 0; i < stalls.Length; i += 2)
            {
                Vector3 stall = stalls[i];
                Vector3 nearest = samples[0].Position;
                float best = float.MaxValue;
                for (int s = 0; s < samples.Count; s += 3)
                {
                    float d = (samples[s].Position - stall).sqrMagnitude;
                    if (d < best) { best = d; nearest = samples[s].Position; }
                }
                Vector3 outward = stall - nearest;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.01f) continue;
                outward.Normalize();
                Vector3 pos = stall + outward * 6f;      // 6 m beyond the stall, away from track
                Quaternion rot = Quaternion.LookRotation(-outward, Vector3.up);
                GameObject g = Instantiate(garageTemplate, pos, rot, pit);
                g.transform.localScale = Vector3.one;
                foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
                foreach (Collider c in g.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }
        }

        /// <summary>
        /// A restrained URP lighting improvement: ensures the sun casts shadows with
        /// a sensible distance and lifts ambient a touch. It does not add fog or
        /// override the weather/mood system (avoids fighting existing lighting).
        /// </summary>
        void ApplyLightingNudge()
        {
            try
            {
                if (QualitySettings.shadowDistance < 250f) QualitySettings.shadowDistance = 250f;
                Light sun = RenderSettings.sun;
                if (sun == null)
                {
                    foreach (Light l in FindObjectsOfType<Light>())
                    {
                        if (l.type == LightType.Directional) { sun = l; break; }
                    }
                }
                if (sun != null)
                {
                    if (sun.shadows == LightShadows.None) sun.shadows = LightShadows.Soft;
                    sun.shadowStrength = Mathf.Max(sun.shadowStrength, 0.6f);
                }
                if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat &&
                    RenderSettings.ambientIntensity < 0.4f)
                {
                    RenderSettings.ambientIntensity = 0.6f;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RuntimeArt] lighting nudge skipped: " + e.Message);
            }
        }
    }
}
