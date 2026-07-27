using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class CoreTargetingTests
    {
        [Test]
        public void CoreSystems_AcquireTargetAndPlanPathWithoutExternalAI()
        {
            var world = new SimWorld(ComponentTypeRegistry.CreateCombatRegistry(), 64);
            SimEntity defender = world.CreateEntity(
                typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent));
            world.SetComponent(defender, SimTransformComponent.Create(8, 8, LogicInt.FromInt(1)));
            world.SetComponent(defender, SimHealthComponent.Create(200));
            world.SetComponent(defender, SimTeamComponent.Create(SimTeam.Defender));

            SimEntity attacker = world.CreateEntity(
                typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent),
                typeof(SimMovementComponent), typeof(SimAttackComponent), typeof(SimTargetComponent));
            world.SetComponent(attacker, SimTransformComponent.Create(2, 2, LogicInt.FromInt(1)));
            world.SetComponent(attacker, SimHealthComponent.Create(100));
            world.SetComponent(attacker, SimTeamComponent.Create(SimTeam.Attacker));
            world.SetComponent(attacker, SimMovementComponent.Create(3f, 1f));
            world.SetComponent(attacker, SimAttackComponent.Create(25, 1f, 1f, 20));
            world.SetComponent(attacker, SimTargetComponent.Create());

            var systems = SimCombatBootstrap.Build(world);
            systems.UpdateAll();

            Assert.That(world.TryGetComponent(attacker, out SimTargetComponent target), Is.True);
            Assert.That(target.TargetEntity, Is.EqualTo(defender));
            Assert.That(world.TryGetComponent(attacker, out SimMovementComponent movement), Is.True);
            Assert.That(movement.Path, Is.Not.Null.And.Not.Empty);
            Assert.That(movement.PathIndex, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(movement.Path.Count));
        }

        [Test]
        public void BattlePathfinding_DiagonalBesideSingleWall_IsRejected()
        {
            var pathfinder = new IntAStarPathfinding(4, 4,
                (x, y) => !(x == 1 && y == 0));
            List<(int x, int y)> path = pathfinder.FindPath(0, 0, 1, 1);
            Assert.That(path, Is.Not.Empty);
            Assert.That(path[0], Is.Not.EqualTo((1, 1)));
        }

        [Test]
        public void DefenseBuilding_AcquiresAndDamagesAttacker()
        {
            var world = new SimWorld(ComponentTypeRegistry.CreateCombatRegistry(), 64);
            SimEntity defender = CreateCombatant(world, SimTeam.Defender, 2, 2, 200, 25);
            SimEntity attacker = CreateCombatant(world, SimTeam.Attacker, 3, 2, 100, 0);
            SimCombatBootstrap.Build(world).UpdateAll();

            Assert.That(world.TryGetComponent(defender, out SimTargetComponent target), Is.True);
            Assert.That(target.TargetEntity, Is.EqualTo(attacker));
            Assert.That(world.TryGetComponent(attacker, out SimHealthComponent health), Is.True);
            Assert.That(health.CurrentHP, Is.LessThan(100));
        }

        [Test]
        public void DefenseTargetingAndAI_UnityAndDotNet_ProduceSameChecksums()
        {
            var first = CreateTargetingWorld();
            var second = CreateTargetingWorld();
            SimSystemGroup firstSystems = SimCombatBootstrap.Build(first);
            SimSystemGroup secondSystems = SimCombatBootstrap.Build(second);
            ulong checksum = 0;

            for (int tick = 0; tick < 25; tick++)
            {
                firstSystems.UpdateAll();
                secondSystems.UpdateAll();
                checksum = first.ComputeChecksum();
                Assert.That(second.ComputeChecksum(), Is.EqualTo(checksum), $"tick {tick}");
            }

            Assert.That(checksum, Is.EqualTo(0xC80775A9C0B6F13CUL));
        }

        private static SimWorld CreateTargetingWorld()
        {
            var world = new SimWorld(ComponentTypeRegistry.CreateCombatRegistry(), 64);
            CreateCombatant(world, SimTeam.Defender, 8, 8, 200, 25);
            CreateCombatant(world, SimTeam.Attacker, 2, 2, 100, 0);
            return world;
        }

        private static SimEntity CreateCombatant(SimWorld world, int team, int gridX,
            int gridY, int health, int damage)
        {
            var types = new List<Type>
            {
                typeof(SimTransformComponent), typeof(SimHealthComponent),
                typeof(SimTeamComponent), typeof(SimTargetComponent),
            };
            if (damage > 0) types.Add(typeof(SimAttackComponent));
            SimEntity entity = world.CreateEntity(types.ToArray());
            world.SetComponent(entity, SimTransformComponent.Create(gridX, gridY, LogicInt.FromInt(1)));
            world.SetComponent(entity, SimHealthComponent.Create(health));
            world.SetComponent(entity, SimTeamComponent.Create(team));
            world.SetComponent(entity, SimTargetComponent.Create());
            if (damage > 0)
                world.SetComponent(entity, SimAttackComponent.Create(damage, 10f, 1f, 20));
            return entity;
        }
    }
}
