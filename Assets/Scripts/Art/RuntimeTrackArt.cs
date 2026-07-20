using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using F1Game.Track;
using LocalFormulaRacing;
using UnityEngine;

namespace F1Game.Art
{
    /// <summary>
    /// The live runtime art activator, driven by the FINAL, repaired
    /// <see cref="TrackRuntime"/> centreline — the same representation the playable
    /// road is built from — not the authored <see cref="TrackDefinitionAsset"/>
    /// (which is null on the procedural/legacy path). Called once from
    /// <c>TrackManager.Build</c> after the gameplay track is complete.
    ///
    /// It swaps the road material immediately, then asynchronously streams the
    /// committed GLB kit (glTFast, from StreamingAssets) and places barriers,
    /// kerbs, fencing, gantries, marshal/camera/light towers, grandstands, braking
    /// boards, pit garages and scattered props along the live centreline. All
    /// dressing is visual only (colliders disabled), lives under one root, and is
    /// rebuilt without duplicates on reload. Gameplay geometry, colliders, racing
    /// lines, physics, AI, pit logic, RNG and saves are never touched.
    /// </summary>
    public sealed class RuntimeTrackArt : MonoBehaviour
    {
        public const string RootName = "Track Art (runtime)";

        TrackRuntime runtime;
        TrackDefinitionAsset definition;
        string trackId;
        bool nightSession;
        CancellationTokenSource cts;

        int barriers, kerbs, fences, structures, props;

        public static void Activate(Transform trackRoot, TrackRuntime runtime,
            TrackDefinitionAsset definition, string trackId, bool nightSession)
        {
            if (trackRoot == null) return;
            try
            {
                Transform existing = trackRoot.Find(RootName);
                if (existing != null) Destroy(existing.gameObject);

                var go = new GameObject(RootName);
                go.transform.SetParent(trackRoot, false);
                var art = go.AddComponent<RuntimeTrackArt>();
                art.runtime = runtime;
                art.definition = definition;
                art.trackId = trackId ?? (runtime != null ? runtime.trackId : "");
                art.nightSession = nightSession;
                art.Begin();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RuntimeArt] Activate failed (race continues): " + e);
            }
        }

        void Begin()
        {
            RuntimeMaterialFactory.ApplyRoadSurface(transform.parent, wet: false);
            ApplyLightingNudge();

            if (runtime == null || runtime.centerLine == null || runtime.centerLine.Count < 8 || runtime.length < 200f)
            {
                Debug.LogWarning("[RuntimeArt] No usable TrackRuntime centreline — dressing skipped.");
                return;
            }
            if (!RuntimeArtLibrary.Available())
            {
                Debug.LogWarning("[RuntimeArt] StreamingAssets/Art missing — dressing skipped (road material applied).");
                return;
            }

            cts = new CancellationTokenSource();
            _ = BuildAsync(cts.Token);
        }

        void OnDestroy()
        {
            if (cts != null) { cts.Cancel(); cts.Dispose(); cts = null; }
        }

        async Task BuildAsync(CancellationToken ct)
        {
            try
            {
                RuntimeCircuitProfile p = RuntimeProfileLibrary.Select(trackId, runtime.styleName);

                // --- load every needed template once (cached, parallel) ---
                var need = new List<string>
                {
                    p.primaryBarrier, p.secondaryBarrier, RuntimeArtLibrary.TireWall,
                    RuntimeArtLibrary.CatchFence, p.curb, RuntimeArtLibrary.KerbSausage,
                    RuntimeArtLibrary.StartGantry, RuntimeArtLibrary.MarshalPost,
                    RuntimeArtLibrary.CameraTower, RuntimeArtLibrary.LightTower,
                    RuntimeArtLibrary.BrakingBoard, RuntimeArtLibrary.Grandstand,
                    RuntimeArtLibrary.PitGarageBay, RuntimeArtLibrary.UtilityCabinet,
                    RuntimeArtLibrary.GuardHut, RuntimeArtLibrary.MaintenanceCart,
                    RuntimeArtLibrary.SpeakerPole,
                };
                var tmpl = new Dictionary<string, GameObject>();
                var keys = new List<string>();
                foreach (string k in need) { if (!tmpl.ContainsKey(k)) { tmpl[k] = null; keys.Add(k); } }
                var loads = new List<Task<GameObject>>();
                foreach (string k in keys) loads.Add(RuntimeGltfLoader.GetTemplateAsync(RuntimeArtLibrary.Glb(k), ct));
                GameObject[] got = await Task.WhenAll(loads);
                if (ct.IsCancellationRequested || this == null) return;
                for (int i = 0; i < keys.Count; i++) tmpl[keys[i]] = got[i];
                GameObject T(string k) => tmpl.TryGetValue(k, out var g) ? g : null;

                var rng = new System.Random(20240720 + p.seedOffset);
                Transform barrierRoot = Child("Barriers");
                Transform kerbRoot = Child("Kerbs");
                Transform fenceRoot = Child("Fencing");
                Transform structRoot = Child("Structures");
                Transform propRoot = Child("Props");

                float len = runtime.length;
                float pitSide = Mathf.Sign(runtime.PitLaneLateral == 0f ? 1f : runtime.PitLaneLateral);
                float pitStartN = Mathf.Clamp01(runtime.PitEntryRampStartNormalized - 0.03f);

                // --- barriers + tyre stacks + catch fencing along both edges ---
                GameObject concrete = T(p.secondaryBarrier);
                GameObject armco = T(p.primaryBarrier);
                GameObject tyre = p.tyreStacks ? T(RuntimeArtLibrary.TireWall) : null;
                GameObject fence = T(RuntimeArtLibrary.CatchFence);
                int spawnBudget = 0;
                for (float d = 0f; d < len; d += 5f)
                {
                    runtime.SampleAtDistance(d, out Vector3 point, out Vector3 fwd, out Vector3 right);
                    float hw = runtime.HalfWidthAt(d);
                    float curv = Curvature(d);
                    bool straight = Mathf.Abs(curv) < 0.004f;
                    float n = d / len;
                    Quaternion rot = OrientAlong(fwd);

                    for (int s = -1; s <= 1; s += 2)
                    {
                        // leave the pit side open across the pit corridor
                        if (Mathf.Sign(s) == pitSide && (n >= pitStartN || n <= 0.05f)) continue;
                        Vector3 outward = right * s;
                        Vector3 pos = point + outward * (hw + p.barrierMargin);
                        GameObject b = (straight && p.concreteOnStraights && concrete != null) ? concrete : armco;
                        if (Spawn(b, pos, rot, barrierRoot)) barriers++;

                        bool inside = (curv > 0f && s < 0) || (curv < 0f && s > 0);
                        if (tyre != null && Mathf.Abs(curv) > 0.02f && inside && ((int)(d / 5f) % 2 == 0))
                        {
                            if (Spawn(tyre, pos + outward * 0.6f, rot, barrierRoot)) barriers++;
                        }
                    }

                    if (fence != null && ((int)(d) % 25 == 0))
                    {
                        for (int s = -1; s <= 1; s += 2)
                        {
                            if (Mathf.Sign(s) == pitSide && (n >= pitStartN || n <= 0.05f)) continue;
                            Vector3 pos = point + right * s * (hw + p.barrierMargin + 1.2f);
                            if (Spawn(fence, pos, rot, fenceRoot)) fences++;
                        }
                    }

                    if (++spawnBudget % 120 == 0) { await Task.Yield(); if (ct.IsCancellationRequested || this == null) return; }
                }

                // --- kerbs through corners (both apex sides) ---
                GameObject kerb = T(p.curb);
                GameObject sausage = p.sausageKerbs ? T(RuntimeArtLibrary.KerbSausage) : null;
                for (float d = 0f; d < len; d += 4f)
                {
                    float curv = Curvature(d);
                    if (Mathf.Abs(curv) < 0.008f) continue;
                    runtime.SampleAtDistance(d, out Vector3 point, out Vector3 fwd, out Vector3 right);
                    float hw = runtime.HalfWidthAt(d);
                    int s = curv > 0f ? -1 : 1;               // inside of the corner
                    Vector3 pos = point + right * s * (hw - 0.25f);
                    GameObject k = (sausage != null && Mathf.Abs(curv) > 0.03f && ((int)d % 8 == 0)) ? sausage : kerb;
                    if (Spawn(k, pos, OrientAlong(fwd), kerbRoot)) kerbs++;
                }

                // --- corners: braking boards (always) + collect entries ---
                GameObject brakeBoard = T(RuntimeArtLibrary.BrakingBoard);
                var corners = new List<float>();
                float prevC = 0f;
                for (float d = 0f; d < len; d += 5f)
                {
                    float c = Mathf.Abs(Curvature(d));
                    bool entry = prevC < 0.01f && c >= 0.01f;
                    prevC = c;
                    if (!entry) continue;
                    corners.Add(d);
                    if (brakeBoard != null)
                    {
                        float bd = Mathf.Repeat(d - 90f, len);
                        runtime.SampleAtDistance(bd, out Vector3 bp, out Vector3 bf, out Vector3 br);
                        Vector3 pos = bp - br * (runtime.HalfWidthAt(bd) + 1.5f);
                        if (Spawn(brakeBoard, pos, OrientAlong(bf), structRoot)) structures++;
                    }
                }

                // --- camera towers: authored broadcast nodes, else every 3rd corner ---
                GameObject camera = T(RuntimeArtLibrary.CameraTower);
                if (camera != null)
                {
                    if (definition != null && definition.cameraNodes != null && definition.cameraNodes.Count > 0)
                    {
                        foreach (var node in definition.cameraNodes)
                            if (Spawn(camera, node.position, Facing(NearestOutward(node.position)), structRoot)) structures++;
                    }
                    else
                    {
                        for (int i = 0; i < corners.Count; i += 3)
                        {
                            runtime.SampleAtDistance(corners[i], out Vector3 cp, out _, out Vector3 crr);
                            Vector3 pos = cp + crr * (runtime.HalfWidthAt(corners[i]) + p.barrierMargin + 8f);
                            if (Spawn(camera, pos, Facing(crr), structRoot)) structures++;
                        }
                    }
                }

                // --- grandstands: authored crowd zones, else start/finish + every 2nd corner ---
                GameObject stand = p.grandstands ? T(RuntimeArtLibrary.Grandstand) : null;
                if (stand != null)
                {
                    if (definition != null && definition.crowdZones != null && definition.crowdZones.Count > 0)
                    {
                        foreach (var cz in definition.crowdZones)
                            if (Spawn(stand, cz.position, Facing(NearestOutward(cz.position)), structRoot)) structures++;
                    }
                    else
                    {
                        runtime.SampleAtDistance(0f, out Vector3 s0, out _, out Vector3 sr0);
                        Vector3 sfPos = s0 - sr0 * (runtime.HalfWidthAt(0f) + p.barrierMargin + 12f);
                        if (Spawn(stand, sfPos, Facing(-sr0), structRoot)) structures++;
                        for (int i = 0; i < corners.Count; i += 2)
                        {
                            runtime.SampleAtDistance(corners[i], out Vector3 gp, out _, out Vector3 gr);
                            Vector3 pos = gp - gr * (runtime.HalfWidthAt(corners[i]) + p.barrierMargin + 12f);
                            if (Spawn(stand, pos, Facing(-gr), structRoot)) structures++;
                        }
                    }
                }

                // --- start/finish gantry ---
                GameObject gantry = T(RuntimeArtLibrary.StartGantry);
                if (gantry != null)
                {
                    runtime.SampleAtDistance(0f, out Vector3 sp, out Vector3 sf, out _);
                    if (Spawn(gantry, sp + Vector3.up * 0.01f, OrientAlong(sf), structRoot)) structures++;
                }

                // --- marshal posts: authored posts, else every ~300 m ---
                GameObject marshal = T(RuntimeArtLibrary.MarshalPost);
                if (marshal != null)
                {
                    if (definition != null && definition.marshalPosts != null && definition.marshalPosts.Count > 0)
                    {
                        foreach (var post in definition.marshalPosts)
                            if (Spawn(marshal, post.position, Facing(NearestOutward(post.position)), structRoot)) structures++;
                    }
                    else
                    {
                        for (float d = 150f; d < len; d += 300f)
                        {
                            runtime.SampleAtDistance(d, out Vector3 mp, out _, out Vector3 mr);
                            Vector3 pos = mp + mr * (runtime.HalfWidthAt(d) + p.barrierMargin + 2f);
                            if (Spawn(marshal, pos, Facing(mr), structRoot)) structures++;
                        }
                    }
                }

                // --- light towers on night circuits ---
                GameObject light = p.lightTowers ? T(RuntimeArtLibrary.LightTower) : null;
                if (light != null)
                {
                    for (float d = 0f; d < len; d += 140f)
                    {
                        runtime.SampleAtDistance(d, out Vector3 lp, out Vector3 lf, out Vector3 lr);
                        Vector3 outward = lr;
                        Vector3 pos = lp + outward * (runtime.HalfWidthAt(d) + p.barrierMargin + 5f);
                        if (Spawn(light, pos, Facing(outward), structRoot)) structures++;
                    }
                }

                // --- pit garages beyond the pit lane (off the pit path) ---
                GameObject garage = T(RuntimeArtLibrary.PitGarageBay);
                if (garage != null && p.pitBuildings && Mathf.Abs(runtime.PitLaneLateral) > 0.5f)
                {
                    float corridorStart = Mathf.Clamp01(runtime.PitCorridorStartNormalized) * len;
                    float outwardLateral = runtime.PitLaneLateral + pitSide * 12f;
                    for (float d = corridorStart; d < len - 20f; d += 8f)
                    {
                        runtime.SampleAtDistance(d, out Vector3 pp, out Vector3 pf, out Vector3 pr);
                        Vector3 pos = pp + pr * outwardLateral;
                        if (Spawn(garage, pos, OrientAlong(pf), structRoot)) structures++;
                    }
                }

                // --- scattered clutter / vegetation stand-ins ---
                GameObject[] clutter = ClutterPool(T);
                if (clutter.Length > 0 && p.vegetationDensity > 0f)
                {
                    int attempts = Mathf.RoundToInt(len / 100f * p.vegetationDensity);
                    for (int i = 0; i < attempts; i++)
                    {
                        float d = (float)rng.NextDouble() * len;
                        runtime.SampleAtDistance(d, out Vector3 vp, out Vector3 vf, out Vector3 vr);
                        float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                        float band = Mathf.Lerp(6f, 30f, (float)rng.NextDouble());
                        Vector3 pos = vp + vr * side * (runtime.HalfWidthAt(d) + p.barrierMargin + band);
                        GameObject c = clutter[rng.Next(clutter.Length)];
                        if (Spawn(c, pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), propRoot)) props++;
                    }
                }

                Debug.Log($"[RuntimeArt] {trackId}: placed {barriers} barriers, {kerbs} kerbs, " +
                          $"{fences} fences, {structures} structures");
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogWarning("[RuntimeArt] dressing failed (race continues): " + e);
            }
        }

        // ---------------------------------------------------------------- helpers

        GameObject[] ClutterPool(Func<string, GameObject> T)
        {
            var list = new List<GameObject>();
            void Add(string k) { var g = T(k); if (g != null) list.Add(g); }
            Add(RuntimeArtLibrary.UtilityCabinet);
            Add(RuntimeArtLibrary.GuardHut);
            Add(RuntimeArtLibrary.MaintenanceCart);
            Add(RuntimeArtLibrary.SpeakerPole);
            return list.ToArray();
        }

        Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        // Direction pointing away from the track at a world position (for facing
        // authored nodes toward the circuit).
        Vector3 NearestOutward(Vector3 worldPos)
        {
            var cl = runtime.centerLine;
            float best = float.MaxValue;
            Vector3 nearest = cl.Count > 0 ? cl[0] : worldPos;
            for (int i = 0; i < cl.Count; i += 2)
            {
                float dd = (cl[i] - worldPos).sqrMagnitude;
                if (dd < best) { best = dd; nearest = cl[i]; }
            }
            Vector3 o = worldPos - nearest; o.y = 0f;
            return o.sqrMagnitude < 1e-4f ? Vector3.forward : o.normalized;
        }

        float Curvature(float d)
        {
            const float h = 4f;
            runtime.SampleAtDistance(Mathf.Repeat(d - h, runtime.length), out _, out Vector3 f0, out _);
            runtime.SampleAtDistance(Mathf.Repeat(d + h, runtime.length), out _, out Vector3 f1, out _);
            f0.y = 0f; f1.y = 0f;
            if (f0.sqrMagnitude < 1e-4f || f1.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(f0, f1, Vector3.up) * Mathf.Deg2Rad / (2f * h);
        }

        // Module length authored along local +X → align local X with the tangent.
        static Quaternion OrientAlong(Vector3 forward)
        {
            Vector3 z = Vector3.Cross(forward, Vector3.up);
            if (z.sqrMagnitude < 1e-5f) z = Vector3.forward;
            return Quaternion.LookRotation(z.normalized, Vector3.up);
        }

        // Face toward the track (opposite the outward direction).
        static Quaternion Facing(Vector3 outward)
        {
            Vector3 dir = -outward; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        bool Spawn(GameObject template, Vector3 pos, Quaternion rot, Transform parent)
        {
            if (template == null) return false;
            GameObject clone = UnityEngine.Object.Instantiate(template, pos, rot, parent);
            foreach (Transform t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            clone.SetActive(true);
            foreach (Collider c in clone.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            return true;
        }

        void ApplyLightingNudge()
        {
            try
            {
                if (QualitySettings.shadowDistance < 250f) QualitySettings.shadowDistance = 250f;
                Light sun = RenderSettings.sun;
                if (sun == null)
                {
                    foreach (Light l in FindObjectsOfType<Light>())
                        if (l.type == LightType.Directional) { sun = l; break; }
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
            catch (Exception e)
            {
                Debug.LogWarning("[RuntimeArt] lighting nudge skipped: " + e.Message);
            }
        }
    }
}
