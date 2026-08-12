using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class GameObjectPoolExternalDestroyTests : RuntimeTestBase
    {
        [Test]
        public void LegacyThreeParameterConstructor_ExistsAndForwards()
        {
            ConstructorInfo constructor = typeof(GameObjectPool).GetConstructor(new[]
                { typeof(GameObject), typeof(Transform), typeof(int) });
            Assert.That(constructor, Is.Not.Null, "missing legacy CLR constructor");
            GameObject prefab = CreateOwnedObject("LegacyPoolPrefab");
            var pool = (GameObjectPool)constructor.Invoke(
                new object[] { prefab, null, 1 });
            Assert.That(pool.Count, Is.EqualTo(1));
            pool.Dispose();
        }

        [UnityTest]
        public IEnumerator Release_PrunesDestroyedRetainedBeforeCapacityDecision()
        {
            GameObjectPool pool = NewPool("PruneRetained", 1);
            GameObject first = pool.Get();
            GameObject second = pool.Get();
            pool.Release(first);
            UnityEngine.Object.Destroy(first);
            yield return null;

            pool.Release(second);
            Assert.That(pool.Count, Is.EqualTo(1));
            Assert.That(pool.Get(), Is.SameAs(second));
            pool.Dispose();
        }

        [UnityTest]
        public IEnumerator DestroyedCheckedOut_RemainsOwnedAcrossGetAndClearUntilRelease()
        {
            GameObjectPool pool = NewPool("DestroyedCheckedOut", 1);
            GameObject destroyed = pool.Get();
            UnityEngine.Object.Destroy(destroyed);
            yield return null;

            GameObject live = pool.Get();
            pool.Clear();
            Assert.DoesNotThrow(() => pool.Release(destroyed));
            pool.Release(live);
            pool.Dispose();
        }

        [UnityTest]
        public IEnumerator Release_WhenRetentionIsFullDestroysLiveOverflow()
        {
            GameObjectPool pool = NewPool("Overflow", 1);
            GameObject retained = pool.Get();
            GameObject overflow = pool.Get();
            pool.Release(retained);
            pool.Release(overflow);
            Assert.That(pool.Count, Is.EqualTo(1));
            Assert.That(pool.Get(), Is.SameAs(retained));
            yield return null;
            Assert.That(overflow == null, Is.True);
            pool.Dispose();
        }

        private GameObjectPool NewPool(string name, int maxRetained)
        {
            GameObject prefab = CreateOwnedObject(name + "Prefab");
            return new GameObjectPool(prefab, null, 0, maxRetained);
        }
    }
}
