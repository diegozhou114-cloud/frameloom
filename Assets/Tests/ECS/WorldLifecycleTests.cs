using System;
using NUnit.Framework;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.ECS.Tests
{
    [TestFixture]
    public sealed class WorldLifecycleTests
    {
        [Test]
        public void DestroyEntity_Twice_DoesNotCreateSimultaneousEntitiesWithTheSameId()
        {
            var world = new World();
            Entity destroyed = world.CreateEntity();

            world.DestroyEntity(destroyed);
            world.DestroyEntity(destroyed);

            Entity firstReplacement = world.CreateEntity();
            Entity secondReplacement = world.CreateEntity();

            Assert.That(firstReplacement.Id, Is.Not.EqualTo(secondReplacement.Id));
            Assert.That(world.EntityCount, Is.EqualTo(2));
        }

        [Test]
        public void Reset_DoesNotAllowAPreResetEntityToDestroyItsReplacement()
        {
            var world = new World();
            Entity preResetEntity = world.CreateEntity();

            world.Reset();
            Entity replacement = world.CreateEntity();
            world.DestroyEntity(preResetEntity);

            Assert.That(preResetEntity, Is.Not.EqualTo(replacement));
            Assert.That(world.EntityCount, Is.EqualTo(1));
        }

        [Test]
        public void CreateEntity_AllowsZeroInitialCapacity()
        {
            var world = new World(0);

            Entity entity = world.CreateEntity();

            Assert.That(entity.Id, Is.EqualTo(0));
            Assert.That(world.EntityCount, Is.EqualTo(1));
        }

        [Test]
        public void ComponentQueries_ReturnFalseForInvalidAndDestroyedEntities()
        {
            var world = new World();
            Entity entity = world.CreateEntity();
            world.DestroyEntity(entity);

            Assert.That(world.HasComponent<TestComponent>(Entity.Invalid), Is.False);
            Assert.That(world.TryGetComponent<TestComponent>(Entity.Invalid, out _), Is.False);
            Assert.That(world.HasComponent<TestComponent>(entity), Is.False);
            Assert.That(world.TryGetComponent<TestComponent>(entity, out _), Is.False);
        }

        [Test]
        public void ComponentWrites_RejectDestroyedEntities()
        {
            var world = new World();
            Entity entity = world.CreateEntity();
            world.DestroyEntity(entity);

            Assert.DoesNotThrow(() => world.RemoveComponent<TestComponent>(entity));
            Assert.Throws<InvalidOperationException>(() =>
                world.AddComponent(entity, default(TestComponent)));
            Assert.Throws<InvalidOperationException>(() =>
                world.SetComponent(entity, default(TestComponent)));
        }

        [Test]
        public void GetComponent_ReportsMissingComponentAfterWorldExpansion()
        {
            var world = new World(1);
            Entity entityWithComponent = world.CreateEntity();
            world.AddComponent(entityWithComponent, default(TestComponent));
            Entity entityWithoutComponent = world.CreateEntity();

            Assert.Throws<InvalidOperationException>(() =>
                world.GetComponent<TestComponent>(entityWithoutComponent).GetHashCode());
        }

        private struct TestComponent : IComponent
        {
        }
    }
}
