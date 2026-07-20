using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace F1Game.Art
{
    /// <summary>
    /// Reusable asynchronous runtime GLB loader built on glTFast's runtime API.
    ///
    ///   * Each GLB is loaded once into an inactive, parked TEMPLATE GameObject
    ///     (kept across scene loads under a DontDestroyOnLoad host). Concurrent
    ///     requests for the same file share one in-flight Task (no double-load).
    ///   * Placement code clones the template with <see cref="Instantiate"/>
    ///     (cheap <c>Object.Instantiate</c>) instead of re-running glTFast per copy
    ///     — the "safe reusable template" approach the brief calls for.
    ///   * On load, every non-LOD0 node (`_LOD1/_LOD2/_COL`) is stripped so the raw
    ///     multi-LOD/collision GLB renders as a single clean mesh.
    ///   * Cancellation-aware: a token cancels mid-load; teardown between tracks is
    ///     the caller destroying the clones (templates stay cached for reuse).
    ///   * Failures are reported clearly and never throw into the caller's frame.
    /// </summary>
    public static class RuntimeGltfLoader
    {
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Task<GameObject>> inFlight = new Dictionary<string, Task<GameObject>>();
        static Transform host;

        static Transform Host
        {
            get
            {
                if (host == null)
                {
                    var go = new GameObject("__RuntimeArtTemplates__");
                    // Parked far below the world; templates are also SetActive(false).
                    go.transform.position = new Vector3(0f, -100000f, 0f);
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    host = go.transform;
                }
                return host;
            }
        }

        /// <summary>
        /// Get (loading once, then caching) the template for a GLB. Returns null on
        /// failure or cancellation.
        /// </summary>
        public static async Task<GameObject> GetTemplateAsync(string absPath, CancellationToken ct)
        {
            if (templates.TryGetValue(absPath, out GameObject cached) && cached != null)
            {
                return cached;
            }
            if (inFlight.TryGetValue(absPath, out Task<GameObject> pending))
            {
                return await pending;
            }

            Task<GameObject> task = LoadAsync(absPath, ct);
            inFlight[absPath] = task;
            try
            {
                GameObject result = await task;
                return result;
            }
            finally
            {
                inFlight.Remove(absPath);
            }
        }

        static async Task<GameObject> LoadAsync(string absPath, CancellationToken ct)
        {
            try
            {
                if (!File.Exists(absPath))
                {
                    Debug.LogWarning("[RuntimeArt] GLB not found: " + absPath);
                    return null;
                }

                byte[] data = File.ReadAllBytes(absPath);
                if (ct.IsCancellationRequested) return null;

                var import = new GltfImport();
                bool loaded = await import.LoadGltfBinary(data);
                if (!loaded || ct.IsCancellationRequested)
                {
                    Debug.LogWarning("[RuntimeArt] glTFast failed to parse: " + absPath);
                    import.Dispose();
                    return null;
                }

                var holder = new GameObject(Path.GetFileNameWithoutExtension(absPath) + "_Template");
                holder.transform.SetParent(Host, false);
                bool instantiated = await import.InstantiateMainSceneAsync(holder.transform);
                import.Dispose();

                if (!instantiated || ct.IsCancellationRequested)
                {
                    Debug.LogWarning("[RuntimeArt] glTFast failed to instantiate: " + absPath);
                    UnityEngine.Object.Destroy(holder);
                    return null;
                }

                StripToLod0(holder);
                holder.SetActive(false);
                templates[absPath] = holder;
                return holder;
            }
            catch (Exception e)
            {
                Debug.LogError("[RuntimeArt] load exception for " + absPath + ": " + e);
                return null;
            }
        }

        /// <summary>Remove LOD1/LOD2 renderers and the collision proxy mesh from a template.</summary>
        static void StripToLod0(GameObject template)
        {
            var toDestroy = new List<GameObject>();
            foreach (Transform t in template.GetComponentsInChildren<Transform>(true))
            {
                string n = t.gameObject.name;
                if (n.Contains("_LOD1") || n.Contains("_LOD2") || n.Contains("_COL"))
                {
                    toDestroy.Add(t.gameObject);
                }
            }
            foreach (GameObject g in toDestroy)
            {
                if (g != null) UnityEngine.Object.Destroy(g);
            }
        }

        /// <summary>Clone a template into the scene (active, correctly parented).</summary>
        public static GameObject Instantiate(GameObject template, Transform parent,
            Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (template == null) return null;
            GameObject clone = UnityEngine.Object.Instantiate(template, position, rotation, parent);
            clone.transform.localScale = scale;
            SetActiveRecursively(clone);
            return clone;
        }

        static void SetActiveRecursively(GameObject go)
        {
            // Templates are parked inactive, so clones start inactive; wake them up.
            go.SetActive(true);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.SetActive(true);
            }
        }
    }
}
