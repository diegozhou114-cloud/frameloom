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

        [Test]
        public void Village_AddBuilding_NullBuildingIsRejected()
        {
            var village = new VillageData(_ => null);

            Assert.That(village.AddBuilding(null), Is.False);
            Assert.That(village.Buildings, Is.Empty);
            Assert.DoesNotThrow(() => village.IsOccupied(0, 0));
        }

        [Test]
        public void Village_AddBuilding_EmptyIdIsRejected()
        {
            var village = new VillageData(_ => null);
            var building = new BuildingData { Id = string.Empty, TypeId = 1 };

            Assert.That(village.AddBuilding(building), Is.False);
            Assert.That(village.Buildings, Is.Empty);
        }

        [Test]
        public void Village_AddBuilding_DuplicateIdIsRejected()
        {
            var village = new VillageData(_ => null);
            var first = new BuildingData { Id = "shared-id", TypeId = 1 };
            var duplicate = new BuildingData { Id = "shared-id", TypeId = 2 };

            Assert.That(village.AddBuilding(first), Is.True);
            Assert.That(village.AddBuilding(duplicate), Is.False);
            Assert.That(village.Buildings, Is.EquivalentTo(new[] { first }));
        }
    }
}
