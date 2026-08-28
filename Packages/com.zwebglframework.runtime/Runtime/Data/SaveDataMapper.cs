using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Data
{
    public static partial class SaveDataMapper
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
                AppliedBattleResultIds = new List<string>(player.AppliedBattleResultIds),
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

        public static void RestoreRewardReceipts(PlayerSaveData saved, PlayerData player)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            if (player == null) throw new ArgumentNullException(nameof(player));

            player.AppliedBattleResultIds.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string resultId in saved.AppliedBattleResultIds ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(resultId) && seen.Add(resultId))
                    player.AppliedBattleResultIds.Add(resultId);
            }
        }

        public static BuildingData RestoreBuilding(BuildingSaveData saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            var building = new BuildingData
            {
                Id = saved.Id,
                TypeId = saved.TypeId,
            };
            building.GridX.SetSilent(saved.GridX);
            building.GridY.SetSilent(saved.GridY);
            building.Level.SetSilent(saved.Level);
            building.State.SetSilent(saved.State);
            building.UpgradeEndTime.SetSilent(saved.UpgradeEndTime);
            return building;
        }
    }
}
