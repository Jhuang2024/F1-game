using System.IO;
using F1Game.Rendering;
using F1Game.Vehicle;
using UnityEditor;
using UnityEngine;

namespace F1Game.Editor
{
    /// <summary>
    /// Builds the production Formula-style car prefab skeleton per CarRigSpec:
    /// full node hierarchy with correct pivots (suspension travel, steering
    /// axis, wheel spin, DRS hinge), material slots from the PBR library, LOD
    /// group slots, VFX/camera/audio anchors and a simplified collision proxy.
    ///
    /// Every visible proxy mesh is tagged with PlaceholderArtMarker — this tool
    /// produces the SLOTS for real art, not the art. Import spec:
    /// Docs/ART_PIPELINE.md.
    /// </summary>
    public static class CarPrefabBuilder
    {
        const string PrefabPath = "Assets/Game/Prefabs/Cars/F1Car_PLACEHOLDER.prefab";

        [MenuItem("F1 Game/Art/Build Placeholder Car Prefab")]
        public static void Build()
        {
            var root = new GameObject(CarRigSpec.Root);
            try
            {
                BuildHierarchy(root.transform);
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[Car Pipeline] Placeholder car prefab written to " + PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void BuildHierarchy(Transform root)
        {
            // Body group.
            Node(root, "Body");
            ProxyMesh(root, CarRigSpec.Chassis, new Vector3(0f, 0.45f, 0.2f), new Vector3(1.4f, 0.5f, 4.4f), MaterialLibrary.Slot.CarPaint, "Chassis mesh (LOD0-3)");
            ProxyMesh(root, CarRigSpec.FrontWing, new Vector3(0f, 0.16f, 2.62f), new Vector3(1.9f, 0.1f, 0.55f), MaterialLibrary.Slot.CarbonFibre, "Front wing assembly");
            ProxyMesh(root, CarRigSpec.RearWing, new Vector3(0f, 0.85f, -2.45f), new Vector3(1.0f, 0.35f, 0.35f), MaterialLibrary.Slot.CarbonFibre, "Rear wing assembly");
            ProxyMesh(root, CarRigSpec.DrsFlap, new Vector3(0f, 1.0f, -2.5f), new Vector3(0.95f, 0.02f, 0.22f), MaterialLibrary.Slot.CarbonFibre, "DRS flap (hinged around local X)");
            ProxyMesh(root, CarRigSpec.Floor, new Vector3(0f, 0.08f, 0.1f), new Vector3(1.7f, 0.04f, 4.0f), MaterialLibrary.Slot.CarbonFibre, "Floor + plank");
            ProxyMesh(root, CarRigSpec.Sidepods, new Vector3(0f, 0.45f, -0.4f), new Vector3(1.75f, 0.4f, 1.6f), MaterialLibrary.Slot.CarPaint, "Sidepods");
            ProxyMesh(root, CarRigSpec.EngineCover, new Vector3(0f, 0.75f, -0.9f), new Vector3(0.5f, 0.5f, 1.9f), MaterialLibrary.Slot.CarPaint, "Engine cover + airbox");
            ProxyMesh(root, CarRigSpec.Halo, new Vector3(0f, 0.95f, 0.75f), new Vector3(0.85f, 0.3f, 0.7f), MaterialLibrary.Slot.Metal, "Halo");
            ProxyMesh(root, CarRigSpec.Cockpit, new Vector3(0f, 0.6f, 0.7f), new Vector3(0.7f, 0.3f, 1.1f), MaterialLibrary.Slot.CarbonFibre, "Cockpit tub interior");
            ProxyMesh(root, CarRigSpec.SteeringWheel, new Vector3(0f, 0.72f, 1.05f), new Vector3(0.28f, 0.18f, 0.06f), MaterialLibrary.Slot.CarbonFibre, "Steering wheel (rotates around local Z)");
            ProxyMesh(root, CarRigSpec.Driver, new Vector3(0f, 0.7f, 0.55f), new Vector3(0.45f, 0.5f, 0.9f), MaterialLibrary.Slot.Rubber, "Driver body + helmet + gloves");
            ProxyMesh(root, CarRigSpec.RainLight, new Vector3(0f, 0.5f, -2.75f), new Vector3(0.12f, 0.2f, 0.04f), MaterialLibrary.Slot.Emissive, "LED rain light");

            // Wheel corners with pivot chains.
            for (int i = 0; i < CarRigSpec.WheelCorners.Length; i++)
            {
                string corner = CarRigSpec.WheelCorners[i];
                bool front = corner[0] == 'F';
                float x = (corner[1] == 'L' ? -1f : 1f) * (front ? CarRigSpec.FrontTrack : CarRigSpec.RearTrack) * 0.5f;
                float z = front ? CarRigSpec.ReferenceWheelbase * 0.5f : -CarRigSpec.ReferenceWheelbase * 0.5f;
                float radius = front ? CarRigSpec.WheelRadiusFront : CarRigSpec.WheelRadiusRear;
                Vector3 hub = new Vector3(x, radius, z);

                Transform suspension = Node(root, CarRigSpec.Suspension(corner));
                suspension.localPosition = hub;
                if (front)
                {
                    Node(root, CarRigSpec.SteeringPivot(corner));
                }

                Node(root, CarRigSpec.SpinPivot(corner, front));
                ProxyMesh(root, CarRigSpec.Tyre(corner, front), Vector3.zero, new Vector3(radius * 2f, 0.36f, radius * 2f), MaterialLibrary.Slot.Rubber, "Tyre " + corner, relativeToOwnPivot: true);
                ProxyMesh(root, CarRigSpec.Rim(corner, front), Vector3.zero, new Vector3(radius * 1.15f, 0.3f, radius * 1.15f), MaterialLibrary.Slot.Metal, "Rim " + corner, relativeToOwnPivot: true);
                ProxyMesh(root, CarRigSpec.BrakeDisc(corner, front), Vector3.zero, new Vector3(radius * 0.9f, 0.05f, radius * 0.9f), MaterialLibrary.Slot.Emissive, "Brake disc " + corner, relativeToOwnPivot: true);
            }

            // Damage, VFX, cameras, audio, collision.
            Node(root, CarRigSpec.DamageFrontWing);
            Node(root, CarRigSpec.DamageRearWing);
            Node(root, CarRigSpec.DamageDebrisRoot);

            Anchor(root, CarRigSpec.VfxExhaust, new Vector3(0f, 0.55f, -2.6f));
            Anchor(root, CarRigSpec.VfxFloorSparksL, new Vector3(-0.8f, 0.05f, -1.2f));
            Anchor(root, CarRigSpec.VfxFloorSparksR, new Vector3(0.8f, 0.05f, -1.2f));
            foreach (string corner in CarRigSpec.WheelCorners)
            {
                Anchor(root, CarRigSpec.VfxTyreSmoke(corner), Vector3.zero);
                Anchor(root, CarRigSpec.VfxRainSpray(corner), Vector3.zero);
            }

            Anchor(root, CarRigSpec.CamChase, new Vector3(0f, 1.5f, -6.5f));
            // Onboard mounts (kept in sync with RaceCameraDirector's runtime
            // fallbacks): the T-cam sits on the airbox main intake framing the
            // helmet + wheel + road, the cockpit cam at the driver's eye line
            // behind the wheel.
            Anchor(root, CarRigSpec.CamTCam, new Vector3(0f, 1.16f, -0.52f));
            Anchor(root, CarRigSpec.CamCockpit, new Vector3(0f, 0.84f, 0.3f));
            Anchor(root, CarRigSpec.CamNose, new Vector3(0f, 0.35f, 2.7f));

            Anchor(root, CarRigSpec.AudioEngine, new Vector3(0f, 0.5f, -1.6f));
            Anchor(root, CarRigSpec.AudioTyres, new Vector3(0f, 0.2f, 0f));

            // Simplified collision proxy: one convex box, renderer disabled.
            Transform collision = Node(root, CarRigSpec.CollisionProxy);
            var box = collision.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.5f, 0f);
            box.size = new Vector3(CarRigSpec.ReferenceWidth, 0.95f, CarRigSpec.ReferenceLength);

            // LOD slots on the root (renderers assigned when real art lands).
            var lodGroup = root.gameObject.AddComponent<LODGroup>();
            var lods = new LOD[CarRigSpec.LodGroups.Length];
            for (int i = 0; i < lods.Length; i++)
            {
                lods[i] = new LOD(new[] { 0.5f, 0.18f, 0.07f, 0.015f }[i], new Renderer[0]);
            }

            lodGroup.SetLODs(lods);

            var marker = root.gameObject.AddComponent<PlaceholderArtMarker>();
            marker.expectedAsset = "Licensed/commissioned Formula-style car model (see Docs/ART_PIPELINE.md §Car)";
        }

        [MenuItem("F1 Game/Art/Validate Car Prefab")]
        public static void ValidateCarPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[Car Pipeline] No car prefab at " + PrefabPath + " - run Build Placeholder Car Prefab first.");
                return;
            }

            int missing = 0;
            var requiredPaths = new System.Collections.Generic.List<string>
            {
                CarRigSpec.Chassis, CarRigSpec.FrontWing, CarRigSpec.RearWing, CarRigSpec.DrsFlap,
                CarRigSpec.Floor, CarRigSpec.Halo, CarRigSpec.Cockpit, CarRigSpec.SteeringWheel,
                CarRigSpec.Driver, CarRigSpec.CollisionProxy,
                CarRigSpec.CamChase, CarRigSpec.CamTCam, CarRigSpec.CamCockpit,
                CarRigSpec.AudioEngine, CarRigSpec.AudioTyres,
            };
            foreach (string corner in CarRigSpec.WheelCorners)
            {
                bool front = corner[0] == 'F';
                requiredPaths.Add(CarRigSpec.Suspension(corner));
                requiredPaths.Add(CarRigSpec.SpinPivot(corner, front));
                requiredPaths.Add(CarRigSpec.Tyre(corner, front));
            }

            foreach (string path in requiredPaths)
            {
                if (prefab.transform.Find(path) == null)
                {
                    Debug.LogError("[Car Pipeline] Missing rig node: " + path);
                    missing++;
                }
            }

            int placeholders = prefab.GetComponentsInChildren<PlaceholderArtMarker>(true).Length;
            Debug.Log(string.Format(
                "[Car Pipeline] Validation: {0} missing node(s), {1} placeholder marker(s) remaining{2}.",
                missing, placeholders,
                placeholders > 0 ? " - NOT final art" : ""));
        }

        static Transform Node(Transform root, string path)
        {
            Transform current = root;
            foreach (string part in path.Split('/'))
            {
                Transform next = current.Find(part);
                if (next == null)
                {
                    next = new GameObject(part).transform;
                    next.SetParent(current, false);
                }

                current = next;
            }

            return current;
        }

        static Transform Anchor(Transform root, string path, Vector3 localPosition)
        {
            Transform node = Node(root, path);
            node.localPosition = localPosition;
            return node;
        }

        static void ProxyMesh(Transform root, string path, Vector3 center, Vector3 size, MaterialLibrary.Slot slot, string expectedAsset, bool relativeToOwnPivot = false)
        {
            Transform node = Node(root, path);
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            proxy.name = "PLACEHOLDER_proxy";
            Object.DestroyImmediate(proxy.GetComponent<Collider>());
            proxy.transform.SetParent(node, false);
            proxy.transform.localPosition = relativeToOwnPivot ? Vector3.zero : Vector3.zero;
            proxy.transform.localScale = size;
            if (!relativeToOwnPivot)
            {
                node.localPosition = center;
            }

            proxy.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get(slot);
            var marker = proxy.AddComponent<PlaceholderArtMarker>();
            marker.expectedAsset = expectedAsset;
        }
    }
}
