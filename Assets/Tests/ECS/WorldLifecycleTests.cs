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
    }
}
