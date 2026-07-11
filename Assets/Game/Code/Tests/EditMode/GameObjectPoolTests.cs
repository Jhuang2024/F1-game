using F1Game.Core.Pooling;
using NUnit.Framework;
using UnityEngine;

namespace F1Game.Tests
{
    /// <summary>
    /// GameObjectPool backs the pooled VFX and HUD rows, so its reuse accounting
    /// (prewarm, take/return, grow-on-demand, no-op on foreign returns) is pinned
    /// here. Instances are created/destroyed within the test.
    /// </summary>
    public class GameObjectPoolTests
    {
        GameObject template;
        Transform root;

        [SetUp]
        public void SetUp()
        {
            template = new GameObject("PoolTemplate");
            root = new GameObject("PoolRoot").transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (template != null) Object.DestroyImmediate(template);
            if (root != null) Object.DestroyImmediate(root.gameObject);
        }

        [Test]
        public void PrewarmCreatesAvailableInstances()
        {
            var pool = new GameObjectPool(template, root, prewarm: 3);
            Assert.AreEqual(3, pool.CountAvailable);
            Assert.AreEqual(0, pool.CountLive);
        }

        [Test]
        public void TakeReusesPrewarmedThenReturnRecycles()
        {
            var pool = new GameObjectPool(template, root, prewarm: 2);
            GameObject a = pool.Take(Vector3.zero, Quaternion.identity);
            Assert.AreEqual(1, pool.CountLive);
            Assert.AreEqual(1, pool.CountAvailable);
            Assert.IsTrue(a.activeSelf);

            pool.Return(a);
            Assert.AreEqual(0, pool.CountLive);
            Assert.AreEqual(2, pool.CountAvailable);
            Assert.IsFalse(a.activeSelf);

            // The next take reuses the same instance (no growth).
            GameObject b = pool.Take(Vector3.one, Quaternion.identity);
            Assert.AreSame(a, b);
        }

        [Test]
        public void TakeBeyondAvailableGrowsThePool()
        {
            var pool = new GameObjectPool(template, root, prewarm: 1);
            pool.Take(Vector3.zero, Quaternion.identity);
            pool.Take(Vector3.zero, Quaternion.identity); // grows: creates a new instance
            Assert.AreEqual(2, pool.CountLive);
            Assert.AreEqual(0, pool.CountAvailable);
        }

        [Test]
        public void ReturnAllRecyclesEverything()
        {
            var pool = new GameObjectPool(template, root, prewarm: 0);
            pool.Take(Vector3.zero, Quaternion.identity);
            pool.Take(Vector3.zero, Quaternion.identity);
            pool.Take(Vector3.zero, Quaternion.identity);
            Assert.AreEqual(3, pool.CountLive);

            pool.ReturnAll();
            Assert.AreEqual(0, pool.CountLive);
            Assert.AreEqual(3, pool.CountAvailable);
        }

        [Test]
        public void ReturningAForeignObjectIsANoOp()
        {
            var pool = new GameObjectPool(template, root, prewarm: 1);
            var foreign = new GameObject("Foreign");
            pool.Return(foreign);
            Assert.AreEqual(0, pool.CountLive);
            Assert.AreEqual(1, pool.CountAvailable);
            Object.DestroyImmediate(foreign);
        }
    }
}
