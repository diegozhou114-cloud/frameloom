using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Data
{
    public static class SaveDataMapper
    {
        public static PlayerSaveData Capture(PlayerData player, VillageData village)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (village == null) throw new ArgumentNullException(nameof(village));

            var result = new PlayerSaveData
            {
                Version = PlayerSaveData.CurrentVersion,
                PlayerName = player.Name.Value,
                PlayerLevel = player.Level.Value,
                TownHallLevel = player.TownHallLevel.Value,
                Gold = player.Gold.Value,
                Elixir = player.Elixir.Value,
                DarkElixir = player.DarkElixir.Value,
                Gems = player.Gems.Value,
                Trophies = player.Trophies.Value,
                Exp = player.Exp.Value,
                VillageName = village.Name.Value,
                MaxBuildings = village.MaxBuildings.Value,
                Buildings = new List<BuildingSaveData>(),
                SaveTime = DateTime.UtcNow.ToString("O"),
            };

            foreach (BuildingData building in village.Buildings)
            {
                result.Buildings.Add(new BuildingSaveData
                {
                    Id = building.Id,
                    TypeId = building.TypeId,
                    GridX = building.GridX.Value,
                    GridY = building.GridY.Value,
                    Level = building.Level.Value,
                    State = building.State.Value,
                    UpgradeEndTime = building.UpgradeEndTime.Value,
                });
            }
            return result;
        }

        public static BuildingData RestoreBuilding(BuildingSaveData saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            return new BuildingData
            {
                Id = saved.Id,
                TypeId = saved.TypeId,
                GridX = { Value = saved.GridX },
                GridY = { Value = saved.GridY },
                Level = { Value = saved.Level },
                State = { Value = saved.State },
                UpgradeEndTime = { Value = saved.UpgradeEndTime },
            };
        }
    }
}
