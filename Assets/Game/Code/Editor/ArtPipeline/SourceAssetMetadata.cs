using UnityEngine;

namespace F1Game.Editor.ArtPipeline
{
    /// <summary>
    /// Authoring-time provenance record connecting a processed Unity art asset
    /// back to its source and licence (mirrors an entry in
    /// Assets/ThirdParty/asset_manifest.json). Editor-only metadata — it is not
    /// referenced by gameplay and carries no runtime cost.
    ///
    /// Created automatically by <see cref="KitAssetImporter"/> when a GLB is
    /// assembled into a prefab. Every art asset in the project must be able to
    /// point at one of these; an asset with no metadata is flagged by the
    /// validation pass (F1 Game → Art → Validate Art Provenance).
    /// </summary>
    public sealed class SourceAssetMetadata : ScriptableObject
    {
        [Header("Identity")]
        public string assetId;
        public string assetName;

        [Header("Provenance")]
        [Tooltip("generated-blender | generated-texture | downloaded")]
        public string provider;
        public string creator;
        public string sourcePage;
        public string downloadDate;

        [Header("Licence")]
        public string license = "CC0-1.0 (project-owned original)";
        public string licenseUrl = "https://creativecommons.org/publicdomain/zero/1.0/";
        public bool attributionRequired;
        public bool commercialUse = true;

        [Header("Technical")]
        public string sourceFormat = "GLB (glTF 2.0)";
        public int polycountLod0;
        public int generatedLods;
        [Tooltip("Repo-relative source asset this metadata describes.")]
        public string unityDestination;
        public string sha256;
        public bool aiGenerated;

        [TextArea]
        public string modifications;
        [TextArea]
        public string notices;

        /// <summary>True when the record has enough to establish source + licence.</summary>
        public bool IsComplete()
        {
            return !string.IsNullOrEmpty(assetName)
                && !string.IsNullOrEmpty(provider)
                && !string.IsNullOrEmpty(license)
                && !string.IsNullOrEmpty(unityDestination);
        }
    }
}
