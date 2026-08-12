using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.SimulationCore;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Combat.PlayModeTests
{
    public sealed class CombatManagerPoolLifecyclePlayModeTests
    {
        [TearDown]
        public void TearDown() => CombatPoolTestSupport.CleanupScene();

        [Test]
        public void CombatManager_StopBattleKeepsProductionFactoryForNextBattle()
        {
            CombatManager manager = CombatPoolTestSupport.CreateManager(
                "CombatPoolCrossBattleHost");
            GameObject host = manager.gameObject;
            try
            {
                UnityEntityViewFactory factory =
                    CombatPoolTestSupport.ProductionFactory(manager);
                UniTask<SimBattleResult> first = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                HashSet<int> firstIds = CombatPoolTestSupport.ActiveViewIds(manager);
                manager.StopBattle();
                Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Canceled));
                Assert.That(manager.ActiveViewCount, Is.Zero);
                UniTask<SimBattleResult> second = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                HashSet<int> secondIds = CombatPoolTestSupport.ActiveViewIds(manager);
                firstIds.IntersectWith(secondIds);
                Assert.That(CombatPoolTestSupport.ProductionFactory(manager), Is.SameAs(factory));
                Assert.That(firstIds.Count, Is.EqualTo(secondIds.Count));
                manager.StopBattle();
                Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Canceled));
                Assert.That(manager.ActiveViewCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator CombatManager_OnDestroyDisposesProductionFactory()
        {
            CombatManager manager = CombatPoolTestSupport.CreateManager(
                "CombatPoolDisposeHost");
            GameObject host = manager.gameObject;
            UnityEntityViewFactory factory = CombatPoolTestSupport.ProductionFactory(manager);
            try
            {
                manager.StartBattleAsync(SessionPlayModeFixture.VillageAttackParams(),
                    CancellationToken.None).Forget();
                Assert.That(GameObject.Find("[CombatViewPool]"), Is.Not.Null);
                Object.DestroyImmediate(host);
                Assert.Throws<ObjectDisposedException>(() => factory.Create(
                    new EntityPresentationKey(50, 1), 1));
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
            }
            yield return null;
            Assert.That(GameObject.Find("[CombatViewPool]"), Is.Null);
        }

        [Test]
        public void ConfigureForTests_DoesNotOwnInjectedFactory()
        {
            CombatManager manager = CombatPoolTestSupport.CreateManager(
                "CombatPoolConfigureHost");
            GameObject host = manager.gameObject;
            var injected = new DisposableEntityViewFactory();
            try
            {
                UnityEntityViewFactory production =
                    CombatPoolTestSupport.ProductionFactory(manager);
                production.Create(new EntityPresentationKey(1, 1), 1);
                manager.ConfigureForTests(new SimBattleSessionFactory(),
                    FrameworkBattleContent.CanonicalBytes(), injected);
                Assert.Throws<ObjectDisposedException>(() => production.Create(
                    new EntityPresentationKey(2, 1), 1));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
            Assert.That(injected.DisposeCalls, Is.Zero);
        }

        [Test]
        public void ConfigureForTests_CleanupFailureCommitsReplacementAndDisposesOwnedFactory()
        {
            CombatManager manager = CombatPoolTestSupport.CreateManager(
                "CombatPoolCleanupFailureHost");
            GameObject host = manager.gameObject;
            UnityEntityViewFactory production =
                CombatPoolTestSupport.ProductionFactory(manager);
            var failure = new InvalidOperationException("old bridge cleanup failed");
            var throwing = new ThrowingDestroyEntityViewFactory(production, failure);
            var injected = new DisposableEntityViewFactory();
            var replacementSessionFactory = new SimBattleSessionFactory();
            var replacementLogger = new SilentSimLogger();
            byte[] replacementManifest = FrameworkBattleContent.CanonicalBytes();
            var oldBridge = new SessionPresentationBridge(throwing);
            oldBridge.Apply(SessionPlayModeFixture.InitialFrame(1));
            CombatPoolTestSupport.SetPresentation(manager, oldBridge);
            try
            {
                Exception actual = Assert.Throws<InvalidOperationException>(() =>
                    manager.ConfigureForTests(replacementSessionFactory,
                        replacementManifest, injected, replacementLogger));

                Assert.That(actual, Is.SameAs(failure));
                Assert.That(CombatPoolTestSupport.Field(manager, "_sessionFactory"),
                    Is.SameAs(replacementSessionFactory));
                byte[] installedManifest = (byte[])CombatPoolTestSupport.Field(
                    manager, "_canonicalManifestBytes");
                Assert.That(installedManifest, Is.EqualTo(replacementManifest));
                Assert.That(installedManifest, Is.Not.SameAs(replacementManifest));
                Assert.That(CombatPoolTestSupport.Field(manager, "_battleInitFactory"),
                    Is.TypeOf<BattleInitFactory>());
                Assert.That(CombatPoolTestSupport.PresentationFactory(manager),
                    Is.SameAs(injected));
                Assert.That(CombatPoolTestSupport.Field(manager, "_logger"),
                    Is.SameAs(replacementLogger));
                Assert.That(CombatPoolTestSupport.Field(manager, "_ownedViewFactory"),
                    Is.Null);
                Assert.Throws<ObjectDisposedException>(() => production.Create(
                    new EntityPresentationKey(2, 1), 1));
                UniTask<SimBattleResult> battle = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                Assert.That(injected.CreateCalls, Is.GreaterThan(0));
                manager.StopBattle();
                Assert.That(battle.Status, Is.EqualTo(UniTaskStatus.Canceled));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
            Assert.That(throwing.DestroyCalls, Is.EqualTo(1));
            Assert.That(injected.DisposeCalls, Is.Zero);
        }

        [Test]
        public void ConfigureForTests_ReplacementConstructionFailureLeavesConfigurationUntouched()
        {
            CombatManager manager = CombatPoolTestSupport.CreateManager(
                "CombatPoolConstructionFailureHost");
            GameObject host = manager.gameObject;
            var injected = new DisposableEntityViewFactory();
            object sessionFactory = CombatPoolTestSupport.Field(manager, "_sessionFactory");
            object manifest = CombatPoolTestSupport.Field(manager, "_canonicalManifestBytes");
            object initFactory = CombatPoolTestSupport.Field(manager, "_battleInitFactory");
            object presentation = CombatPoolTestSupport.Field(manager, "_presentation");
            object logger = CombatPoolTestSupport.Field(manager, "_logger");
            object ownedFactory = CombatPoolTestSupport.Field(manager, "_ownedViewFactory");
            try
            {
                Assert.Throws<FormatException>(() => manager.ConfigureForTests(
                    new SimBattleSessionFactory(), new byte[] { 1 }, injected));
                Assert.That(CombatPoolTestSupport.Field(manager, "_sessionFactory"),
                    Is.SameAs(sessionFactory));
                Assert.That(CombatPoolTestSupport.Field(manager, "_canonicalManifestBytes"),
                    Is.SameAs(manifest));
                Assert.That(CombatPoolTestSupport.Field(manager, "_battleInitFactory"),
                    Is.SameAs(initFactory));
                Assert.That(CombatPoolTestSupport.Field(manager, "_presentation"),
                    Is.SameAs(presentation));
                Assert.That(CombatPoolTestSupport.Field(manager, "_logger"),
                    Is.SameAs(logger));
                Assert.That(CombatPoolTestSupport.Field(manager, "_ownedViewFactory"),
                    Is.SameAs(ownedFactory));
                IEntityView view = ((UnityEntityViewFactory)ownedFactory).Create(
                    new EntityPresentationKey(3, 1), 1);
                ((UnityEntityViewFactory)ownedFactory).Destroy(view);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
            Assert.That(injected.DisposeCalls, Is.Zero);
        }
    }
}
