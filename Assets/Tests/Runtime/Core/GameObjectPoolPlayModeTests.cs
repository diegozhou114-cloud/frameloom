using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class GameObjectPoolPlayModeTests : RuntimeTestBase
    {
        [Test]
        public void Constructor_RejectsInvalidGameObjectPoolArgumentsAndCapacity()
        {
            Assert.Throws<ArgumentNullException>(() => new GameObjectPool(null));
            GameObject prefab = CreateOwnedObject("PoolPrefab");
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameObjectPool(prefab, preload: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewPool(prefab, null, 2, 1));
            ParameterInfo limit = Constructor.GetParameters()[3];
            Assert.That(limit.Name, Is.EqualTo("maxRetained"));
            Assert.That(limit.DefaultValue, Is.EqualTo(256));
        }

        [Test]
        public void GetRelease_PreservesActivationParentPositionAndRotation()
        {
            GameObject prefab = CreateOwnedObject("TransformPrefab");
            Transform root = CreateOwnedObject("PoolRoot").transform;
            Transform activeParent = CreateOwnedObject("ActiveRoot").transform;
            GameObjectPool pool = NewPool(prefab, root, 1, 1);
            Vector3 position = new Vector3(3f, 4f, 5f);
            Quaternion rotation = Quaternion.Euler(10f, 20f, 30f);
            GameObject item = pool.Get(position, rotation, activeParent);
            Assert.That(item.activeSelf, Is.True);
            Assert.That(item.transform.parent, Is.SameAs(activeParent));
            Assert.That(item.transform.position, Is.EqualTo(position));
            Assert.That(Quaternion.Angle(item.transform.rotation, rotation),
                Is.LessThan(0.001f));
            pool.Release(item);
            Assert.That(item.activeSelf, Is.False);
            Assert.That(item.transform.parent, Is.SameAs(root));
            ((IDisposable)pool).Dispose();
        }

        [Test]
        public void Release_RejectsNullForeignAndDuplicateGameObjects()
        {
            GameObject prefab = CreateOwnedObject("IdentityPrefab");
            GameObject foreign = CreateOwnedObject("Foreign");
            GameObjectPool pool = NewPool(prefab, null, 1, 1);
            GameObject item = pool.Get();
            Assert.Throws<ArgumentNullException>(() => pool.Release(null));
            Assert.Throws<ArgumentException>(() => pool.Release(foreign));
            pool.Release(item);
            Assert.Throws<InvalidOperationException>(() => pool.Release(item));
            ((IDisposable)pool).Dispose();
        }

        [UnityTest]
        public IEnumerator Clear_DestroysAvailableButCheckedOutCanReturn()
        {
            GameObject prefab = CreateOwnedObject("ClearPrefab");
            GameObjectPool pool = NewPool(prefab, null, 2, 2);
            GameObject checkedOut = pool.Get();
            pool.Clear();
            Assert.That(pool.Count, Is.Zero);
            Assert.That(checkedOut != null, Is.True);
            pool.Release(checkedOut);
            Assert.That(pool.Count, Is.EqualTo(1));
            ((IDisposable)pool).Dispose();
            yield return null;
            Assert.That(checkedOut == null, Is.True);
        }

        [UnityTest]
        public IEnumerator Dispose_DestroysAllOwnedAndRejectsGameObjectPoolUse()
        {
            GameObject prefab = CreateOwnedObject("DisposePrefab");
            GameObjectPool pool = NewPool(prefab, null, 2, 2);
            GameObject checkedOut = pool.Get();
            IDisposable disposable = pool as IDisposable;
            Assert.That(disposable, Is.Not.Null);
            disposable.Dispose();
            disposable.Dispose();
            Assert.Throws<ObjectDisposedException>(() => pool.Get());
            Assert.Throws<ObjectDisposedException>(() => pool.Release(checkedOut));
            yield return null;
            Assert.That(checkedOut == null, Is.True);
            Assert.That(pool.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ExternallyDestroyedGameObjects_ArePrunedSafely()
        {
            GameObject prefab = CreateOwnedObject("DestroyedPrefab");
            GameObjectPool pool = NewPool(prefab, null, 1, 1);
            GameObject available = pool.Get();
            pool.Release(available);
            UnityEngine.Object.Destroy(available);
            yield return null;
            GameObject replacement = pool.Get();
            Assert.That(replacement != null, Is.True);
            UnityEngine.Object.Destroy(replacement);
            yield return null;
            Assert.DoesNotThrow(() => pool.Release(replacement));
            Assert.DoesNotThrow(pool.Clear);
            Assert.DoesNotThrow(((IDisposable)pool).Dispose);
            Assert.That(pool.Count, Is.Zero);
        }

        [Test]
        public void SteadyStateGameObjectPool_GetReleaseDoesNotAllocateAfterWarmup()
        {
            GameObject prefab = CreateOwnedObject("AllocationPrefab");
            GameObjectPool pool = NewPool(prefab, null, 1, 1);
            for (int index = 0; index < 100; index++) pool.Release(pool.Get());
            Recorder recorder = CreateValidatedRecorder();
            recorder.enabled = true;
            for (int index = 0; index < 600; index++) pool.Release(pool.Get());
            recorder.enabled = false;
            long events = recorder.sampleBlockCount;
            ((IDisposable)pool).Dispose();
            Assert.That(events, Is.Zero, "steady GameObject Get/Release allocated");
        }

        private static ConstructorInfo Constructor => typeof(GameObjectPool).GetConstructor(
            new[] { typeof(GameObject), typeof(Transform), typeof(int), typeof(int) });

        private static GameObjectPool NewPool(GameObject prefab, Transform parent,
            int preload, int maxRetained)
        {
            Assert.That(Constructor, Is.Not.Null, "missing bounded constructor");
            try
            {
                return (GameObjectPool)Constructor.Invoke(
                    new object[] { prefab, parent, preload, maxRetained });
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static Recorder CreateValidatedRecorder()
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            Assert.That(recorder, Is.Not.Null);
            Assert.That(recorder.isValid, Is.True);
            recorder.FilterToCurrentThread();
            recorder.enabled = false;
            recorder.enabled = true;
            AllocateCalibrationBuffer();
            recorder.enabled = false;
            Assert.That(recorder.sampleBlockCount, Is.GreaterThanOrEqualTo(1));
            return recorder;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AllocateCalibrationBuffer() =>
            GC.KeepAlive(new byte[4096]);
    }
}
