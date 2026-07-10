using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Interim car visual construction, extracted verbatim from RaceManager as
    /// part of the monolith split (Docs/REFACTOR_MAP.md). Everything built here
    /// is primitive-based INTERIM art: the production path is the CarRigSpec
    /// prefab pipeline (F1Game.Vehicle) fed by real modelled assets. When an
    /// authored car prefab exists at Resources/Cars/F1Car and
    /// <see cref="PreferAuthoredPrefab"/> is enabled, it is used instead.
    /// </summary>
    public static class CarVisualFactory
    {
        /// <summary>Flip once the authored prefab path is validated in-editor.</summary>
        public static bool PreferAuthoredPrefab;

        const string AuthoredCarResourcePath = "Cars/F1Car";

        static PhysicMaterial carBodyPhysicsMaterial;

        /// <summary>
        /// Authored-prefab hook used by CreateOpenWheelCar. Returns null while no
        /// validated prefab is available, which keeps the interim primitive path.
        /// </summary>
        static GameObject TryInstantiateAuthoredCar(string driverName)
        {
            if (!PreferAuthoredPrefab)
            {
                return null;
            }

            GameObject prefab = Resources.Load<GameObject>(AuthoredCarResourcePath);
            if (prefab == null)
            {
                return null;
            }

            GameObject car = Object.Instantiate(prefab);
            car.name = driverName + " car";
            return car;
        }

        public static GameObject CreateOpenWheelCar(string driverName, Color primary, Color secondary)
        {
            GameObject authored = TryInstantiateAuthoredCar(driverName);
            if (authored != null)
            {
                return authored;
            }

            GameObject root = new GameObject(driverName + " car");
            root.layer = 0;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 16;
            body.solverVelocityIterations = 8;
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.75f, 0.68f, 4.7f);
            collider.center = new Vector3(0f, 0.22f, 0.08f);
            collider.sharedMaterial = GetCarBodyPhysicsMaterial();

            Material primaryMaterial = CreateMaterial(driverName + " primary", primary, 0.22f, 0.86f);
            Material secondaryMaterial = CreateMaterial(driverName + " secondary", secondary, 0.18f, 0.82f);
            Material tyreMaterial = CreateMaterial(driverName + " tyre", new Color(0.008f, 0.009f, 0.011f), 0.02f, 0.28f);
            Material rimMaterial = CreateMaterial(driverName + " rim", new Color(0.76f, 0.76f, 0.74f), 0.65f, 0.82f);
            Material floorMaterial = CreateMaterial(driverName + " carbon floor", new Color(0.012f, 0.014f, 0.016f), 0.35f, 0.68f);
            Material visorMaterial = CreateMaterial(driverName + " visor", new Color(0.01f, 0.04f, 0.08f), 0.45f, 0.96f);
            Material helmetMaterial = CreateMaterial(driverName + " helmet", Color.Lerp(secondary, Color.white, 0.35f), 0.15f, 0.88f);
            Material inletMaterial = CreateMaterial(driverName + " inlet shadow", new Color(0.002f, 0.003f, 0.004f), 0f, 0.48f);
            Material detailMaterial = CreateMaterial(driverName + " tech detail", new Color(0.12f, 0.14f, 0.16f), 0.55f, 0.78f);
            Material brakeDiscMaterial = CreateMaterial(driverName + " brake disc", new Color(0.34f, 0.34f, 0.32f), 0.42f, 0.48f);
            Material caliperMaterial = CreateMaterial(driverName + " brake caliper", Color.Lerp(secondary, Color.black, 0.18f), 0.12f, 0.55f);

            CreateTaperedBox(root.transform, "survival cell", new Vector3(0f, 0.38f, 0.0f), 0.64f, 1.04f, 0.42f, 2.35f, primaryMaterial);
            CreateTaperedBox(root.transform, "carbon floor", new Vector3(0f, 0.14f, -0.18f), 1.34f, 1.62f, 0.1f, 3.72f, floorMaterial);
            CreateTaperedBox(root.transform, "left sidepod", new Vector3(-0.64f, 0.35f, -0.48f), 0.22f, 0.42f, 0.34f, 1.28f, primaryMaterial);
            CreateTaperedBox(root.transform, "right sidepod", new Vector3(0.64f, 0.35f, -0.48f), 0.22f, 0.42f, 0.34f, 1.28f, primaryMaterial);
            CreateChildCube(root.transform, "left sidepod inlet", new Vector3(-0.86f, 0.42f, 0.02f), new Vector3(0.05f, 0.22f, 0.36f), inletMaterial);
            CreateChildCube(root.transform, "right sidepod inlet", new Vector3(0.86f, 0.42f, 0.02f), new Vector3(0.05f, 0.22f, 0.36f), inletMaterial);
            CreateChildCube(root.transform, "left livery flash", new Vector3(-0.88f, 0.52f, -0.44f), new Vector3(0.045f, 0.16f, 1.08f), secondaryMaterial);
            CreateChildCube(root.transform, "right livery flash", new Vector3(0.88f, 0.52f, -0.44f), new Vector3(0.045f, 0.16f, 1.08f), secondaryMaterial);
            CreateTaperedBox(root.transform, "narrow nose", new Vector3(0f, 0.3f, 1.62f), 0.2f, 0.48f, 0.22f, 1.95f, primaryMaterial);
            CreateChildCube(root.transform, "nose detail upper", new Vector3(0f, 0.46f, 1.63f), new Vector3(0.18f, 0.055f, 1.52f), secondaryMaterial);
            CreateChildCube(root.transform, "nose detail tip", new Vector3(0f, 0.22f, 2.58f), new Vector3(0.12f, 0.08f, 0.18f), detailMaterial);

            // Front wing: cascaded elements at increasing attack angles so it reads
            // as an aero surface, not a stack of shelves.
            CreateChildCube(root.transform, "front wing base", new Vector3(0f, 0.15f, 2.55f), new Vector3(2.0f, 0.05f, 0.5f), Quaternion.Euler(-4f, 0f, 0f), secondaryMaterial);
            CreateChildCube(root.transform, "front wing mid flap", new Vector3(0f, 0.24f, 2.66f), new Vector3(1.9f, 0.04f, 0.3f), Quaternion.Euler(-11f, 0f, 0f), primaryMaterial);
            CreateChildCube(root.transform, "front wing upper flap", new Vector3(0f, 0.33f, 2.78f), new Vector3(1.72f, 0.035f, 0.22f), Quaternion.Euler(-18f, 0f, 0f), detailMaterial);
            CreateChildCube(root.transform, "left front endplate", new Vector3(-1.04f, 0.26f, 2.6f), new Vector3(0.05f, 0.36f, 0.56f), Quaternion.Euler(0f, -6f, 0f), secondaryMaterial);
            CreateChildCube(root.transform, "right front endplate", new Vector3(1.04f, 0.26f, 2.6f), new Vector3(0.05f, 0.36f, 0.56f), Quaternion.Euler(0f, 6f, 0f), secondaryMaterial);
            CreateChildCube(root.transform, "left endplate winglet", new Vector3(-1.02f, 0.42f, 2.62f), new Vector3(0.16f, 0.03f, 0.4f), Quaternion.Euler(0f, 0f, 14f), primaryMaterial);
            CreateChildCube(root.transform, "right endplate winglet", new Vector3(1.02f, 0.42f, 2.62f), new Vector3(0.16f, 0.03f, 0.4f), Quaternion.Euler(0f, 0f, -14f), primaryMaterial);

            // Rear wing with swan-neck pillar, angled flap, and beam wing.
            CreateChildCube(root.transform, "rear wing pillar", new Vector3(0f, 0.68f, -1.9f), new Vector3(0.07f, 0.42f, 0.1f), Quaternion.Euler(16f, 0f, 0f), detailMaterial);
            CreateChildCube(root.transform, "rear wing main plane", new Vector3(0f, 0.66f, -2.04f), new Vector3(1.72f, 0.07f, 0.42f), Quaternion.Euler(9f, 0f, 0f), secondaryMaterial);
            CreateChildCube(root.transform, "rear wing flap", new Vector3(0f, 0.85f, -2.16f), new Vector3(1.66f, 0.05f, 0.3f), Quaternion.Euler(24f, 0f, 0f), primaryMaterial);
            CreateChildCube(root.transform, "rear beam wing", new Vector3(0f, 0.42f, -2.02f), new Vector3(1.5f, 0.05f, 0.24f), Quaternion.Euler(14f, 0f, 0f), detailMaterial);
            CreateChildCube(root.transform, "left rear endplate", new Vector3(-0.9f, 0.72f, -2.06f), new Vector3(0.06f, 0.66f, 0.52f), Quaternion.Euler(0f, 0f, 4f), secondaryMaterial);
            CreateChildCube(root.transform, "right rear endplate", new Vector3(0.9f, 0.72f, -2.06f), new Vector3(0.06f, 0.66f, 0.52f), Quaternion.Euler(0f, 0f, -4f), secondaryMaterial);

            CreateTaperedBox(root.transform, "engine cover", new Vector3(0f, 0.66f, -0.72f), 0.42f, 0.72f, 0.58f, 1.38f, primaryMaterial);
            CreateChildCube(root.transform, "shark fin", new Vector3(0f, 0.88f, -1.15f), new Vector3(0.035f, 0.32f, 0.85f), secondaryMaterial);
            CreateTaperedBox(root.transform, "rear diffuser", new Vector3(0f, 0.18f, -1.94f), 1.12f, 1.48f, 0.18f, 0.72f, floorMaterial);
            CreateChildCube(root.transform, "airbox", new Vector3(0f, 0.98f, -0.34f), new Vector3(0.35f, 0.22f, 0.52f), secondaryMaterial);

            CreateChildCube(root.transform, "halo center", new Vector3(0f, 0.88f, 0.52f), new Vector3(0.06f, 0.18f, 0.08f), detailMaterial);
            CreateChildCube(root.transform, "halo rim", new Vector3(0f, 0.95f, 0.28f), new Vector3(0.74f, 0.06f, 0.72f), secondaryMaterial);
            CreateChildCube(root.transform, "left halo stay", new Vector3(-0.32f, 0.78f, 0.22f), new Vector3(0.055f, 0.32f, 0.07f), detailMaterial);
            CreateChildCube(root.transform, "right halo stay", new Vector3(0.32f, 0.78f, 0.22f), new Vector3(0.055f, 0.32f, 0.07f), detailMaterial);
            CreateChildSphere(root.transform, "cockpit visor", new Vector3(0f, 0.78f, 0.44f), new Vector3(0.48f, 0.24f, 0.52f), visorMaterial);
            CreateChildSphere(root.transform, "driver helmet", new Vector3(0f, 0.88f, 0.2f), new Vector3(0.32f, 0.32f, 0.32f), helmetMaterial);
            CreateChildCube(root.transform, "steering wheel", new Vector3(0f, 0.76f, 0.62f), new Vector3(0.24f, 0.18f, 0.05f), detailMaterial);

            // Detail pass: mirrors, bargeboards, and livery accents that make each
            // team car read as designed rather than assembled from crates.
            CreateChildCube(root.transform, "left mirror", new Vector3(-0.5f, 0.72f, 0.72f), new Vector3(0.14f, 0.07f, 0.06f), secondaryMaterial);
            CreateChildCube(root.transform, "right mirror", new Vector3(0.5f, 0.72f, 0.72f), new Vector3(0.14f, 0.07f, 0.06f), secondaryMaterial);
            CreateChildCube(root.transform, "left bargeboard", new Vector3(-0.58f, 0.26f, 0.62f), new Vector3(0.035f, 0.24f, 0.5f), detailMaterial);
            CreateChildCube(root.transform, "right bargeboard", new Vector3(0.58f, 0.26f, 0.62f), new Vector3(0.035f, 0.24f, 0.5f), detailMaterial);
            CreateChildCube(root.transform, "engine cover stripe", new Vector3(0f, 0.86f, -0.66f), new Vector3(0.1f, 0.05f, 1.3f), secondaryMaterial);
            CreateChildCube(root.transform, "nose number panel", new Vector3(0f, 0.42f, 2.1f), new Vector3(0.24f, 0.03f, 0.3f), CreateMaterial(driverName + " number panel", Color.Lerp(Color.white, secondary, 0.15f), 0.1f, 0.7f));
            CreateChildCube(root.transform, "cockpit surround pad", new Vector3(0f, 0.72f, 0.34f), new Vector3(0.58f, 0.08f, 0.5f), inletMaterial);

            // Nose tip cone softens the front silhouette.
            CreateChildSphere(root.transform, "nose tip", new Vector3(0f, 0.28f, 2.62f), new Vector3(0.2f, 0.18f, 0.42f), primaryMaterial);

            // Rear rain light: glows under braking, blinks while harvesting.
            Material rainLightMaterial = CreateMaterial(driverName + " rain light", new Color(0.28f, 0.02f, 0.02f), 0.1f, 0.6f);
            CreateChildCube(root.transform, "rear rain light", new Vector3(0f, 0.42f, -2.12f), new Vector3(0.1f, 0.22f, 0.05f), rainLightMaterial);

            CreateSuspension(root.transform, floorMaterial, detailMaterial);
            Transform wheelFl = CreateWheel(root.transform, new Vector3(-1.06f, 0.24f, 1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);
            Transform wheelFr = CreateWheel(root.transform, new Vector3(1.06f, 0.24f, 1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);
            Transform wheelRl = CreateWheel(root.transform, new Vector3(-1.06f, 0.24f, -1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);
            Transform wheelRr = CreateWheel(root.transform, new Vector3(1.06f, 0.24f, -1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);

            var placeholderTag = root.AddComponent<F1Game.Vehicle.PlaceholderArtMarker>();
            placeholderTag.expectedAsset = "Modelled Formula-style car via CarRigSpec pipeline (Docs/ART_PIPELINE.md)";

            VehicleVisuals visuals = root.AddComponent<VehicleVisuals>();
            visuals.Initialize(root.GetComponent<VehicleController>(), rainLightMaterial);
            visuals.SetWheels(wheelFl, wheelFr, wheelRl, wheelRr);
            visuals.SetBrakeGlowMaterial(brakeDiscMaterial);

            return root;
        }

        // A simple polished coupe silhouette - deliberately NOT an open-wheel F1
        // shape, so it reads as a distinct support vehicle rather than another
        // race car. Kinematic Rigidbody + a real collider (Part 1): the object is
        // solid, so a careless approach gets a genuine physical bump rather than
        // clipping through, while SafetyCarController drives it directly via
        // transform/rigidbody movement instead of engine/tyre physics - it never
        // races, it only needs to look right and block the road.
        // Generic, unbranded high-visibility "safety car" livery: bright
        // fluorescent body with black contrast panels and an amber light bar -
        // deliberately NOT any real series' colour scheme, just built to read
        // clearly and instantly as "official car, not a competitor" from far
        // down the straight, per the graphics brief (larger/readable
        // silhouette, smoother body, clear generic livery, no branding).
        public static GameObject CreateSafetyCarVisual(out Renderer beaconRenderer, out Renderer brakeLightRenderer)
        {
            GameObject root = new GameObject("Safety car");
            root.layer = 0;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(2f, 1.2f, 4.7f);
            collider.center = new Vector3(0f, 0.6f, 0f);
            collider.sharedMaterial = GetCarBodyPhysicsMaterial();

            // Fluorescent lime-yellow with black contrast roof/skirt - high
            // visibility, unmistakably not a competitor's livery, and easy to
            // read as an "official" car at a glance.
            Color bodyColor = new Color(0.62f, 0.95f, 0.06f);
            Color contrastColor = new Color(0.03f, 0.03f, 0.04f);
            Color accentColor = new Color(1f, 0.55f, 0.02f);
            Material bodyMaterial = CreateMaterial("Safety car body", bodyColor, 0.35f, 0.65f);
            Material contrastMaterial = CreateMaterial("Safety car contrast", contrastColor, 0.4f, 0.75f);
            Material accentMaterial = CreateMaterial("Safety car accent", accentColor, 0.1f, 0.7f);
            Material glassMaterial = CreateMaterial("Safety car glass", new Color(0.08f, 0.12f, 0.16f, 0.9f), 0.2f, 0.95f);
            Material wheelMaterial = CreateMaterial("Safety car wheel", new Color(0.02f, 0.02f, 0.02f), 0.05f, 0.3f);
            Material rimMaterial = CreateMaterial("Safety car rim", new Color(0.78f, 0.78f, 0.76f), 0.7f, 0.85f);
            Material headlightMaterial = CreateMaterial("Safety car headlight", new Color(1.3f, 1.3f, 1.1f), 0f, 0.9f, new Color(1f, 1f, 0.85f));
            Material beaconMaterial = CreateMaterial("Safety car beacon", accentColor, 0f, 0.9f, accentColor);
            Material blueMarkerMaterial = CreateMaterial("Safety car marker", new Color(0.1f, 0.35f, 1f), 0f, 0.8f, new Color(0.15f, 0.4f, 1.4f));
            Material brakeLightMaterial = CreateMaterial("Safety car brake light", new Color(0.12f, 0.01f, 0.01f), 0.1f, 0.6f, new Color(0.12f, 0.01f, 0.01f));
            Material markerPanelMaterial = CreateMaterial("Safety car marker panel", Color.white, 0.05f, 0.5f);

            // Smoother, less boxy shell: a tapered nose/tail instead of flat
            // cube fronts/rears, slightly larger than a standard car for a
            // bigger, more readable silhouette.
            CreateTaperedBox(root.transform, "SC body lower", new Vector3(0f, 0.42f, 0.2f), 1.7f, 1.9f, 0.62f, 4.0f, bodyMaterial);
            CreateTaperedBox(root.transform, "SC nose", new Vector3(0f, 0.34f, 2.55f), 1.2f, 1.9f, 0.46f, 0.9f, bodyMaterial);
            CreateChildCube(root.transform, "SC cabin", new Vector3(0f, 0.96f, -0.15f), new Vector3(1.55f, 0.5f, 2.3f), contrastMaterial);
            CreateChildCube(root.transform, "SC roof panel", new Vector3(0f, 1.22f, -0.15f), new Vector3(1.5f, 0.06f, 2.1f), contrastMaterial);
            CreateChildCube(root.transform, "SC windshield", new Vector3(0f, 1.0f, 1.0f), new Vector3(1.44f, 0.42f, 0.06f), Quaternion.Euler(-24f, 0f, 0f), glassMaterial);
            CreateChildCube(root.transform, "SC rear glass", new Vector3(0f, 1.0f, -1.24f), new Vector3(1.44f, 0.4f, 0.06f), Quaternion.Euler(20f, 0f, 0f), glassMaterial);
            CreateChildCube(root.transform, "SC side glass left", new Vector3(-0.78f, 1.02f, -0.15f), new Vector3(0.04f, 0.32f, 1.95f), glassMaterial);
            CreateChildCube(root.transform, "SC side glass right", new Vector3(0.78f, 1.02f, -0.15f), new Vector3(0.04f, 0.32f, 1.95f), glassMaterial);
            CreateChildCube(root.transform, "SC front bumper", new Vector3(0f, 0.28f, 2.3f), new Vector3(1.96f, 0.3f, 0.22f), contrastMaterial);
            CreateChildCube(root.transform, "SC rear bumper", new Vector3(0f, 0.28f, -2.28f), new Vector3(1.96f, 0.3f, 0.22f), contrastMaterial);
            CreateChildCube(root.transform, "SC skirt left", new Vector3(-0.96f, 0.24f, 0.1f), new Vector3(0.05f, 0.16f, 3.9f), contrastMaterial);
            CreateChildCube(root.transform, "SC skirt right", new Vector3(0.96f, 0.24f, 0.1f), new Vector3(0.05f, 0.16f, 3.9f), contrastMaterial);
            CreateChildCube(root.transform, "SC bonnet stripe", new Vector3(0f, 0.66f, 1.7f), new Vector3(0.5f, 0.03f, 1.6f), contrastMaterial);

            // Generic bold door marker panels - a clear "this is an official
            // car" identity read without any real branding/text/logos.
            CreateChildCube(root.transform, "SC door marker left", new Vector3(-0.99f, 0.62f, -0.2f), new Vector3(0.03f, 0.34f, 0.9f), markerPanelMaterial);
            CreateChildCube(root.transform, "SC door marker right", new Vector3(0.99f, 0.62f, -0.2f), new Vector3(0.03f, 0.34f, 0.9f), markerPanelMaterial);
            CreateChildCube(root.transform, "SC door marker accent left", new Vector3(-1.0f, 0.62f, -0.2f), new Vector3(0.01f, 0.34f, 0.9f), accentMaterial);
            CreateChildCube(root.transform, "SC door marker accent right", new Vector3(1.0f, 0.62f, -0.2f), new Vector3(0.01f, 0.34f, 0.9f), accentMaterial);

            // Wing mirrors - a small detail pass that reads well in chase cam.
            CreateChildCube(root.transform, "SC mirror left", new Vector3(-0.92f, 0.86f, 1.1f), new Vector3(0.14f, 0.1f, 0.22f), contrastMaterial);
            CreateChildCube(root.transform, "SC mirror right", new Vector3(0.92f, 0.86f, 1.1f), new Vector3(0.14f, 0.1f, 0.22f), contrastMaterial);

            GameObject headlightLeft = CreateChildCubeReturn(root.transform, "SC headlight left", new Vector3(-0.66f, 0.42f, 2.34f), new Vector3(0.28f, 0.15f, 0.08f), headlightMaterial);
            GameObject headlightRight = CreateChildCubeReturn(root.transform, "SC headlight right", new Vector3(0.66f, 0.42f, 2.34f), new Vector3(0.28f, 0.15f, 0.08f), headlightMaterial);
            MakeVisualOnlyIfPossible(headlightLeft);
            MakeVisualOnlyIfPossible(headlightRight);

            GameObject brakeLightLeft = CreateChildCubeReturn(root.transform, "SC brake light left", new Vector3(-0.64f, 0.46f, -2.34f), new Vector3(0.32f, 0.17f, 0.06f), brakeLightMaterial);
            CreateChildCubeReturn(root.transform, "SC brake light right", new Vector3(0.64f, 0.46f, -2.34f), new Vector3(0.32f, 0.17f, 0.06f), brakeLightMaterial);
            brakeLightRenderer = brakeLightLeft.GetComponent<Renderer>();

            // Roof light bar: the clearest "this is the safety car" identity read
            // from a distance - wider and taller than before for a bigger
            // silhouette, plus static blue corner markers flanking the pulsing
            // amber beacon SafetyCarController drives for extra contrast.
            CreateChildCube(root.transform, "SC roof bar mount", new Vector3(0f, 1.26f, -0.2f), new Vector3(1.1f, 0.06f, 0.4f), wheelMaterial);
            GameObject beacon = CreateChildCubeReturn(root.transform, "SC roof beacon", new Vector3(0f, 1.36f, -0.2f), new Vector3(1.0f, 0.16f, 0.34f), beaconMaterial);
            beaconRenderer = beacon.GetComponent<Renderer>();
            CreateChildCubeReturn(root.transform, "SC roof marker left", new Vector3(-0.58f, 1.34f, -0.2f), new Vector3(0.14f, 0.12f, 0.28f), blueMarkerMaterial);
            CreateChildCubeReturn(root.transform, "SC roof marker right", new Vector3(0.58f, 1.34f, -0.2f), new Vector3(0.14f, 0.12f, 0.28f), blueMarkerMaterial);

            CreateSafetyCarWheel(root.transform, new Vector3(-1.0f, 0.34f, 1.48f), wheelMaterial, rimMaterial);
            CreateSafetyCarWheel(root.transform, new Vector3(1.0f, 0.34f, 1.48f), wheelMaterial, rimMaterial);
            CreateSafetyCarWheel(root.transform, new Vector3(-1.0f, 0.34f, -1.48f), wheelMaterial, rimMaterial);
            CreateSafetyCarWheel(root.transform, new Vector3(1.0f, 0.34f, -1.48f), wheelMaterial, rimMaterial);

            return root;
        }

        static void CreateSafetyCarWheel(Transform parent, Vector3 localPosition, Material tyreMaterial, Material rimMaterial)
        {
            GameObject tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tyre.name = "SC wheel";
            tyre.transform.SetParent(parent);
            tyre.transform.localPosition = localPosition;
            tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tyre.transform.localScale = new Vector3(0.34f, 0.34f, 0.34f);
            tyre.GetComponent<Renderer>().sharedMaterial = tyreMaterial;
            MakeVisualOnlyIfPossible(tyre);

            GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "SC wheel rim";
            rim.transform.SetParent(tyre.transform);
            rim.transform.localPosition = Vector3.zero;
            rim.transform.localScale = new Vector3(0.62f, 1.05f, 0.62f);
            rim.GetComponent<Renderer>().sharedMaterial = rimMaterial;
            MakeVisualOnlyIfPossible(rim);
        }

        static GameObject CreateChildCubeReturn(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            MakeVisualOnlyIfPossible(cube);
            return cube;
        }

        static void MakeVisualOnlyIfPossible(GameObject visualObject)
        {
            Collider objectCollider = visualObject.GetComponent<Collider>();
            if (objectCollider != null)
            {
                Object.Destroy(objectCollider);
            }
        }

        static void CreateTaperedBox(Transform parent, string objectName, Vector3 localPosition, float frontWidth, float rearWidth, float height, float length, Material material)
        {
            GameObject meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(parent);
            meshObject.transform.localPosition = localPosition;
            meshObject.transform.localRotation = Quaternion.identity;

            float front = length * 0.5f;
            float rear = -length * 0.5f;
            float y0 = -height * 0.5f;
            float y1 = height * 0.5f;
            float fw = frontWidth * 0.5f;
            float rw = rearWidth * 0.5f;

            Mesh mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(-fw, y0, front), new Vector3(fw, y0, front), new Vector3(fw, y1, front), new Vector3(-fw, y1, front),
                new Vector3(-rw, y0, rear), new Vector3(rw, y0, rear), new Vector3(rw, y1, rear), new Vector3(-rw, y1, rear)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,
                3, 7, 6, 3, 6, 2,
                0, 1, 5, 0, 5, 4
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MeshFilter filter = meshObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;
        }

        static void CreateChildCube(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            CreateChildCube(parent, objectName, localPosition, localScale, Quaternion.identity, material);
        }

        static void CreateChildCube(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = localRotation;
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }
        }

        static void CreateChildSphere(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = objectName;
            sphere.transform.SetParent(parent);
            sphere.transform.localPosition = localPosition;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = localScale;
            sphere.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }
        }

        // Builds one wheel assembly under a spin pivot so VehicleVisuals can rotate
        // the whole wheel with road speed and steer the fronts. Returns the pivot.
        static Transform CreateWheel(Transform parent, Vector3 localPosition, Material tyreMaterial, Material rimMaterial, Material brakeDiscMaterial, Material caliperMaterial)
        {
            GameObject pivot = new GameObject(localPosition.z > 0f ? "wheel pivot front" : "wheel pivot rear");
            pivot.transform.SetParent(parent);
            pivot.transform.localPosition = localPosition;
            pivot.transform.localRotation = Quaternion.identity;

            CreateWheelPart(pivot.transform, "open wheel", Vector3.zero, new Vector3(0.62f, 0.24f, 0.62f), tyreMaterial);
            CreateWheelPart(pivot.transform, "wheel rim", Vector3.zero, new Vector3(0.4f, 0.245f, 0.4f), rimMaterial);

            // Aero wheel cover on the outboard face.
            float outboard = localPosition.x < 0f ? -0.25f : 0.25f;
            CreateWheelPart(pivot.transform, "wheel cover", new Vector3(outboard, 0f, 0f), new Vector3(0.5f, 0.012f, 0.5f), rimMaterial);

            float inboard = localPosition.x < 0f ? 0.14f : -0.14f;
            CreateWheelPart(pivot.transform, "brake disc", new Vector3(inboard, 0f, 0f), new Vector3(0.3f, 0.035f, 0.3f), brakeDiscMaterial);

            // Caliper stays on the upright (parent), it must not spin with the wheel.
            GameObject caliper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            caliper.name = "brake caliper";
            caliper.transform.SetParent(parent);
            caliper.transform.localPosition = localPosition + new Vector3(inboard * 1.08f, 0.12f, 0.07f);
            caliper.transform.localRotation = Quaternion.identity;
            caliper.transform.localScale = new Vector3(0.07f, 0.2f, 0.16f);
            caliper.GetComponent<Renderer>().sharedMaterial = caliperMaterial;
            Collider caliperCollider = caliper.GetComponent<Collider>();
            if (caliperCollider != null)
            {
                Object.Destroy(caliperCollider);
            }

            return pivot.transform;
        }

        static void CreateWheelPart(Transform pivot, string partName, Vector3 localOffset, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            part.name = partName;
            part.transform.SetParent(pivot);
            part.transform.localPosition = localOffset;
            part.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }
        }

        static void CreateSuspension(Transform parent, Material armMaterial, Material detailMaterial)
        {
            // Front
            CreateSuspensionArm(parent, new Vector3(-0.52f, 0.32f, 1.32f), new Vector3(-1.02f, 0.26f, 1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(0.52f, 0.32f, 1.32f), new Vector3(1.02f, 0.26f, 1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(-0.52f, 0.18f, 1.32f), new Vector3(-1.02f, 0.22f, 1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(0.52f, 0.18f, 1.32f), new Vector3(1.02f, 0.22f, 1.35f), armMaterial);

            // Rear
            CreateSuspensionArm(parent, new Vector3(-0.52f, 0.32f, -1.34f), new Vector3(-1.02f, 0.26f, -1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(0.52f, 0.32f, -1.34f), new Vector3(1.02f, 0.26f, -1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(-0.52f, 0.18f, -1.34f), new Vector3(-1.02f, 0.22f, -1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(0.52f, 0.18f, -1.34f), new Vector3(1.02f, 0.22f, -1.35f), armMaterial);

            // Brake assemblies
            CreateChildCube(parent, "brake fl", new Vector3(-1.02f, 0.26f, 1.35f), new Vector3(0.12f, 0.22f, 0.22f), detailMaterial);
            CreateChildCube(parent, "brake fr", new Vector3(1.02f, 0.26f, 1.35f), new Vector3(0.12f, 0.22f, 0.22f), detailMaterial);
            CreateChildCube(parent, "brake rl", new Vector3(-1.02f, 0.26f, -1.35f), new Vector3(0.12f, 0.22f, 0.22f), detailMaterial);
            CreateChildCube(parent, "brake rr", new Vector3(1.02f, 0.26f, -1.35f), new Vector3(0.12f, 0.22f, 0.22f), detailMaterial);
        }

        static void CreateSuspensionArm(Transform parent, Vector3 a, Vector3 b, Material material)
        {
            Vector3 midpoint = (a + b) * 0.5f;
            Vector3 delta = b - a;
            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "suspension arm";
            arm.transform.SetParent(parent);
            arm.transform.localPosition = midpoint;
            arm.transform.localRotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            arm.transform.localScale = new Vector3(0.035f, 0.035f, delta.magnitude);
            arm.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = arm.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }
        }

        public static void CreateDriverLabel(Transform parent, string driverName, Color color)
        {
            GameObject labelObject = new GameObject("driver label");
            labelObject.transform.SetParent(parent);
            labelObject.transform.localPosition = new Vector3(0f, 0.96f, -0.22f);
            labelObject.transform.localRotation = Quaternion.Euler(76f, 0f, 0f);
            TextMesh text = labelObject.AddComponent<TextMesh>();
            text.text = driverName.Length > 3 ? driverName.Substring(0, 3).ToUpper() : driverName.ToUpper();
            text.fontSize = 38;
            text.characterSize = 0.055f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.Lerp(color, Color.white, 0.35f);
        }

        public static Material CreateMaterial(string materialName, Color color)
        {
            return CreateMaterial(materialName, color, 0f, 0.35f);
        }

        static PhysicMaterial GetCarBodyPhysicsMaterial()
        {
            if (carBodyPhysicsMaterial != null)
            {
                return carBodyPhysicsMaterial;
            }

            carBodyPhysicsMaterial = new PhysicMaterial("Open wheel low-friction body");
            carBodyPhysicsMaterial.dynamicFriction = 0.02f;
            carBodyPhysicsMaterial.staticFriction = 0.02f;
            carBodyPhysicsMaterial.bounciness = 0f;
            carBodyPhysicsMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
            carBodyPhysicsMaterial.bounceCombine = PhysicMaterialCombine.Minimum;
            return carBodyPhysicsMaterial;
        }

        public static Material CreateMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            Material material = F1Game.Rendering.ShaderCompat.CreateLitMaterial();
            material.name = materialName;
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            F1Game.Rendering.ShaderCompat.SetSmoothness(material, smoothness);
            return material;
        }

        public static Material CreateMaterial(string materialName, Color color, float metallic, float smoothness, Color emission)
        {
            Material material = CreateMaterial(materialName, color, metallic, smoothness);
            if (emission.r > 0f || emission.g > 0f || emission.b > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
            }

            return material;
        }
    }
}
