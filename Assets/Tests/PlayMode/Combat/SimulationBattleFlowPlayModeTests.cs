using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.SimulationCore;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Combat.PlayModeTests
{
    public sealed class SimulationBattleFlowPlayModeTests
    {
        [UnityTest]
        public IEnumerator BattleState_UsesSingleSimulationSession_EndToEnd()
        {
            var host = new GameObject("CombatManagerTestHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new SimBattleSessionFactory(),
                FrameworkBattleContent.CanonicalBytes(), new RecordingEntityViewFactory());
            BattleParams parameters = SessionPlayModeFixture.VillageAttackParams();
            try
            {
                UniTask<SimBattleResult> completion = manager.StartBattleAsync(parameters,
                    CancellationToken.None);

                Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted,
                    Is.True);
                Assert.That(manager.EndDeployPhase().Accepted, Is.True);
                float deadline = Time.realtimeSinceStartup + 5f;
                for (int frame = 0; frame < 100000 &&
                    Time.realtimeSinceStartup < deadline &&
                    completion.Status == UniTaskStatus.Pending; frame++)
                    yield return null;

                Assert.That(completion.Status, Is.EqualTo(UniTaskStatus.Succeeded));
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Ended));
                Assert.That(manager.LastResult, Is.Not.Null);
                manager.StopBattle();
                Assert.That(manager.ActiveViewCount, Is.Zero);
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator BattleState_ExitBeforeStartCompletes_CancelsLateStart()
        {
            var host = new GameObject("BattleStateCancellationHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new SimBattleSessionFactory(),
                FrameworkBattleContent.CanonicalBytes(), new RecordingEntityViewFactory());
            var state = new ZWebGLFrameWork.FSM.BattleState(manager);
            bool exited = false;

            try
            {
                state.OnEnter(SessionPlayModeFixture.VillageAttackParams());
                yield return null;
                Assert.That(manager.HasActiveSession, Is.True);

                state.OnExit();
                exited = true;
                yield return null;

                Assert.That(manager.HasActiveSession, Is.False);
                Assert.That(manager.ActiveViewCount, Is.Zero);
                Assert.That(manager.LastResult, Is.Null,
                    "a late completion after state exit must not commit or present a result");
            }
            finally
            {
                if (!exited) state.OnExit();
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator CompletedBattle_ReentrantReplacementDoesNotCommitStaleResult()
        {
            var host = new GameObject("CombatManagerReentrantCompletionHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new SimBattleSessionFactory(),
                FrameworkBattleContent.CanonicalBytes(), new RecordingEntityViewFactory());

            try
            {
                BattleParams firstParameters = SessionPlayModeFixture.VillageAttackParams();
                UniTask<SimBattleResult> firstCompletion = manager.StartBattleAsync(
                    firstParameters, CancellationToken.None);
                UniTask<SimBattleResult> replacementCompletion = default;
                bool replacementStarted = false;
                firstCompletion.GetAwaiter().OnCompleted(() =>
                {
                    BattleParams replacement = SessionPlayModeFixture.VillageAttackParams();
                    replacement.MatchId = "playmode-replacement-match";
                    replacement.SessionId = "playmode-replacement-session";
                    replacementCompletion = manager.StartBattleAsync(
                        replacement, CancellationToken.None);
                    replacementStarted = true;
                });

                Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted,
                    Is.True);
                Assert.That(manager.EndDeployPhase().Accepted, Is.True);
                float deadline = Time.realtimeSinceStartup + 5f;
                for (int frame = 0; frame < 100000 &&
                    Time.realtimeSinceStartup < deadline && !replacementStarted; frame++)
                    yield return null;

                Assert.That(firstCompletion.Status, Is.EqualTo(UniTaskStatus.Succeeded));
                Assert.That(replacementStarted, Is.True);
                Assert.That(replacementCompletion.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(manager.HasActiveSession, Is.True);
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Deploying));
                Assert.That(manager.LastResult, Is.Null,
                    "the completed battle must not commit into its replacement Session");
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator TickPresentation_ReentrantReplacementKeepsReplacementState()
        {
            var viewFactory = new RecordingEntityViewFactory();
            var host = new GameObject("CombatManagerTickPresentationReentrantHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new SimBattleSessionFactory(),
                FrameworkBattleContent.CanonicalBytes(), viewFactory);

            UniTask<SimBattleResult> replacementCompletion = default;
            bool replacementStarted = false;
            try
            {
                UniTask<SimBattleResult> firstCompletion = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                viewFactory.OnCreate = () =>
                {
                    BattleParams replacement = SessionPlayModeFixture.VillageAttackParams();
                    replacement.MatchId = "tick-presentation-replacement-match";
                    replacement.SessionId = "tick-presentation-replacement-session";
                    replacementCompletion = manager.StartBattleAsync(
                        replacement, CancellationToken.None);
                    replacementStarted = true;
                };

                Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted,
                    Is.True);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!replacementStarted && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(replacementStarted, Is.True);
                Assert.That(firstCompletion.Status, Is.EqualTo(UniTaskStatus.Canceled));
                Assert.That(replacementCompletion.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(manager.HasActiveSession, Is.True);
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Deploying));
                Assert.That(manager.CompletedTick, Is.EqualTo(-1),
                    "battle A must not commit its completed tick into battle B");
                Assert.That(manager.LastResult, Is.Null);
                Assert.That(manager.ActiveViewCount, Is.EqualTo(1));
                Assert.That(viewFactory.HasActiveArchetype(2), Is.True);
                Assert.That(viewFactory.HasActiveArchetype(10), Is.False,
                    "battle A's tick view must not survive replacement");
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator TickPresentation_ReentrantReplacementThenThrowKeepsReplacement()
        {
            var expected = new InvalidOperationException("stale tick presentation failure");
            var viewFactory = new RecordingEntityViewFactory();
            var host = new GameObject("CombatManagerTickPresentationReentrantThrowHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new SimBattleSessionFactory(),
                FrameworkBattleContent.CanonicalBytes(), viewFactory, new SilentSimLogger());

            UniTask<SimBattleResult> replacementCompletion = default;
            bool replacementStarted = false;
            try
            {
                UniTask<SimBattleResult> firstCompletion = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);
                viewFactory.OnCreate = () =>
                {
                    BattleParams replacement = SessionPlayModeFixture.VillageAttackParams();
                    replacement.MatchId = "tick-throw-replacement-match";
                    replacement.SessionId = "tick-throw-replacement-session";
                    replacementCompletion = manager.StartBattleAsync(
                        replacement, CancellationToken.None);
                    replacementStarted = true;
                    throw expected;
                };

                Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted,
                    Is.True);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!replacementStarted && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(replacementStarted, Is.True);
                Assert.That(firstCompletion.Status, Is.EqualTo(UniTaskStatus.Canceled));
                Assert.That(replacementCompletion.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(manager.HasActiveSession, Is.True);
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Deploying));
                Assert.That(manager.CompletedTick, Is.EqualTo(-1));
                Assert.That(manager.LastResult, Is.Null);
                Assert.That(manager.ActiveViewCount, Is.EqualTo(1));
                Assert.That(viewFactory.HasActiveArchetype(2), Is.True);
                Assert.That(viewFactory.HasActiveArchetype(10), Is.False);
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void InitialPhase_ReentrantReplacementKeepsReplacementCompletion()
        {
            var firstSession = FakeSimBattleSession.Accepting(
                SessionPlayModeFixture.InitialFrame(101));
            var replacementSession = FakeSimBattleSession.Accepting(
                SessionPlayModeFixture.InitialFrame(202));
            var viewFactory = new RecordingEntityViewFactory();
            var host = new GameObject("CombatManagerInitialPhaseReentrantHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new FakeSimBattleSessionFactory(
                    firstSession, replacementSession),
                FrameworkBattleContent.CanonicalBytes(), viewFactory);

            UniTask<SimBattleResult> replacementCompletion = default;
            bool replacementStarted = false;
            Action<SimCombatPhase> handler = null;
            handler = phase =>
            {
                if (phase != SimCombatPhase.Deploying) return;
                EventBus.Unsubscribe(CombatManager.EventPhaseChanged, handler);
                BattleParams replacement = SessionPlayModeFixture.VillageAttackParams();
                replacement.MatchId = "initial-phase-replacement-match";
                replacement.SessionId = "initial-phase-replacement-session";
                replacementCompletion = manager.StartBattleAsync(
                    replacement, CancellationToken.None);
                replacementStarted = true;
            };
            EventBus.Subscribe(CombatManager.EventPhaseChanged, handler);

            try
            {
                UniTask<SimBattleResult> firstCompletion = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);

                Assert.That(replacementStarted, Is.True);
                Assert.That(firstCompletion.Status, Is.EqualTo(UniTaskStatus.Canceled));
                Assert.That(replacementCompletion.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(manager.HasActiveSession, Is.True);
                Assert.That(viewFactory.HasActiveArchetype(202), Is.True);
                Assert.That(viewFactory.HasActiveArchetype(101), Is.False,
                    "the replaced startup must not apply its frame after battle B owns the manager");

                manager.StopBattle();

                Assert.That(replacementCompletion.Status, Is.EqualTo(UniTaskStatus.Canceled));
                Assert.That(replacementSession.DisposeCalls, Is.EqualTo(1));
                Assert.That(manager.HasActiveSession, Is.False);
                Assert.That(manager.ActiveViewCount, Is.Zero);
            }
            finally
            {
                EventBus.Unsubscribe(CombatManager.EventPhaseChanged, handler);
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void InitialPhase_ReentrantReplacementThenThrowKeepsCanceledCompletion()
        {
            var firstSession = FakeSimBattleSession.Accepting(
                SessionPlayModeFixture.InitialFrame(111));
            var replacementSession = FakeSimBattleSession.Accepting(
                SessionPlayModeFixture.InitialFrame(222));
            var viewFactory = new RecordingEntityViewFactory();
            var host = new GameObject("CombatManagerInitialPhaseReentrantThrowHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new FakeSimBattleSessionFactory(
                    firstSession, replacementSession),
                FrameworkBattleContent.CanonicalBytes(), viewFactory);

            UniTask<SimBattleResult> replacementCompletion = default;
            Action<SimCombatPhase> handler = null;
            handler = phase =>
            {
                if (phase != SimCombatPhase.Deploying) return;
                EventBus.Unsubscribe(CombatManager.EventPhaseChanged, handler);
                BattleParams replacement = SessionPlayModeFixture.VillageAttackParams();
                replacement.MatchId = "reentrant-throw-replacement-match";
                replacement.SessionId = "reentrant-throw-replacement-session";
                replacementCompletion = manager.StartBattleAsync(
                    replacement, CancellationToken.None);
                throw new InvalidOperationException(
                    "replacement subscriber failure must remain stale");
            };
            EventBus.Subscribe(CombatManager.EventPhaseChanged, handler);

            try
            {
                UniTask<SimBattleResult> firstCompletion = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);

                Assert.That(firstCompletion.Status, Is.EqualTo(UniTaskStatus.Canceled));
                Assert.That(replacementCompletion.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(manager.HasActiveSession, Is.True);
                Assert.That(viewFactory.HasActiveArchetype(222), Is.True);
                Assert.That(viewFactory.HasActiveArchetype(111), Is.False);

                manager.StopBattle();

                Assert.That(replacementCompletion.Status, Is.EqualTo(UniTaskStatus.Canceled));
                Assert.That(replacementSession.DisposeCalls, Is.EqualTo(1));
                Assert.That(manager.HasActiveSession, Is.False);
                Assert.That(manager.ActiveViewCount, Is.Zero);
            }
            finally
            {
                EventBus.Unsubscribe(CombatManager.EventPhaseChanged, handler);
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void StartBattle_WhenSessionStartThrows_DisposesCandidateAndLeavesNoActiveSession()
        {
            var expected = new InvalidOperationException("session start failed");
            var session = FakeSimBattleSession.ThrowingOnStart(expected);
            var host = new GameObject("CombatManagerStartFailureHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new FakeSimBattleSessionFactory(session),
                FrameworkBattleContent.CanonicalBytes(), new RecordingEntityViewFactory());

            try
            {
                UniTask<SimBattleResult> completion = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);

                Assert.That(completion.Status, Is.EqualTo(UniTaskStatus.Faulted));
                InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
                    () => completion.GetAwaiter().GetResult());
                Assert.That(actual, Is.SameAs(expected));
                Assert.That(session.DisposeCalls, Is.EqualTo(1));
                Assert.That(manager.HasActiveSession, Is.False);
                Assert.That(manager.ActiveViewCount, Is.Zero);
                Assert.That(manager.LastResult, Is.Null);
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Idle));
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void StartBattle_WhenInitialPresentationThrows_CleansOwnedSessionAndViews()
        {
            var expected = new InvalidOperationException("initial presentation failed");
            var session = FakeSimBattleSession.Accepting(
                SessionPlayModeFixture.InitialFrame(301, 302));
            var viewFactory = new ThrowingEntityViewFactory(expected, 2);
            var host = new GameObject("CombatManagerPresentationFailureHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new FakeSimBattleSessionFactory(session),
                FrameworkBattleContent.CanonicalBytes(), viewFactory);

            try
            {
                UniTask<SimBattleResult> completion = manager.StartBattleAsync(
                    SessionPlayModeFixture.VillageAttackParams(), CancellationToken.None);

                Assert.That(completion.Status, Is.EqualTo(UniTaskStatus.Faulted));
                InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
                    () => completion.GetAwaiter().GetResult());
                Assert.That(actual, Is.SameAs(expected));
                Assert.That(session.DisposeCalls, Is.EqualTo(1));
                Assert.That(viewFactory.DestroyCalls, Is.EqualTo(1));
                Assert.That(manager.HasActiveSession, Is.False);
                Assert.That(manager.ActiveViewCount, Is.Zero);
                Assert.That(manager.LastResult, Is.Null);
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Idle));
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }
    }

    internal static class SessionPlayModeFixture
    {
        public static BattleParams VillageAttackParams()
        {
            var attacker = new ZWebGLFrameWork.Data.VillageData();
            var defender = new ZWebGLFrameWork.Data.VillageData();
            defender.AddBuilding(new ZWebGLFrameWork.Data.BuildingData
            {
                Id = "defender-cannon",
                TypeId = 5,
                GridX = { Value = 8 },
                GridY = { Value = 2 }
            });
            return new BattleParams
            {
                ModeId = "village_attack",
                AttackerVillage = attacker,
                DefenderVillage = defender,
                MatchId = "playmode-match",
                SessionId = "playmode-session",
                Seed = 42,
                AttackerPlayerId = 0,
                DefenderPlayerId = 1,
                AttackerAccountId = "playmode-attacker",
                DefenderAccountId = "playmode-defender",
            };
        }

        public static PresentationFrame InitialFrame(params int[] archetypeIds)
        {
            EntityPresentationState[] entities = archetypeIds.Select((archetypeId, index) =>
                new EntityPresentationState(index + 1, 1, archetypeId, 1,
                    SimTeam.Defender, index + 1, 1, 10000L * (index + 1),
                    10000L, 0, 100)).ToArray();
            return new PresentationFrame(true, -1, 0, 0, entities,
                Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationKey>());
        }
    }

    internal sealed class RecordingEntityViewFactory : IEntityViewFactory
    {
        private sealed class View : IEntityView
        {
            public int ArchetypeId { get; }

            public View(int archetypeId) => ArchetypeId = archetypeId;

            public void SetPosition(float x, float y) { }
        }

        private readonly HashSet<View> _active = new HashSet<View>();

        public Action OnCreate { get; set; }

        public IEntityView Create(EntityPresentationKey key, int archetypeId)
        {
            Action callback = OnCreate;
            OnCreate = null;
            callback?.Invoke();
            var view = new View(archetypeId);
            _active.Add(view);
            return view;
        }

        public void Destroy(IEntityView view) => _active.Remove((View)view);

        public bool HasActiveArchetype(int archetypeId) =>
            _active.Any(view => view.ArchetypeId == archetypeId);
    }

    internal sealed class ThrowingEntityViewFactory : IEntityViewFactory
    {
        private sealed class View : IEntityView
        {
            public void SetPosition(float x, float y) { }
        }

        private readonly Exception _exception;
        private readonly int _throwOnCreateCall;
        private int _createCalls;

        public int DestroyCalls { get; private set; }

        public ThrowingEntityViewFactory(Exception exception, int throwOnCreateCall)
        {
            _exception = exception;
            _throwOnCreateCall = throwOnCreateCall;
        }

        public IEntityView Create(EntityPresentationKey key, int archetypeId)
        {
            _createCalls++;
            if (_createCalls == _throwOnCreateCall) throw _exception;
            return new View();
        }

        public void Destroy(IEntityView view) => DestroyCalls++;
    }

    internal sealed class SilentSimLogger : ISimLogger
    {
        public void Log(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
    }

    internal sealed class FakeSimBattleSessionFactory : ISimBattleSessionFactory
    {
        private readonly Queue<ISimBattleSession> _sessions;

        public FakeSimBattleSessionFactory(params ISimBattleSession[] sessions) =>
            _sessions = new Queue<ISimBattleSession>(sessions);

        public ISimBattleSession Create(byte[] canonicalManifestBytes,
            ISimLogger logger = null)
        {
            if (_sessions.Count == 0)
                throw new InvalidOperationException("No fake Session remains.");
            return _sessions.Dequeue();
        }
    }

    internal sealed class FakeSimBattleSession : ISimBattleSession
    {
        private readonly PresentationFrame _initialFrame;
        private readonly Exception _startException;

        public SimBattleState State { get; private set; }
        public int DisposeCalls { get; private set; }

        private FakeSimBattleSession(PresentationFrame initialFrame, Exception startException)
        {
            _initialFrame = initialFrame;
            _startException = startException;
            State = new SimBattleState(SessionLifecycle.Created, SimCombatPhase.Idle,
                -1, 0, 0, 0, initialFrame, null);
        }

        public static FakeSimBattleSession Accepting(PresentationFrame initialFrame) =>
            new FakeSimBattleSession(initialFrame, null);

        public static FakeSimBattleSession ThrowingOnStart(Exception exception) =>
            new FakeSimBattleSession(null, exception);

        public StartResult Start(BattleInit init)
        {
            if (_startException != null) throw _startException;
            State = new SimBattleState(SessionLifecycle.Running,
                SimCombatPhase.Deploying, -1, 0, 0, 0, _initialFrame, null);
            return new StartResult(StartCode.Accepted, _initialFrame);
        }

        public CommandSubmitResult Submit(ISimCommand command) =>
            new CommandSubmitResult(CommandDisposition.Accepted);

        public TickResult Tick() => throw new InvalidOperationException(
            "Fake Session does not support ticking.");

        public SimSnapshot CaptureSnapshot() => throw new InvalidOperationException(
            "Fake Session does not support snapshots.");

        public SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot) =>
            throw new InvalidOperationException("Fake Session does not support snapshots.");

        public void Dispose()
        {
            DisposeCalls++;
            State = new SimBattleState(SessionLifecycle.Disposed, State.Phase,
                State.CompletedTick, State.NextTick, State.TimelineRevision,
                State.Checksum, State.Presentation, State.Result);
        }
    }
}
