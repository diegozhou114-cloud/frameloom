using System;
using System.Collections;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class ChecksumWorkingSetTests
    {
        [Test]
        public void WorldChecksum_GoldenCoversChunksArchetypesSwapRemoveAndPendingDamage()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 300);
            var healthEntities = new SimEntity[130];
            var transformEntities = new SimEntity[130];
            for (int index = 0; index < healthEntities.Length; index++)
            {
                SimEntity entity = world.CreateEntity(typeof(SimHealthComponent));
                world.SetComponent(entity, SimHealthComponent.Create(1000 + index));
                healthEntities[index] = entity;
            }
            for (int index = 0; index < transformEntities.Length; index++)
            {
                SimEntity entity = world.CreateEntity(
                    typeof(SimTransformComponent), typeof(SimHealthComponent));
                world.SetComponent(entity, SimTransformComponent.Create(
                    index, index * 2, LogicInt.FromInt(1)));
                world.SetComponent(entity, SimHealthComponent.Create(2000 + index));
                transformEntities[index] = entity;
            }

            world.DestroyEntity(healthEntities[3]);
            world.DestroyEntity(transformEntities[5]);
            world.EnqueueEvent(new SimDamageEvent
            {
                Amount = 17,
                Attacker = healthEntities[0],
                Target = transformEntities[0],
            });
            ulong expected = world.ComputeChecksum();
            Assert.That(expected, Is.EqualTo(0x48735325DD7104FEUL));

            world.ConsumeEvents<SimDamageEvent>(_ => { });
            ulong snapshotChecksum = world.ComputeChecksum();
            TickSnapshot snapshot = world.ExportFullSnapshot();

            world.DestroyEntity(healthEntities[4]);
            world.RestoreFromFull(snapshot);

            Assert.That(world.ComputeChecksum(), Is.EqualTo(snapshotChecksum));
        }

        [Test]
        public void CommandBufferChecksum_GoldenCoversPendingCommands()
        {
            var commands = new CommandBuffer();
            commands.AddCommand(new MoveCmd
            {
                Tick = 8, PlayerId = 2, Sequence = 4,
                EntityId = 12, TargetX = 30, TargetY = 40,
            });
            commands.AddCommand(new DeployCmd
            {
                Tick = 7, PlayerId = 1, Sequence = 3,
                UnitTypeId = 10, GridX = 5, GridY = 6,
            });

            Assert.That(commands.HashInto(DeterministicHash64.OffsetBasis),
                Is.EqualTo(0x746C6820E2460E80UL));
        }

        [Test]
        public void SessionFactory_ExternalManifestMutationDoesNotChangeOwnedChecksum()
        {
            byte[] firstBytes = SessionTestDriver.ManifestBytes();
            byte[] secondBytes = (byte[])firstBytes.Clone();
            using ISimBattleSession first = new SimBattleSessionFactory().Create(firstBytes);
            using ISimBattleSession second = new SimBattleSessionFactory().Create(secondBytes);
            firstBytes[0] ^= 0x7F;

            BattleInit init = SessionTestDriver.BattleInit(42);
            Assert.That(first.Start(init).Accepted, Is.True);
            Assert.That(second.Start(init).Accepted, Is.True);
            Assert.That(first.State.Checksum, Is.EqualTo(second.State.Checksum));
        }

        [Test]
        public void SessionChecksumState_PublicConstructorDefensivelyCopiesContentHash()
        {
            byte[] contentHash = { 1, 2, 3, 4 };
            var state = new SessionChecksumState(1, SimComponentSchema.Version,
                contentHash, 42, SessionLifecycle.Running, SimCombatPhase.Battling,
                3, 4, 0, null, -1, 5, 6);
            ulong expected = state.HashInto(DeterministicHash64.OffsetBasis);

            contentHash[0] = 99;

            Assert.That(state.HashInto(DeterministicHash64.OffsetBasis), Is.EqualTo(expected));
        }

        [Test]
        public void DeterministicHash64_ByteRangeExcludesUnusedUtf8BufferCapacity()
        {
            MethodInfo addRange = typeof(DeterministicHash64).GetMethod(
                "AddBytes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(byte[]), typeof(int), typeof(int) }, null);
            Assert.That(addRange, Is.Not.Null,
                "Checksum hashing needs an internal byte-range entry point.");

            byte[] buffer = Encoding.UTF8.GetBytes("long-\u4F24\u5BB3-buffer");
            byte[] encoded = Encoding.UTF8.GetBytes("\u4F24");
            Buffer.BlockCopy(encoded, 0, buffer, 0, encoded.Length);
            for (int index = encoded.Length; index < buffer.Length; index++) buffer[index] = 0xA5;

            object boxed = new DeterministicHash64(DeterministicHash64.OffsetBasis);
            addRange.Invoke(boxed, new object[] { buffer, 0, encoded.Length });
            ulong actual = (ulong)typeof(DeterministicHash64)
                .GetProperty(nameof(DeterministicHash64.Value)).GetValue(boxed);
            var expected = new DeterministicHash64(DeterministicHash64.OffsetBasis);
            expected.AddBytes(encoded);

            Assert.That(actual, Is.EqualTo(expected.Value));
        }

        [Test]
        public void SimWorld_ComputeChecksumClearsSortedKeyWorkingSetAfterSuccess()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            world.CreateEntity(typeof(SimHealthComponent));

            world.ComputeChecksum();

            ICollection sortedKeys = (ICollection)typeof(SimWorld).GetField(
                "_sortedKeys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            Assert.That(sortedKeys.Count, Is.EqualTo(0));
        }

        [Test]
        public void SimWorld_ExportFullSnapshotClearsSortedKeyWorkingSetAfterSuccess()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            world.CreateEntity(typeof(SimHealthComponent));

            world.ExportFullSnapshot();

            ICollection sortedKeys = (ICollection)typeof(SimWorld).GetField(
                "_sortedKeys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            Assert.That(sortedKeys.Count, Is.EqualTo(0));
        }

        [Test]
        public void SimWorld_ClearPoolsReleasesWorkingSetReferences()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity entity = world.CreateEntity(typeof(SimHealthComponent));
            world.SetComponent(entity, SimHealthComponent.Create(100));
            TickSnapshot snapshot = world.ExportFullSnapshot();
            IList sortedKeys = (IList)typeof(SimWorld).GetField(
                "_sortedKeys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            ICollection sortedLocations = (ICollection)typeof(SimWorld).GetField(
                "_sortedLocations", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            sortedKeys.Add("sentinel");
            world.ForEachComponent<SimHealthComponent>((_, _) => { });
            Assert.That(sortedKeys.Count, Is.GreaterThan(0));
            Assert.That(sortedLocations.Count, Is.GreaterThan(0));

            world.RestoreFromFull(snapshot);

            Assert.That(sortedKeys.Count, Is.EqualTo(0));
            Assert.That(sortedLocations.Count, Is.EqualTo(0));

            sortedKeys.Add("sentinel");
            world.ForEachComponent<SimHealthComponent>((_, _) => { });
            world.ClearPools();

            Assert.That(sortedKeys.Count, Is.EqualTo(0));
            Assert.That(sortedLocations.Count, Is.EqualTo(0));
        }
    }
}
