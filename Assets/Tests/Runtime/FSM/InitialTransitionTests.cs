using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class InitialTransitionTests
    {
        private enum StateId { Launch, Ready }

        private sealed class ProbeState : FSMState<StateId>
        {
            public ProbeState(StateId id) : base(id) { }
        }

        [Test]
        public void GameFSM_InitialTransition_EntersLaunchExactlyOnce()
        {
            var fsm = new FSM<StateId>();
            fsm.AddState(new ProbeState(StateId.Launch));
            int guardCalls = 0;
            var transitions = new List<FSMTransition<StateId>>();
            fsm.TransitionGuard = (_, _) => { guardCalls++; return false; };
            fsm.OnStateChanged += transitions.Add;

            bool changed = fsm.ChangeState(StateId.Launch);

            Assert.That(changed, Is.True);
            Assert.That(fsm.HasCurrentState, Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Launch));
            Assert.That(guardCalls, Is.Zero);
            Assert.That(transitions, Has.Count.EqualTo(1));
            Assert.That(transitions[0].HasOldState, Is.False);
        }
    }
}
