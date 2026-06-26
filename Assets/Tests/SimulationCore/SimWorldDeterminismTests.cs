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
            for (int i = 0; i < 10; i++)
            {
                var e = world.CreateEntity(typeof(SimTransformComponent), typeof(SimHealthComponent));
                world.SetComponent(e, new SimTransformComponent { GridX = i, GridY = i * 2 });
                world.SetComponent(e, new SimHealthComponent { CurrentHP = 100 - i * 3, MaxHP = 100 });
            }

            ulong csBefore = world.ComputeChecksum();
            var snapshot = world.ExportFullSnapshot();

            // 修改世界
            world.DestroyEntity(new SimEntity(0, 1));
            world.CreateEntity(typeof(SimHealthComponent));

            // 恢复
            world.RestoreFromFull(snapshot);
            ulong csAfter = world.ComputeChecksum();

            Assert.That(csAfter, Is.EqualTo(csBefore),
                "快照导出+恢复后 checksum 应与原始一致");
        }
    }
}
