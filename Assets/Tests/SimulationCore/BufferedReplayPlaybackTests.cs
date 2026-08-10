using System;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class BufferedReplayPlaybackTests
    {
        [Test]
        public void ReplayPlayback_BufferedCapabilityAvoidsOwningTickAndStateMaterialization()
        {
            byte[] manifest = SessionTestDriver.OneHitManifestBytes();
            ReplayFile replay = ReplayFixture.RecordTerminalBattle(manifest);
            using var session = new BufferedSpy(
                new SimBattleSessionFactory().Create(manifest));

            ReplayPlaybackResult result = ReplayPlayback.Run(session, replay);

            Assert.That(result.Valid, Is.True);
            Assert.That(result.FinalChecksum, Is.EqualTo(replay.FinalChecksum));
            Assert.That(session.TickCalls, Is.Zero);
            Assert.That(session.TickIntoCalls, Is.EqualTo(replay.Records.Count));
            Assert.That(session.StateCalls, Is.Zero);
            Assert.That(session.LastFields, Is.EqualTo(
                SimTickOutputFields.ExecutedCommands));
        }

        [Test]
        public void ReplayPlayback_OwningOnlySessionRetainsCompatibility()
        {
            byte[] manifest = SessionTestDriver.OneHitManifestBytes();
            ReplayFile replay = ReplayFixture.RecordTerminalBattle(manifest);
            using var session = new OwningSpy(
                new SimBattleSessionFactory().Create(manifest));

            ReplayPlaybackResult result = ReplayPlayback.Run(session, replay);

            Assert.That(result.Valid, Is.True);
            Assert.That(result.FinalChecksum, Is.EqualTo(replay.FinalChecksum));
            Assert.That(result.ExecutedCommandCount, Is.EqualTo(1));
            Assert.That(session.TickCalls, Is.EqualTo(replay.Records.Count));
        }

        private class OwningSpy : ISimBattleSession
        {
            protected readonly ISimBattleSession Inner;
            public int TickCalls { get; protected set; }
            public int StateCalls { get; protected set; }

            public OwningSpy(ISimBattleSession inner) =>
                Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            public virtual SimBattleState State
            {
                get { StateCalls++; return Inner.State; }
            }
            public StartResult Start(BattleInit init) => Inner.Start(init);
            public CommandSubmitResult Submit(ISimCommand command) => Inner.Submit(command);
            public virtual TickResult Tick()
            {
                TickCalls++;
                return Inner.Tick();
            }
            public SimSnapshot CaptureSnapshot() => Inner.CaptureSnapshot();
            public SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot) =>
                Inner.RestoreSnapshot(snapshot);
            public void Dispose() => Inner.Dispose();
        }

        private sealed class BufferedSpy : OwningSpy, IBufferedSimBattleSession
        {
            private bool _dirty;
            public int TickIntoCalls { get; private set; }
            public SimTickOutputFields LastFields { get; private set; }

            public BufferedSpy(ISimBattleSession inner) : base(inner)
            {
            }

            public override SimBattleState State
            {
                get
                {
                    StateCalls++;
                    if (_dirty) throw new InvalidOperationException(
                        "Buffered playback read dirty owning State.");
                    return Inner.State;
                }
            }

            public override TickResult Tick()
            {
                TickCalls++;
                throw new InvalidOperationException("Owning Tick was used.");
            }

            public void TickInto(SimTickOutputBuffer destination)
            {
                TickIntoCalls++;
                LastFields = destination.Fields;
                ((IBufferedSimBattleSession)Inner).TickInto(destination);
                _dirty = true;
            }
        }
    }
}
