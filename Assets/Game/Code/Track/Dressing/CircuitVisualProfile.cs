using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track.Dressing
{
    /// <summary>
    /// A reusable visual identity for a class of circuits (green permanent,
    /// desert, street, night, coastal, …). Instead of authoring 24 independent
    /// asset sets, each circuit is mapped to a profile via its
    /// <see cref="TrackDefinitionAsset.environmentStyle"/> keyword, then may add
    /// per-circuit overrides.
    ///
    /// Prefab slots are assigned on the dev machine from the imported kit
    /// (Assets/Art/.../Prefabs/*.prefab — produced by KitAssetImporter). Null
    /// slots are tolerated: the dressing builder logs a warning and skips that
    /// element rather than throwing, so the system is usable before every slot
    /// is filled.
    /// </summary>
    [CreateAssetMenu(menuName = "F1 Game/Track/Circuit Visual Profile", fileName = "Profile_New")]
    public sealed class CircuitVisualProfile : ScriptableObject
    {
        [Serializable]
        public struct WeightedPrefab
        {
            public GameObject prefab;
            [Min(0f)] public float weight;
        }

        [Header("Identity")]
        public string profileId = "green_permanent";
        [Tooltip("Substrings matched against TrackDefinitionAsset.environmentStyle (any match selects this profile).")]
        public string[] matchKeywords = { "permanent", "green" };

        [Header("Barriers")]
        public GameObject barrierPrimary;      // e.g. Armco (module length below)
        public GameObject barrierSecondary;    // e.g. concrete for fast sections
        [Min(0.5f)] public float barrierModuleLength = 4f;
        [Tooltip("Lateral gap from the road edge to the barrier face, metres.")]
        public float barrierMargin = 3.5f;
        [Tooltip("Use concrete on straights above this centreline speed proxy (curvature below threshold).")]
        public bool concreteOnStraights = true;

        [Header("Tyre stacks (corner apex protection)")]
        public GameObject tyreWall;

        [Header("Fencing")]
        public GameObject catchFence;
        [Min(1f)] public float catchFenceEvery = 20f;
        [Tooltip("Extra lateral offset behind the barrier for catch fencing.")]
        public float fenceExtraMargin = 1.2f;

        [Header("Kerbs")]
        public GameObject kerbPainted;
        public GameObject kerbSausage;
        [Min(0.5f)] public float kerbModuleLength = 4f;

        [Header("Structures")]
        public GameObject startGantry;
        public GameObject marshalPost;
        public GameObject cameraTower;
        public WeightedPrefab[] grandstands;
        public GameObject lightTower;
        [Tooltip("Place light towers at this spacing when the circuit supports night sessions.")]
        public float lightTowerEvery = 120f;
        public GameObject brakingBoard;
        [Tooltip("Braking boards placed this many metres before a detected corner entry.")]
        public float brakingBoardLead = 100f;

        [Header("Vegetation / clutter")]
        public WeightedPrefab[] vegetation;
        public WeightedPrefab[] clutter;
        [Tooltip("Vegetation scatter attempts per 100 m of track edge.")]
        public float vegetationDensity = 4f;
        [Tooltip("Lateral band beyond the barrier where vegetation may scatter.")]
        public Vector2 vegetationBand = new Vector2(6f, 30f);

        [Header("Surface materials (optional swap for the road/runoff builder)")]
        public Material asphaltMaterial;
        public Material kerbMaterial;
        public Material runoffGravelMaterial;
        public Material runoffGrassMaterial;

        public GameObject PickWeighted(WeightedPrefab[] pool, System.Random rng)
        {
            if (pool == null || pool.Length == 0) return null;
            float total = 0f;
            foreach (var w in pool) total += Mathf.Max(0f, w.weight);
            if (total <= 0f) return pool[rng.Next(pool.Length)].prefab;
            float pick = (float)rng.NextDouble() * total;
            foreach (var w in pool)
            {
                pick -= Mathf.Max(0f, w.weight);
                if (pick <= 0f) return w.prefab;
            }
            return pool[pool.Length - 1].prefab;
        }
    }
}
