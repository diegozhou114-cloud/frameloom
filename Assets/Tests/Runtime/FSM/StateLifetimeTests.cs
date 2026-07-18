using System.Threading;
using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class StateLifetimeTests
    {
        private enum StateId { Active, Done }

        private sealed class TokenState : FSMState<StateId>
        {
            public TokenState(StateId id) : base(id) { }
            public CancellationToken ExposedToken => LifetimeToken;
        }

        [Test]
        public void StateLifetime_ExitCancelsTokenAndReentryRenewsIt()
        {
            var active = new TokenState(StateId.Active);
            var done = new TokenState(StateId.Done);
            var fsm = new FSM<StateId>();
            fsm.AddStates(active, done);
            fsm.TransitionGuard = (_, _) => true;
            Assert.That(fsm.ChangeState(StateId.Active), Is.True);
            CancellationToken first = active.ExposedToken;

            Assert.That(fsm.ChangeState(StateId.Done), Is.True);
            Assert.That(first.IsCancellationRequested, Is.True);
            bool lateStartCommitted = false;
            if (!first.IsCancellationRequested)
                lateStartCommitted = true;
            Assert.That(lateStartCommitted, Is.False);

            Assert.That(fsm.ChangeState(StateId.Active), Is.True);
            Assert.That(active.ExposedToken.IsCancellationRequested, Is.False);
            Assert.That(active.ExposedToken, Is.Not.EqualTo(first));
        }
    }
}
