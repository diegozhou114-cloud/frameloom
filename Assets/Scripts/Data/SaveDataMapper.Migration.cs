using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Data
{
    public static partial class SaveDataMapper
    {
        public static bool TryMigrateToCurrent(
            PlayerSaveData saved,
            int slot,
            out PlayerSaveData current,
            out string error)
        {
            current = null;
            error = null;
            if (saved == null)
            {
                error = "save is null";
                return false;
            }
            if (saved.Version == PlayerSaveData.CurrentVersion)
            {
                current = saved;
                return true;
            }
            if (saved.Version != 1)
            {
                error = "unsupported version";
                return false;
            }

            var buildings = new List<BuildingSaveData>();
            var legacyBuildings = saved.Buildings ?? new List<BuildingSaveData>();
            for (int index = 0; index < legacyBuildings.Count; index++)
            {
                BuildingSaveData building = legacyBuildings[index];
                if (building == null)
                {
                    error = "legacy building is null";
                    return false;
                }
                buildings.Add(new BuildingSaveData
                {
                    Id = $"legacy-{slot:D2}-{index:D4}",
                    TypeId = building.TypeId,
                    GridX = building.GridX,
                    GridY = building.GridY,
                    Level = building.Level,
                    State = BuildingState.Normal,
                    UpgradeEndTime = 0,
                });
            }

            current = new PlayerSaveData
            {
                Version = PlayerSaveData.CurrentVersion,
                PlayerName = saved.PlayerName,
                PlayerLevel = saved.PlayerLevel,
                TownHallLevel = saved.TownHallLevel,
                Gold = saved.Gold,
                Elixir = saved.Elixir,
                DarkElixir = saved.DarkElixir,
                Gems = saved.Gems,
                Trophies = saved.Trophies,
                Exp = saved.Exp,
                VillageName = string.IsNullOrWhiteSpace(saved.VillageName)
                    ? "My Village"
                    : saved.VillageName,
                MaxBuildings = saved.MaxBuildings > 0 ? saved.MaxBuildings : 50,
                Buildings = buildings,
                AppliedBattleResultIds = new List<string>(),
                SaveTime = string.IsNullOrWhiteSpace(saved.SaveTime)
                    ? DateTime.UnixEpoch.ToString("O")
                    : saved.SaveTime,
            };
            return true;
        }
    }
}
