using System.Collections.Generic;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class SessionDeterminismTests
    {
        [Test]
        public void DifferentSeed_ChangesSeededInitialStateChecksum()
        {
            using SimBattleSession seed42 = SessionTestDriver.StartedBattle(42);
            using SimBattleSession seed99 = SessionTestDriver.StartedBattle(99);

            Assert.That(seed42.State.Checksum, Is.Not.EqualTo(seed99.State.Checksum));
        }

        [Test]
        public void Checksum_ChangesForTickPhaseAllocatorAndPendingEventDifferences()
        {
            using SimBattleSession beforeTick = SessionTestDriver.StartedDeployingBattle();
            using SimBattleSession afterTick = SessionTestDriver.StartedDeployingBattle();
            afterTick.Tick();
            Assert.That(afterTick.State.Checksum, Is.Not.EqualTo(beforeTick.State.Checksum));

            var cleanWorld = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            var advancedAllocator = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity temporary = advancedAllocator.CreateEntity(typeof(SimHealthComponent));
            advancedAllocator.DestroyEntity(temporary);
            Assert.That(advancedAllocator.ComputeChecksum(), Is.Not.EqualTo(cleanWorld.ComputeChecksum()));

            var noEventWorld = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            var pendingEventWorld = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            noEventWorld.CreateEntity(typeof(SimHealthComponent));
            noEventWorld.CreateEntity(typeof(SimHealthComponent));
            SimEntity attacker = pendingEventWorld.CreateEntity(typeof(SimHealthComponent));
            SimEntity target = pendingEventWorld.CreateEntity(typeof(SimHealthComponent));
            pendingEventWorld.EnqueueEvent(new SimDamageEvent
            {
                Amount = 5,
                Attacker = attacker,
                Target = target,
            });
            Assert.That(pendingEventWorld.ComputeChecksum(), Is.Not.EqualTo(noEventWorld.ComputeChecksum()));
        }

        [Test]
        public void PendingCommands_DifferentTotalOrderChangesSessionChecksum()
        {
            using SimBattleSession orderA = SessionTestDriver.StartedDeployingBattle();
            using SimBattleSession orderB = SessionTestDriver.StartedDeployingBattle();

            SubmitDeploy(orderA, 1, 3);
            SubmitDeploy(orderA, 2, 4);
            SubmitDeploy(orderB, 1, 4);
            SubmitDeploy(orderB, 2, 3);

            Assert.That(orderA.State.Checksum, Is.Not.EqualTo(orderB.State.Checksum));
        }

        [Test]
        public void Snapshot_PathMutationIsIsolatedFromCapturedState()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity entity = world.CreateEntity(typeof(SimMovementComponent));
            world.SetComponent(entity, new SimMovementComponent
            {
                Speed = LogicInt.FromInt(1),
                CellSize = LogicInt.FromInt(1),
                Path = new List<int> { 11, 22 },
            });
            TickSnapshot snapshot = world.ExportFullSnapshot();

            Assert.That(world.TryGetComponent(entity, out SimMovementComponent live), Is.True);
            live.Path[0] = 99;
            world.SetComponent(entity, live);
            world.RestoreFromFull(snapshot);

            SimEntity restoredEntity = SimEntity.Invalid;
            world.ForEachComponent<SimMovementComponent>((candidate, movement) => restoredEntity = candidate);
            Assert.That(world.TryGetComponent(restoredEntity, out SimMovementComponent restored), Is.True);
            Assert.That(restored.Path, Is.EqualTo(new[] { 11, 22 }));
        }

        [Test]
        public void Snapshot_CaptureAndRestoreRecoversChecksumAndNextTick()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            SimSnapshot snapshot = session.CaptureSnapshot();
            ulong expectedChecksum = snapshot.StateChecksum;

            Assert.That(snapshot.Payload, Is.Not.Empty);
            session.Tick();
            SnapshotRestoreResult restored = session.RestoreSnapshot(snapshot);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.Restored));
            Assert.That(session.State.Checksum, Is.EqualTo(expectedChecksum));
            Assert.That(session.State.NextTick, Is.EqualTo(snapshot.NextTick));
            Assert.That(restored.FullFrame.IsFull, Is.True);
        }

        [Test]
        public void Snapshot_ForeignOrCorruptEnvelopeIsRejectedAtomically()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            SimSnapshot source = session.CaptureSnapshot();
            ulong before = session.State.Checksum;

            SimSnapshot foreign = CopySnapshot(source, matchId: source.MatchId + "-foreign");
            Assert.That(session.RestoreSnapshot(foreign).Code, Is.EqualTo(SnapshotRestoreCode.ForeignMatch));
            Assert.That(session.State.Checksum, Is.EqualTo(before));

            byte[] corruptPayload = source.Payload;
            corruptPayload[0] ^= 0x01;
            SimSnapshot corrupt = CopySnapshot(source, payload: corruptPayload);
            Assert.That(session.RestoreSnapshot(corrupt).Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(before));
        }

        private static void SubmitDeploy(SimBattleSession session, int sequence, int gridX)
        {
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 1,
                PlayerId = 0,
                Sequence = sequence,
                UnitTypeId = 10,
                GridX = gridX,
                GridY = 3,
            }).Accepted, Is.True);
        }

        private static SimSnapshot CopySnapshot(SimSnapshot source, string matchId = null,
            byte[] payload = null)
        {
            return new SimSnapshot(
                matchId ?? source.MatchId,
                source.SessionId,
                source.SchemaVersion,
                source.ContentHash,
                source.CompletedTick,
                source.NextTick,
                source.TimelineRevision,
                payload?.Length ?? source.UncompressedLength,
                source.StateChecksum,
                payload ?? source.Payload,
                source.PayloadSha256);
        }
    }
}
