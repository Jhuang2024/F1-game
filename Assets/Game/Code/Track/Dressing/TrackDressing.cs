using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track.Dressing
{
    /// <summary>
    /// Scene component that drives <see cref="TrackDressingBuilder"/> for one
    /// circuit. Attach to an (otherwise empty) child of the track world; assign a
    /// definition + profile; call <see cref="Regenerate"/> (also on the context
    /// menu). Nothing here touches gameplay — it only spawns presentation objects
    /// as children of this transform.
    ///
    /// Manual overrides survive regeneration: objects under a child named
    /// "Overrides", and any object carrying <see cref="DressingOverrideMarker"/>,
    /// are rescued across the rebuild.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrackDressing : MonoBehaviour
    {
        [Tooltip("Authored circuit this dressing decorates (read-only; never mutated).")]
        public TrackDefinitionAsset definition;

        [Tooltip("Visual profile (green permanent, desert, street, night, …).")]
        public CircuitVisualProfile profile;

        [Tooltip("Deterministic seed — same seed reproduces the exact same layout.")]
        public int seed = 12345;

        [Tooltip("Distance ranges (metres) to skip on a given edge — gates, access roads, etc.")]
        public List<TrackDressingBuilder.ExclusionZone> exclusionZones =
            new List<TrackDressingBuilder.ExclusionZone>();

        [Header("Last build (read-only)")]
        public int lastPlaced;
        [TextArea] public string lastWarnings;

        [ContextMenu("Regenerate")]
        public void Regenerate()
        {
            if (definition == null || profile == null)
            {
                Debug.LogWarning("[TrackDressing] Assign both a definition and a profile first.");
                return;
            }

            // Rescue marker-carrying overrides that are not already under "Overrides".
            var rescued = new List<Transform>();
            foreach (var marker in GetComponentsInChildren<DressingOverrideMarker>(true))
            {
                rescued.Add(marker.transform);
            }
            foreach (var t in rescued) t.SetParent(transform, true);

            var result = TrackDressingBuilder.Build(definition, profile, transform, seed, exclusionZones);

            lastPlaced = result.Placed;
            lastWarnings = result.Warnings.Count == 0 ? "none" : string.Join("\n", result.Warnings);
            Debug.Log($"[TrackDressing] {definition.trackId}: placed {result.Placed} objects " +
                      $"(barriers {result.Barriers}, kerbs {result.Kerbs}, fences {result.Fences}, " +
                      $"structures {result.Structures}, veg {result.Vegetation}). " +
                      $"{result.Warnings.Count} warning(s).");
        }

        [ContextMenu("Clear (keep overrides)")]
        public void Clear()
        {
            TrackDressingBuilder.Clear(transform);
            lastPlaced = 0;
        }
    }
}
