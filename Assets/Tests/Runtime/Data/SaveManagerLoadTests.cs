using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class SaveManagerLoadTests : RuntimeTestBase
    {
        [Test]
        public void LoadToDataManager_UnknownVersionLeavesRuntimeStateUnchanged()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            DataManager manager = DataManager.Instance;
            manager.Player.Name.Value = "CurrentPlayer";
            manager.Player.Gold.Value = 123;
            manager.Village.Name.Value = "CurrentVillage";
            Assert.That(manager.Village.AddBuilding(BuildingData.Create(1, 2, 3)), Is.True);

            int slot = SaveManager.MaxSaveSlots + 2000;
            string path = SaveManager.GetFilePath(slot);
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var incompatible = new PlayerSaveData
                {
                    Version = PlayerSaveData.CurrentVersion + 1,
                    PlayerName = "CorruptedPlayer",
                    Gold = 9999,
                    VillageName = "CorruptedVillage",
                };
                File.WriteAllText(path, JsonUtility.ToJson(incompatible));

                LogAssert.Expect(LogType.Error,
                    "[SaveManager] 存档无效: 存档版本不匹配");
                Assert.That(SaveManager.LoadToDataManager(slot), Is.False);
                Assert.That(manager.Player.Name.Value, Is.EqualTo("CurrentPlayer"));
                Assert.That(manager.Player.Gold.Value, Is.EqualTo(123));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("CurrentVillage"));
                Assert.That(manager.Village.Buildings, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(path);
                if (previous != null) File.WriteAllBytes(path, previous);
            }
        }

        [Test]
        public void LoadToDataManager_InvalidBuildingTimingLeavesRuntimeStateUnchanged()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            DataManager manager = DataManager.Instance;
            manager.Player.Name.Value = "CurrentPlayer";
            manager.Player.Gold.Value = 123;
            manager.Village.Name.Value = "CurrentVillage";
            Assert.That(manager.Village.AddBuilding(BuildingData.Create(1, 2, 3)), Is.True);

            int slot = SaveManager.MaxSaveSlots + 2001;
            string path = SaveManager.GetFilePath(slot);
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var malformed = new PlayerSaveData
                {
                    PlayerName = "CorruptedPlayer",
                    VillageName = "CorruptedVillage",
                    Buildings = new System.Collections.Generic.List<BuildingSaveData>
                    {
                        new BuildingSaveData
                        {
                            Id = "invalid-timing",
                            TypeId = 1,
                            Level = 1,
                            State = BuildingState.Normal,
                            UpgradeEndTime = 1,
                        },
                    },
                };
                File.WriteAllText(path, JsonUtility.ToJson(malformed));

                LogAssert.Expect(LogType.Error,
                    "[SaveManager] 存档无效: 建筑数据无效");
                Assert.That(SaveManager.LoadToDataManager(slot), Is.False);
                Assert.That(manager.Player.Name.Value, Is.EqualTo("CurrentPlayer"));
                Assert.That(manager.Player.Gold.Value, Is.EqualTo(123));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("CurrentVillage"));
                Assert.That(manager.Village.Buildings, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(path);
                if (previous != null) File.WriteAllBytes(path, previous);
            }
        }

        [Test]
        public void LoadToDataManager_OverlappingBuildingOriginsLeaveRuntimeStateUnchanged()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            DataManager manager = DataManager.Instance;
            manager.Player.Name.Value = "CurrentPlayer";
            manager.Village.Name.Value = "CurrentVillage";
            Assert.That(manager.Village.AddBuilding(BuildingData.Create(1, 2, 3)), Is.True);

            int slot = SaveManager.MaxSaveSlots + 2002;
            string path = SaveManager.GetFilePath(slot);
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var malformed = new PlayerSaveData
                {
                    PlayerName = "CorruptedPlayer",
                    VillageName = "CorruptedVillage",
                    Buildings = new System.Collections.Generic.List<BuildingSaveData>
                    {
                        new BuildingSaveData
                        {
                            Id = "first", TypeId = 1, GridX = 4, GridY = 5, Level = 1,
                        },
                        new BuildingSaveData
                        {
                            Id = "overlap", TypeId = 2, GridX = 4, GridY = 5, Level = 1,
                        },
                    },
                };
                File.WriteAllText(path, JsonUtility.ToJson(malformed));

                LogAssert.Expect(LogType.Error,
                    "[SaveManager] 存档无效: 建筑位置重叠");
                Assert.That(SaveManager.LoadToDataManager(slot), Is.False);
                Assert.That(manager.Player.Name.Value, Is.EqualTo("CurrentPlayer"));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("CurrentVillage"));
                Assert.That(manager.Village.Buildings, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(path);
                if (previous != null) File.WriteAllBytes(path, previous);
            }
        }

        [Test]
        public void LoadToDataManager_LevelAboveLoadedConfigLeavesRuntimeStateUnchanged()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            ConfigTableManager config = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            Assert.That(config.PreloadAllAsync().GetAwaiter().GetResult(), Is.True);

            DataManager manager = DataManager.Instance;
            manager.Player.Name.Value = "CurrentPlayer";
            manager.Village.Name.Value = "CurrentVillage";
            Assert.That(manager.Village.AddBuilding(BuildingData.Create(1, 2, 3)), Is.True);

            int slot = SaveManager.MaxSaveSlots + 2003;
            string path = SaveManager.GetFilePath(slot);
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var malformed = new PlayerSaveData
                {
                    PlayerName = "CorruptedPlayer",
                    VillageName = "CorruptedVillage",
                    Buildings = new System.Collections.Generic.List<BuildingSaveData>
                    {
                        new BuildingSaveData
                        {
                            Id = "overleveled", TypeId = 1, GridX = 4, GridY = 5,
                            Level = 11,
                        },
                    },
                };
                File.WriteAllText(path, JsonUtility.ToJson(malformed));

                LogAssert.Expect(LogType.Error,
                    "[SaveManager] 存档无效: 建筑等级超出配置范围");
                Assert.That(SaveManager.LoadToDataManager(slot), Is.False);
                Assert.That(manager.Player.Name.Value, Is.EqualTo("CurrentPlayer"));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("CurrentVillage"));
                Assert.That(manager.Village.Buildings, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(path);
                if (previous != null) File.WriteAllBytes(path, previous);
            }
        }

        [Test]
        public void LoadToDataManager_MaxLevelBuildingStillUpgradingLeavesRuntimeStateUnchanged()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            ConfigTableManager config = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            Assert.That(config.PreloadAllAsync().GetAwaiter().GetResult(), Is.True);

            DataManager manager = DataManager.Instance;
            manager.Player.Name.Value = "CurrentPlayer";
            manager.Village.Name.Value = "CurrentVillage";
            Assert.That(manager.Village.AddBuilding(BuildingData.Create(1, 2, 3)), Is.True);

            int slot = SaveManager.MaxSaveSlots + 2004;
            string path = SaveManager.GetFilePath(slot);
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var malformed = new PlayerSaveData
                {
                    PlayerName = "CorruptedPlayer",
                    VillageName = "CorruptedVillage",
                    Buildings = new System.Collections.Generic.List<BuildingSaveData>
                    {
                        new BuildingSaveData
                        {
                            Id = "max-level-upgrading", TypeId = 1, GridX = 4, GridY = 5,
                            Level = 10, State = BuildingState.Upgrading,
                            UpgradeEndTime = 1924992000,
                        },
                    },
                };
                File.WriteAllText(path, JsonUtility.ToJson(malformed));

                LogAssert.Expect(LogType.Error,
                    "[SaveManager] 存档无效: 满级建筑不能处于升级状态");
                Assert.That(SaveManager.LoadToDataManager(slot), Is.False);
                Assert.That(manager.Player.Name.Value, Is.EqualTo("CurrentPlayer"));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("CurrentVillage"));
                Assert.That(manager.Village.Buildings, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(path);
                if (previous != null) File.WriteAllBytes(path, previous);
            }
        }

        [Test]
        public void LoadToDataManager_TownHallLevelMismatchLeavesRuntimeStateUnchanged()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            ConfigTableManager config = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            Assert.That(config.PreloadAllAsync().GetAwaiter().GetResult(), Is.True);

            DataManager manager = DataManager.Instance;
            manager.Player.Name.Value = "CurrentPlayer";
            manager.Player.TownHallLevel.Value = 1;
            manager.Village.Name.Value = "CurrentVillage";
            Assert.That(manager.Village.AddBuilding(BuildingData.Create(1, 2, 3)), Is.True);

            int slot = SaveManager.MaxSaveSlots + 2005;
            string path = SaveManager.GetFilePath(slot);
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var malformed = new PlayerSaveData
                {
                    PlayerName = "CorruptedPlayer",
                    TownHallLevel = 2,
                    VillageName = "CorruptedVillage",
                    Buildings = new System.Collections.Generic.List<BuildingSaveData>
                    {
                        new BuildingSaveData
                        {
                            Id = "town-hall", TypeId = 1, GridX = 4, GridY = 5, Level = 1,
                        },
                    },
                };
                File.WriteAllText(path, JsonUtility.ToJson(malformed));

                LogAssert.Expect(LogType.Error,
                    "[SaveManager] 存档无效: 玩家大本营等级与建筑等级不一致");
                Assert.That(SaveManager.LoadToDataManager(slot), Is.False);
                Assert.That(manager.Player.Name.Value, Is.EqualTo("CurrentPlayer"));
                Assert.That(manager.Player.TownHallLevel.Value, Is.EqualTo(1));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("CurrentVillage"));
                Assert.That(manager.Village.Buildings, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(path);
                if (previous != null) File.WriteAllBytes(path, previous);
            }
        }

        [Test]
        public void LoadToDataManager_MultipleTownHallsLeaveRuntimeStateUnchanged()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            ConfigTableManager config = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            Assert.That(config.PreloadAllAsync().GetAwaiter().GetResult(), Is.True);

            DataManager manager = DataManager.Instance;
            manager.Player.Name.Value = "CurrentPlayer";
            manager.Player.TownHallLevel.Value = 1;
            manager.Village.Name.Value = "CurrentVillage";
            Assert.That(manager.Village.AddBuilding(BuildingData.Create(1, 2, 3)), Is.True);

            int slot = SaveManager.MaxSaveSlots + 2006;
            string path = SaveManager.GetFilePath(slot);
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var malformed = new PlayerSaveData
                {
                    PlayerName = "CorruptedPlayer",
                    TownHallLevel = 1,
                    VillageName = "CorruptedVillage",
                    Buildings = new System.Collections.Generic.List<BuildingSaveData>
                    {
                        new BuildingSaveData
                        {
                            Id = "town-hall-first", TypeId = 1, GridX = 4, GridY = 5, Level = 1,
                        },
                        new BuildingSaveData
                        {
                            Id = "town-hall-second", TypeId = 1, GridX = 8, GridY = 5, Level = 1,
                        },
                    },
                };
                File.WriteAllText(path, JsonUtility.ToJson(malformed));

                LogAssert.Expect(LogType.Error,
                    "[SaveManager] 存档无效: 村庄包含多个大本营");
                Assert.That(SaveManager.LoadToDataManager(slot), Is.False);
                Assert.That(manager.Player.Name.Value, Is.EqualTo("CurrentPlayer"));
                Assert.That(manager.Player.TownHallLevel.Value, Is.EqualTo(1));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("CurrentVillage"));
                Assert.That(manager.Village.Buildings, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(path);
                if (previous != null) File.WriteAllBytes(path, previous);
            }
        }

        [Test]
        public void LoadToDataManager_TownHallLevelAboveConfigLeavesRuntimeStateUnchanged()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            ConfigTableManager config = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            Assert.That(config.PreloadAllAsync().GetAwaiter().GetResult(), Is.True);

            DataManager manager = DataManager.Instance;
            manager.Player.Name.Value = "CurrentPlayer";
            manager.Player.TownHallLevel.Value = 1;
            manager.Village.Name.Value = "CurrentVillage";
            Assert.That(manager.Village.AddBuilding(BuildingData.Create(1, 2, 3)), Is.True);

            int slot = SaveManager.MaxSaveSlots + 2007;
            string path = SaveManager.GetFilePath(slot);
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var malformed = new PlayerSaveData
                {
                    PlayerName = "CorruptedPlayer",
                    TownHallLevel = 11,
                    VillageName = "CorruptedVillage",
                    Buildings = new System.Collections.Generic.List<BuildingSaveData>(),
                };
                File.WriteAllText(path, JsonUtility.ToJson(malformed));

                LogAssert.Expect(LogType.Error,
                    "[SaveManager] 存档无效: 玩家大本营等级超出配置范围");
                Assert.That(SaveManager.LoadToDataManager(slot), Is.False);
                Assert.That(manager.Player.Name.Value, Is.EqualTo("CurrentPlayer"));
                Assert.That(manager.Player.TownHallLevel.Value, Is.EqualTo(1));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("CurrentVillage"));
                Assert.That(manager.Village.Buildings, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(path);
                if (previous != null) File.WriteAllBytes(path, previous);
            }
        }
    }
}
