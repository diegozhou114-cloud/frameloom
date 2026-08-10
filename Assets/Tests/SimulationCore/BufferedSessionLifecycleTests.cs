using System;
using System.Reflection;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed partial class BufferedSessionLifecycleTests
    {
        [Test]
        public void BufferedSession_RejectsAlternatingAndCrossSessionBuffersBeforeAdvance()
        {
            using ISimBattleSession first = SessionTestDriver.StartedBattle();
            using ISimBattleSession second = SessionTestDriver.StartedBattle();
            using var firstOutput = new SimTickOutputBuffer(initialEntityCapacity: 4);
            using var secondOutput = new SimTickOutputBuffer(initialEntityCapacity: 4);
            IBufferedSimBattleSession firstBuffered = BufferedSessionTestSupport.Buffered(first);
            firstBuffered.TickInto(firstOutput);
            int firstTick = firstOutput.Tick;
            ulong firstChecksum = firstOutput.Checksum;

            Assert.Throws<InvalidOperationException>(() => firstBuffered.TickInto(secondOutput));
            Assert.That(((SimBattleSession)first).LastChecksum, Is.EqualTo(firstChecksum));
            Assert.Throws<InvalidOperationException>(() =>
                BufferedSessionTestSupport.Buffered(second).TickInto(firstOutput));
            Assert.That(second.State.CompletedTick, Is.EqualTo(-1));
            Assert.That(firstOutput.Tick, Is.EqualTo(firstTick));

            firstOutput.Dispose();
            BufferedSessionTestSupport.Buffered(second).TickInto(firstOutput);
            Assert.That(firstOutput.Tick, Is.EqualTo(0));
        }

        [Test]
        public void OutputBuffer_EnsureCapacityAndClearPreserveViewsCapacityAndBinding()
        {
            using ISimBattleSession first = SessionTestDriver.StartedBattle();
            using ISimBattleSession second = SessionTestDriver.StartedBattle();
            using var output = new SimTickOutputBuffer(initialEntityCapacity: 2,
                initialEventCapacity: 2, initialCommandCapacity: 2);
            IBufferedSimBattleSession buffered = BufferedSessionTestSupport.Buffered(first);
            buffered.TickInto(output);
            object entityView = output.Presentation.Entities;
            int tick = output.Tick;
            output.EnsureCapacity(256, 64, 32);

            Assert.That(output.Tick, Is.EqualTo(tick));
            Assert.That(output.Presentation.Entities, Is.SameAs(entityView));
            output.Clear();
            AssertEmpty(output);
            Assert.That(Capacity(output, "_events"), Is.GreaterThanOrEqualTo(64));
            Assert.Throws<InvalidOperationException>(() =>
                BufferedSessionTestSupport.Buffered(second).TickInto(output));
            buffered.TickInto(output);
            Assert.That(output.Tick, Is.EqualTo(tick + 1));
        }

        [Test]
        public void StartAndRestoreAttemptsInvalidateBorrowedContentsWithoutUnbinding()
        {
            using ISimBattleSession session = SessionTestDriver.StartedBattle();
            using ISimBattleSession other = SessionTestDriver.StartedBattle();
            using var output = new SimTickOutputBuffer(initialEntityCapacity: 4);
            IBufferedSimBattleSession buffered = BufferedSessionTestSupport.Buffered(session);
            buffered.TickInto(output);

            Assert.That(session.Start(SessionTestDriver.BattleInit(42)).Code,
                Is.EqualTo(StartCode.AlreadyStarted));
            AssertEmpty(output);
            buffered.TickInto(output);
            Assert.That(session.RestoreSnapshot(null).Code,
                Is.EqualTo(SnapshotRestoreCode.SnapshotUnavailable));
            AssertEmpty(output);
            Assert.Throws<InvalidOperationException>(() =>
                BufferedSessionTestSupport.Buffered(other).TickInto(output));
        }

        [Test]
        public void SuccessfulRestoreDisposeAndExceptionClearReferencesAndReleaseBinding()
        {
            ISimBattleSession session = SessionTestDriver.StartedBattle();
            using ISimBattleSession other = SessionTestDriver.StartedBattle();
            var output = new SimTickOutputBuffer(initialEntityCapacity: 8,
                initialEventCapacity: 8, initialCommandCapacity: 8);
            SimSnapshot snapshot = session.CaptureSnapshot();
            IBufferedSimBattleSession buffered = BufferedSessionTestSupport.Buffered(session);
            buffered.TickInto(output);
            Assert.That(session.RestoreSnapshot(snapshot).Restored, Is.True);
            AssertEmpty(output);
            SimBattleState restored = session.State;
            Assert.That(session.State, Is.SameAs(restored));

            buffered.TickInto(output);
            SimBattleState beforeRejectedRestore = session.State;
            SnapshotRestoreResult rejected = session.RestoreSnapshot(null);
            Assert.That(rejected.Code, Is.EqualTo(SnapshotRestoreCode.SnapshotUnavailable));
            AssertCurrentFrame(beforeRejectedRestore.Presentation, rejected.FullFrame);
            buffered.TickInto(output);
            Assert.Throws<ArgumentNullException>(() => buffered.TickInto(null));
            AssertEmpty(output);
            SimBattleState beforeDispose = session.State;
            int entityCapacity = Capacity(output.Presentation, "_entities");
            session.Dispose();
            AssertEmpty(output);
            AssertCurrentFrame(beforeDispose.Presentation, session.State.Presentation);
            Assert.That(Capacity(output.Presentation, "_entities"),
                Is.EqualTo(entityCapacity));
            BufferedSessionTestSupport.Buffered(other).TickInto(output);
            Assert.That(output.Tick, Is.EqualTo(0));
            output.Dispose();

            using var afterDispose = new SimTickOutputBuffer(initialEntityCapacity: 8);
            buffered.TickInto(afterDispose);
            Assert.That(afterDispose.Code, Is.EqualTo(TickCode.Disposed));
            BufferedSessionTestSupport.Buffered(other).TickInto(afterDispose);
            Assert.That(afterDispose.Tick, Is.EqualTo(1));
        }

        [Test]
        public void TickInto_RespectsNoneFieldSelectionWhilePublishingScalars()
        {
            using ISimBattleSession session = SessionTestDriver.StartedBattle();
            using var output = new SimTickOutputBuffer(SimTickOutputFields.None, 4, 4, 4);

            BufferedSessionTestSupport.Buffered(session).TickInto(output);

            Assert.That(output.Code, Is.EqualTo(TickCode.Advanced));
            Assert.That(output.Checksum, Is.EqualTo(((SimBattleSession)session).LastChecksum));
            AssertEmpty(output, includeScalars: false);
        }

        private static void AssertEmpty(SimTickOutputBuffer output, bool includeScalars = true)
        {
            Assert.That(output.ExecutedCommands, Is.Empty);
            Assert.That(output.CommandOutcomes, Is.Empty);
            Assert.That(output.Events, Is.Empty);
            Assert.That(output.Presentation.Entities, Is.Empty);
            Assert.That(output.Presentation.Created, Is.Empty);
            Assert.That(output.Presentation.Updated, Is.Empty);
            Assert.That(output.Presentation.Destroyed, Is.Empty);
            if (includeScalars) Assert.That(output.Result, Is.Null);
        }

        private static int Capacity(object owner, string fieldName)
        {
            object list = owner.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
            return (int)list.GetType().GetProperty("Capacity").GetValue(list);
        }

        private static void AssertCurrentFrame(
            PresentationFrame expected, PresentationFrame actual)
        {
            Assert.That(actual.IsFull, Is.EqualTo(expected.IsFull));
            Assert.That(actual.CompletedTick, Is.EqualTo(expected.CompletedTick));
            Assert.That(actual.NextTick, Is.EqualTo(expected.NextTick));
            Assert.That(actual.TimelineRevision, Is.EqualTo(expected.TimelineRevision));
            Assert.That(actual.Entities, Has.Count.EqualTo(expected.Entities.Count));
        }
    }
}
