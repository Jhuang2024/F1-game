using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track
{
    /// <summary>Progress of a point relative to the authored centerline.</summary>
    public struct TrackProgressSample
    {
        public float Distance;       // along the lap
        public float Normalized;     // 0..1
        public float LateralOffset;  // signed metres from centerline (+ = right)
        public Vector3 CenterPosition;
        public Vector3 Tangent;
    }

    /// <summary>Surface classification queries over the authored surface zones.</summary>
    public sealed class TrackSurfaceRuntime
    {
        readonly List<TrackDefinitionAsset.SurfaceZone> zones;

        public TrackSurfaceRuntime(List<TrackDefinitionAsset.SurfaceZone> zones)
        {
            this.zones = zones ?? new List<TrackDefinitionAsset.SurfaceZone>();
        }

        public TrackDefinitionAsset.SurfaceKind KindAt(float distance)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (distance >= zones[i].startDistance && distance <= zones[i].endDistance)
                {
                    return zones[i].kind;
                }
            }

            return TrackDefinitionAsset.SurfaceKind.Asphalt;
        }

        public float GripMultiplierAt(float distance)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (distance >= zones[i].startDistance && distance <= zones[i].endDistance)
                {
                    return zones[i].gripMultiplier <= 0f ? 1f : zones[i].gripMultiplier;
                }
            }

            return 1f;
        }
    }

    /// <summary>Track-limits queries: on-road / off-track / white-line boundary.</summary>
    public sealed class TrackLimitRuntime
    {
        readonly TrackSplineSampler sampler;

        public TrackLimitRuntime(TrackSplineSampler sampler)
        {
            this.sampler = sampler;
        }

        public float HalfWidthAt(float distance) => sampler.AtDistance(distance).Width * 0.5f;

        public bool IsOnRoad(in TrackProgressSample progress)
        {
            float halfWidth = sampler.AtDistance(progress.Distance).Width * 0.5f;
            return Mathf.Abs(progress.LateralOffset) <= halfWidth;
        }

        public bool IsBeyondWhiteLine(in TrackProgressSample progress, float marginMeters)
        {
            float halfWidth = sampler.AtDistance(progress.Distance).Width * 0.5f;
            return Mathf.Abs(progress.LateralOffset) > halfWidth + marginMeters;
        }
    }

    /// <summary>Pit-lane geometry queries (entry/exit windows, limiter line).</summary>
    public sealed class PitLaneRuntime
    {
        readonly TrackDefinitionAsset.PitLaneData data;
        readonly float length;

        public PitLaneRuntime(TrackDefinitionAsset.PitLaneData data, float lapLength)
        {
            this.data = data;
            length = lapLength;
        }

        public int StallCount => data.stallCount;

        public bool IsInEntryWindow(float distance) => Between(distance, data.entryDistance, data.entryCommitDistance);
        public bool HasCrossedCommitLine(float distance) => Between(distance, data.entryCommitDistance, data.exitDistance);
        public bool IsInExitWindow(float distance) => Between(distance, data.exitDistance - 20f, data.exitDistance);

        public Vector3 StallPosition(int index)
        {
            if (data.stallPositions == null || data.stallPositions.Length == 0)
            {
                return Vector3.zero;
            }

            return data.stallPositions[Mathf.Clamp(index, 0, data.stallPositions.Length - 1)];
        }

        bool Between(float distance, float start, float end)
        {
            if (length <= 0f)
            {
                return distance >= start && distance <= end;
            }

            // Handle wrap-around windows on a closed lap.
            if (start <= end)
            {
                return distance >= start && distance <= end;
            }

            return distance >= start || distance <= end;
        }
    }

    /// <summary>DRS zone queries (detection + activation).</summary>
    public sealed class DrsRuntime
    {
        readonly List<TrackDefinitionAsset.DrsZone> zones;

        public DrsRuntime(List<TrackDefinitionAsset.DrsZone> zones)
        {
            this.zones = zones ?? new List<TrackDefinitionAsset.DrsZone>();
        }

        public int ZoneCount => zones.Count;

        public int ZoneIndexAt(float distance)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (distance >= zones[i].activationDistance && distance <= zones[i].endDistance)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool IsInActivationZone(float distance) => ZoneIndexAt(distance) >= 0;

        public bool CrossedDetection(float previousDistance, float distance)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                float d = zones[i].detectionDistance;
                if (previousDistance < d && distance >= d)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Sector membership + timing-line queries.</summary>
    public sealed class SectorRuntime
    {
        readonly float[] boundaries;
        readonly float length;

        public SectorRuntime(float[] boundaries, float lapLength)
        {
            this.boundaries = boundaries ?? new float[0];
            length = lapLength;
        }

        public int SectorAt(float distance)
        {
            for (int i = 0; i < boundaries.Length; i++)
            {
                if (distance < boundaries[i])
                {
                    return i;
                }
            }

            return boundaries.Length; // final sector
        }

        public int SectorCount => boundaries.Length + 1;
    }

    /// <summary>Grid-slot queries.</summary>
    public sealed class GridRuntime
    {
        readonly List<TrackDefinitionAsset.GridSlot> slots;

        public GridRuntime(List<TrackDefinitionAsset.GridSlot> slots)
        {
            this.slots = slots ?? new List<TrackDefinitionAsset.GridSlot>();
        }

        public int SlotCount => slots.Count;

        public bool TryGetSlot(int index, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (index < 0 || index >= slots.Count)
            {
                return false;
            }

            position = slots[index].position;
            rotation = Quaternion.Euler(0f, slots[index].headingDegrees, 0f);
            return true;
        }
    }

    /// <summary>AI racing-line queries (lateral offset per distance).</summary>
    public sealed class AiLineRuntime
    {
        readonly TrackSplineSampler sampler;
        readonly List<float> offsets;

        public AiLineRuntime(TrackSplineSampler sampler, List<float> racingLineOffsets)
        {
            this.sampler = sampler;
            offsets = racingLineOffsets ?? new List<float>();
        }

        /// <summary>Racing-line world point at a lap distance (offset across the width).</summary>
        public Vector3 RacingLinePoint(float distance)
        {
            TrackSplineSampler.Sample s = sampler.AtDistance(distance);
            float offset01 = SampleOffset(distance);
            return s.Position + s.Normal * (offset01 * s.Width * 0.5f);
        }

        float SampleOffset(float distance)
        {
            if (offsets.Count == 0 || sampler.Length <= 0f)
            {
                return 0f;
            }

            float t = sampler.WrapDistance(distance) / sampler.Length;
            int index = Mathf.Clamp(Mathf.RoundToInt(t * (offsets.Count - 1)), 0, offsets.Count - 1);
            return Mathf.Clamp(offsets[index], -1f, 1f);
        }
    }

    /// <summary>Broadcast camera-node queries.</summary>
    public sealed class TrackCameraRuntime
    {
        readonly List<TrackDefinitionAsset.TrackCameraNode> nodes;

        public TrackCameraRuntime(List<TrackDefinitionAsset.TrackCameraNode> nodes)
        {
            this.nodes = nodes ?? new List<TrackDefinitionAsset.TrackCameraNode>();
        }

        public int NodeCount => nodes.Count;

        /// <summary>The camera node whose coverage window contains the distance, or -1.</summary>
        public int NodeCovering(float distance)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (distance >= nodes[i].coverageStartDistance && distance <= nodes[i].coverageEndDistance)
                {
                    return i;
                }
            }

            return -1;
        }

        public Vector3 NodePosition(int index)
        {
            return index >= 0 && index < nodes.Count ? nodes[index].position : Vector3.zero;
        }
    }

    /// <summary>
    /// Composed authored-track runtime: the sampled centerline plus every query
    /// service. This mirrors the query surface the legacy TrackRuntime exposes so
    /// a future RaceManager query-interface migration can swap to it. It is a
    /// pure data/query object — the world GameObject is built by TrackRuntimeBuilder.
    /// </summary>
    public sealed class AuthoredTrackRuntime
    {
        public TrackDefinitionAsset Definition { get; }
        public TrackSplineSampler Sampler { get; }
        public TrackSurfaceRuntime Surface { get; }
        public TrackLimitRuntime Limits { get; }
        public PitLaneRuntime PitLane { get; }
        public DrsRuntime Drs { get; }
        public SectorRuntime Sectors { get; }
        public GridRuntime Grid { get; }
        public AiLineRuntime AiLine { get; }
        public TrackCameraRuntime Cameras { get; }

        public float Length => Sampler.Length;

        public AuthoredTrackRuntime(TrackDefinitionAsset definition)
        {
            Definition = definition;
            Sampler = new TrackSplineSampler();
            Sampler.Build(definition.spline, definition.closedLoop);
            Surface = new TrackSurfaceRuntime(definition.surfaces);
            Limits = new TrackLimitRuntime(Sampler);
            PitLane = new PitLaneRuntime(definition.pitLane, Sampler.Length);
            Drs = new DrsRuntime(definition.drsZones);
            Sectors = new SectorRuntime(definition.sectorBoundaryDistances, Sampler.Length);
            Grid = new GridRuntime(definition.gridSlots);
            AiLine = new AiLineRuntime(Sampler, definition.racingLineOffsets);
            Cameras = new TrackCameraRuntime(definition.cameraNodes);
        }

        /// <summary>Progress of a world position relative to the centerline.</summary>
        public TrackProgressSample GetProgress(Vector3 worldPos)
        {
            float distance = Sampler.NearestDistance(worldPos);
            TrackSplineSampler.Sample s = Sampler.AtDistance(distance);
            Vector3 toPoint = worldPos - s.Position;
            float lateral = Vector3.Dot(toPoint, s.Normal);
            return new TrackProgressSample
            {
                Distance = distance,
                Normalized = Sampler.Length > 0f ? distance / Sampler.Length : 0f,
                LateralOffset = lateral,
                CenterPosition = s.Position,
                Tangent = s.Tangent,
            };
        }
    }
}
