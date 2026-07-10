using UnityEngine;

namespace F1Game.Vehicle
{
    /// <summary>
    /// Canonical Formula-style car rig specification: the node names, pivots and
    /// material slots every car prefab (placeholder or final art) must provide.
    /// The editor CarPrefabBuilder constructs this hierarchy; the validator
    /// checks imported art against it; runtime systems bind by these names.
    /// Full import requirements (UVs, LODs, budgets): Docs/ART_PIPELINE.md.
    /// </summary>
    public static class CarRigSpec
    {
        public const string Root = "CarRoot";

        // Body group — separate meshes so damage/DRS/livery systems can bind.
        public const string Chassis = "Body/Chassis";
        public const string FrontWing = "Body/FrontWing";
        public const string RearWing = "Body/RearWing";
        public const string DrsFlap = "Body/RearWing/DrsFlap";       // pivot: flap hinge axis (X)
        public const string Floor = "Body/Floor";
        public const string Sidepods = "Body/Sidepods";
        public const string EngineCover = "Body/EngineCover";
        public const string Halo = "Body/Halo";
        public const string Cockpit = "Body/Cockpit";
        public const string SteeringWheel = "Body/Cockpit/SteeringWheel"; // pivot: steering column axis
        public const string Driver = "Body/Cockpit/Driver";
        public const string RainLight = "Body/RainLight";                 // emissive slot

        // Wheels — suspension pivot (vertical travel), steering pivot (Y, fronts
        // only), spin pivot (X). Rig order: Suspension > Steering? > Spin > Mesh.
        public static readonly string[] WheelCorners = { "FL", "FR", "RL", "RR" };
        public static string Suspension(string corner) => "Wheels/" + corner + "/Suspension";
        public static string SteeringPivot(string corner) => "Wheels/" + corner + "/Suspension/Steering";
        public static string SpinPivot(string corner, bool front) =>
            (front ? SteeringPivot(corner) : Suspension(corner)) + "/Spin";
        public static string Tyre(string corner, bool front) => SpinPivot(corner, front) + "/Tyre";
        public static string Rim(string corner, bool front) => SpinPivot(corner, front) + "/Rim";
        public static string BrakeDisc(string corner, bool front) => SpinPivot(corner, front) + "/BrakeDisc";

        // Damage parts (swap/detach targets).
        public const string DamageFrontWing = "Damage/FrontWingDetached";
        public const string DamageRearWing = "Damage/RearWingDetached";
        public const string DamageDebrisRoot = "Damage/DebrisSpawns";

        // VFX anchor points.
        public const string VfxExhaust = "VFX/Exhaust";
        public const string VfxFloorSparksL = "VFX/FloorSparksL";
        public const string VfxFloorSparksR = "VFX/FloorSparksR";
        public static string VfxTyreSmoke(string corner) => "VFX/TyreSmoke_" + corner;
        public static string VfxRainSpray(string corner) => "VFX/RainSpray_" + corner;

        // Camera mounts.
        public const string CamChase = "Cameras/ChaseMount";
        public const string CamTCam = "Cameras/TCamMount";
        public const string CamCockpit = "Cameras/CockpitMount";
        public const string CamNose = "Cameras/NoseMount";

        // Audio emitters.
        public const string AudioEngine = "Audio/EngineEmitter";
        public const string AudioTyres = "Audio/TyreEmitter";

        // Collision proxy (separate simplified mesh, no render).
        public const string CollisionProxy = "Collision/CollisionProxy";

        /// <summary>LOD renderer group names; final art must fill all four.</summary>
        public static readonly string[] LodGroups = { "LOD0", "LOD1", "LOD2", "LOD3" };

        /// <summary>Material slot names, matching MaterialLibrary car slots.</summary>
        public static readonly string[] MaterialSlots =
        {
            "Paint", "CarbonFibre", "Rubber", "Metal", "Glass", "Decals", "Emissive",
        };

        /// <summary>Reference dimensions (metres) for proxy geometry and validation.</summary>
        public const float ReferenceLength = 5.6f;
        public const float ReferenceWidth = 2.0f;
        public const float ReferenceWheelbase = 3.6f;
        public const float FrontTrack = 1.6f;
        public const float RearTrack = 1.55f;
        public const float WheelRadiusFront = 0.33f;
        public const float WheelRadiusRear = 0.34f;
    }
}
