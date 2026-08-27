using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.SimulationCore;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Combat.PlayModeTests
{
    public sealed class BufferedCombatManagerPlayModeTests
    {
        [UnityTest]
        public IEnumerator CombatManager_BufferedSessionAvoidsOwningTickAndHotStateReads()
        {
            var factory = new CombatSessionSpyFactory(true);
            var host = new GameObject("BufferedCombatManagerTestHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(factory, FrameworkBattleContent.CanonicalBytes(),
                new RecordingEntityViewFactory(), new SilentSimLogger());
            UniTask<SimBattleResult> completion = default;
            try
            {
                completion = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                Assert.That(completion.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Deploying));
                Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted,
                    Is.True);
                _ = manager.AttackerAliveCount;
                _ = manager.DefenderAliveCount;
                _ = manager.CurrentWave;

                var spy = (BufferedCombatSessionSpy)factory.LastSession;
                float deadline = Time.realtimeSinceStartup + 3f;
                while (spy.TickIntoCalls < 2 && completion.Status == UniTaskStatus.Pending &&
                    Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(spy.TickCalls, Is.Zero);
                Assert.That(spy.TickIntoCalls, Is.GreaterThanOrEqualTo(2));
                Assert.That(spy.StateCallsBeforeFirstTickInto, Is.EqualTo(1));
                Assert.That(spy.StateCallsAfterStart,
                    Is.EqualTo(spy.StateCallsBeforeFirstTickInto));
                Assert.That(spy.ReusedSingleOutput, Is.True);
                Assert.That(spy.LastFields, Is.EqualTo(SimTickOutputFields.Presentation));
                Assert.That(manager.CompletedTick, Is.EqualTo(spy.LastCompletedTick));
                Assert.That(manager.AttackerAliveCount, Is.EqualTo(1));
                Assert.That(manager.DefenderAliveCount, Is.EqualTo(1));
                Assert.That(manager.LastResult, Is.Null);
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
            Assert.That(factory.LastSession.DisposeCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CombatManager_BufferedStartUsesAuthoritativeSessionPhase()
        {
            var factory = new CombatSessionSpyFactory(true, SimCombatPhase.Battling);
            var host = new GameObject("BufferedCombatManagerPhaseHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(factory, FrameworkBattleContent.CanonicalBytes(),
                new RecordingEntityViewFactory(), new SilentSimLogger());
            try
            {
                _ = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Battling));
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator CombatManager_BufferedPresentationCallbackUsesAdvancedCommandTick()
        {
            var sessionFactory = new CombatSessionSpyFactory(true);
            var viewFactory = new RecordingEntityViewFactory();
            var host = new GameObject("BufferedCombatManagerReentrantSubmitHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(sessionFactory, FrameworkBattleContent.CanonicalBytes(),
                viewFactory, new SilentSimLogger());
            CommandSubmitResult reentrantSubmit = default;
            bool callbackInvoked = false;
            try
            {
                _ = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                viewFactory.OnCreate = () =>
                {
                    callbackInvoked = true;
                    reentrantSubmit = manager.SubmitDeployUnit(new Vector2Int(3, 3), 10);
                };
                Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted,
                    Is.True);

                float deadline = Time.realtimeSinceStartup + 3f;
                while (!callbackInvoked && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(callbackInvoked, Is.True);
                Assert.That(reentrantSubmit.Accepted, Is.True,
                    $"presentation callback submitted with {reentrantSubmit.Disposition}");
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator CombatManager_BufferedPresentationFailureDoesNotCommitCompletedTick()
        {
            var sessionFactory = new CombatSessionSpyFactory(true);
            var viewFactory = new RecordingEntityViewFactory();
            var host = new GameObject("BufferedCombatManagerPresentationFailureHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(sessionFactory, FrameworkBattleContent.CanonicalBytes(),
                viewFactory, new SilentSimLogger());
            UniTask<SimBattleResult> completion = default;
            try
            {
                completion = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                viewFactory.OnCreate = () => throw new InvalidOperationException(
                    "buffered presentation failed");
                Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted,
                    Is.True);

                float deadline = Time.realtimeSinceStartup + 3f;
                while (completion.Status == UniTaskStatus.Pending &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(completion.Status, Is.EqualTo(UniTaskStatus.Faulted));
                Assert.That(manager.CompletedTick, Is.EqualTo(-1));
                InvalidOperationException observed = Assert.Throws<InvalidOperationException>(
                    () => completion.GetAwaiter().GetResult());
                Assert.That(observed.Message, Is.EqualTo("buffered presentation failed"));
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator CombatManager_OwningOnlySessionRetainsCompatibility()
        {
            var factory = new CombatSessionSpyFactory(false);
            var host = new GameObject("OwningCombatManagerTestHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(factory, FrameworkBattleContent.CanonicalBytes(),
                new RecordingEntityViewFactory(), new SilentSimLogger());
            UniTask<SimBattleResult> completion = default;
            try
            {
                completion = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                Assert.That(completion.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Deploying));
                Assert.That(manager.ActiveViewCount, Is.EqualTo(1));
                Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted,
                    Is.True);

                CombatSessionSpy spy = factory.LastSession;
                float deadline = Time.realtimeSinceStartup + 3f;
                while (spy.TickCalls < 2 && completion.Status == UniTaskStatus.Pending &&
                    Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(spy.TickCalls, Is.GreaterThanOrEqualTo(2));
                Assert.That(spy, Is.Not.InstanceOf<IBufferedSimBattleSession>());
                Assert.That(spy.StateCallsAfterStart, Is.GreaterThan(0));
                Assert.That(manager.CompletedTick, Is.EqualTo(spy.LastCompletedTick));
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Deploying));
                Assert.That(manager.LastResult, Is.Null);
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
            Assert.That(factory.LastSession.DisposeCalls, Is.EqualTo(1));
        }
    }
}
