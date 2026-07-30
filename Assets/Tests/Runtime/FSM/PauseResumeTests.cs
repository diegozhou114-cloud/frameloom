using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class PauseResumeTests
    {
        private enum StateId { Battle, Pause, MainVillage, Login }

        private sealed class BattleContext
        {
            public int Seed;
        }

        private sealed class BattleProbe : FSMState<StateId>
        {
            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }
            public int ResumeCount { get; private set; }
            public System.Action OnResuming { get; set; }

            public BattleProbe() : base(StateId.Battle) { }

            public void Request(StateId target, object data = null) =>
                ChangeState(target, data);

            public override void OnEnter(object data = null)
            {
                base.OnEnter(data);
                EnterCount++;
            }

            public override void OnExit()
            {
                ExitCount++;
                base.OnExit();
            }

            public override void OnResume(object data)
            {
                base.OnResume(data);
                ResumeCount++;
                OnResuming?.Invoke();
            }
        }

        private sealed class PauseProbe : FSMState<StateId>
        {
            public int ExitCount { get; private set; }
            public System.Action OnExiting { get; set; }

            public PauseProbe() : base(StateId.Pause) { }

            public void Request(StateId target) => ChangeState(target);

            public override void OnExit()
            {
                ExitCount++;
                OnExiting?.Invoke();
                base.OnExit();
            }
        }

        private sealed class StateProbe : FSMState<StateId>
        {
            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }

            public StateProbe(StateId stateId) : base(stateId) { }

            public void Request(StateId target, object data = null) =>
                ChangeState(target, data);

            public override void OnEnter(object data = null)
            {
                base.OnEnter(data);
                EnterCount++;
            }

            public override void OnExit()
            {
                ExitCount++;
                base.OnExit();
            }
        }

        [Test]
        public void GameFSM_PauseResume_PreservesBattleContext()
        {
            var battle = new BattleProbe();
            var fsm = new FSM<StateId>();
            fsm.AddStates(battle, new PauseProbe());
            fsm.TransitionGuard = (_, _) => true;
            var context = new BattleContext { Seed = 42 };
            Assert.That(fsm.ChangeState(StateId.Battle, context), Is.True);

            Assert.That(fsm.PushState(StateId.Pause, "background"), Is.True);
            Assert.That(fsm.PopState(3.5f), Is.True);

            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Battle));
            Assert.That(battle.EnterCount, Is.EqualTo(1));
            Assert.That(battle.ExitCount, Is.Zero);
            Assert.That(battle.ResumeCount, Is.EqualTo(1));
            Assert.That(battle.EnterData, Is.SameAs(context));
        }

        [Test]
        public void SuspendedSource_TransitionExecutesAfterResumeWithOriginalData()
        {
            var battle = new BattleProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, new PauseProbe(), new StateProbe(StateId.MainVillage));
            var transitions =
                new System.Collections.Generic.List<FSMTransition<StateId>>();
            fsm.OnStateChanged += transitions.Add;
            var data = new BattleContext { Seed = 73 };
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);

            battle.Request(StateId.MainVillage, data);

            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Pause));
            Assert.That(transitions.ConvertAll(item => item.NewState),
                Is.EqualTo(new[] { StateId.Battle, StateId.Pause }));
            Assert.That(fsm.PopState(), Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.MainVillage));
            Assert.That(battle.ResumeCount, Is.EqualTo(1));
            Assert.That(battle.ExitCount, Is.EqualTo(1));
            Assert.That(transitions.ConvertAll(item => item.NewState), Is.EqualTo(new[]
            {
                StateId.Battle, StateId.Pause, StateId.Battle, StateId.MainVillage,
            }));
            Assert.That(transitions[3].Data, Is.SameAs(data));
        }

        [Test]
        public void SuspendedSource_FirstValidTransitionWins()
        {
            var battle = new BattleProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, new PauseProbe(),
                new StateProbe(StateId.MainVillage), new StateProbe(StateId.Login));
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);

            battle.Request(StateId.MainVillage);
            battle.Request(StateId.Login);

            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Pause));
            Assert.That(fsm.PopState(), Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.MainVillage));
        }

        [Test]
        public void ActiveOverlay_RejectsBypassAndNestedPush()
        {
            var battle = new BattleProbe();
            var pause = new PauseProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, pause, new StateProbe(StateId.MainVillage));
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);

            Assert.That(fsm.ChangeState(StateId.MainVillage), Is.False);
            pause.Request(StateId.MainVillage);
            Assert.That(fsm.PushState(StateId.MainVillage), Is.False);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Pause));
            Assert.That(fsm.PopState(), Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Battle));
        }

        [Test]
        public void ActiveOverlay_RemoveStateKeepsParticipantsRegistered()
        {
            var battle = new BattleProbe();
            var pause = new PauseProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, pause);
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);

            fsm.RemoveState(StateId.Battle);
            fsm.RemoveState(StateId.Pause);

            Assert.That(fsm.HasState(StateId.Battle), Is.True);
            Assert.That(fsm.HasState(StateId.Pause), Is.True);
            Assert.That(battle.Owner, Is.SameAs(fsm));
            Assert.That(pause.Owner, Is.SameAs(fsm));
            Assert.That(fsm.PopState(), Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Battle));
        }

        [Test]
        public void ExitedSource_CannotMoveTheCurrentState()
        {
            var battle = new BattleProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, new StateProbe(StateId.MainVillage),
                new StateProbe(StateId.Login));
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.ChangeState(StateId.MainVillage), Is.True);

            battle.Request(StateId.Login);

            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.MainVillage));
        }

        [Test]
        public void OverlayExitTransition_CannotEscapePopLifecycle()
        {
            var battle = new BattleProbe();
            var pause = new PauseProbe();
            var login = new StateProbe(StateId.Login);
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, pause, login);
            pause.OnExiting = () => pause.Request(StateId.Login);
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);

            Assert.That(fsm.PopState(), Is.True);

            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Battle));
            Assert.That(login.EnterCount, Is.Zero);
            Assert.That(pause.ExitCount, Is.EqualTo(1));
        }

        [Test]
        public void ResumeTransition_CannotReplaceEarlierDeferredRequest()
        {
            var battle = new BattleProbe();
            var login = new StateProbe(StateId.Login);
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, new PauseProbe(), login,
                new StateProbe(StateId.MainVillage));
            battle.OnResuming = () => battle.Request(StateId.Login);
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);
            battle.Request(StateId.MainVillage);

            Assert.That(fsm.PopState(), Is.True);

            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.MainVillage));
            Assert.That(login.EnterCount, Is.Zero);
        }

        [Test]
        public void ResumeEventTransition_CannotReplaceEarlierDeferredRequest()
        {
            var battle = new BattleProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, new PauseProbe(), new StateProbe(StateId.Login),
                new StateProbe(StateId.MainVillage));
            bool? eventRequestResult = null;
            fsm.OnStateChanged += transition =>
            {
                if (transition.OldState.Equals(StateId.Pause) &&
                    transition.NewState.Equals(StateId.Battle))
                    eventRequestResult = fsm.ChangeState(StateId.Login);
            };
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);
            battle.Request(StateId.MainVillage);

            Assert.That(fsm.PopState(), Is.True);

            Assert.That(eventRequestResult, Is.False);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.MainVillage));
        }

        [Test]
        public void QueuedPublicTransition_CannotBypassOverlayCreatedBeforeDequeue()
        {
            var battle = new BattleProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, new PauseProbe(), new StateProbe(StateId.Login),
                new StateProbe(StateId.MainVillage));
            bool? queuedResult = null;
            bool? pushResult = null;
            fsm.OnStateChanged += transition =>
            {
                if (!transition.NewState.Equals(StateId.MainVillage)) return;
                queuedResult = fsm.ChangeState(StateId.Login);
                pushResult = fsm.PushState(StateId.Pause);
            };
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);

            Assert.That(fsm.ChangeState(StateId.MainVillage), Is.True);

            Assert.That(queuedResult, Is.True);
            Assert.That(pushResult, Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Pause));
            Assert.That(fsm.PopState(), Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.MainVillage));
        }

        [Test]
        public void QueuedPublicTransition_CannotSurviveCompletedOverlayLifecycle()
        {
            var battle = new BattleProbe();
            var village = new StateProbe(StateId.MainVillage);
            var login = new StateProbe(StateId.Login);
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, village, login, new PauseProbe());
            bool? queuedResult = null;
            bool? pushResult = null;
            bool? popResult = null;
            fsm.OnStateChanged += transition =>
            {
                if (!transition.OldState.Equals(StateId.Battle) ||
                    !transition.NewState.Equals(StateId.MainVillage))
                    return;
                queuedResult = fsm.ChangeState(StateId.Login);
                pushResult = fsm.PushState(StateId.Pause);
                village.Request(StateId.Battle);
                popResult = fsm.PopState();
            };
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);

            Assert.That(fsm.ChangeState(StateId.MainVillage), Is.True);

            Assert.That(queuedResult, Is.True);
            Assert.That(pushResult, Is.True);
            Assert.That(popResult, Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Battle));
            Assert.That(login.EnterCount, Is.Zero);
        }

        [TestCase(GameState.Launch, GameState.Pause, true)]
        [TestCase(GameState.Login, GameState.Pause, true)]
        [TestCase(GameState.MainVillage, GameState.Pause, true)]
        [TestCase(GameState.Battle, GameState.Pause, true)]
        [TestCase(GameState.Loading, GameState.Pause, false)]
        [TestCase(GameState.Pause, GameState.Pause, false)]
        [TestCase(GameState.Launch, GameState.Login, false)]
        public void GameFSM_OverlayPolicy_MatchesSupportedPauseSources(
            GameState source, GameState overlay, bool expected)
        {
            Assert.That(GameFSM.CanPushOverlay(source, overlay), Is.EqualTo(expected));
        }

        [Test]
        public void Clear_ExitsCurrentAndSuspendedStates()
        {
            var battle = new BattleProbe();
            var pause = new PauseProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, pause, new StateProbe(StateId.MainVillage));
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);

            battle.Request(StateId.MainVillage);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Pause));

            fsm.Clear();

            Assert.That(battle.ExitCount, Is.EqualTo(1));
            Assert.That(pause.ExitCount, Is.EqualTo(1));
            Assert.That(fsm.HasCurrentState, Is.False);
            Assert.That(fsm.PopState(), Is.False);

            var nextBattle = new BattleProbe();
            fsm.AddStates(nextBattle, new PauseProbe(),
                new StateProbe(StateId.Login));
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);
            nextBattle.Request(StateId.Login);
            Assert.That(fsm.PopState(), Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Login));
        }
    }
}
