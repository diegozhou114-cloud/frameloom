using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class ResourceProducerLifecycleTests
    {
        [Test]
        public void ResourceProducer_AddRemoveUpgrade_RecalculatesAndStopUnsubscribes()
        {
            var goldConfig = new BuildingConfig
            {
                Id = 2,
                Name = "Gold Mine",
                Category = "Resource",
                ResourceType = ResourceType.Gold,
            };
            var village = new VillageData(_ => goldConfig);
            var player = new PlayerData();
            BuildingData first = BuildingData.Create(2, 0, 0);
            village.AddBuilding(first);
            var producer = new ResourceProducer(village, player, _ => goldConfig);

            producer.Start();
            Assert.That(producer.GoldPerSecond, Is.EqualTo(0.5f));

            BuildingData second = BuildingData.Create(2, 3, 0);
            second.Level.Value = 2;
            village.AddBuilding(second);
            Assert.That(producer.GoldPerSecond, Is.EqualTo(1.5f));

            second.Level.Value = 3;
            Assert.That(producer.GoldPerSecond, Is.EqualTo(2f));

            village.RemoveBuilding(second.Id);
            Assert.That(producer.GoldPerSecond, Is.EqualTo(0.5f));

            producer.Stop();
            first.Level.Value = 5;
            Assert.That(producer.GoldPerSecond, Is.EqualTo(0.5f));
        }

        [Test]
        public void ResourceProducer_ClearBuildings_UnsubscribesRemovedBuildings()
        {
            var goldConfig = new BuildingConfig
            {
                Id = 2,
                Name = "Gold Mine",
                Category = "Resource",
                ResourceType = ResourceType.Gold,
            };
            var village = new VillageData(_ => goldConfig);
            var player = new PlayerData();
            BuildingData building = BuildingData.Create(2, 0, 0);
            Assert.That(village.AddBuilding(building), Is.True);

            var producer = new ResourceProducer(village, player, _ => goldConfig);
            producer.Start();
            village.ClearBuildings();

            Assert.That(producer.GoldPerSecond, Is.EqualTo(0f));
            building.Level.Value = 5;
            Assert.That(producer.GoldPerSecond, Is.EqualTo(0f));
        }
    }
}
