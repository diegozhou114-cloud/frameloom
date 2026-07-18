using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class PauseResumeTests
    {
        private enum StateId { Battle, Pause }

        private sealed class BattleContext
        {
            public int Seed;
        }

        private sealed class BattleProbe : FSMState<StateId>
        {
            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }
            public int ResumeCount { get; private set; }

            public BattleProbe() : base(StateId.Battle) { }

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
            }
        }

        private sealed class PauseProbe : FSMState<StateId>
        {
            public int ExitCount { get; private set; }

            public PauseProbe() : base(StateId.Pause) { }

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
        public void Clear_ExitsCurrentAndSuspendedStates()
        {
            var battle = new BattleProbe();
            var pause = new PauseProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, pause);
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);

            fsm.Clear();

            Assert.That(battle.ExitCount, Is.EqualTo(1));
            Assert.That(pause.ExitCount, Is.EqualTo(1));
        }
    }
}
