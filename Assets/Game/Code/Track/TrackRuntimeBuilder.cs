using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track
{
    /// <summary>Structured validation result for an authored track build.</summary>
    public sealed class TrackValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool IsValid => Errors.Count == 0;

        public string Summarize()
        {
            if (IsValid && Warnings.Count == 0)
            {
                return "Track valid.";
            }

            return $"Track validation: {Errors.Count} error(s), {Warnings.Count} warning(s).";
        }
    }

    /// <summary>
    /// Environment placement over authored crowd/environment data. Real modular
    /// environment kits are external assets; until then this places lightweight
    /// grandstand/crowd-zone markers so the authored data has a runtime effect.
    /// </summary>
    public sealed class TrackEnvironmentRuntime
    {
        public void Build(TrackDefinitionAsset definition, Transform parent)
        {
            if (definition.crowdZones == null)
            {
                return;
            }

            var root = new GameObject("Environment (placeholder)").transform;
            root.SetParent(parent, false);
            for (int i = 0; i < definition.crowdZones.Count; i++)
            {
                var marker = new GameObject("CrowdZone_" + i).transform;
                marker.SetParent(root, false);
                marker.position = definition.crowdZones[i].position;
                marker.localScale = definition.crowdZones[i].size;
            }
        }
    }

    /// <summary>
    /// Orchestrates building a live track world from a <see cref="TrackDefinitionAsset"/>:
    /// samples the spline, builds the road mesh + collider, places grid slots,
    /// pit stalls, camera nodes and environment markers, and returns the world
    /// root together with the query <see cref="AuthoredTrackRuntime"/>.
    ///
    /// This makes the authored asset a genuine runtime source (not merely an
    /// exporter target). The legacy procedural TrackManager remains the live
    /// race path until the RaceManager query interface is migrated onto
    /// AuthoredTrackRuntime (a Phase 3 monolith-split task); this builder is the
    /// destination and is exercised by the benchmark/reference-track path.
    /// </summary>
    public static class TrackRuntimeBuilder
    {
        public struct BuildResult
        {
            public GameObject Root;
            public AuthoredTrackRuntime Runtime;
            public TrackValidationReport Report;
        }

        public static BuildResult Build(TrackDefinitionAsset definition, Transform parent = null)
        {
            var report = new TrackValidationReport();
            if (definition == null)
            {
                report.Errors.Add("Null track definition.");
                return new BuildResult { Report = report };
            }

            foreach (string problem in definition.Validate())
            {
                report.Warnings.Add(problem);
            }

            var runtime = new AuthoredTrackRuntime(definition);
            if (runtime.Length <= 0f)
            {
                report.Errors.Add("Spline produced zero length (need >= 4 control points).");
                return new BuildResult { Runtime = runtime, Report = report };
            }

            var root = new GameObject("Authored Track: " + (string.IsNullOrEmpty(definition.displayName) ? definition.trackId : definition.displayName));
            if (parent != null)
            {
                root.transform.SetParent(parent, false);
            }

            TrackMeshBuilder.BuildRoad(runtime.Sampler, root.transform);
            PlaceMarkers(definition, runtime, root.transform);
            new TrackEnvironmentRuntime().Build(definition, root.transform);

            return new BuildResult { Root = root, Runtime = runtime, Report = report };
        }

        static void PlaceMarkers(TrackDefinitionAsset definition, AuthoredTrackRuntime runtime, Transform parent)
        {
            var grid = new GameObject("Grid").transform;
            grid.SetParent(parent, false);
            for (int i = 0; i < runtime.Grid.SlotCount; i++)
            {
                if (runtime.Grid.TryGetSlot(i, out Vector3 pos, out Quaternion rot))
                {
                    var slot = new GameObject("Grid_" + (i + 1)).transform;
                    slot.SetParent(grid, false);
                    slot.SetPositionAndRotation(pos, rot);
                }
            }

            if (definition.pitLane.stallPositions != null)
            {
                var pit = new GameObject("PitStalls").transform;
                pit.SetParent(parent, false);
                for (int i = 0; i < definition.pitLane.stallPositions.Length; i++)
                {
                    var stall = new GameObject("Stall_" + (i + 1)).transform;
                    stall.SetParent(pit, false);
                    stall.position = definition.pitLane.stallPositions[i];
                }
            }

            var cams = new GameObject("BroadcastCameras").transform;
            cams.SetParent(parent, false);
            for (int i = 0; i < runtime.Cameras.NodeCount; i++)
            {
                var node = new GameObject("Cam_" + i).transform;
                node.SetParent(cams, false);
                node.position = runtime.Cameras.NodePosition(i);
            }
        }
    }
}
