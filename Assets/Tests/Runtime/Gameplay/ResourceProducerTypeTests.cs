using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class ResourceProducerTypeTests
    {
        [Test]
        public void ResourceProducer_DarkElixirDrill_ProducesDarkElixirOnly()
        {
            var config = new BuildingConfig
            {
                Id = 10,
                Name = "Dark Elixir Drill",
                Category = "Resource",
                ResourceType = ResourceType.DarkElixir,
            };
            var village = new VillageData(_ => config);
            village.AddBuilding(BuildingData.Create(10, 0, 0));
            var player = new PlayerData();
            player.Gold.Value = 0;
            player.Elixir.Value = 0;
            player.DarkElixir.Value = 0;
            var producer = new ResourceProducer(village, player, _ => config);

            producer.Start();
            producer.Tick(2f);

            Assert.That(player.Gold.Value, Is.Zero);
            Assert.That(player.Elixir.Value, Is.Zero);
            Assert.That(player.DarkElixir.Value, Is.EqualTo(1));
        }
    }
}
