using System.IO;
using UnityEngine;

namespace F1Game.Art
{
    /// <summary>
    /// Stable, explicit runtime paths to the committed art assets under
    /// StreamingAssets/Art (mirrored from Assets/Art). No AssetDatabase, no
    /// editor code, no recursive filesystem scans — every path is a constant
    /// composed from <see cref="Application.streamingAssetsPath"/> so it resolves
    /// identically in a standalone build.
    /// </summary>
    public static class RuntimeArtLibrary
    {
        public static string Root => Path.Combine(Application.streamingAssetsPath, "Art");

        /// <summary>Absolute path to a GLB given its "category/Name.glb" relative key.</summary>
        public static string Glb(string relativeKey)
        {
            return Path.Combine(Root, relativeKey.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Absolute path to a generated surface texture map.</summary>
        public static string SurfaceMap(string surface, string map)
        {
            return Path.Combine(Root, "Materials", surface, surface + "_" + map + ".png");
        }

        // ---- Modules (relative keys) ---------------------------------------
        public const string ConcreteBarrier = "Track/Barriers/Barrier_Concrete_3m.glb";
        public const string Armco = "Track/Barriers/Barrier_Armco_4m.glb";
        public const string TecproBarrier = "Track/Barriers/Barrier_Tecpro_1m.glb";
        public const string TireWall = "Track/Barriers/Barrier_TireWall_2m.glb";

        public const string CatchFence = "Track/Fencing/Fence_CatchPanel_5m.glb";
        public const string FenceGate = "Track/Fencing/Fence_Gate_3m.glb";
        public const string FencePost = "Track/Fencing/Fence_Post_3m.glb";

        public const string KerbPainted = "Track/Curbs/Kerb_Painted_4m.glb";
        public const string KerbSausage = "Track/Curbs/Kerb_Sausage_1m.glb";
        public const string KerbFlat = "Track/Curbs/Kerb_Flat_4m.glb";
        public const string Drain = "Track/Curbs/Track_Drain_2m.glb";

        public const string PitWall = "PitLane/Pit_Wall_4m.glb";
        public const string PitGarageBay = "PitLane/Pit_GarageBay_6m.glb";
        public const string PitGarageDoor = "PitLane/Pit_GarageDoor_6m.glb";

        public const string Grandstand = "Grandstands/Struct_Grandstand_12m.glb";

        public const string MarshalPost = "Props/Prop_MarshalPost.glb";
        public const string CameraTower = "Props/Struct_CameraTower_6m.glb";
        public const string LightTower = "Props/Struct_LightTower_10m.glb";
        public const string BrakingBoard = "Props/Prop_BrakingBoard.glb";
        public const string StartGantry = "Props/Struct_StartGantry_16m.glb";
        public const string TimingGantry = "Props/Struct_TimingGantry_14m.glb";
        public const string SpeakerPole = "Props/Prop_SpeakerPole_5m.glb";
        public const string PedBridge = "Props/Struct_PedBridge_14m.glb";
        public const string GuardHut = "Props/Prop_GuardHut.glb";
        public const string UtilityCabinet = "Props/Prop_UtilityCabinet.glb";
        public const string MaintenanceCart = "Props/Prop_MaintenanceCart.glb";

        // ---- Surfaces (folder names under Materials/) ----------------------
        public const string SurfAsphalt = "asphalt_clean";
        public const string SurfAsphaltWorn = "asphalt_worn";
        public const string SurfAsphaltPatched = "asphalt_patched";
        public const string SurfConcrete = "concrete_smooth";
        public const string SurfConcretePit = "concrete_pit";
        public const string SurfGravel = "gravel";
        public const string SurfGrass = "grass_green";
        public const string SurfGrassDry = "grass_dry";
        public const string SurfRubber = "rubber";
        public const string SurfMetalPainted = "metal_painted";
        public const string SurfMetalGalv = "metal_galvanised";
        public const string SurfCurbPaint = "curb_paint";

        /// <summary>True when the art payload is present in this build.</summary>
        public static bool Available()
        {
            return Directory.Exists(Root) && File.Exists(Glb(Armco));
        }
    }
}
