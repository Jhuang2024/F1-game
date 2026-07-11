using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track
{
    /// <summary>
    /// Authored track data for the production track pipeline. This is the asset
    /// the reference track migrates into: spline, widths, camber/elevation,
    /// kerbs, surfaces, grid, sectors, pit lane, DRS, AI lines, marshal posts,
    /// camera nodes, crowd zones and lighting — data that today lives implicit
    /// inside TrackManager's 11k-line builder.
    ///
    /// Migration path (Docs/TRACK_PIPELINE.md): the editor exporter samples a
    /// TrackManager runtime build into one of these assets; the runtime then
    /// builds FROM the asset, making the mesh builder a pure function of
    /// authored data. Only the reference track migrates until this proves out.
    /// </summary>
    [CreateAssetMenu(menuName = "F1 Game/Track/Track Definition", fileName = "Track_New")]
    public class TrackDefinitionAsset : ScriptableObject
    {
        [Serializable]
        public struct SplinePoint
        {
            public Vector3 position;       // includes elevation
            public float width;            // full road width at this point
            public float camberDegrees;    // positive = banked toward inside
            public bool kerbLeft;
            public bool kerbRight;
        }

        [Serializable]
        public struct SurfaceZone
        {
            public float startDistance;
            public float endDistance;
            public SurfaceKind kind;
            public float gripMultiplier;
        }

        public enum SurfaceKind { Asphalt, RubberedLine, Kerb, Grass, Gravel, Concrete, PitLane }

        [Serializable]
        public struct DrsZone
        {
            public float detectionDistance;
            public float activationDistance;
            public float endDistance;
        }

        [Serializable]
        public struct PitLaneData
        {
            public float entryDistance;
            public float exitDistance;
            public float entryCommitDistance;   // limiter line
            public int stallCount;
            public Vector3[] stallPositions;
        }

        [Serializable]
        public struct GridSlot
        {
            public Vector3 position;
            public float headingDegrees;
        }

        [Serializable]
        public struct TrackCameraNode
        {
            public Vector3 position;
            public float coverageStartDistance;
            public float coverageEndDistance;
        }

        [Serializable]
        public struct MarshalPost
        {
            public Vector3 position;
            public float sectorStartDistance;
            public float sectorEndDistance;
        }

        [Serializable]
        public struct CrowdZone
        {
            public Vector3 position;
            public Vector3 size;
            public float density;
        }

        [Header("Identity")]
        public string trackId;
        public string displayName;
        public string country;
        [Tooltip("Environment style keywords the world decorator reads (e.g. contains 'street' or 'coastal'). Empty = neutral authored default.")]
        public string environmentStyle = "";

        [Header("Geometry (authored)")]
        public List<SplinePoint> spline = new List<SplinePoint>();
        public bool closedLoop = true;
        [Tooltip("Lateral distance from centerline where trackside kerbs begin (0 = derive from width).")]
        public float kerbStartOffset;
        [Tooltip("Catmull-Rom subdivisions per anchor segment when the world builder smooths this spline (0 = builder default). Converted legacy circuits keep their original smoothing density here.")]
        public int anchorSubdivisions;

        [Header("Timing")]
        public float[] sectorBoundaryDistances = new float[2];
        public float startFinishDistance;

        [Header("Racing data")]
        public List<SurfaceZone> surfaces = new List<SurfaceZone>();
        public List<DrsZone> drsZones = new List<DrsZone>();
        public PitLaneData pitLane;
        public List<GridSlot> gridSlots = new List<GridSlot>();
        [Tooltip("Lateral offset (-1..1 across the width) of the AI racing line per spline point.")]
        public List<float> racingLineOffsets = new List<float>();

        [Header("Broadcast / environment")]
        public List<TrackCameraNode> cameraNodes = new List<TrackCameraNode>();
        public List<MarshalPost> marshalPosts = new List<MarshalPost>();
        public List<CrowdZone> crowdZones = new List<CrowdZone>();

        [Header("Lighting / weather")]
        [Tooltip("Default lighting mood resource name (Resources/LightingMoods).")]
        public string defaultLightingMood = "Mood_Clear";
        public bool supportsNightSession;

        /// <summary>Total spline length in metres (approximate, from point spacing).</summary>
        public float ComputeLength()
        {
            float length = 0f;
            for (int i = 1; i < spline.Count; i++)
            {
                length += Vector3.Distance(spline[i - 1].position, spline[i].position);
            }

            if (closedLoop && spline.Count > 1)
            {
                length += Vector3.Distance(spline[spline.Count - 1].position, spline[0].position);
            }

            return length;
        }

        /// <summary>Cheap authoring validation used by the editor tool and tests.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (spline.Count < 16)
            {
                problems.Add("Spline has fewer than 16 points.");
            }

            for (int i = 0; i < spline.Count; i++)
            {
                if (spline[i].width < 8f)
                {
                    problems.Add("Point " + i + " width below 8 m minimum.");
                    break;
                }
            }

            if (racingLineOffsets.Count != 0 && racingLineOffsets.Count != spline.Count)
            {
                problems.Add("Racing line offset count does not match spline point count.");
            }

            if (gridSlots.Count < 22)
            {
                problems.Add("Fewer than 22 grid slots.");
            }

            if (sectorBoundaryDistances == null || sectorBoundaryDistances.Length != 2)
            {
                problems.Add("Expected exactly 2 sector boundaries (3 sectors).");
            }

            return problems;
        }
    }
}
