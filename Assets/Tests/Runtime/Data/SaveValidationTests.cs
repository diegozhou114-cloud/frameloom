using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class SaveValidationTests : RuntimeTestBase
    {
        private const long Now = 1_700_000_000;

        [Test]
        public void SaveGame_UnknownOrInvalidSchema_LeavesRuntimeStateUnchanged()
        {
            var player = new PlayerData();
            var village = new VillageData();
            village.Name.Value = "Original Village";
            village.MaxBuildings.Value = 40;
            village.AddBuilding(BuildingData.Create(1, 1, 1));
            int originalGold = player.Gold.Value;
            var save = Valid(Building("town-hall", 0, 0));
            save.Version = 99;
            save.PlayerName = "Mutated";
            save.Gold = 1;
            save.VillageName = "Mutated Village";

            bool migrated = SaveDataMapper.TryMigrateToCurrent(
                save, 0, out _, out string error);

            Assert.That(migrated, Is.False);
            Assert.That(error, Does.Contain("version"));
            Assert.That(player.Gold.Value, Is.EqualTo(originalGold));
            Assert.That(village.Name.Value, Is.EqualTo("Original Village"));
            Assert.That(village.MaxBuildings.Value, Is.EqualTo(40));
            Assert.That(village.Buildings, Has.Count.EqualTo(1));
        }

        [Test]
        public void InvalidBuilding_LeavesPlayerAndVillageUnchanged()
        {
            var player = new PlayerData();
            var village = new VillageData();
            village.AddBuilding(BuildingData.Create(1, 1, 1));
            int originalGold = player.Gold.Value;
            PlayerSaveData save = Valid(new BuildingSaveData
            {
                Id = "bad",
                TypeId = 999,
                Level = 1,
                State = BuildingState.Upgrading,
                UpgradeEndTime = 0,
            });

            bool applied = SaveDataMapper.TryBuildValidatedState(save,
                PlayerSaveData.CurrentVersion, _ => null, 44, 44, Now,
                out _, out _);

            Assert.That(applied, Is.False);
            Assert.That(player.Gold.Value, Is.EqualTo(originalGold));
            Assert.That(village.Buildings, Has.Count.EqualTo(1));
        }

        [Test]
        public void MalformedRowsRangesTimestampsAndFootprints_AreRejectedBeforeModelSwap()
        {
            PlayerSaveData nullRow = Valid(Building("a", 0, 0));
            nullRow.Buildings.Add(null);
            PlayerSaveData highResource = Valid(Building("a", 0, 0));
            highResource.Gold = int.MaxValue;
            PlayerSaveData highPlayerLevel = Valid(Building("a", 0, 0));
            highPlayerLevel.PlayerLevel = 1001;
            PlayerSaveData highBuildingLevel = Valid(Building("a", 0, 0, level: 6));
            PlayerSaveData outOfBounds = Valid(Building("a", 43, 43));
            PlayerSaveData overlapping = Valid(
                Building("a", 0, 0), Building("b", 1, 1));
            PlayerSaveData badUpgradeTime = Valid(Building("a", 0, 0,
                state: BuildingState.Upgrading, upgradeEnd: Now + 31_536_001));
            PlayerSaveData badSaveTime = Valid(Building("a", 0, 0));
            badSaveTime.SaveTime = "2026-99-99";

            foreach (PlayerSaveData malformed in new[]
            {
                nullRow, highResource, highPlayerLevel, highBuildingLevel,
                outOfBounds, overlapping, badUpgradeTime, badSaveTime,
            })
            {
                Assert.That(SaveDataMapper.TryBuildValidatedState(malformed,
                    PlayerSaveData.CurrentVersion, Resolve, 44, 44, Now,
                    out _, out _), Is.False);
            }
        }

        [Test]
        public void ValidatedState_ModelSwapPublishesOnlyAfterBothModelsAreVisible()
        {
            var manager = CreateOwnedObject("DataManager").AddComponent<DataManager>();
            var player = new PlayerData();
            player.Name.SetSilent("Atomic Player");
            var village = new VillageData();
            village.Name.SetSilent("Atomic Village");
            int notifications = 0;
            EventBus.Subscribe(DataManager.EventDataReady, () =>
            {
                notifications++;
                Assert.That(manager.Player.Name.Value, Is.EqualTo("Atomic Player"));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("Atomic Village"));
            });

            manager.ReplaceValidatedState(new ValidatedSaveState(player, village));

            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void DefaultV2AndLegacyV1Saves_NormalizeThenValidateDeterministically()
        {
            string defaultJson = UnityEngine.JsonUtility.ToJson(
                GameInitializer.CreateDefaultSaveData());
            PlayerSaveData current =
                UnityEngine.JsonUtility.FromJson<PlayerSaveData>(defaultJson);
            Assert.That(SaveDataMapper.TryMigrateToCurrent(
                current, 0, out PlayerSaveData normalizedDefault,
                out string defaultError), Is.True, defaultError);
            Assert.That(SaveDataMapper.TryBuildValidatedState(normalizedDefault,
                PlayerSaveData.CurrentVersion, ResolveDefault, 44, 44,
                System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                out ValidatedSaveState validated, out string applyError), Is.True, applyError);
            Assert.That(validated.Village.Buildings, Has.Count.EqualTo(4));
            Assert.That(validated.Village.Buildings.Select(building => building.Id)
                .Distinct().Count(), Is.EqualTo(4));

            const string legacyJson =
                "{\"Version\":1,\"PlayerName\":\"Legacy\",\"PlayerLevel\":1," +
                "\"TownHallLevel\":1,\"Gold\":10,\"Elixir\":20,\"DarkElixir\":0," +
                "\"Gems\":1,\"Trophies\":0,\"Exp\":0," +
                "\"Buildings\":[{\"TypeId\":1,\"GridX\":2,\"GridY\":3,\"Level\":1}]}";
            PlayerSaveData legacy =
                UnityEngine.JsonUtility.FromJson<PlayerSaveData>(legacyJson);
            Assert.That(SaveDataMapper.TryMigrateToCurrent(
                legacy, 2, out PlayerSaveData first, out string firstError),
                Is.True, firstError);
            Assert.That(SaveDataMapper.TryMigrateToCurrent(
                legacy, 2, out PlayerSaveData second, out string secondError),
                Is.True, secondError);
            Assert.That(first.Buildings[0].Id, Is.EqualTo("legacy-02-0000"));
            Assert.That(second.Buildings[0].Id, Is.EqualTo(first.Buildings[0].Id));
            Assert.That(first.Buildings[0].State, Is.EqualTo(BuildingState.Normal));
            Assert.That(first.Buildings[0].UpgradeEndTime, Is.Zero);
        }

        private static BuildingConfig Resolve(int id) => id == 1
            ? new BuildingConfig { Id = 1, MaxLevel = 5, SizeX = 2, SizeY = 2 }
            : null;

        private static BuildingConfig ResolveDefault(int id) =>
            id == 1 || id == 2 || id == 3 || id == 5
                ? new BuildingConfig { Id = id, MaxLevel = 20, SizeX = 1, SizeY = 1 }
                : null;

        private static BuildingSaveData Building(string id, int x, int y, int level = 1,
            BuildingState state = BuildingState.Normal, long upgradeEnd = 0) =>
            new BuildingSaveData
            {
                Id = id,
                TypeId = 1,
                GridX = x,
                GridY = y,
                Level = level,
                State = state,
                UpgradeEndTime = upgradeEnd,
            };

        private static PlayerSaveData Valid(params BuildingSaveData[] rows) =>
            new PlayerSaveData
            {
                Version = PlayerSaveData.CurrentVersion,
                PlayerName = "Valid",
                PlayerLevel = 1,
                TownHallLevel = 1,
                Gold = 10,
                Elixir = 10,
                DarkElixir = 0,
                Gems = 1,
                Trophies = 0,
                Exp = 0,
                VillageName = "Village",
                MaxBuildings = 40,
                SaveTime = System.DateTimeOffset.FromUnixTimeSeconds(Now).ToString("O"),
                Buildings = rows.ToList(),
            };
    }
}
