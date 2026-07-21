using System;
using System.Collections.Generic;
using F1Game.Track;
using LocalFormulaRacing;
using UnityEngine;

namespace F1Game.Art
{
    /// <summary>
    /// The live runtime art activator, driven by the FINAL repaired
    /// <see cref="TrackRuntime"/> centreline (the same data the playable road is
    /// built from). Called once from <c>TrackManager.Build</c> after the gameplay
    /// track is complete.
    ///
    /// It applies the runtime asphalt material to the road, a restrained lighting
    /// nudge, and then spawns the trackside modules — built in code by
    /// <see cref="ProceduralKit"/> (no glTFast, no async, no file IO, so it always
    /// renders) — along the centreline: barriers, tyre stacks, catch fencing,
    /// kerbs, braking boards, gantry, marshal posts, camera/light towers,
    /// grandstands, pit garages and scattered props. Everything is visual only
    /// (no colliders added), lives under one "Track Art (runtime)" root, and is
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

        int barriers, kerbs, fences, structures, props;

        public static void Activate(Transform trackRoot, TrackRuntime runtime,
            TrackDefinitionAsset definition, string trackId, bool nightSession)
        {
            if (trackRoot == null) return;
            try
            {
                Transform existing = trackRoot.Find(RootName);
                if (existing != null) DestroyImmediate(existing.gameObject);

                var go = new GameObject(RootName);
                go.transform.SetParent(trackRoot, false);
                var art = go.AddComponent<RuntimeTrackArt>();
                art.runtime = runtime;
                art.definition = definition;
                art.trackId = trackId ?? (runtime != null ? runtime.trackId : "");
                art.nightSession = nightSession;
                art.Build();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RuntimeArt] Activate failed (race continues): " + e);
            }
        }

        void Build()
        {
            RuntimeMaterialFactory.ApplyRoadSurface(transform.parent, wet: false);
            ApplyLightingNudge();

            if (runtime == null || runtime.centerLine == null || runtime.centerLine.Count < 8 || runtime.length < 200f)
            {
                Debug.LogWarning("[RuntimeArt] No usable TrackRuntime centreline — dressing skipped.");
                return;
            }

            try
            {
                Place();
                Debug.Log($"[RuntimeArt] {trackId}: placed {barriers} barriers, {kerbs} kerbs, " +
                          $"{fences} fences, {structures} structures");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RuntimeArt] dressing failed (race continues): " + e);
            }
        }

        void Place()
        {
            RuntimeCircuitProfile p = RuntimeProfileLibrary.Select(trackId, runtime.styleName);
            var rng = new System.Random(20240720 + p.seedOffset);

            Transform barrierRoot = Child("Barriers");
            Transform kerbRoot = Child("Kerbs");
            Transform fenceRoot = Child("Fencing");
            Transform structRoot = Child("Structures");
            Transform propRoot = Child("Props");

            GameObject cornerBarrier = ProceduralKit.GetTemplate(BarrierProc(p.primaryBarrier));
            GameObject straightBarrier = ProceduralKit.GetTemplate(BarrierProc(p.secondaryBarrier));
            GameObject tyre = p.tyreStacks ? ProceduralKit.GetTemplate(ProceduralKit.TyreWall) : null;
            GameObject fence = ProceduralKit.GetTemplate(ProceduralKit.CatchFence);
            GameObject kerb = ProceduralKit.GetTemplate(ProceduralKit.KerbPainted);
            GameObject sausage = p.sausageKerbs ? ProceduralKit.GetTemplate(ProceduralKit.KerbSausage) : null;
            GameObject brakeBoard = ProceduralKit.GetTemplate(ProceduralKit.BrakingBoard);
            GameObject camera = ProceduralKit.GetTemplate(ProceduralKit.CameraTower);
            GameObject stand = p.grandstands ? ProceduralKit.GetTemplate(ProceduralKit.Grandstand) : null;
            GameObject gantry = ProceduralKit.GetTemplate(ProceduralKit.StartGantry);
            GameObject marshal = ProceduralKit.GetTemplate(ProceduralKit.MarshalPost);
            GameObject light = p.lightTowers ? ProceduralKit.GetTemplate(ProceduralKit.LightTower) : null;
            GameObject garage = ProceduralKit.GetTemplate(ProceduralKit.Garage);
            GameObject[] clutter =
            {
                ProceduralKit.GetTemplate(ProceduralKit.Cabinet),
                ProceduralKit.GetTemplate(ProceduralKit.Hut),
                ProceduralKit.GetTemplate(ProceduralKit.Cart),
                ProceduralKit.GetTemplate(ProceduralKit.Speaker),
            };

            float len = runtime.length;
            float pitSide = Mathf.Sign(runtime.PitLaneLateral == 0f ? 1f : runtime.PitLaneLateral);
            float pitStartN = Mathf.Clamp01(runtime.PitEntryRampStartNormalized - 0.03f);

            // --- barriers + tyre stacks + catch fencing along both edges ---
            for (float d = 0f; d < len; d += 6f)
            {
                runtime.SampleAtDistance(d, out Vector3 point, out Vector3 fwd, out Vector3 right);
                float hw = runtime.HalfWidthAt(d);
                float curv = Curvature(d);
                bool straight = Mathf.Abs(curv) < 0.004f;
                float n = d / len;
                Quaternion rot = OrientAlong(fwd);

                for (int s = -1; s <= 1; s += 2)
                {
                    if (Mathf.Sign(s) == pitSide && (n >= pitStartN || n <= 0.05f)) continue;
                    Vector3 outward = right * s;
                    Vector3 pos = point + outward * (hw + p.barrierMargin);
                    GameObject b = (straight && p.concreteOnStraights) ? straightBarrier : cornerBarrier;
                    if (Spawn(b, pos, rot, barrierRoot)) barriers++;

                    bool inside = (curv > 0f && s < 0) || (curv < 0f && s > 0);
                    if (tyre != null && Mathf.Abs(curv) > 0.02f && inside && ((int)(d / 6f) % 2 == 0))
                        if (Spawn(tyre, pos + outward * 0.6f, rot, barrierRoot)) barriers++;
                }

                if (((int)d % 24 == 0))
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        if (Mathf.Sign(s) == pitSide && (n >= pitStartN || n <= 0.05f)) continue;
                        Vector3 pos = point + right * s * (hw + p.barrierMargin + 1.2f);
                        if (Spawn(fence, pos, rot, fenceRoot)) fences++;
                    }
                }
            }

            // --- kerbs through corners (inside edge) ---
            for (float d = 0f; d < len; d += 4f)
            {
                float curv = Curvature(d);
                if (Mathf.Abs(curv) < 0.008f) continue;
                runtime.SampleAtDistance(d, out Vector3 point, out Vector3 fwd, out Vector3 right);
                float hw = runtime.HalfWidthAt(d);
                int s = curv > 0f ? -1 : 1;
                Vector3 pos = point + right * s * (hw - 0.25f);
                GameObject k = (sausage != null && Mathf.Abs(curv) > 0.03f && ((int)d % 8 == 0)) ? sausage : kerb;
                if (Spawn(k, pos, OrientAlong(fwd), kerbRoot)) kerbs++;
            }

            // --- corners: braking boards + collect entries ---
            var corners = new List<float>();
            float prevC = 0f;
            for (float d = 0f; d < len; d += 5f)
            {
                float c = Mathf.Abs(Curvature(d));
                bool entry = prevC < 0.01f && c >= 0.01f;
                prevC = c;
                if (!entry) continue;
                corners.Add(d);
                float bd = Mathf.Repeat(d - 90f, len);
                runtime.SampleAtDistance(bd, out Vector3 bp, out Vector3 bf, out Vector3 br);
                if (Spawn(brakeBoard, bp - br * (runtime.HalfWidthAt(bd) + 1.5f), OrientAlong(bf), structRoot)) structures++;
            }

            // --- camera towers: authored nodes else every 3rd corner ---
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
                    if (Spawn(camera, cp + crr * (runtime.HalfWidthAt(corners[i]) + p.barrierMargin + 8f), Facing(crr), structRoot)) structures++;
                }
            }

            // --- grandstands: authored crowd zones else start/finish + every 2nd corner ---
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
                    if (Spawn(stand, s0 - sr0 * (runtime.HalfWidthAt(0f) + p.barrierMargin + 12f), Facing(-sr0), structRoot)) structures++;
                    for (int i = 0; i < corners.Count; i += 2)
                    {
                        runtime.SampleAtDistance(corners[i], out Vector3 gp, out _, out Vector3 gr);
                        if (Spawn(stand, gp - gr * (runtime.HalfWidthAt(corners[i]) + p.barrierMargin + 12f), Facing(-gr), structRoot)) structures++;
                    }
                }
            }

            // --- start/finish gantry ---
            runtime.SampleAtDistance(0f, out Vector3 sp, out Vector3 sf, out _);
            if (Spawn(gantry, sp + Vector3.up * 0.01f, OrientAlong(sf), structRoot)) structures++;

            // --- marshal posts: authored else every ~300 m ---
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
                    if (Spawn(marshal, mp + mr * (runtime.HalfWidthAt(d) + p.barrierMargin + 2f), Facing(mr), structRoot)) structures++;
                }
            }

            // --- light towers on night circuits ---
            if (light != null)
            {
                for (float d = 0f; d < len; d += 140f)
                {
                    runtime.SampleAtDistance(d, out Vector3 lp, out _, out Vector3 lr);
                    if (Spawn(light, lp + lr * (runtime.HalfWidthAt(d) + p.barrierMargin + 5f), Facing(lr), structRoot)) structures++;
                }
            }

            // --- pit garages beyond the pit lane (off the pit path) ---
            if (p.pitBuildings && Mathf.Abs(runtime.PitLaneLateral) > 0.5f)
            {
                float corridorStart = Mathf.Clamp01(runtime.PitCorridorStartNormalized) * len;
                float outwardLateral = runtime.PitLaneLateral + pitSide * 12f;
                for (float d = corridorStart; d < len - 20f; d += 8f)
                {
                    runtime.SampleAtDistance(d, out Vector3 pp, out _, out Vector3 pr);
                    if (Spawn(garage, pp + pr * outwardLateral, OrientAlong(GetForward(d)), structRoot)) structures++;
                }
            }

            // --- scattered clutter ---
            if (p.vegetationDensity > 0f)
            {
                int attempts = Mathf.RoundToInt(len / 100f * p.vegetationDensity);
                for (int i = 0; i < attempts; i++)
                {
                    float d = (float)rng.NextDouble() * len;
                    runtime.SampleAtDistance(d, out Vector3 vp, out _, out Vector3 vr);
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float band = Mathf.Lerp(6f, 30f, (float)rng.NextDouble());
                    Vector3 pos = vp + vr * side * (runtime.HalfWidthAt(d) + p.barrierMargin + band);
                    if (Spawn(clutter[rng.Next(clutter.Length)], pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), propRoot)) props++;
                }
            }

            DiagnoseVisibility(barrierRoot);
        }

        /// <summary>
        /// One-shot visibility diagnostic: compares a spawned barrier to the visible
        /// road so we can tell position bugs from render-state bugs. Remove once art
        /// is confirmed on-screen.
        /// </summary>
        void DiagnoseVisibility(Transform barrierRoot)
        {
            try
            {
                Debug.Log($"[RuntimeArt][diag] artRoot activeInHierarchy={gameObject.activeInHierarchy} " +
                          $"worldPos={transform.position} scale={transform.lossyScale} barrierChildren={barrierRoot.childCount}");

                MeshRenderer road = null;
                foreach (var r in transform.parent.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.gameObject.name.StartsWith("Procedural road")) { road = r; break; }
                if (road != null)
                    Debug.Log($"[RuntimeArt][diag] ROAD boundsCenter={road.bounds.center} boundsSize={road.bounds.size} " +
                              $"shader={(road.sharedMaterial != null ? road.sharedMaterial.shader.name : "null")}");

                if (barrierRoot.childCount > 0)
                {
                    Transform b0 = barrierRoot.GetChild(0);
                    var mr = b0.GetComponentInChildren<MeshRenderer>(true);
                    var mf = b0.GetComponentInChildren<MeshFilter>(true);
                    Debug.Log($"[RuntimeArt][diag] BARRIER0 name={b0.name} activeInHierarchy={b0.gameObject.activeInHierarchy} " +
                              $"worldPos={b0.position} lossyScale={b0.lossyScale} layer={b0.gameObject.layer} " +
                              $"hasRenderer={(mr != null)} rendererEnabled={(mr != null && mr.enabled)} " +
                              $"mesh={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "null")} " +
                              $"meshVerts={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0)} " +
                              $"shader={(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "null")} " +
                              $"boundsCenter={(mr != null ? mr.bounds.center.ToString() : "n/a")} " +
                              $"boundsSize={(mr != null ? mr.bounds.size.ToString() : "n/a")}");
                }
            }
            catch (Exception e) { Debug.LogWarning("[RuntimeArt][diag] " + e.Message); }
        }

        // ---------------------------------------------------------------- helpers

        static string BarrierProc(string key)
        {
            if (key == RuntimeArtLibrary.ConcreteBarrier) return ProceduralKit.Concrete;
            if (key == RuntimeArtLibrary.TecproBarrier) return ProceduralKit.Tecpro;
            return ProceduralKit.Armco;
        }

        Vector3 GetForward(float d)
        {
            runtime.SampleAtDistance(d, out _, out Vector3 f, out _);
            return f;
        }

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

        static Quaternion OrientAlong(Vector3 forward)
        {
            Vector3 z = Vector3.Cross(forward, Vector3.up);
            if (z.sqrMagnitude < 1e-5f) z = Vector3.forward;
            return Quaternion.LookRotation(z.normalized, Vector3.up);
        }

        static Quaternion Facing(Vector3 outward)
        {
            Vector3 dir = -outward; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        static bool Spawn(GameObject template, Vector3 pos, Quaternion rot, Transform parent)
        {
            if (template == null) return false;
            GameObject clone = Instantiate(template, pos, rot, parent);
            clone.SetActive(true);
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
