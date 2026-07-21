using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class PresentationFrameTests
    {
        [Test]
        public void PresentationFrames_ProvideInitialDeltaAndRollbackRevisionWithoutWorldAccess()
        {
            using ISimBattleSession session = SessionTestDriver.StartedDeployingBattle();
            PresentationFrame initial = session.State.Presentation;
            Assert.That(initial.IsFull, Is.True);
            Assert.That(initial.CompletedTick, Is.EqualTo(-1));
            Assert.That(initial.Entities, Is.Not.Empty);

            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0,
                PlayerId = 0,
                Sequence = 1,
                UnitTypeId = 10,
                GridX = 2,
                GridY = 2,
            }).Accepted, Is.True);
            TickResult tick0 = session.Tick();
            Assert.That(tick0.Presentation.IsFull, Is.False);
            Assert.That(tick0.Presentation.CompletedTick, Is.Zero);
            Assert.That(tick0.Presentation.Created, Has.Count.EqualTo(1));
            Assert.That(tick0.Presentation.Entities, Is.Empty);

            SimSnapshot snapshot = session.CaptureSnapshot();
            session.Tick();
            SnapshotRestoreResult restored = session.RestoreSnapshot(snapshot);
            Assert.That(restored.Restored, Is.True);
            Assert.That(restored.FullFrame.IsFull, Is.True);
            Assert.That(restored.FullFrame.TimelineRevision,
                Is.GreaterThan(tick0.TimelineRevision));
        }
    }
}
