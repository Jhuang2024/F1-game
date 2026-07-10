using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Rendering
{
    /// <summary>
    /// Access to the authored PBR surface library
    /// (Assets/Game/Resources/BaseMaterials). Every entry is currently an
    /// explicitly-named *_Placeholder material — flat URP/Lit values holding the
    /// slot (and the shader's build inclusion) until real texture sets arrive.
    /// See Docs/ART_PIPELINE.md for the import specification each slot expects.
    /// </summary>
    public static class MaterialLibrary
    {
        public enum Slot
        {
            // Car material slots
            CarPaint, CarbonFibre, Rubber, Metal, Glass, Decal, Emissive,
            // Track surface slots
            Asphalt, RubberedLine, WetAsphalt, PaintedLine, Kerb, Concrete,
            Gravel, Grass, ArtificialTurf, MetalBarrier, TyreWall, Fencing,
            PitConcrete, GarageFloor,
        }

        const string ResourceFolder = "BaseMaterials/";
        static readonly Dictionary<Slot, Material> cache = new Dictionary<Slot, Material>();

        /// <summary>Shared material for a slot (do not mutate; Instantiate for per-object tints).</summary>
        public static Material Get(Slot slot)
        {
            if (cache.TryGetValue(slot, out Material cached) && cached != null)
            {
                return cached;
            }

            Material material = Resources.Load<Material>(ResourceFolder + "M_" + slot + "_Placeholder");
            if (material == null)
            {
                Debug.LogWarning("[Rendering] Material library slot missing: " + slot + " - creating runtime fallback.");
                material = ShaderCompat.CreateLitMaterial();
                material.name = "M_" + slot + "_RuntimeFallback";
            }

            cache[slot] = material;
            return material;
        }

        /// <summary>Instanced copy for callers that need per-object colour changes.</summary>
        public static Material GetInstance(Slot slot)
        {
            return Object.Instantiate(Get(slot));
        }
    }
}
