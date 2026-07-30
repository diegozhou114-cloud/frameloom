using System;
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

        private sealed class ThrowingEnterState : FSMState<StateId>
        {
            public ThrowingEnterState() : base(StateId.B) { }
            public int ExitCount { get; private set; }

            public override void OnEnter(object data = null)
            {
                base.OnEnter(data);
                throw new InvalidOperationException("enter failed");
            }

            public override void OnExit()
            {
                ExitCount++;
                base.OnExit();
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

        [Test]
        public void FSM_OnEnterFailure_RestoresPreviousActiveStateWithoutCommit()
        {
            var fsm = new FSM<StateId>();
            var previous = new PlainState(StateId.A);
            var failed = new ThrowingEnterState();
            fsm.AddStates(previous, failed, new PlainState(StateId.C));
            fsm.TransitionGuard = (_, _) => true;
            var transitions = new List<FSMTransition<StateId>>();
            fsm.OnStateChanged += transitions.Add;

            Assert.That(fsm.ChangeState(StateId.A, "initial"), Is.True);
            Assert.Throws<InvalidOperationException>(() => fsm.ChangeState(StateId.B));

            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.A));
            Assert.That(previous.IsActive, Is.True);
            Assert.That(previous.EnterData, Is.EqualTo("initial"));
            Assert.That(failed.IsActive, Is.False);
            Assert.That(failed.EnterData, Is.Null);
            Assert.That(failed.ExitCount, Is.EqualTo(1));
            Assert.That(transitions, Has.Count.EqualTo(1));
        }
    }
}
