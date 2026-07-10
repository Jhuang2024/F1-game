using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Core.Pooling
{
    /// <summary>
    /// Pool for transient scene objects (VFX bursts, debris, HUD notifications).
    /// Replaces the Instantiate/Destroy churn the profiler flags during incidents.
    /// The pool owns a disabled parent so pooled objects never tick while parked.
    /// </summary>
    public sealed class GameObjectPool
    {
        readonly GameObject template;
        readonly Transform parkingRoot;
        readonly Stack<GameObject> available = new Stack<GameObject>();
        readonly HashSet<GameObject> live = new HashSet<GameObject>();

        public int CountLive => live.Count;
        public int CountAvailable => available.Count;

        public GameObjectPool(GameObject template, Transform poolRoot, int prewarm)
        {
            this.template = template;
            parkingRoot = poolRoot;

            for (int i = 0; i < prewarm; i++)
            {
                GameObject instance = CreateInstance();
                instance.SetActive(false);
                available.Push(instance);
            }
        }

        public GameObject Take(Vector3 position, Quaternion rotation)
        {
            GameObject instance = null;
            while (available.Count > 0 && instance == null)
            {
                // Entries can be nulled by scene teardown; skip them.
                instance = available.Pop();
            }

            if (instance == null)
            {
                instance = CreateInstance();
            }

            Transform t = instance.transform;
            t.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            live.Add(instance);
            return instance;
        }

        public void Return(GameObject instance)
        {
            if (instance == null || !live.Remove(instance))
            {
                return;
            }

            instance.SetActive(false);
            if (parkingRoot != null)
            {
                instance.transform.SetParent(parkingRoot, false);
            }

            available.Push(instance);
        }

        public void ReturnAll()
        {
            // Snapshot: Return mutates the live set.
            var snapshot = new List<GameObject>(live);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Return(snapshot[i]);
            }
        }

        GameObject CreateInstance()
        {
            GameObject instance = Object.Instantiate(template, parkingRoot);
            instance.name = template.name;
            return instance;
        }
    }

    /// <summary>
    /// Auto-return helper: attach to a pooled effect and it hands itself back to
    /// its pool after <see cref="lifetimeSeconds"/> instead of being destroyed.
    /// </summary>
    public sealed class PooledLifetime : MonoBehaviour
    {
        public float lifetimeSeconds = 2f;

        GameObjectPool owner;
        float age;

        public void Arm(GameObjectPool pool)
        {
            owner = pool;
            age = 0f;
        }

        void OnEnable()
        {
            age = 0f;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetimeSeconds && owner != null)
            {
                owner.Return(gameObject);
            }
        }
    }
}
