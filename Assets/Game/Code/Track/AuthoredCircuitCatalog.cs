using System.Collections.Generic;
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
    ///
    /// Converted circuits use LegacyCircuitSpec: the legacy anchor sketch
    /// verbatim, scaled outright to the real length band the legacy
    /// NormalizeTrackLength pass produced (elevation scaled by the same
    /// gentle scale^0.55 that pass used), with the legacy width, kerb inset,
    /// environment style and DRS zones carried over as authored data.
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
                return GenerateFromSpec(MonzaSpec());
            }

            return null;
        }

        // ---- Legacy-sketch conversion ---------------------------------------

        public struct LegacyCircuitSpec
        {
            public string TrackId;
            public string DisplayName;
            public string Country;
            public string EnvironmentStyle;
            public float HalfWidthMeters;
            public float KerbStartMeters;
            public Vector2 DrsZoneOneNormalized;   // (start, end), wrap allowed
            public Vector2 DrsZoneTwoNormalized;
            public float TargetLengthMeters;
            public Vector3[] SketchAnchors;
        }

        static LegacyCircuitSpec MonzaSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = MonzaTrackId,
                DisplayName = "Italy-style Speed GP",
                Country = "Italy-inspired",
                EnvironmentStyle = "Low-downforce park",
                HalfWidthMeters = 15.98f,
                KerbStartMeters = 9.4f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.44f, 0.62f),
                // 5300 m legacy base band x 1.5625 car-speed rebalance.
                TargetLengthMeters = 8281f,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(230f, 0f, 0f), new Vector3(272f, 0f, 26f),
                    new Vector3(246f, 0f, 58f), new Vector3(194f, 0f, 48f), new Vector3(238f, 0f, 92f),
                    new Vector3(252f, 0f, 148f), new Vector3(196f, 0f, 184f), new Vector3(92f, 0f, 190f),
                    new Vector3(20f, 0f, 164f), new Vector3(-42f, 0f, 174f), new Vector3(-86f, 0f, 132f),
                    new Vector3(-48f, 0f, 86f), new Vector3(62f, 0f, 76f), new Vector3(112f, 0f, 42f),
                    new Vector3(74f, 0f, 14f), new Vector3(-210f, 0f, 0f)
                },
            };
        }

        static TrackDefinitionAsset GenerateFromSpec(in LegacyCircuitSpec spec)
        {
            Vector3[] sketch = spec.SketchAnchors;
            float sketchLength = 0f;
            for (int i = 0; i < sketch.Length; i++)
            {
                sketchLength += Vector3.Distance(sketch[i], sketch[(i + 1) % sketch.Length]);
            }

            float scale = sketchLength > 1f ? spec.TargetLengthMeters / sketchLength : 1f;
            // Same gentle elevation treatment the legacy normalize pass applied.
            float elevationScale = Mathf.Pow(scale, 0.55f);

            var asset = ScriptableObject.CreateInstance<TrackDefinitionAsset>();
            asset.name = "Track_" + spec.TrackId + "_Authored";
            asset.trackId = spec.TrackId;
            asset.displayName = spec.DisplayName;
            asset.country = spec.Country;
            asset.environmentStyle = spec.EnvironmentStyle;
            asset.closedLoop = true;
            asset.kerbStartOffset = spec.KerbStartMeters;

            for (int i = 0; i < sketch.Length; i++)
            {
                asset.spline.Add(new TrackDefinitionAsset.SplinePoint
                {
                    position = new Vector3(sketch[i].x * scale, sketch[i].y * elevationScale, sketch[i].z * scale),
                    width = spec.HalfWidthMeters * 2f,
                    camberDegrees = 0f,
                    kerbLeft = false,
                    kerbRight = false,
                });
                asset.racingLineOffsets.Add(0f);
            }

            float length = asset.ComputeLength();
            asset.startFinishDistance = 0f;
            asset.sectorBoundaryDistances = new[] { length / 3f, length * 2f / 3f };

            asset.surfaces.Add(new TrackDefinitionAsset.SurfaceZone
            {
                startDistance = 0f,
                endDistance = length,
                kind = TrackDefinitionAsset.SurfaceKind.RubberedLine,
                gripMultiplier = 1f,
            });

            AddDrsZone(asset, spec.DrsZoneOneNormalized, length);
            AddDrsZone(asset, spec.DrsZoneTwoNormalized, length);

            var sampler = new TrackSplineSampler();
            sampler.Build(asset.spline, true);

            var stalls = new List<Vector3>();
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

        static void AddDrsZone(TrackDefinitionAsset asset, Vector2 normalizedStartEnd, float length)
        {
            if (normalizedStartEnd == Vector2.zero || length <= 1f)
            {
                return;
            }

            // Detection sits a short run before activation, the same distance
            // the legacy ValidateLayout derives.
            asset.drsZones.Add(new TrackDefinitionAsset.DrsZone
            {
                detectionDistance = Mathf.Repeat(normalizedStartEnd.x - 0.04f, 1f) * length,
                activationDistance = normalizedStartEnd.x * length,
                endDistance = normalizedStartEnd.y * length,
            });
        }
    }
}
