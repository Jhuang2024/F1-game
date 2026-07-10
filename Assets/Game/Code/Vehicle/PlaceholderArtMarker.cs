using UnityEngine;

namespace F1Game.Vehicle
{
    /// <summary>
    /// Explicit marker for placeholder content. Anything carrying this component
    /// is NOT final art: it holds a slot (correct hierarchy, pivots, material
    /// slots) until a real asset replaces it. The content validator fails a
    /// "release" check while any marker remains in a shipped prefab.
    /// </summary>
    public class PlaceholderArtMarker : MonoBehaviour
    {
        [Tooltip("What real asset this placeholder stands in for.")]
        public string expectedAsset;

        [Tooltip("Import/spec reference for the real asset.")]
        public string specReference = "Docs/ART_PIPELINE.md";
    }
}
