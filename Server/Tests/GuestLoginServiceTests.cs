using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class GuestLoginServiceTests
    {
        [Test]
        public void Login_IssuesCredentialsOnlyForMatchmakerAllocation()
        {
            var tickets = new InMemoryRealtimeTicketStore();
            var service = new GuestLoginService(tickets,
                new FixedAllocator(new RealtimeLoginAssignment("match-1", 0)));

            GuestLoginOutcome result = service.Login(100);

            Assert.That(result.IsAvailable, Is.True);
            Assert.That(result.Assignment.MatchId, Is.EqualTo("match-1"));
            Assert.That(result.Credentials.PlayerId, Is.Zero);
            Assert.That(tickets.Validate(result.Credentials.OpaqueTicket, 100,
                    out RealtimeSessionClaims claims), Is.EqualTo(
                ZWebGLFrameWork.Network.Contracts.Protocol.ProtocolError.None));
            Assert.That(claims.MatchId, Is.EqualTo("match-1"));
        }

        [Test]
        public void Login_RefusesToIssueCredentialsWithoutMatchmakerAllocation()
        {
            var service = new GuestLoginService(new InMemoryRealtimeTicketStore(),
                new FixedAllocator(null));

            GuestLoginOutcome result = service.Login(100);

            Assert.That(result.IsAvailable, Is.False);
            Assert.That(result.Credentials, Is.Null);
        }

        private sealed class FixedAllocator : IRealtimeLoginAllocator
        {
            private readonly RealtimeLoginAssignment _assignment;

            public FixedAllocator(RealtimeLoginAssignment assignment) =>
                _assignment = assignment;

            public bool TryAssignGuest(out RealtimeLoginAssignment assignment)
            {
                assignment = _assignment;
                return assignment != null;
            }
        }
    }
}
