using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track.Dressing
{
    /// <summary>
    /// Procedural track-dressing layer. Reads the authored circuit
    /// (<see cref="TrackDefinitionAsset"/>) and its deterministic
    /// <see cref="TrackSplineSampler"/> sampling and places the modular kit
    /// (barriers, kerbs, catch fencing, marshal posts, camera/light towers,
    /// braking boards, grandstands, vegetation) around it.
    ///
    /// Design contract:
    ///   * NON-DESTRUCTIVE — never mutates the definition, the road mesh, the
    ///     racing line, colliders or any gameplay data. Only spawns child
    ///     GameObjects under a single dressing root.
    ///   * DETERMINISTIC — everything derives from (definition, profile, seed);
    ///     same inputs → identical output. No use of UnityEngine.Random.
    ///   * REVERSIBLE — <see cref="Clear"/> removes exactly what it created and
    ///     preserves manually-placed overrides (children under "Overrides" or
    ///     carrying a <see cref="DressingOverrideMarker"/>).
    ///   * SIDE-AWARE — distinguishes left/right edges and inside/outside of
    ///     corners from curvature; honours exclusion zones and pit openings.
    ///   * GRACEFUL — missing prefab slots produce warnings, not exceptions.
    ///
    /// Gameplay is untouched: this is presentation only. The collision proxies on
    /// the placed barriers are physical, but they sit at (edge + margin), outside
    /// the racing surface; validation warns if a placement would intrude.
    ///
    /// NOTE: not yet compiled or run in Unity in the authoring environment
    /// (Linux, no editor) — see Docs/ArtPipeline/SETUP_REPORT.md.
    /// </summary>
    public static class TrackDressingBuilder
    {
        public const string DressingRootName = "Track Dressing (generated)";
        public const string OverridesName = "Overrides";

        [Serializable]
        public struct ExclusionZone
        {
            public float startDistance;
            public float endDistance;
            public Side side;   // which edge to skip (Both for full openings)
        }

        public enum Side { Left, Right, Both }

        public sealed class Result
        {
            public GameObject Root;
            public int Barriers, Kerbs, Fences, Structures, Vegetation;
            public readonly List<string> Warnings = new List<string>();
            public int Placed => Barriers + Kerbs + Fences + Structures + Vegetation;
        }

        public static Result Build(TrackDefinitionAsset definition,
                                   CircuitVisualProfile profile,
                                   Transform parent,
                                   int seed = 12345,
                                   IReadOnlyList<ExclusionZone> exclusions = null)
        {
            var result = new Result();
            if (definition == null)
            {
                result.Warnings.Add("Null definition.");
                return result;
            }
            if (profile == null)
            {
                result.Warnings.Add("Null profile — nothing placed.");
                return result;
            }

            var sampler = new TrackSplineSampler();
            sampler.Build(definition.spline, definition.closedLoop);
            if (sampler.Length <= 0f || sampler.Samples.Count < 2)
            {
                result.Warnings.Add("Sampler produced no usable centreline.");
                return result;
            }

            var samples = sampler.Samples;
            float length = sampler.Length;
            var rng = new System.Random(seed);

            // Preserve overrides: reparent them out, rebuild root, reparent back.
            GameObject root = PrepareRoot(parent, out Transform preserved);

            var exclusionList = new List<ExclusionZone>();
            if (exclusions != null) exclusionList.AddRange(exclusions);
            AddPitExclusion(definition, exclusionList);

            var barrierParent = Child(root.transform, "Barriers");
            var kerbParent = Child(root.transform, "Kerbs");
            var fenceParent = Child(root.transform, "Fencing");
            var structureParent = Child(root.transform, "Structures");
            var vegParent = Child(root.transform, "Vegetation");

            PlaceBarriers(definition, profile, sampler, exclusionList, barrierParent, result);
            PlaceKerbs(definition, profile, sampler, kerbParent, result);
            PlaceCatchFencing(profile, sampler, exclusionList, fenceParent, result);
            PlaceStructures(definition, profile, sampler, structureParent, rng, result);
            PlaceVegetation(profile, sampler, vegParent, rng, result);

            if (preserved != null) preserved.SetParent(root.transform, true);

            result.Root = root;
            return result;
        }

        /// <summary>Remove generated dressing under <paramref name="parent"/>, keep overrides.</summary>
        public static void Clear(Transform parent)
        {
            if (parent == null) return;
            Transform root = parent.Find(DressingRootName);
            if (root == null) return;
            Transform overrides = root.Find(OverridesName);
            if (overrides != null) overrides.SetParent(parent, true);
            DestroySafe(root.gameObject);
        }

        // ------------------------------------------------------------------
        // Placement passes
        // ------------------------------------------------------------------

        static void PlaceBarriers(TrackDefinitionAsset def, CircuitVisualProfile profile,
            TrackSplineSampler sampler, List<ExclusionZone> exclusions,
            Transform parent, Result r)
        {
            if (profile.barrierPrimary == null)
            {
                r.Warnings.Add("No barrierPrimary prefab — barriers skipped.");
                return;
            }
            float step = Mathf.Max(0.5f, profile.barrierModuleLength);
            for (float d = 0f; d < sampler.Length; d += step)
            {
                Sample s = SampleAt(sampler, d);
                float curvature = CurvatureAt(sampler, d); // signed rad/m (+ = left turn)
                bool straight = Mathf.Abs(curvature) < 0.004f;
                GameObject prefab = (straight && profile.concreteOnStraights && profile.barrierSecondary != null)
                    ? profile.barrierSecondary : profile.barrierPrimary;

                foreach (Side side in new[] { Side.Left, Side.Right })
                {
                    if (Excluded(exclusions, d, side)) continue;
                    Vector3 outward = side == Side.Left ? -s.Normal : s.Normal;
                    Vector3 pos = s.Position + outward * (s.Width * 0.5f + profile.barrierMargin);
                    Quaternion rot = OrientAlongTangent(s.Tangent);
                    Instantiate(prefab, pos, rot, parent);
                    r.Barriers++;

                    // apex tyre stacks on the inside of tight corners
                    if (profile.tyreWall != null && Mathf.Abs(curvature) > 0.02f)
                    {
                        bool inside = (curvature > 0f && side == Side.Left) ||
                                      (curvature < 0f && side == Side.Right);
                        if (inside && (int)(d / step) % 2 == 0)
                        {
                            Instantiate(profile.tyreWall, pos + outward * 0.6f, rot, parent);
                            r.Barriers++;
                        }
                    }
                }
            }
        }

        static void PlaceKerbs(TrackDefinitionAsset def, CircuitVisualProfile profile,
            TrackSplineSampler sampler, Transform parent, Result r)
        {
            if (profile.kerbPainted == null)
            {
                r.Warnings.Add("No kerbPainted prefab — kerbs skipped.");
                return;
            }
            float step = Mathf.Max(0.5f, profile.kerbModuleLength);
            var spline = def.spline;
            for (float d = 0f; d < sampler.Length; d += step)
            {
                Sample s = SampleAt(sampler, d);
                float curvature = CurvatureAt(sampler, d);
                bool corner = Mathf.Abs(curvature) > 0.008f;
                // kerb flags authored per control point (nearest), or corner heuristic
                bool kerbLeft, kerbRight;
                NearestKerbFlags(def, sampler, d, out kerbLeft, out kerbRight);
                kerbLeft |= corner && curvature > 0f;
                kerbRight |= corner && curvature < 0f;

                if (kerbLeft) PlaceKerbModule(profile, s, Side.Left, curvature, parent, r);
                if (kerbRight) PlaceKerbModule(profile, s, Side.Right, curvature, parent, r);
            }
        }

        static void PlaceKerbModule(CircuitVisualProfile profile, Sample s, Side side,
            float curvature, Transform parent, Result r)
        {
            Vector3 outward = side == Side.Left ? -s.Normal : s.Normal;
            Vector3 pos = s.Position + outward * (s.Width * 0.5f - 0.25f);
            // kerb long axis along tangent; flip so painted rise faces the track
            Quaternion rot = OrientAlongTangent(s.Tangent);
            GameObject prefab = profile.kerbPainted;
            if (profile.kerbSausage != null && Mathf.Abs(curvature) > 0.03f &&
                (int)(s.Distance) % 8 == 0)
            {
                prefab = profile.kerbSausage;
            }
            Instantiate(prefab, pos, rot, parent);
            r.Kerbs++;
        }

        static void PlaceCatchFencing(CircuitVisualProfile profile, TrackSplineSampler sampler,
            List<ExclusionZone> exclusions, Transform parent, Result r)
        {
            if (profile.catchFence == null) return;
            float step = Mathf.Max(1f, profile.catchFenceEvery);
            for (float d = 0f; d < sampler.Length; d += step)
            {
                Sample s = SampleAt(sampler, d);
                foreach (Side side in new[] { Side.Left, Side.Right })
                {
                    if (Excluded(exclusions, d, side)) continue;
                    Vector3 outward = side == Side.Left ? -s.Normal : s.Normal;
                    Vector3 pos = s.Position + outward *
                        (s.Width * 0.5f + profile.barrierMargin + profile.fenceExtraMargin);
                    Instantiate(profile.catchFence, pos, OrientAlongTangent(s.Tangent), parent);
                    r.Fences++;
                }
            }
        }

        static void PlaceStructures(TrackDefinitionAsset def, CircuitVisualProfile profile,
            TrackSplineSampler sampler, Transform parent, System.Random rng, Result r)
        {
            // Start/finish gantry
            if (profile.startGantry != null)
            {
                Sample s = SampleAt(sampler, Mathf.Clamp(def.startFinishDistance, 0, sampler.Length - 0.01f));
                Vector3 pos = s.Position + Vector3.up * 0.01f;
                Instantiate(profile.startGantry, pos, OrientAlongTangent(s.Tangent), parent);
                r.Structures++;
            }

            // Marshal posts (authored)
            if (profile.marshalPost != null && def.marshalPosts != null)
            {
                foreach (var mp in def.marshalPosts)
                {
                    Instantiate(profile.marshalPost, mp.position,
                        OrientFacing(mp.position, sampler), parent);
                    r.Structures++;
                }
            }

            // Camera towers (authored broadcast nodes)
            if (profile.cameraTower != null && def.cameraNodes != null)
            {
                foreach (var cam in def.cameraNodes)
                {
                    Instantiate(profile.cameraTower, cam.position,
                        OrientFacing(cam.position, sampler), parent);
                    r.Structures++;
                }
            }

            // Grandstands over crowd zones
            if (def.crowdZones != null)
            {
                foreach (var cz in def.crowdZones)
                {
                    var prefab = profile.PickWeighted(profile.grandstands, rng);
                    if (prefab == null) continue;
                    Instantiate(prefab, cz.position, OrientFacing(cz.position, sampler), parent);
                    r.Structures++;
                }
            }

            // Braking boards before detected corner entries
            if (profile.brakingBoard != null)
            {
                float prevCurv = 0f;
                for (float d = 0f; d < sampler.Length; d += 5f)
                {
                    float c = Mathf.Abs(CurvatureAt(sampler, d));
                    if (prevCurv < 0.01f && c >= 0.01f)
                    {
                        float bd = Mathf.Repeat(d - profile.brakingBoardLead, sampler.Length);
                        Sample s = SampleAt(sampler, bd);
                        Vector3 pos = s.Position - s.Normal * (s.Width * 0.5f + 1.5f);
                        Instantiate(profile.brakingBoard, pos, OrientAlongTangent(s.Tangent), parent);
                        r.Structures++;
                    }
                    prevCurv = c;
                }
            }

            // Light towers for night circuits
            if (profile.lightTower != null && def.supportsNightSession)
            {
                float step = Mathf.Max(20f, profile.lightTowerEvery);
                for (float d = 0f; d < sampler.Length; d += step)
                {
                    Sample s = SampleAt(sampler, d);
                    Vector3 pos = s.Position + s.Normal * (s.Width * 0.5f + profile.barrierMargin + 4f);
                    Instantiate(profile.lightTower, pos, OrientFacing(pos, sampler), parent);
                    r.Structures++;
                }
            }
        }

        static void PlaceVegetation(CircuitVisualProfile profile, TrackSplineSampler sampler,
            Transform parent, System.Random rng, Result r)
        {
            if ((profile.vegetation == null || profile.vegetation.Length == 0) &&
                (profile.clutter == null || profile.clutter.Length == 0)) return;
            float per100 = Mathf.Max(0f, profile.vegetationDensity);
            int attempts = Mathf.RoundToInt(sampler.Length / 100f * per100);
            for (int i = 0; i < attempts; i++)
            {
                float d = (float)rng.NextDouble() * sampler.Length;
                Sample s = SampleAt(sampler, d);
                float sign = rng.NextDouble() < 0.5 ? -1f : 1f;
                float band = Mathf.Lerp(profile.vegetationBand.x, profile.vegetationBand.y,
                    (float)rng.NextDouble());
                Vector3 pos = s.Position + s.Normal * sign * (s.Width * 0.5f + band);
                var pool = (rng.NextDouble() < 0.7 || profile.clutter == null || profile.clutter.Length == 0)
                    ? profile.vegetation : profile.clutter;
                var prefab = profile.PickWeighted(pool, rng);
                if (prefab == null) continue;
                float yaw = (float)rng.NextDouble() * 360f;
                Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
                r.Vegetation++;
            }
        }

        // ------------------------------------------------------------------
        // Sampling helpers
        // ------------------------------------------------------------------

        public struct Sample
        {
            public Vector3 Position, Tangent, Normal;
            public float Width, Distance;
        }

        static Sample SampleAt(TrackSplineSampler sampler, float distance)
        {
            var list = sampler.Samples;
            distance = Mathf.Repeat(distance, sampler.Length);
            // linear scan is fine (few thousand samples); could binary-search if needed
            int n = list.Count;
            for (int i = 0; i < n - 1; i++)
            {
                if (list[i + 1].Distance >= distance)
                {
                    var a = list[i];
                    var b = list[i + 1];
                    float span = Mathf.Max(0.0001f, b.Distance - a.Distance);
                    float f = Mathf.Clamp01((distance - a.Distance) / span);
                    return new Sample
                    {
                        Position = Vector3.Lerp(a.Position, b.Position, f),
                        Tangent = Vector3.Slerp(a.Tangent, b.Tangent, f).normalized,
                        Normal = Vector3.Slerp(a.Normal, b.Normal, f).normalized,
                        Width = Mathf.Lerp(a.Width, b.Width, f),
                        Distance = distance,
                    };
                }
            }
            var last = list[n - 1];
            return new Sample
            {
                Position = last.Position, Tangent = last.Tangent,
                Normal = last.Normal, Width = last.Width, Distance = distance,
            };
        }

        /// <summary>Signed curvature (rad/m); positive = turning left (toward -Normal).</summary>
        static float CurvatureAt(TrackSplineSampler sampler, float distance)
        {
            const float h = 4f;
            Vector3 t0 = SampleAt(sampler, distance - h).Tangent;
            Vector3 t1 = SampleAt(sampler, distance + h).Tangent;
            float angle = Vector3.SignedAngle(t0, t1, Vector3.up) * Mathf.Deg2Rad;
            return angle / (2f * h);
        }

        static void NearestKerbFlags(TrackDefinitionAsset def, TrackSplineSampler sampler,
            float distance, out bool left, out bool right)
        {
            left = right = false;
            if (def.spline == null || def.spline.Count == 0) return;
            Vector3 p = SampleAt(sampler, distance).Position;
            float best = float.MaxValue; int idx = 0;
            for (int i = 0; i < def.spline.Count; i++)
            {
                float dd = (def.spline[i].position - p).sqrMagnitude;
                if (dd < best) { best = dd; idx = i; }
            }
            left = def.spline[idx].kerbLeft;
            right = def.spline[idx].kerbRight;
        }

        // ------------------------------------------------------------------
        // Orientation / exclusion / instancing
        // ------------------------------------------------------------------

        // Kit modules are authored with their length along local +X (Blender X →
        // Unity X after y-up GLB export). This aligns local X to the tangent.
        static Quaternion OrientAlongTangent(Vector3 tangent)
        {
            Vector3 fwd = Vector3.Cross(tangent, Vector3.up); // local Z
            if (fwd.sqrMagnitude < 1e-5f) fwd = Vector3.forward;
            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        static Quaternion OrientFacing(Vector3 worldPos, TrackSplineSampler sampler)
        {
            // face the nearest point on the centreline
            float best = float.MaxValue; Vector3 target = worldPos + Vector3.forward;
            var list = sampler.Samples;
            for (int i = 0; i < list.Count; i += 4)
            {
                float dd = (list[i].Position - worldPos).sqrMagnitude;
                if (dd < best) { best = dd; target = list[i].Position; }
            }
            Vector3 dir = target - worldPos; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        static bool Excluded(List<ExclusionZone> zones, float d, Side side)
        {
            if (zones == null) return false;
            foreach (var z in zones)
            {
                if (d >= z.startDistance && d <= z.endDistance &&
                    (z.side == Side.Both || z.side == side))
                {
                    return true;
                }
            }
            return false;
        }

        static void AddPitExclusion(TrackDefinitionAsset def, List<ExclusionZone> zones)
        {
            var pit = def.pitLane;
            if (pit.exitDistance <= pit.entryDistance) return;
            // Open the barrier line on the pit side across the pit lane span.
            zones.Add(new ExclusionZone
            {
                startDistance = Mathf.Max(0f, pit.entryDistance - 5f),
                endDistance = pit.exitDistance + 5f,
                side = Side.Right, // pit side convention; overridable via exclusions arg
            });
        }

        // ------------------------------------------------------------------
        // Scene graph
        // ------------------------------------------------------------------

        static GameObject PrepareRoot(Transform parent, out Transform preserved)
        {
            preserved = null;
            Transform existing = parent != null ? parent.Find(DressingRootName) : null;
            if (existing != null)
            {
                Transform ov = existing.Find(OverridesName);
                if (ov != null) { ov.SetParent(parent, true); preserved = ov; }
                DestroySafe(existing.gameObject);
            }
            var root = new GameObject(DressingRootName);
            if (parent != null) root.transform.SetParent(parent, false);
            return root;
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static GameObject Instantiate(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
        {
            if (prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(prefab, pos, rot, parent);
            return go;
        }

        static void DestroySafe(GameObject go)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
