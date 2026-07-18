using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class BuildingSaveRoundTripTests
    {
        [Test]
        public void SaveGame_BuildingRoundTrip_PreservesCompleteState()
        {
            var player = new PlayerData();
            var village = new VillageData();
            var building = BuildingData.Create(10, 7, 9);
            building.Id = "building-42";
            building.Level.Value = 6;
            building.State.Value = BuildingState.Upgrading;
            building.UpgradeEndTime.Value = 1924992000;
            Assert.That(village.AddBuilding(building), Is.True);

            PlayerSaveData save = SaveDataMapper.Capture(player, village);
            BuildingData restored = SaveDataMapper.RestoreBuilding(save.Buildings[0]);

            Assert.That(save.Version, Is.EqualTo(PlayerSaveData.CurrentVersion));
            Assert.That(save.VillageName, Is.EqualTo(village.Name.Value));
            Assert.That(save.MaxBuildings, Is.EqualTo(village.MaxBuildings.Value));
            Assert.That(restored.Id, Is.EqualTo("building-42"));
            Assert.That(restored.TypeId, Is.EqualTo(10));
            Assert.That(restored.GridX.Value, Is.EqualTo(7));
            Assert.That(restored.GridY.Value, Is.EqualTo(9));
            Assert.That(restored.Level.Value, Is.EqualTo(6));
            Assert.That(restored.State.Value, Is.EqualTo(BuildingState.Upgrading));
            Assert.That(restored.UpgradeEndTime.Value, Is.EqualTo(1924992000));
        }
    }
}
