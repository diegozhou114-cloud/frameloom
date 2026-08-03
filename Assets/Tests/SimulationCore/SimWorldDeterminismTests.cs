using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public class SimWorldDeterminismTests
    {
        private SimWorld CreateWorld()
        {
            return new SimWorld(ComponentTypeRegistry.CreateCombatRegistry(), 64);
        }

        [Test]
        public void SameOperations_SameChecksum()
        {
            var w1 = CreateWorld();
            var w2 = CreateWorld();

            foreach (var world in new[] { w1, w2 })
            {
                var e1 = world.CreateEntity(typeof(SimTransformComponent), typeof(SimHealthComponent));
                world.SetComponent(e1, new SimTransformComponent { GridX = 10, GridY = 20 });
                world.SetComponent(e1, new SimHealthComponent { CurrentHP = 100, MaxHP = 100 });

                var e2 = world.CreateEntity(typeof(SimTransformComponent), typeof(SimHealthComponent));
                world.SetComponent(e2, new SimTransformComponent { GridX = 30, GridY = 40 });
                world.SetComponent(e2, new SimHealthComponent { CurrentHP = 50, MaxHP = 80 });
            }

            Assert.That(w1.ComputeChecksum(), Is.EqualTo(w2.ComputeChecksum()));
        }

        [Test]
        public void DifferentPosition_DifferentChecksum()
        {
            var w1 = CreateWorld();
            var e1 = w1.CreateEntity(typeof(SimTransformComponent));
            w1.SetComponent(e1, new SimTransformComponent { GridX = 10, GridY = 20 });
            var cs1 = w1.ComputeChecksum();

            var w2 = CreateWorld();
            var e2 = w2.CreateEntity(typeof(SimTransformComponent));
            w2.SetComponent(e2, new SimTransformComponent { GridX = 99, GridY = 99 });
            var cs2 = w2.ComputeChecksum();

            Assert.That(cs1, Is.Not.EqualTo(cs2),
                "不同组件值应产生不同 checksum");
        }

        [Test]
        public void DestroyEntity_ChangesChecksum()
        {
            var w1 = CreateWorld();
            var e = w1.CreateEntity(typeof(SimHealthComponent));
            w1.SetComponent(e, new SimHealthComponent { CurrentHP = 50, MaxHP = 100 });
            var csBefore = w1.ComputeChecksum();

            w1.DestroyEntity(e);
            var csAfter = w1.ComputeChecksum();

            Assert.That(csBefore, Is.Not.EqualTo(csAfter),
                "销毁实体后 checksum 应改变");
        }

        [Test]
        public void EntityID_MonotonicIncrease()
        {
            var world = CreateWorld();
            var e1 = world.CreateEntity(typeof(SimHealthComponent));
            var e2 = world.CreateEntity(typeof(SimHealthComponent));
            var e3 = world.CreateEntity(typeof(SimHealthComponent));

            Assert.That(e1.Id, Is.EqualTo(0));
            Assert.That(e2.Id, Is.EqualTo(1));
            Assert.That(e3.Id, Is.EqualTo(2));
        }

        [Test]
        public void DestroyThenCreate_NewEntityHasNewId()
        {
            var world = CreateWorld();
            var e1 = world.CreateEntity(typeof(SimHealthComponent)); // Id=0
            world.DestroyEntity(e1);
            var e2 = world.CreateEntity(typeof(SimHealthComponent));
            // 不复用 Id — 单调递增
            Assert.That(e2.Id, Is.EqualTo(1));
        }

        [Test]
        public void TenRuns_SameOps_SameChecksum()
        {
            ulong expected = 0;
            for (int run = 0; run < 10; run++)
            {
                var world = CreateWorld();

                for (int i = 0; i < 10; i++)
                {
                    var e = world.CreateEntity(typeof(SimTransformComponent), typeof(SimHealthComponent));
                    world.SetComponent(e, new SimTransformComponent { GridX = i, GridY = i * 2 });
                    world.SetComponent(e, new SimHealthComponent { CurrentHP = 100 - i * 3, MaxHP = 100 });
                }

                ulong cs = world.ComputeChecksum();
                if (run == 0)
                    expected = cs;
                else
                    Assert.That(cs, Is.EqualTo(expected),
                        $"run={run}: checksum 不一致");
            }
        }

        [Test]
        public void FullSnapshot_Restore_ChecksumIdentical()
        {
            var world = CreateWorld();
            SimEntity firstEntity = SimEntity.Invalid;
            for (int i = 0; i < 10; i++)
            {
                var e = world.CreateEntity(typeof(SimTransformComponent), typeof(SimHealthComponent));
                if (i == 0) firstEntity = e;
                world.SetComponent(e, new SimTransformComponent { GridX = i, GridY = i * 2 });
                world.SetComponent(e, new SimHealthComponent { CurrentHP = 100 - i * 3, MaxHP = 100 });
            }

            ulong csBefore = world.ComputeChecksum();
            var snapshot = world.ExportFullSnapshot();

            // 修改世界
            world.DestroyEntity(firstEntity);
            world.CreateEntity(typeof(SimHealthComponent));

            // 恢复
            world.RestoreFromFull(snapshot);
            ulong csAfter = world.ComputeChecksum();

            Assert.That(csAfter, Is.EqualTo(csBefore),
                "快照导出+恢复后 checksum 应与原始一致");
        }

        [Test]
        public void RollbackThenReuseId_StaleEntityHandleIsRejected()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            TickSnapshot beforeFutureEntity = world.ExportFullSnapshot();
            SimEntity stale = world.CreateEntity(typeof(SimHealthComponent));

            world.RestoreFromFull(beforeFutureEntity);
            SimEntity replacement = world.CreateEntity(typeof(SimHealthComponent));

            Assert.That(replacement.Id, Is.EqualTo(stale.Id));
            Assert.That(world.TryGetComponent<SimHealthComponent>(stale, out _), Is.False);
            Assert.That(world.TryGetComponent<SimHealthComponent>(replacement, out _), Is.True);
        }

        [Test]
        public void EquivalentWorlds_DifferentChunkHistories_SameOrderAndChecksum()
        {
            SimWorld left = BuildWorldWithEntities(130);
            SimWorld right = BuildWorldWithEntities(130);
            left.DestroyEntity(FindEntity(left, 3));
            left.DestroyEntity(FindEntity(left, 5));
            right.DestroyEntity(FindEntity(right, 5));
            right.DestroyEntity(FindEntity(right, 3));

            var ids = new List<int>();
            left.ForEachComponent<SimHealthComponent>((entity, health) => ids.Add(entity.Id));

            Assert.That(ids, Is.Ordered.Ascending);
            Assert.That(left.ComputeChecksum(), Is.EqualTo(right.ComputeChecksum()));
        }

        [Test]
        public void DifferentMovementPath_ChangesChecksum()
        {
            SimWorld first = CreateWorld();
            SimWorld second = CreateWorld();
            SimEntity firstEntity = first.CreateEntity(typeof(SimMovementComponent));
            SimEntity secondEntity = second.CreateEntity(typeof(SimMovementComponent));
            first.SetComponent(firstEntity, new SimMovementComponent
            {
                Speed = LogicInt.FromInt(1),
                CellSize = LogicInt.FromInt(1),
                Path = new List<int> { (1 << 16) | 1 },
            });
            second.SetComponent(secondEntity, new SimMovementComponent
            {
                Speed = LogicInt.FromInt(1),
                CellSize = LogicInt.FromInt(1),
                Path = new List<int> { (2 << 16) | 2 },
            });

            Assert.That(first.ComputeChecksum(), Is.Not.EqualTo(second.ComputeChecksum()));
        }

        private static SimWorld BuildWorldWithEntities(int count)
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 256);
            for (int id = 0; id < count; id++)
            {
                SimEntity entity = world.CreateEntity(typeof(SimHealthComponent));
                world.SetComponent(entity, SimHealthComponent.Create(100 + id));
            }
            return world;
        }

        private static SimEntity FindEntity(SimWorld world, int id)
        {
            SimEntity found = SimEntity.Invalid;
            world.ForEachComponent<SimHealthComponent>((entity, health) =>
            {
                if (entity.Id == id) found = entity;
            });
            Assert.That(found.IsValid, Is.True, $"Entity {id} was not found");
            return found;
        }
    }
}
