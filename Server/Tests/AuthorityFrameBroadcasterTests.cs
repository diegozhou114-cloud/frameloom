using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class AuthorityFrameBroadcasterTests
    {
        [Test]
        public void Broadcast_DeliversEncodedFrameOnlyToMatchingRecipients()
        {
            var broadcaster = new AuthorityFrameBroadcaster();
            var player0 = new RecordingSession("match-1");
            var player1 = new RecordingSession("match-1");
            var otherMatch = new RecordingSession("match-2");
            broadcaster.Register(player0);
            broadcaster.Register(player1);
            broadcaster.Register(otherMatch);

            broadcaster.Broadcast(Frame("match-1", 7));

            Assert.That(player0.Messages, Has.Count.EqualTo(1));
            Assert.That(player1.Messages, Has.Count.EqualTo(1));
            Assert.That(otherMatch.Messages, Is.Empty);
            DecodeResult decoded = RealtimeCodec.Decode(player0.Messages[0]);
            Assert.That(decoded.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(decoded.Kind, Is.EqualTo(RealtimeMessageKind.Authority));
            Assert.That(decoded.Authority.MatchId, Is.EqualTo("match-1"));
            Assert.That(decoded.Authority.Tick, Is.EqualTo(7));
        }

        [Test]
        public void Broadcast_IsolatesRecipientPayloadBuffers()
        {
            var broadcaster = new AuthorityFrameBroadcaster();
            var mutating = new MutatingSession("match-1");
            var recording = new RecordingSession("match-1");
            broadcaster.Register(mutating);
            broadcaster.Register(recording);

            broadcaster.Broadcast(Frame("match-1", 3));

            DecodeResult decoded = RealtimeCodec.Decode(recording.Messages[0]);
            Assert.That(decoded.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(decoded.Authority.Tick, Is.EqualTo(3));
        }

        [Test]
        public void Broadcast_ContinuesWhenOneRecipientThrows()
        {
            var broadcaster = new AuthorityFrameBroadcaster();
            broadcaster.Register(new ThrowingSession("match-1"));
            var healthy = new RecordingSession("match-1");
            broadcaster.Register(healthy);

            Assert.DoesNotThrow(() => broadcaster.Broadcast(Frame("match-1", 2)));

            Assert.That(healthy.Messages, Has.Count.EqualTo(1));
        }

        [Test]
        public void Register_RejectsAnotherActiveRecipientForTheSameSession()
        {
            var broadcaster = new AuthorityFrameBroadcaster();
            Guid sessionId = Guid.NewGuid();
            broadcaster.Register(new RecordingSession("match-1", sessionId));

            Assert.Throws<InvalidOperationException>(() => broadcaster.Register(
                new RecordingSession("match-1", sessionId)));
        }

        [Test]
        public void Unregister_LeavesAReplacementRecipientRegistered()
        {
            var broadcaster = new AuthorityFrameBroadcaster();
            Guid sessionId = Guid.NewGuid();
            var original = new RecordingSession("match-1", sessionId);
            broadcaster.Register(original);
            broadcaster.Unregister(original);
            var replacement = new RecordingSession("match-1", sessionId);
            broadcaster.Register(replacement);

            broadcaster.Unregister(original);
            broadcaster.Broadcast(Frame("match-1", 1));

            Assert.That(replacement.Messages, Has.Count.EqualTo(1));
        }

        private static AuthorityFrame Frame(string matchId, int tick)
        {
            return new AuthorityFrame
            {
                MatchId = matchId,
                Tick = tick,
                OrderedInputs = new[]
                {
                    Command(matchId, tick, 0, 1),
                    Command(matchId, tick, 1, 1)
                },
                AckPlayer0 = 1,
                AckPlayer1 = 1,
                Checksum = 99
            };
        }

        private static CommandEnvelope Command(string matchId, int tick,
            int playerId, uint sequence)
        {
            return new CommandEnvelope
            {
                Version = RealtimeProtocol.Version,
                MatchId = matchId,
                Tick = tick,
                PlayerId = playerId,
                Sequence = sequence,
                MessageId = 0,
                Payload = Array.Empty<byte>()
            };
        }

        private class RecordingSession : IRealtimeOutboundSession
        {
            public Guid SessionId { get; }
            public string MatchId { get; }
            public List<byte[]> Messages { get; } = new List<byte[]>();

            public RecordingSession(string matchId, Guid? sessionId = null)
            {
                MatchId = matchId;
                SessionId = sessionId ?? Guid.NewGuid();
            }

            public virtual bool TryEnqueue(byte[] completeMessage)
            {
                Messages.Add(completeMessage);
                return true;
            }
        }

        private sealed class MutatingSession : RecordingSession
        {
            public MutatingSession(string matchId) : base(matchId)
            {
            }

            public override bool TryEnqueue(byte[] completeMessage)
            {
                completeMessage[0] ^= 0xff;
                return base.TryEnqueue(completeMessage);
            }
        }

        private sealed class ThrowingSession : IRealtimeOutboundSession
        {
            public Guid SessionId { get; } = Guid.NewGuid();
            public string MatchId { get; }

            public ThrowingSession(string matchId)
            {
                MatchId = matchId;
            }

            public bool TryEnqueue(byte[] completeMessage) =>
                throw new InvalidOperationException("Simulated transport failure.");
        }
    }
}
