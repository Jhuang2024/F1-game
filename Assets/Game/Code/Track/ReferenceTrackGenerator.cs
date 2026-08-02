using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track
{
    /// <summary>
    /// Deterministically builds a reference-circuit <see cref="TrackDefinitionAsset"/>
    /// entirely in code (no Unity editor required), so the authored-track runtime
    /// path can be exercised end-to-end. The layout is an original fictional
    /// circuit — a closed loop with a mix of straights and corners, a pit lane,
    /// two DRS zones, three sectors and a full 22-car grid. Same seed → same
    /// track every run.
    /// </summary>
    public static class ReferenceTrackGenerator
    {
        public const string ReferenceTrackId = "aurora-park";

        public static TrackDefinitionAsset Generate()
        {
            var asset = ScriptableObject.CreateInstance<TrackDefinitionAsset>();
            asset.name = "Track_AuroraPark_Reference";
            asset.trackId = ReferenceTrackId;
            asset.displayName = "Aurora Park (Reference)";
            asset.country = "Fictional";
            asset.closedLoop = true;
            asset.supportsNightSession = true;
            asset.defaultLightingMood = "Mood_Clear";

            // Control points: an oval-with-kinks fantasy layout, ~4.6 km.
            // Deterministic parametric curve — radius modulated to create corners.
            const int points = 48;
            const float baseRadius = 620f;
            for (int i = 0; i < points; i++)
            {
                float a = i / (float)points * Mathf.PI * 2f;
                // Modulate radius to carve corners/straights (fixed harmonics).
                float r = baseRadius
                    + Mathf.Sin(a * 2f) * 120f
                    + Mathf.Sin(a * 3f + 0.7f) * 70f
                    - Mathf.Cos(a * 5f) * 40f;
                float x = Mathf.Cos(a) * r;
                float z = Mathf.Sin(a) * r * 0.72f; // squash into an oval
                float elevation = Mathf.Sin(a * 2f + 1.1f) * 8f; // gentle hills

                // Width narrows through the tighter (smaller-radius) sections.
                float width = Mathf.Lerp(12f, 15f, Mathf.InverseLerp(baseRadius - 200f, baseRadius + 200f, r));
                float camber = Mathf.Sin(a * 3f) * 3f;

                asset.spline.Add(new TrackDefinitionAsset.SplinePoint
                {
                    position = new Vector3(x, elevation, z),
                    width = width,
                    camberDegrees = camber,
                    kerbLeft = (i % 6) == 0,
                    kerbRight = (i % 6) == 3,
                });
            }

            // Length comes from the sampler's arc, not ComputeLength()'s chord sum -
            // the two differ by ~1-2.5% and every distance below is consumed against
            // sampler distances. See the same fix in AuthoredCircuitCatalog.
            var sampler = new TrackSplineSampler();
            sampler.Build(asset.spline, true);

            float length = sampler.Length;
            asset.startFinishDistance = 0f;
            asset.sectorBoundaryDistances = new[] { length / 3f, length * 2f / 3f };

            // Racing line: bias toward the inside through modelled corner phases.
            for (int i = 0; i < points; i++)
            {
                float a = i / (float)points * Mathf.PI * 2f;
                asset.racingLineOffsets.Add(Mathf.Clamp(Mathf.Sin(a * 2f) * 0.6f, -0.8f, 0.8f));
            }

            // Surfaces: rubbered racing line most of the lap, gravel on two exits.
            asset.surfaces.Add(new TrackDefinitionAsset.SurfaceZone
            {
                startDistance = 0f, endDistance = length,
                kind = TrackDefinitionAsset.SurfaceKind.RubberedLine, gripMultiplier = 1f,
            });

            // DRS: two zones on the longer straights.
            asset.drsZones.Add(new TrackDefinitionAsset.DrsZone
            {
                detectionDistance = length * 0.10f,
                activationDistance = length * 0.14f,
                endDistance = length * 0.24f,
            });
            asset.drsZones.Add(new TrackDefinitionAsset.DrsZone
            {
                detectionDistance = length * 0.60f,
                activationDistance = length * 0.64f,
                endDistance = length * 0.74f,
            });

            // Pit lane along the start/finish straight.
            var stalls = new List<Vector3>();
            Vector3 pitBase = asset.spline[0].position + new Vector3(0f, 0f, -20f);
            for (int i = 0; i < 22; i++)
            {
                stalls.Add(pitBase + new Vector3(i * 8f - 88f, 0f, 0f));
            }

            asset.pitLane = new TrackDefinitionAsset.PitLaneData
            {
                entryDistance = length * 0.94f,
                entryCommitDistance = length * 0.965f,
                exitDistance = length * 0.05f,
                stallCount = 22,
                stallPositions = stalls.ToArray(),
            };

            // Grid: staggered pairs running BACKWARDS from the start/finish line, so
            // slot 0 (pole) sits at the greatest lap distance and the field stacks up
            // behind it. `30 + i * 8` put pole at the back and the grid past the line.
            const float gridStartOffset = 52f;
            const float gridRowSpacing = 19f;
            const float gridStaggerOffset = 8f;
            for (int i = 0; i < 22; i++)
            {
                int row = i / 2;
                bool leftSlot = (i % 2) == 0;
                float d = sampler.WrapDistance(
                    length - gridStartOffset - row * gridRowSpacing - (leftSlot ? 0f : gridStaggerOffset));
                TrackSplineSampler.Sample s = sampler.AtDistance(d);
                float side = leftSlot ? -2.5f : 2.5f;
                asset.gridSlots.Add(new TrackDefinitionAsset.GridSlot
                {
                    position = s.Position + s.Normal * side,
                    headingDegrees = Quaternion.LookRotation(s.Tangent, Vector3.up).eulerAngles.y,
                });
            }

            // A handful of broadcast camera nodes + marshal posts + crowd zones.
            for (int i = 0; i < 8; i++)
            {
                float frac = i / 8f;
                TrackSplineSampler.Sample s = sampler.AtDistance(frac * length);
                asset.cameraNodes.Add(new TrackDefinitionAsset.TrackCameraNode
                {
                    position = s.Position + s.Normal * 40f + Vector3.up * 12f,
                    coverageStartDistance = frac * length,
                    coverageEndDistance = (frac + 0.125f) * length,
                });
                asset.marshalPosts.Add(new TrackDefinitionAsset.MarshalPost
                {
                    position = s.Position - s.Normal * (s.Width * 0.5f + 4f),
                    sectorStartDistance = frac * length,
                    sectorEndDistance = (frac + 0.125f) * length,
                });
                asset.crowdZones.Add(new TrackDefinitionAsset.CrowdZone
                {
                    position = s.Position + s.Normal * 55f,
                    size = new Vector3(60f, 12f, 30f),
                    density = 0.8f,
                });
            }

            return asset;
        }
    }
}
