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
        public void AcceptedSequenceState_ChangesChecksumAfterCommandsDrain()
        {
            using SimBattleSession first = SessionTestDriver.StartedBattle();
            using SimBattleSession second = SessionTestDriver.StartedBattle();
            EntityPresentationState entity = first.State.Presentation.Entities[0];

            Assert.That(first.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            Assert.That(second.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            first.Tick();
            second.Tick();

            Assert.That(first.Submit(new MoveCmd
            {
                Tick = 1, PlayerId = 0, Sequence = 2,
                EntityId = entity.EntityId, TargetX = entity.GridX, TargetY = entity.GridY,
            }).Accepted, Is.True);
            Assert.That(second.Submit(new MoveCmd
            {
                Tick = 1, PlayerId = 0, Sequence = 3,
                EntityId = entity.EntityId, TargetX = entity.GridX, TargetY = entity.GridY,
            }).Accepted, Is.True);

            first.Tick();
            second.Tick();

            Assert.That(first.State.Checksum, Is.Not.EqualTo(second.State.Checksum));
        }

        [Test]
        public void Checksum_ChangesForPathPhaseAllocatorAndPendingEventDifferences()
        {
            var pathA = WorldWithPath(11, 22, out _);
            var pathB = WorldWithPath(11, 33, out _);
            Assert.That(pathA.ComputeChecksum(), Is.Not.EqualTo(pathB.ComputeChecksum()));

            using SimBattleSession deploying = SessionTestDriver.StartedDeployingBattle();
            using SimBattleSession battling = SessionTestDriver.StartedBattle();
            Assert.That(battling.Submit(new EndDeploymentCmd
            {
                Tick = battling.State.NextTick,
                PlayerId = 0,
                Sequence = 1,
            }).Accepted, Is.True);
            battling.Tick();
            Assert.That(deploying.State.Phase, Is.Not.EqualTo(battling.State.Phase));
            Assert.That(deploying.State.Checksum, Is.Not.EqualTo(battling.State.Checksum));

            var cleanWorld = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            var advancedAllocator = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity temporary = advancedAllocator.CreateEntity(typeof(SimHealthComponent));
            advancedAllocator.DestroyEntity(temporary);
            Assert.That(advancedAllocator.ComputeChecksum(), Is.Not.EqualTo(cleanWorld.ComputeChecksum()));

            var noEventWorld = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            var pendingEventWorld = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity attacker = pendingEventWorld.CreateEntity(typeof(SimHealthComponent));
            SimEntity target = pendingEventWorld.CreateEntity(typeof(SimHealthComponent));
            noEventWorld.CreateEntity(typeof(SimHealthComponent));
            noEventWorld.CreateEntity(typeof(SimHealthComponent));
            pendingEventWorld.EnqueueEvent(new SimDamageEvent
            {
                Amount = 5,
                Attacker = attacker,
                Target = target,
            });
            Assert.That(pendingEventWorld.ComputeChecksum(), Is.Not.EqualTo(noEventWorld.ComputeChecksum()));
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
        public void Rollback_AfterTickT_ResumesAtTPlusOneWithOriginalChecksumSequence()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattlingBattle();
            session.Tick();
            SimSnapshot snapshot = session.CaptureSnapshot();
            var expectedChecksums = new List<ulong>();

            for (int index = 0; index < 3; index++)
                expectedChecksums.Add(session.Tick().Checksum);

            SnapshotRestoreResult restored = session.RestoreSnapshot(snapshot);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.Restored));
            Assert.That(session.State.NextTick, Is.EqualTo(snapshot.CompletedTick + 1));
            for (int index = 0; index < expectedChecksums.Count; index++)
                Assert.That(session.Tick().Checksum, Is.EqualTo(expectedChecksums[index]));
        }

        [Test]
        public void RollbackReplay_MatchesOriginalChecksumSequence()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            var original = new List<ulong>();
            SimSnapshot snapshot = null;

            for (int tick = 0; tick < 8; tick++)
            {
                original.Add(session.Tick().Checksum);
                if (tick == 3) snapshot = session.CaptureSnapshot();
            }

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(session.RestoreSnapshot(snapshot).Restored, Is.True);
            Assert.That(session.State.NextTick, Is.EqualTo(snapshot.CompletedTick + 1));

            for (int tick = snapshot.NextTick; tick < original.Count; tick++)
                Assert.That(session.Tick().Checksum, Is.EqualTo(original[tick]));
        }

        [Test]
        public void SessionRestart_MatchesFreshSessionState()
        {
            byte[] manifest = SessionTestDriver.ManifestBytes();
            BattleInit init = SessionTestDriver.BattleInit(42);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(manifest);
            using ISimBattleSession fresh = new SimBattleSessionFactory().Create(manifest);

            StartResult accepted = session.Start(init);
            ulong checksum = session.State.Checksum;
            StartResult rejected = session.Start(init);
            StartResult freshAccepted = fresh.Start(init);

            Assert.That(accepted.Code, Is.EqualTo(StartCode.Accepted));
            Assert.That(rejected.Code, Is.EqualTo(StartCode.AlreadyStarted));
            Assert.That(session.State.Checksum, Is.EqualTo(checksum));
            Assert.That(freshAccepted.Code, Is.EqualTo(StartCode.Accepted));
            Assert.That(session.State.Checksum, Is.EqualTo(fresh.State.Checksum));
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

        [Test]
        public void Snapshot_PathMutationAndSystemClock_AreIsolatedAndRestored()
        {
            var world = WorldWithPath(11, 22, out SimEntity entity);
            TickSnapshot snapshot = world.ExportFullSnapshot();
            Assert.That(world.TryGetComponent(entity, out SimMovementComponent live), Is.True);
            live.Path[0] = 99;
            world.SetComponent(entity, live);
            world.RestoreFromFull(snapshot);
            SimEntity restoredEntity = SimEntity.Invalid;
            world.ForEachComponent<SimMovementComponent>((candidate, movement) => restoredEntity = candidate);
            Assert.That(world.TryGetComponent(restoredEntity, out SimMovementComponent restored), Is.True);
            Assert.That(restored.Path, Is.EqualTo(new[] { 11, 22 }));

            using SimBattleSession session = SessionTestDriver.StartedBattle();
            SimSnapshot captured = session.CaptureSnapshot();
            session.Tick();
            SnapshotRestoreResult result = session.RestoreSnapshot(captured);
            Assert.That(result.Code, Is.EqualTo(SnapshotRestoreCode.Restored));
            Assert.That(session.State.Checksum, Is.EqualTo(captured.StateChecksum));
            Assert.That(session.State.NextTick, Is.EqualTo(captured.NextTick));
        }

        [Test]
        public void SnapshotRestore_ForeignSchemaCorruptOrStale_IsRejectedAtomically()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            SimSnapshot source = session.CaptureSnapshot();
            ulong before = session.State.Checksum;

            Assert.That(session.RestoreSnapshot(CopySnapshot(source,
                matchId: source.MatchId + "-foreign")).Code,
                Is.EqualTo(SnapshotRestoreCode.ForeignMatch));
            Assert.That(session.State.Checksum, Is.EqualTo(before));

            Assert.That(session.RestoreSnapshot(CopySnapshot(source,
                schemaVersion: source.SchemaVersion + 1)).Code,
                Is.EqualTo(SnapshotRestoreCode.UnsupportedSchema));
            Assert.That(session.State.Checksum, Is.EqualTo(before));

            byte[] corruptPayload = source.Payload;
            corruptPayload[0] ^= 0x01;
            Assert.That(session.RestoreSnapshot(CopySnapshot(source, payload: corruptPayload)).Code,
                Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(before));

            Assert.That(session.RestoreSnapshot(CopySnapshot(source, timelineRevision: -1)).Code,
                Is.EqualTo(SnapshotRestoreCode.StaleRevision));
            Assert.That(session.State.Checksum, Is.EqualTo(before));
        }

        private static SimWorld WorldWithPath(int first, int second, out SimEntity entity)
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            entity = world.CreateEntity(typeof(SimMovementComponent));
            world.SetComponent(entity, new SimMovementComponent
            {
                Speed = LogicInt.FromInt(1),
                CellSize = LogicInt.FromInt(1),
                Path = new List<int> { first, second },
            });
            return world;
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
            int? schemaVersion = null, long? timelineRevision = null, byte[] payload = null)
        {
            return new SimSnapshot(
                matchId ?? source.MatchId,
                source.SessionId,
                schemaVersion ?? source.SchemaVersion,
                source.ContentHash,
                source.CompletedTick,
                source.NextTick,
                timelineRevision ?? source.TimelineRevision,
                payload?.Length ?? source.UncompressedLength,
                source.StateChecksum,
                payload ?? source.Payload,
                source.PayloadSha256);
        }
    }
}
