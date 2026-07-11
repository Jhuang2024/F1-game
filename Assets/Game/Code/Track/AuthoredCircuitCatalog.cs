using UnityEngine;

namespace F1Game.Track
{
    /// <summary>
    /// Authored circuit definitions by trackId. Each entry emits a
    /// TrackDefinitionAsset deterministically in code (the same way the
    /// reference circuit does) until hand-tuned assets replace them; the race
    /// layer's authored build branch consumes whatever this returns, so a
    /// circuit converts to the authored pipeline by moving its geometry here
    /// and retiring its legacy Build*Layout method. The catalog is the single
    /// source for a converted circuit's layout.
    /// </summary>
    public static class AuthoredCircuitCatalog
    {
        public const string MonzaTrackId = "monza_low_downforce";

        public static bool Contains(string trackId)
        {
            return trackId == ReferenceTrackGenerator.ReferenceTrackId
                || trackId == MonzaTrackId;
        }

        /// <summary>Definition for an authored circuit, or null when the id is not authored.</summary>
        public static TrackDefinitionAsset Generate(string trackId)
        {
            if (trackId == ReferenceTrackGenerator.ReferenceTrackId)
            {
                return ReferenceTrackGenerator.Generate();
            }

            if (trackId == MonzaTrackId)
            {
                return GenerateMonza();
            }

            return null;
        }

        // The low-downforce park circuit, converted from the legacy
        // hand-sketched anchor layout (TrackManager.BuildMonzaLayout, retired
        // with this conversion) to a real-scale authored spline. The sketch is
        // scaled so the smoothed lap lands on the same length band the legacy
        // NormalizeTrackLength pass produced for this circuit, so lap times
        // stay in the tuned range.
        static TrackDefinitionAsset GenerateMonza()
        {
            // Legacy anchor sketch, verbatim.
            Vector3[] sketch =
            {
                new Vector3(0f, 0f, 0f), new Vector3(230f, 0f, 0f), new Vector3(272f, 0f, 26f),
                new Vector3(246f, 0f, 58f), new Vector3(194f, 0f, 48f), new Vector3(238f, 0f, 92f),
                new Vector3(252f, 0f, 148f), new Vector3(196f, 0f, 184f), new Vector3(92f, 0f, 190f),
                new Vector3(20f, 0f, 164f), new Vector3(-42f, 0f, 174f), new Vector3(-86f, 0f, 132f),
                new Vector3(-48f, 0f, 86f), new Vector3(62f, 0f, 76f), new Vector3(112f, 0f, 42f),
                new Vector3(74f, 0f, 14f), new Vector3(-210f, 0f, 0f)
            };

            // Same band the legacy target used for this circuit (5300 m base
            // scaled by the car-speed rebalance) - authored data owns the real
            // number outright.
            const float TargetLengthMeters = 8281f;
            const float FullRoadWidthMeters = 31.96f; // legacy 15.98 half width

            float sketchLength = 0f;
            for (int i = 0; i < sketch.Length; i++)
            {
                sketchLength += Vector3.Distance(sketch[i], sketch[(i + 1) % sketch.Length]);
            }

            float scale = sketchLength > 1f ? TargetLengthMeters / sketchLength : 1f;

            var asset = ScriptableObject.CreateInstance<TrackDefinitionAsset>();
            asset.name = "Track_MonzaLowDownforce_Authored";
            asset.trackId = MonzaTrackId;
            asset.displayName = "Italy-style Speed GP";
            asset.country = "Italy-inspired";
            asset.closedLoop = true;

            for (int i = 0; i < sketch.Length; i++)
            {
                asset.spline.Add(new TrackDefinitionAsset.SplinePoint
                {
                    position = new Vector3(sketch[i].x * scale, sketch[i].y, sketch[i].z * scale),
                    width = FullRoadWidthMeters,
                    camberDegrees = 0f,
                    kerbLeft = false,
                    kerbRight = false,
                });
            }

            float length = asset.ComputeLength();
            asset.startFinishDistance = 0f;
            asset.sectorBoundaryDistances = new[] { length / 3f, length * 2f / 3f };

            // Neutral racing-line offsets: the runtime computes its own line
            // from geometry either way; these exist for the authored query path.
            for (int i = 0; i < sketch.Length; i++)
            {
                asset.racingLineOffsets.Add(0f);
            }

            asset.surfaces.Add(new TrackDefinitionAsset.SurfaceZone
            {
                startDistance = 0f,
                endDistance = length,
                kind = TrackDefinitionAsset.SurfaceKind.RubberedLine,
                gripMultiplier = 1f,
            });

            // The legacy layout's DRS zones, converted from its normalized
            // (start, end) pairs to authored metre distances; detection sits a
            // short run before each activation the same way ValidateLayout
            // derives it.
            asset.drsZones.Add(new TrackDefinitionAsset.DrsZone
            {
                detectionDistance = length * 0.84f,
                activationDistance = length * 0.88f,
                endDistance = length * 0.08f,
            });
            asset.drsZones.Add(new TrackDefinitionAsset.DrsZone
            {
                detectionDistance = length * 0.40f,
                activationDistance = length * 0.44f,
                endDistance = length * 0.62f,
            });

            var sampler = new TrackSplineSampler();
            sampler.Build(asset.spline, true);

            var stalls = new System.Collections.Generic.List<Vector3>();
            TrackSplineSampler.Sample pitAnchor = sampler.AtDistance(0f);
            for (int i = 0; i < 22; i++)
            {
                stalls.Add(pitAnchor.Position - pitAnchor.Normal * 20f + pitAnchor.Tangent * (i * 8f - 88f));
            }

            asset.pitLane = new TrackDefinitionAsset.PitLaneData
            {
                entryDistance = length * 0.94f,
                entryCommitDistance = length * 0.965f,
                exitDistance = length * 0.05f,
                stallCount = 22,
                stallPositions = stalls.ToArray(),
            };

            for (int i = 0; i < 22; i++)
            {
                TrackSplineSampler.Sample s = sampler.AtDistance(30f + i * 8f);
                float side = (i % 2 == 0) ? -2.5f : 2.5f;
                asset.gridSlots.Add(new TrackDefinitionAsset.GridSlot
                {
                    position = s.Position + s.Normal * side,
                    headingDegrees = Quaternion.LookRotation(s.Tangent, Vector3.up).eulerAngles.y,
                });
            }

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
