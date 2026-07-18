using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class ResourceProducerSaturationTests
    {
        [Test]
        public void ResourceProducer_NearCap_SaturatesWithoutOverflow()
        {
            var configs = new Dictionary<int, BuildingConfig>
            {
                [2] = Producer(2, ResourceType.Gold),
                [3] = Producer(3, ResourceType.Elixir),
                [10] = Producer(10, ResourceType.DarkElixir),
            };
            var village = new VillageData(id => configs[id]);
            foreach (int typeId in configs.Keys)
            {
                BuildingData building = BuildingData.Create(typeId, typeId, 0);
                building.Level.Value = 10;
                village.AddBuilding(building);
            }

            var player = new PlayerData();
            player.Gold.Value = int.MaxValue - 2;
            player.Elixir.Value = int.MaxValue - 2;
            player.DarkElixir.Value = int.MaxValue - 2;
            var producer = new ResourceProducer(village, player, id => configs[id]);

            producer.Start();
            producer.Tick(1f);

            Assert.That(player.Gold.Value, Is.EqualTo(int.MaxValue - 1));
            Assert.That(player.Elixir.Value, Is.EqualTo(int.MaxValue - 1));
            Assert.That(player.DarkElixir.Value, Is.EqualTo(int.MaxValue - 1));
        }

        private static BuildingConfig Producer(int id, ResourceType type) =>
            new BuildingConfig { Id = id, Category = "Resource", ResourceType = type };
    }
}
