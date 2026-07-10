using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Core.Pooling
{
    /// <summary>
    /// Pooled transient VFX controller: tyre smoke, lockups, wheelspin, sparks,
    /// gravel/grass, spray, debris, impacts. Each effect kind has its own pool so
    /// nothing is Instantiated/Destroyed during a race. Real particle art is an
    /// external asset; until it lands each kind uses a clearly-labelled
    /// placeholder ParticleSystem built from a tuning table, so the spawn/pool/
    /// event pipeline is complete and only the art is swapped in later.
    /// </summary>
    public sealed class RaceVfxController : MonoBehaviour
    {
        public enum VfxKind { TyreSmoke, Lockup, Wheelspin, Sparks, Gravel, Grass, Spray, Debris, Impact }

        struct Tuning
        {
            public Color Color;
            public float StartSize;
            public float StartSpeed;
            public float Lifetime;
            public int Burst;
        }

        static readonly Dictionary<VfxKind, Tuning> Table = new Dictionary<VfxKind, Tuning>
        {
            { VfxKind.TyreSmoke, new Tuning { Color = new Color(0.8f, 0.8f, 0.8f, 0.5f), StartSize = 1.4f, StartSpeed = 1.5f, Lifetime = 1.2f, Burst = 12 } },
            { VfxKind.Lockup,    new Tuning { Color = new Color(0.9f, 0.9f, 0.9f, 0.6f), StartSize = 1.1f, StartSpeed = 2.0f, Lifetime = 1.0f, Burst = 16 } },
            { VfxKind.Wheelspin, new Tuning { Color = new Color(0.7f, 0.7f, 0.7f, 0.45f), StartSize = 1.0f, StartSpeed = 1.8f, Lifetime = 0.9f, Burst = 10 } },
            { VfxKind.Sparks,    new Tuning { Color = new Color(1.3f, 0.7f, 0.2f, 1f), StartSize = 0.12f, StartSpeed = 6f, Lifetime = 0.5f, Burst = 20 } },
            { VfxKind.Gravel,    new Tuning { Color = new Color(0.5f, 0.45f, 0.38f, 0.9f), StartSize = 0.2f, StartSpeed = 4f, Lifetime = 0.8f, Burst = 14 } },
            { VfxKind.Grass,     new Tuning { Color = new Color(0.2f, 0.4f, 0.14f, 0.9f), StartSize = 0.18f, StartSpeed = 3f, Lifetime = 0.8f, Burst = 12 } },
            { VfxKind.Spray,     new Tuning { Color = new Color(0.75f, 0.8f, 0.85f, 0.4f), StartSize = 1.6f, StartSpeed = 2.5f, Lifetime = 0.9f, Burst = 18 } },
            { VfxKind.Debris,    new Tuning { Color = new Color(0.1f, 0.1f, 0.11f, 1f), StartSize = 0.15f, StartSpeed = 5f, Lifetime = 1.4f, Burst = 8 } },
            { VfxKind.Impact,    new Tuning { Color = new Color(1f, 0.85f, 0.4f, 1f), StartSize = 0.3f, StartSpeed = 7f, Lifetime = 0.4f, Burst = 24 } },
        };

        readonly Dictionary<VfxKind, GameObjectPool> pools = new Dictionary<VfxKind, GameObjectPool>();
        Transform poolRoot;

        public static RaceVfxController Create(Transform parent, int prewarmPerKind = 4)
        {
            var go = new GameObject("Race VFX (placeholder art)");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var controller = go.AddComponent<RaceVfxController>();
            controller.BuildPools(prewarmPerKind);
            return controller;
        }

        void BuildPools(int prewarm)
        {
            poolRoot = new GameObject("Parked").transform;
            poolRoot.SetParent(transform, false);
            poolRoot.gameObject.SetActive(false);

            foreach (var pair in Table)
            {
                GameObject template = BuildPlaceholderEffect(pair.Key, pair.Value);
                template.transform.SetParent(poolRoot, false);
                pools[pair.Key] = new GameObjectPool(template, poolRoot, prewarm);
            }
        }

        /// <summary>Spawns a one-shot burst of the given kind at a world position.</summary>
        public void Spawn(VfxKind kind, Vector3 position, Quaternion rotation)
        {
            if (!pools.TryGetValue(kind, out GameObjectPool pool))
            {
                return;
            }

            GameObject instance = pool.Take(position, rotation);
            var lifetime = instance.GetComponent<PooledLifetime>();
            if (lifetime == null)
            {
                lifetime = instance.AddComponent<PooledLifetime>();
            }

            lifetime.lifetimeSeconds = Table[kind].Lifetime + 0.3f;
            lifetime.Arm(pool);

            var ps = instance.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Clear();
                ps.Play();
            }
        }

        static GameObject BuildPlaceholderEffect(VfxKind kind, Tuning tuning)
        {
            var go = new GameObject("VFX_" + kind + "_PLACEHOLDER");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.playOnAwake = false;
            main.startColor = tuning.Color;
            main.startSize = tuning.StartSize;
            main.startSpeed = tuning.StartSpeed;
            main.startLifetime = tuning.Lifetime;
            main.maxParticles = 64;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)tuning.Burst) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.15f;

            return go;
        }
    }
}
