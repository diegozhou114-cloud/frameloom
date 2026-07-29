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

        [Test]
        public void Village_AddBuilding_SecondTownHallIsRejected()
        {
            var village = new VillageData(_ => null);
            var first = new BuildingData { Id = "town-hall-first", TypeId = 1 };
            var second = new BuildingData { Id = "town-hall-second", TypeId = 1 };

            Assert.That(village.AddBuilding(first), Is.True);
            Assert.That(village.AddBuilding(second), Is.False);
            Assert.That(village.Buildings, Is.EquivalentTo(new[] { first }));
        }

        [Test]
        public void Village_AddBuilding_NegativeGridCoordinateIsRejected()
        {
            var village = new VillageData(_ => null);
            BuildingData building = BuildingData.Create(2, -1, 0);

            Assert.That(village.AddBuilding(building), Is.False);
            Assert.That(village.Buildings, Is.Empty);
        }

        [Test]
        public void Village_AddBuilding_OverlappingFootprintIsRejected()
        {
            var config = new BuildingConfig { Id = 10, SizeX = 3, SizeY = 2 };
            var village = new VillageData(id => id == config.Id ? config : null);
            BuildingData first = BuildingData.Create(10, 5, 7);
            BuildingData overlapping = BuildingData.Create(10, 7, 8);

            Assert.That(village.AddBuilding(first), Is.True);
            Assert.That(village.AddBuilding(overlapping), Is.False);
            Assert.That(village.Buildings, Is.EquivalentTo(new[] { first }));
        }

        [Test]
        public void Village_Reset_RestoresDefaultBuildingCapacity()
        {
            var village = new VillageData(_ => null);
            village.MaxBuildings.Value = 1;

            village.Reset();

            Assert.That(village.MaxBuildings.Value, Is.EqualTo(50));
            Assert.That(village.AddBuilding(BuildingData.Create(1, 0, 0)), Is.True);
        }
    }
}
