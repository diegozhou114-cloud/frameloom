using System.Collections.Generic;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed partial class BufferedSessionLifecycleTests
    {
        [Test]
        public void BufferedTick_DisposeMaterializesLatestOwningStateWithoutStateRead()
        {
            ISimBattleSession session = SessionTestDriver.StartedBattle();
            using var output = new SimTickOutputBuffer(initialEntityCapacity: 8);
            BufferedSessionTestSupport.Buffered(session).TickInto(output);
            int completedTick = output.Tick;
            int nextTick = completedTick + 1;
            long revision = output.Revision;
            EntityPresentationState[] entities = SnapshotEntities(output.Presentation.Entities);

            session.Dispose();
            SimBattleState state = session.State;

            Assert.That(state.CompletedTick, Is.EqualTo(completedTick));
            Assert.That(state.NextTick, Is.EqualTo(nextTick));
            Assert.That(state.TimelineRevision, Is.EqualTo(revision));
            AssertLatestFrame(state.Presentation, completedTick, nextTick, revision, entities);
        }

        [Test]
        public void BufferedTick_RepeatedStartReturnsLatestOwningFrameWithoutStateRead()
        {
            using ISimBattleSession session = SessionTestDriver.StartedBattle();
            using var output = new SimTickOutputBuffer(initialEntityCapacity: 8);
            BufferedSessionTestSupport.Buffered(session).TickInto(output);
            int completedTick = output.Tick;
            int nextTick = completedTick + 1;
            long revision = output.Revision;
            EntityPresentationState[] entities = SnapshotEntities(output.Presentation.Entities);

            StartResult result = session.Start(SessionTestDriver.BattleInit(42));

            Assert.That(result.Code, Is.EqualTo(StartCode.AlreadyStarted));
            AssertLatestFrame(result.InitialFrame, completedTick, nextTick, revision, entities);
        }

        [Test]
        public void BufferedTick_RejectedRestoreReturnsLatestOwningFrameWithoutStateRead()
        {
            using ISimBattleSession session = SessionTestDriver.StartedBattle();
            using var output = new SimTickOutputBuffer(initialEntityCapacity: 8);
            BufferedSessionTestSupport.Buffered(session).TickInto(output);
            int completedTick = output.Tick;
            int nextTick = completedTick + 1;
            long revision = output.Revision;
            EntityPresentationState[] entities = SnapshotEntities(output.Presentation.Entities);

            SnapshotRestoreResult result = session.RestoreSnapshot(null);

            Assert.That(result.Code, Is.EqualTo(SnapshotRestoreCode.SnapshotUnavailable));
            AssertLatestFrame(result.FullFrame, completedTick, nextTick, revision, entities);
        }

        [Test]
        public void OwningTick_AcceptedSubmitRepeatedStartPreservesDeltaPresentation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            TickResult tick = TickAndAcceptCommand(session, out ulong checksum);

            StartResult result = session.Start(SessionTestDriver.BattleInit(42));

            AssertDeltaPreserved(tick.Presentation, result.InitialFrame);
            Assert.That(session.State.Checksum, Is.EqualTo(checksum));
        }

        [Test]
        public void OwningTick_AcceptedSubmitRejectedRestorePreservesDeltaPresentation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            TickResult tick = TickAndAcceptCommand(session, out ulong checksum);

            SnapshotRestoreResult result = session.RestoreSnapshot(null);

            AssertDeltaPreserved(tick.Presentation, result.FullFrame);
            Assert.That(session.State.Checksum, Is.EqualTo(checksum));
        }

        [Test]
        public void OwningTick_AcceptedSubmitDisposePreservesDeltaPresentation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            TickResult tick = TickAndAcceptCommand(session, out _);

            session.Dispose();

            AssertDeltaPreserved(tick.Presentation, session.State.Presentation);
            Assert.That(session.State.Checksum, Is.EqualTo(session.LastChecksum));
        }

        private static TickResult TickAndAcceptCommand(
            SimBattleSession session, out ulong checksum)
        {
            TickResult tick = session.Tick();
            Assert.That(tick.Presentation.IsFull, Is.False);
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = tick.NextTick,
                PlayerId = 0,
                Sequence = 1,
            }).Accepted, Is.True);
            checksum = session.LastChecksum;
            Assert.That(checksum, Is.Not.EqualTo(tick.Checksum));
            return tick;
        }

        private static void AssertDeltaPreserved(
            PresentationFrame expected, PresentationFrame actual)
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(actual.IsFull, Is.False);
            Assert.That(actual.CompletedTick, Is.EqualTo(expected.CompletedTick));
            Assert.That(actual.NextTick, Is.EqualTo(expected.NextTick));
            Assert.That(actual.TimelineRevision, Is.EqualTo(expected.TimelineRevision));
            Assert.That(actual.Created, Is.SameAs(expected.Created));
            Assert.That(actual.Updated, Is.SameAs(expected.Updated));
            Assert.That(actual.Destroyed, Is.SameAs(expected.Destroyed));
        }

        private static EntityPresentationState[] SnapshotEntities(
            IReadOnlyList<EntityPresentationState> source)
        {
            var snapshot = new EntityPresentationState[source.Count];
            for (int index = 0; index < source.Count; index++) snapshot[index] = source[index];
            return snapshot;
        }

        private static void AssertLatestFrame(PresentationFrame actual, int completedTick,
            int nextTick, long revision, IReadOnlyList<EntityPresentationState> entities)
        {
            Assert.That(actual.IsFull, Is.True);
            Assert.That(actual.CompletedTick, Is.EqualTo(completedTick));
            Assert.That(actual.NextTick, Is.EqualTo(nextTick));
            Assert.That(actual.TimelineRevision, Is.EqualTo(revision));
            Assert.That(actual.Entities, Has.Count.EqualTo(entities.Count));
            for (int index = 0; index < entities.Count; index++)
                AssertEntity(entities[index], actual.Entities[index]);
        }

        private static void AssertEntity(
            EntityPresentationState expected, EntityPresentationState actual)
        {
            Assert.That(actual.EntityId, Is.EqualTo(expected.EntityId));
            Assert.That(actual.Generation, Is.EqualTo(expected.Generation));
            Assert.That(actual.ArchetypeId, Is.EqualTo(expected.ArchetypeId));
            Assert.That(actual.OwnerPlayerId, Is.EqualTo(expected.OwnerPlayerId));
            Assert.That(actual.TeamId, Is.EqualTo(expected.TeamId));
            Assert.That(actual.GridX, Is.EqualTo(expected.GridX));
            Assert.That(actual.GridY, Is.EqualTo(expected.GridY));
            Assert.That(actual.PosXRaw, Is.EqualTo(expected.PosXRaw));
            Assert.That(actual.PosYRaw, Is.EqualTo(expected.PosYRaw));
            Assert.That(actual.OrientationMilliDegrees,
                Is.EqualTo(expected.OrientationMilliDegrees));
            Assert.That(actual.Health, Is.EqualTo(expected.Health));
        }
    }
}
