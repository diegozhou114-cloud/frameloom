using NUnit.Framework;
using System.Collections.Generic;

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
    }
}
