using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class FrameSyncMatchRegistryTests
    {
        [Test]
        public async Task Registry_ForwardsAuthenticatedInputToRegisteredActor()
        {
            var actor = CreateActor(maxMailboxEntries: 2);
            var registry = new FrameSyncMatchRegistry();
            registry.Register("match-1", actor);

            ProtocolError error = await registry.PostAsync("match-1", 0,
                Command(0, 0, 1), CancellationToken.None);

            Assert.That(error, Is.EqualTo(ProtocolError.None));
        }

        [Test]
        public async Task Registry_RejectsUnknownOrOverloadedMatchInput()
        {
            var registry = new FrameSyncMatchRegistry();
            Assert.That(await registry.PostAsync("missing", 0,
                    Command(0, 0, 1), CancellationToken.None),
                Is.EqualTo(ProtocolError.WrongSession));

            var actor = CreateActor(maxMailboxEntries: 1);
            registry.Register("match-1", actor);
            Assert.That(await registry.PostAsync("match-1", 0,
                    Command(0, 0, 1), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
            Assert.That(await registry.PostAsync("match-1", 1,
                    Command(0, 1, 1), CancellationToken.None),
                Is.EqualTo(ProtocolError.RateLimited));
        }

        private static FrameSyncMatchActor CreateActor(int maxMailboxEntries)
        {
            return new FrameSyncMatchActor("match-1", 20, 6, new StubSession(),
                new StubMapper(), _ => { }, new NullBroadcaster(), new Clock(),
                maxMailboxEntries: maxMailboxEntries);
        }

        private static CommandEnvelope Command(int tick, int playerId, uint sequence) =>
            new CommandEnvelope
            {
                Version = RealtimeProtocol.Version,
                MatchId = "match-1",
                Tick = tick,
                PlayerId = playerId,
                Sequence = sequence,
                MessageId = (uint)FrameSyncCommandType.EndDeployment,
                Payload = Array.Empty<byte>()
            };

        private sealed class Clock : IMonotonicClock
        {
            public long Milliseconds => 0;
        }

        private sealed class NullBroadcaster : IFrameBroadcaster
        {
            public void Broadcast(AuthorityFrame frame)
            {
            }
        }

        private sealed class StubMapper : IAuthorityCommandMapper
        {
            public ISimCommand ToSimulation(CommandEnvelope input) =>
                new EndDeploymentCmd
                {
                    Tick = input.Tick,
                    PlayerId = input.PlayerId,
                    Sequence = checked((int)input.Sequence)
                };
        }

        private sealed class StubSession : ISimBattleSession
        {
            public SimBattleState State => null;
            public StartResult Start(BattleInit init) => default;
            public CommandSubmitResult Submit(ISimCommand command) =>
                new CommandSubmitResult(CommandDisposition.Accepted);
            public TickResult Tick() => throw new NotSupportedException();
            public SimSnapshot CaptureSnapshot() => throw new NotSupportedException();
            public SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot) =>
                throw new NotSupportedException();
            public void Dispose()
            {
            }
        }
    }
}
