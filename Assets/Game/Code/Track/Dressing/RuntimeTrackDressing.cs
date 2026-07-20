using UnityEngine;

namespace F1Game.Track.Dressing
{
    /// <summary>
    /// Runtime entry point that automatically dresses a freshly-built track world.
    /// Called once at the tail of the track build (see the TrackManager build
    /// path). It requires no scene wiring, no manual profile selection and no
    /// "Regenerate" click.
    ///
    /// Safety contract (why this can be merged before the art is generated, and
    /// why it can never break a race):
    ///   * If the <see cref="CircuitProfileCatalog"/> Resource does not exist yet
    ///     (art not generated/committed) → NO-OP.
    ///   * If the definition is null or no profile matches → NO-OP.
    ///   * Everything is wrapped in try/catch — a failure logs a warning and the
    ///     race continues undressed rather than crashing.
    ///   * Colliders on the dressing are DISABLED (visual-only): the legacy
    ///     TrackManager barriers remain the sole gameplay collision, so the
    ///     dressing can never create an invisible wall, block the racing line, or
    ///     break pit entry/exit.
    ///   * Rebuilds are idempotent: <see cref="TrackDressingBuilder"/> destroys the
    ///     previous dressing root (preserving manual "Overrides") before rebuilding,
    ///     so a race restart or track reload never doubles the dressing.
    /// </summary>
    public static class RuntimeTrackDressing
    {
        static bool cachedCatalogLookup;
        static CircuitProfileCatalog cachedCatalog;

        /// <summary>Load (and cache) the profile catalog, or null if not present.</summary>
        public static CircuitProfileCatalog Catalog
        {
            get
            {
                if (!cachedCatalogLookup)
                {
                    cachedCatalogLookup = true;
                    cachedCatalog = Resources.Load<CircuitProfileCatalog>(
                        CircuitProfileCatalog.ResourcePath);
                }
                return cachedCatalog;
            }
        }

        /// <summary>Forget the cached catalog (editor/test use after (re)generation).</summary>
        public static void InvalidateCache()
        {
            cachedCatalogLookup = false;
            cachedCatalog = null;
        }

        /// <summary>
        /// Dress the track under <paramref name="trackRoot"/> from its definition.
        /// Returns the number of objects placed (0 = no-op or nothing placed).
        /// Never throws.
        /// </summary>
        public static int TryDress(Transform trackRoot, TrackDefinitionAsset definition,
                                   string trackId, int seed = 20240720)
        {
            try
            {
                if (trackRoot == null || definition == null)
                {
                    return 0;
                }
                CircuitProfileCatalog catalog = Catalog;
                if (catalog == null)
                {
                    // Art not generated/committed yet — silent, expected no-op.
                    return 0;
                }
                CircuitVisualProfile profile = catalog.Select(trackId, definition.environmentStyle);
                if (profile == null)
                {
                    Debug.Log("[RuntimeDressing] No profile matched circuit '" + trackId +
                              "' (style '" + definition.environmentStyle + "') — undressed.");
                    return 0;
                }

                TrackDressingBuilder.Result result =
                    TrackDressingBuilder.Build(definition, profile, trackRoot, seed);

                if (result.Root != null)
                {
                    MakeVisualOnly(result.Root);
                }

                Debug.Log("[RuntimeDressing] " + trackId + " -> profile '" + profile.profileId +
                          "': placed " + result.Placed + " objects (barriers " + result.Barriers +
                          ", kerbs " + result.Kerbs + ", fences " + result.Fences +
                          ", structures " + result.Structures + ", veg " + result.Vegetation + ").");
                return result.Placed;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RuntimeDressing] Dressing failed (race continues undressed): " + e);
                return 0;
            }
        }

        /// <summary>
        /// Disable every collider under the dressing so it is purely presentational
        /// and cannot interfere with gameplay physics. Objects under an "Overrides"
        /// subtree are left untouched (a designer may have added real collision).
        /// </summary>
        static void MakeVisualOnly(GameObject dressingRoot)
        {
            foreach (var col in dressingRoot.GetComponentsInChildren<Collider>(true))
            {
                if (IsUnderOverrides(col.transform)) continue;
                col.enabled = false;
            }
        }

        static bool IsUnderOverrides(Transform t)
        {
            while (t != null)
            {
                if (t.name == TrackDressingBuilder.OverridesName) return true;
                t = t.parent;
            }
            return false;
        }
    }
}
