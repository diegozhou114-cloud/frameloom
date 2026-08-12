using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat.PlayModeTests
{
    public sealed class UnityEntityViewFactoryPlayModeTests
    {
        [TearDown]
        public void TearDown() => CombatPoolTestSupport.CleanupScene();

        [Test]
        public void SameArchetype_DestroyThenCreate_ReusesSameViewAndGameObject()
        {
            var factory = new UnityEntityViewFactory(CombatPoolTestSupport.LoadConfig());
            IEntityView first = factory.Create(new EntityPresentationKey(1, 1), 1);
            GameObject firstObject = CombatPoolTestSupport.GameObjectOf(first);
            factory.Destroy(first);
            IEntityView second = factory.Create(new EntityPresentationKey(2, 1), 1);
            Assert.That(second, Is.SameAs(first));
            Assert.That(CombatPoolTestSupport.GameObjectOf(second), Is.SameAs(firstObject));
            CombatPoolTestSupport.DisposeIfSupported(factory);
        }

        [Test]
        public void DifferentConfiguredArchetypes_DoNotSharePools()
        {
            var factory = new UnityEntityViewFactory(CombatPoolTestSupport.LoadConfig());
            IEntityView first = factory.Create(new EntityPresentationKey(1, 1), 1);
            factory.Destroy(first);
            IEntityView other = factory.Create(new EntityPresentationKey(2, 1), 2);
            factory.Destroy(other);
            IEntityView reused = factory.Create(new EntityPresentationKey(3, 1), 1);
            Assert.That(reused, Is.SameAs(first));
            Assert.That(reused, Is.Not.SameAs(other));
            CombatPoolTestSupport.DisposeIfSupported(factory);
        }

        [Test]
        public void UnknownArchetypes_ShareFallbackPool()
        {
            var factory = new UnityEntityViewFactory(CombatPoolTestSupport.LoadConfig());
            IEntityView first = factory.Create(new EntityPresentationKey(1, 1), 1001);
            factory.Destroy(first);
            IEntityView second = factory.Create(new EntityPresentationKey(2, 1), 2002);
            Assert.That(second, Is.SameAs(first));
            CombatPoolTestSupport.DisposeIfSupported(factory);
        }

        [Test]
        public void RetentionLimit_Keeps64AndDestroysThe65th()
        {
            var factory = new UnityEntityViewFactory(CombatPoolTestSupport.LoadConfig());
            var leased = new List<IEntityView>();
            var originalIds = new HashSet<int>();
            for (int index = 0; index < 65; index++)
            {
                IEntityView view = factory.Create(new EntityPresentationKey(index, 1), 1);
                leased.Add(view);
                originalIds.Add(CombatPoolTestSupport.GameObjectOf(view).GetInstanceID());
            }
            foreach (IEntityView view in leased) factory.Destroy(view);
            int reused = 0;
            for (int index = 0; index < 65; index++)
            {
                IEntityView view = factory.Create(new EntityPresentationKey(index, 2), 1);
                if (originalIds.Contains(CombatPoolTestSupport.GameObjectOf(view)
                    .GetInstanceID())) reused++;
            }
            Assert.That(reused, Is.EqualTo(64));
            CombatPoolTestSupport.DisposeIfSupported(factory);
        }

        [Test]
        public void ForeignAndRepeatedDestroy_DoNotReturnWrongPool()
        {
            var factory = new UnityEntityViewFactory(CombatPoolTestSupport.LoadConfig());
            var foreign = new DisposableEntityViewFactory().Create(default, 0);
            IEntityView leased = factory.Create(new EntityPresentationKey(1, 1), 1);
            Assert.DoesNotThrow(() => factory.Destroy(foreign));
            Assert.DoesNotThrow(() => factory.Destroy(leased));
            Assert.DoesNotThrow(() => factory.Destroy(leased));
            IEntityView reused = factory.Create(new EntityPresentationKey(2, 1), 1);
            Assert.That(reused, Is.SameAs(leased));
            CombatPoolTestSupport.DisposeIfSupported(factory);
        }

        [UnityTest]
        public IEnumerator FactoryDispose_DestroysAvailableCheckedOutRootAndRejectsCreate()
        {
            var factory = new UnityEntityViewFactory(CombatPoolTestSupport.LoadConfig());
            IEntityView available = factory.Create(new EntityPresentationKey(1, 1), 1);
            factory.Destroy(available);
            factory.Create(new EntityPresentationKey(2, 1), 2);
            Assert.That(GameObject.Find("[CombatViewPool]"), Is.Not.Null);
            Assert.That((object)factory, Is.InstanceOf<IDisposable>());
            ((IDisposable)(object)factory).Dispose();
            Assert.Throws<ObjectDisposedException>(() =>
                factory.Create(new EntityPresentationKey(3, 1), 1));
            Assert.DoesNotThrow(() => factory.Destroy(available));
            yield return null;
            Assert.That(GameObject.Find("[CombatViewPool]"), Is.Null);
            Assert.DoesNotThrow(() => factory.Destroy(available));
        }
    }
}
