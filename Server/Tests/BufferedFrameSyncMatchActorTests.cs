using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class BufferedFrameSyncMatchActorTests
    {
        [Test]
        public void BufferedCapability_UsesTickIntoForEveryAdvancedTick()
        {
            byte[] manifest = BufferedServerConsumerTestSupport.ManifestBytes();
            BattleInit init = BufferedServerConsumerTestSupport.Init(
                manifest, "match-1", "buffered-session");
            var session = new BufferedSessionSpy(
                new SimBattleSessionFactory().Create(manifest));
            Assert.That(session.Start(init).Accepted, Is.True);
            var clock = new ManualClock();
            var broadcaster = new RecordingBroadcaster();
            using var actor = CreateActor(session, broadcaster, clock);
            clock.Set(10000);

            int advanced = actor.Pump();

            Assert.That(advanced, Is.EqualTo(4));
            Assert.That(session.TickCalls, Is.Zero);
            Assert.That(session.TickIntoCalls, Is.EqualTo(advanced));
            Assert.That(session.StateCalls, Is.Zero);
            Assert.That(session.LastFields, Is.EqualTo(SimTickOutputFields.None));
        }

        [Test]
        public void BufferedAndOwningFallback_BroadcastEquivalentTickChecksumAndEnded()
        {
            byte[] manifest = BufferedServerConsumerTestSupport.ManifestBytes();
            BattleInit init = BufferedServerConsumerTestSupport.Init(
                manifest, "match-1", "shared-session");
            var buffered = new BufferedSessionSpy(
                new SimBattleSessionFactory().Create(manifest));
            var owning = new OwningSessionSpy(
                new SimBattleSessionFactory().Create(manifest));
            Assert.That(buffered.Start(init).Accepted, Is.True);
            Assert.That(owning.Start(init).Accepted, Is.True);
            var command = new EndDeploymentCmd { Tick = 0, PlayerId = 0, Sequence = 1 };
            Assert.That(buffered.Submit(command).Accepted, Is.True);
            Assert.That(owning.Submit(new EndDeploymentCmd
                { Tick = 0, PlayerId = 0, Sequence = 1 }).Accepted, Is.True);
            var bufferedFrames = new RecordingBroadcaster();
            var owningFrames = new RecordingBroadcaster();
            var bufferedClock = new ManualClock();
            var owningClock = new ManualClock();
            using var bufferedActor = CreateActor(
                buffered, bufferedFrames, bufferedClock);
            using var owningActor = CreateActor(
                owning, owningFrames, owningClock);
            bufferedClock.Set(10000);
            owningClock.Set(10000);

            Assert.That(bufferedActor.Pump(), Is.EqualTo(owningActor.Pump()));

            AuthorityFrame expected = owningFrames.Frames[0];
            AuthorityFrame actual = bufferedFrames.Frames[0];
            Assert.That(actual.Tick, Is.EqualTo(expected.Tick));
            Assert.That(actual.Checksum, Is.EqualTo(expected.Checksum));
            Assert.That(bufferedActor.IsEnded, Is.EqualTo(owningActor.IsEnded).And.True);
            Assert.That(buffered.TickCalls, Is.Zero);
            Assert.That(buffered.TickIntoCalls, Is.EqualTo(1));
            Assert.That(owning.TickCalls, Is.EqualTo(1));
        }

        private static FrameSyncMatchActor CreateActor(ISimBattleSession session,
            IFrameBroadcaster broadcaster, IMonotonicClock clock) =>
            new FrameSyncMatchActor("match-1", 20, 6, session,
                new NullMapper(), _ => { }, broadcaster, clock);

        private sealed class ManualClock : IMonotonicClock
        {
            public long Milliseconds { get; private set; }
            public void Set(long milliseconds) => Milliseconds = milliseconds;
        }

        private sealed class RecordingBroadcaster : IFrameBroadcaster
        {
            public List<AuthorityFrame> Frames { get; } = new List<AuthorityFrame>();
            public void Broadcast(AuthorityFrame frame) => Frames.Add(frame);
        }

        private sealed class NullMapper : IAuthorityCommandMapper
        {
            public ISimCommand ToSimulation(CommandEnvelope input) => null;
        }
    }
}
