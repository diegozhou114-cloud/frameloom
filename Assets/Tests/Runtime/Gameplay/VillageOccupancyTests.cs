using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class VillageOccupancyTests
    {
        [Test]
        public void VillageOccupancy_MultiCellFootprintBlocksEveryCell()
        {
            var config = new BuildingConfig { Id = 10, SizeX = 3, SizeY = 2 };
            var village = new VillageData(id => id == config.Id ? config : null);
            BuildingData building = BuildingData.Create(10, 5, 7);
            village.AddBuilding(building);

            for (int x = 5; x < 8; x++)
            for (int y = 7; y < 9; y++)
                Assert.That(village.GetBuildingAt(x, y), Is.SameAs(building));

            Assert.That(village.IsOccupied(8, 7), Is.False);
            Assert.That(village.IsOccupied(5, 9), Is.False);
        }
    }
}
