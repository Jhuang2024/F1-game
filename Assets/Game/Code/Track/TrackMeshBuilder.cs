using UnityEngine;
using F1Game.Rendering;

namespace F1Game.Track
{
    /// <summary>
    /// Builds the road ribbon mesh (and a wider runoff apron) from a sampled
    /// centerline, with a MeshCollider for physics. Uses the shared URP surface
    /// materials from <see cref="MaterialLibrary"/>. This is the authored-track
    /// mesh path — a pure function of the sampled data, not procedural constants
    /// buried in a manager.
    /// </summary>
    public static class TrackMeshBuilder
    {
        public static GameObject BuildRoad(TrackSplineSampler sampler, Transform parent)
        {
            var road = new GameObject("Road");
            road.transform.SetParent(parent, false);

            var samples = sampler.Samples;
            int n = samples.Count;
            if (n < 2)
            {
                return road;
            }

            bool closed = sampler.ClosedLoop;
            int ringCount = closed ? n + 1 : n;

            var verts = new Vector3[ringCount * 2];
            var uvs = new Vector2[ringCount * 2];
            var tris = new int[(ringCount - 1) * 6];

            for (int i = 0; i < ringCount; i++)
            {
                TrackSplineSampler.Sample s = samples[i % n];
                float half = s.Width * 0.5f;
                // Camber tilts the ribbon toward the inside of the corner.
                float tilt = Mathf.Tan(s.CamberDeg * Mathf.Deg2Rad) * half;
                Vector3 left = s.Position - s.Normal * half + Vector3.up * tilt * -0.5f;
                Vector3 right = s.Position + s.Normal * half + Vector3.up * tilt * 0.5f;
                verts[i * 2] = left;
                verts[i * 2 + 1] = right;
                float v = s.Distance / 12f; // tile every ~12 m
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(1f, v);
            }

            int t = 0;
            for (int i = 0; i < ringCount - 1; i++)
            {
                int a = i * 2;
                int b = i * 2 + 1;
                int c = (i + 1) * 2;
                int d = (i + 1) * 2 + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }

            var mesh = new Mesh { name = "AuthoredRoad" };
            mesh.indexFormat = ringCount * 2 > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var filter = road.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = road.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialLibrary.Get(MaterialLibrary.Slot.Asphalt);
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;

            var collider = road.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            return road;
        }
    }
}
