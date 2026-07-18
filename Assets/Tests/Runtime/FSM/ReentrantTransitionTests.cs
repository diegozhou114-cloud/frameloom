using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class ReentrantTransitionTests
    {
        private enum StateId { A, B, C }

        private sealed class PlainState : FSMState<StateId>
        {
            public PlainState(StateId id) : base(id) { }
        }

        private sealed class RedirectState : FSMState<StateId>
        {
            public RedirectState() : base(StateId.B) { }

            public override void OnEnter(object data = null)
            {
                base.OnEnter(data);
                ChangeState(StateId.C);
            }
        }

        [Test]
        public void FSM_OnEnterRedirect_EmitsOnlyOrderedCommittedTransitions()
        {
            var fsm = new FSM<StateId>();
            fsm.AddStates(
                new PlainState(StateId.A),
                new RedirectState(),
                new PlainState(StateId.C));
            fsm.TransitionGuard = (_, _) => true;
            var observed = new List<string>();
            fsm.OnStateChanged += transition =>
                observed.Add($"{transition.OldState}>{transition.NewState}");
            Assert.That(fsm.ChangeState(StateId.A), Is.True);
            observed.Clear();

            Assert.That(fsm.ChangeState(StateId.B), Is.True);

            CollectionAssert.AreEqual(new[] { "A>B", "B>C" }, observed);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.C));
        }
    }
}
