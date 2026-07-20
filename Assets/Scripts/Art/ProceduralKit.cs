using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Art
{
    /// <summary>
    /// Builds the trackside module geometry directly in code at runtime — no
    /// glTFast, no async, no file IO. Each module type is built once into an
    /// inactive template (parked under a hidden host) and cheaply cloned per
    /// placement. Shapes mirror the committed GLB kit (which was itself procedural
    /// primitives); solid URP/Lit materials keep the whole thing dependency-free so
    /// it always renders. Modules are authored length-along-local-X, height along
    /// +Y, lateral along Z (matches RuntimeTrackArt.OrientAlong).
    /// </summary>
    public static class ProceduralKit
    {
        // Module type keys
        public const string Concrete = "concrete";
        public const string Armco = "armco";
        public const string Tecpro = "tecpro";
        public const string TyreWall = "tyre";
        public const string CatchFence = "fence";
        public const string KerbPainted = "kerb";
        public const string KerbSausage = "sausage";
        public const string StartGantry = "gantry";
        public const string MarshalPost = "marshal";
        public const string CameraTower = "camera";
        public const string LightTower = "lighttower";
        public const string BrakingBoard = "brakeboard";
        public const string Grandstand = "grandstand";
        public const string Garage = "garage";
        public const string Cabinet = "cabinet";
        public const string Hut = "hut";
        public const string Cart = "cart";
        public const string Speaker = "speaker";

        static Transform host;
        static Mesh cubeMesh;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();

        static Transform Host
        {
            get
            {
                if (host == null)
                {
                    var go = new GameObject("__ProcKitTemplates__");
                    go.SetActive(false);
                    Object.DontDestroyOnLoad(go);
                    host = go.transform;
                }
                return host;
            }
        }

        // -- shared cube mesh: use Unity's built-in primitive so winding, normals
        //    and UVs are guaranteed correct (a hand-rolled cube risks inside-out
        //    faces that back-face-cull to invisibility). The built-in "Cube" mesh
        //    is a shared engine asset, so destroying the temp object keeps it alive.
        static Mesh Cube
        {
            get
            {
                if (cubeMesh != null) return cubeMesh;
                var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                temp.hideFlags = HideFlags.HideAndDontSave;
                cubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(temp);
                return cubeMesh;
            }
        }

        static Material Solid(string key, Color c, float metallic, float smooth, Color? emission = null)
        {
            if (mats.TryGetValue(key, out Material cached) && cached != null) return cached;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh) { name = "Proc_" + key };
            mat.color = c;
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smooth);
            mat.SetFloat("_Glossiness", smooth);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", emission.Value);
            }
            mat.enableInstancing = true;
            mats[key] = mat;
            return mat;
        }

        // palette
        static Material MConcrete => Solid("concrete", new Color(0.66f, 0.65f, 0.62f), 0f, 0.25f);
        static Material MSteel => Solid("steel", new Color(0.55f, 0.56f, 0.58f), 0.9f, 0.5f);
        static Material MGalv => Solid("galv", new Color(0.62f, 0.63f, 0.64f), 0.85f, 0.45f);
        static Material MRubber => Solid("rubber", new Color(0.06f, 0.06f, 0.065f), 0f, 0.2f);
        static Material MRed => Solid("red", new Color(0.6f, 0.09f, 0.09f), 0f, 0.4f);
        static Material MWhite => Solid("white", new Color(0.85f, 0.85f, 0.85f), 0f, 0.4f);
        static Material MYellow => Solid("yellow", new Color(0.8f, 0.62f, 0.05f), 0f, 0.4f);
        static Material MBlue => Solid("blue", new Color(0.1f, 0.22f, 0.5f), 0f, 0.4f);
        static Material MDark => Solid("dark", new Color(0.16f, 0.17f, 0.2f), 0f, 0.4f);
        static Material MTan => Solid("tan", new Color(0.5f, 0.42f, 0.3f), 0f, 0.5f);
        static Material MEmis => Solid("emis", new Color(0.9f, 0.9f, 0.9f), 0f, 0.5f, new Color(1.2f, 1.2f, 1.1f));

        static GameObject Box(Transform parent, Vector3 center, Vector3 size, Material mat)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = Cube;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Get (build-once) the inactive template for a module type.</summary>
        public static GameObject GetTemplate(string type)
        {
            if (templates.TryGetValue(type, out GameObject t) && t != null) return t;
            var root = new GameObject("Proc_" + type);
            root.transform.SetParent(Host, false);
            Build(type, root.transform);
            templates[type] = root;
            return root;
        }

        static void Build(string t, Transform p)
        {
            switch (t)
            {
                case Concrete:
                    Box(p, new(0, 0.45f, 0), new(3f, 0.9f, 0.35f), MConcrete);
                    break;
                case Armco:
                    Box(p, new(0, 0.6f, 0), new(4f, 0.28f, 0.06f), MSteel);
                    Box(p, new(-1.6f, 0.3f, 0), new(0.1f, 0.6f, 0.1f), MSteel);
                    Box(p, new(1.6f, 0.3f, 0), new(0.1f, 0.6f, 0.1f), MSteel);
                    break;
                case Tecpro:
                    Box(p, new(0, 0.45f, 0), new(1f, 0.9f, 0.55f), MBlue);
                    break;
                case TyreWall:
                    Box(p, new(0, 0.3f, 0), new(2f, 0.6f, 0.5f), MRubber);
                    Box(p, new(0, 0.85f, 0), new(2f, 0.5f, 0.45f), MRubber);
                    break;
                case CatchFence:
                    Box(p, new(-2.4f, 1.5f, 0), new(0.1f, 3f, 0.1f), MGalv);
                    Box(p, new(2.4f, 1.5f, 0), new(0.1f, 3f, 0.1f), MGalv);
                    Box(p, new(0, 1.55f, 0), new(5f, 2.9f, 0.03f), MDark);
                    break;
                case KerbPainted:
                    for (int i = 0; i < 8; i++)
                        Box(p, new(-1.75f + i * 0.5f, 0.03f, 0), new(0.5f, 0.06f, 0.5f), (i % 2 == 0) ? MRed : MWhite);
                    break;
                case KerbSausage:
                    Box(p, new(0, 0.07f, 0), new(1f, 0.14f, 0.14f), MYellow);
                    break;
                case StartGantry:
                    Box(p, new(-8f, 3.5f, 0), new(0.4f, 7f, 0.4f), MSteel);
                    Box(p, new(8f, 3.5f, 0), new(0.4f, 7f, 0.4f), MSteel);
                    Box(p, new(0, 7.2f, 0), new(16.8f, 0.6f, 0.6f), MSteel);
                    for (int i = 0; i < 5; i++)
                        Box(p, new(-4f + i * 2f, 6.7f, 0.35f), new(1.1f, 0.6f, 0.2f), MDark);
                    break;
                case MarshalPost:
                    Box(p, new(0, 0.02f, 0), new(2.2f, 0.04f, 1.6f), MConcrete);
                    Box(p, new(-0.9f, 1.2f, 0), new(0.1f, 2.4f, 0.1f), MGalv);
                    Box(p, new(0.9f, 1.2f, 0), new(0.1f, 2.4f, 0.1f), MGalv);
                    Box(p, new(0, 2.5f, 0), new(2.4f, 0.12f, 1.8f), MYellow);
                    break;
                case CameraTower:
                    Box(p, new(0, 3f, 0), new(0.35f, 6f, 0.35f), MSteel);
                    Box(p, new(0, 6.1f, 0), new(1.4f, 0.1f, 1.4f), MSteel);
                    Box(p, new(0, 6.4f, 0.5f), new(0.4f, 0.3f, 0.4f), MDark);
                    break;
                case LightTower:
                    Box(p, new(0, 5f, 0), new(0.35f, 10f, 0.35f), MGalv);
                    Box(p, new(0, 9.8f, 0.3f), new(2f, 0.5f, 0.4f), MEmis);
                    break;
                case BrakingBoard:
                    Box(p, new(0, 1f, 0), new(0.08f, 2f, 0.08f), MGalv);
                    Box(p, new(0, 1.7f, 0.05f), new(0.7f, 0.9f, 0.05f), MWhite);
                    break;
                case Grandstand:
                    for (int i = 0; i < 8; i++)
                    {
                        Box(p, new(0, i * 0.55f, -i * 0.9f), new(12f, 0.1f, 0.9f), MConcrete);
                        Box(p, new(0, i * 0.55f + 0.25f, -i * 0.9f - 0.35f), new(12f, 0.4f, 0.08f), MBlue);
                    }
                    break;
                case Garage:
                    Box(p, new(0, 0.02f, 3f), new(6f, 0.04f, 6f), MConcrete);
                    Box(p, new(0, 3.5f, 3f), new(6f, 0.15f, 6f), MWhite);
                    Box(p, new(0, 1.75f, 6f), new(6f, 3.5f, 0.2f), MDark);
                    Box(p, new(-3f, 1.75f, 3f), new(0.2f, 3.5f, 6f), MWhite);
                    Box(p, new(3f, 1.75f, 3f), new(0.2f, 3.5f, 6f), MWhite);
                    break;
                case Cabinet:
                    Box(p, new(0, 0.6f, 0), new(0.8f, 1.2f, 0.5f), MYellow);
                    break;
                case Hut:
                    Box(p, new(0, 1.2f, 0), new(2f, 2.4f, 2f), MTan);
                    Box(p, new(0, 2.5f, 0), new(2.3f, 0.15f, 2.3f), MDark);
                    break;
                case Cart:
                    Box(p, new(0, 0.6f, 0), new(1.6f, 0.5f, 0.8f), MYellow);
                    Box(p, new(0.6f, 1.1f, 0), new(0.3f, 0.5f, 0.7f), MDark);
                    break;
                case Speaker:
                    Box(p, new(0, 2.5f, 0), new(0.1f, 5f, 0.1f), MGalv);
                    Box(p, new(0, 4.6f, 0.3f), new(0.3f, 0.4f, 0.3f), MDark);
                    Box(p, new(0, 4.6f, -0.3f), new(0.3f, 0.4f, 0.3f), MDark);
                    break;
                default:
                    Box(p, new(0, 0.5f, 0), new(1f, 1f, 1f), MConcrete);
                    break;
            }
        }
    }
}
