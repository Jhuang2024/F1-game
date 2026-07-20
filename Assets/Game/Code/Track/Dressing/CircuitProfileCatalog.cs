using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track.Dressing
{
    /// <summary>
    /// Runtime registry that maps a circuit (by <see cref="TrackDefinitionAsset.environmentStyle"/>
    /// and/or track id) to a <see cref="CircuitVisualProfile"/>. Lives under a
    /// Resources folder so the runtime dressing hook can load it with no scene
    /// wiring:
    ///
    ///     Resources.Load&lt;CircuitProfileCatalog&gt;(CircuitProfileCatalog.ResourcePath)
    ///
    /// The catalog + its profiles are generated in batch mode by
    /// <c>AutomatedArtIntegration.Run</c> (GitHub Actions). Until that has run and
    /// committed the assets, the catalog does not exist and the runtime hook is a
    /// safe no-op — so merging the hook never changes current gameplay.
    /// </summary>
    public sealed class CircuitProfileCatalog : ScriptableObject
    {
        public const string ResourcePath = "ArtPipeline/CircuitProfileCatalog";

        [System.Serializable]
        public struct Mapping
        {
            [Tooltip("Substrings matched against a circuit's trackId (highest priority).")]
            public string[] trackIdKeywords;
            [Tooltip("Substrings matched against environmentStyle.")]
            public string[] styleKeywords;
            public CircuitVisualProfile profile;
        }

        [Tooltip("Fallback profile used when no mapping matches.")]
        public CircuitVisualProfile defaultProfile;

        [Tooltip("Ordered mappings; first match wins (trackId keywords beat style keywords).")]
        public List<Mapping> mappings = new List<Mapping>();

        /// <summary>Choose a profile for a circuit. Never throws; may return null.</summary>
        public CircuitVisualProfile Select(string trackId, string environmentStyle)
        {
            string id = (trackId ?? string.Empty).ToLowerInvariant();
            string style = (environmentStyle ?? string.Empty).ToLowerInvariant();

            // Pass 1: trackId keyword (most specific).
            foreach (var m in mappings)
            {
                if (m.profile == null || m.trackIdKeywords == null) continue;
                foreach (var kw in m.trackIdKeywords)
                {
                    if (!string.IsNullOrEmpty(kw) && id.Contains(kw.ToLowerInvariant()))
                    {
                        return m.profile;
                    }
                }
            }
            // Pass 2: style keyword.
            foreach (var m in mappings)
            {
                if (m.profile == null || m.styleKeywords == null) continue;
                foreach (var kw in m.styleKeywords)
                {
                    if (!string.IsNullOrEmpty(kw) && style.Contains(kw.ToLowerInvariant()))
                    {
                        return m.profile;
                    }
                }
            }
            return defaultProfile;
        }
    }
}
