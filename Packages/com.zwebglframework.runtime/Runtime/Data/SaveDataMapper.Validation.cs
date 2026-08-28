using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Data
{
    public static partial class SaveDataMapper
    {
        private const int MaxPlayerLevel = 1000;
        private const int MaxTownHallLevel = 100;
        private const int MaxResource = int.MaxValue - 1;
        private const int MaxTrophies = 1_000_000;
        private const int MaxBuildingCount = 500;
        private const long MaxUpgradeDurationSeconds = 31_536_000;

        public static bool TryBuildValidatedState(
            PlayerSaveData saved,
            int expectedVersion,
            Func<int, BuildingConfig> resolveBuilding,
            int gridColumns,
            int gridRows,
            long nowUnixSeconds,
            out ValidatedSaveState state,
            out string error)
        {
            state = null;
            error = null;
            if (!TryValidateHeader(saved, expectedVersion, resolveBuilding,
                    gridColumns, gridRows, nowUnixSeconds, out error))
                return false;
            if (!TryStageBuildings(saved, resolveBuilding, gridColumns, gridRows,
                    nowUnixSeconds, out List<BuildingData> buildings, out error))
                return false;
            if (!TryStageReceipts(saved, out List<string> receipts, out error))
                return false;
            if (!HasValidSaveTime(saved.SaveTime, nowUnixSeconds))
            {
                error = "存档时间无效";
                return false;
            }

            var player = new PlayerData();
            player.Name.SetSilent(saved.PlayerName);
            player.Level.SetSilent(saved.PlayerLevel);
            player.TownHallLevel.SetSilent(saved.TownHallLevel);
            player.Gold.SetSilent(saved.Gold);
            player.Elixir.SetSilent(saved.Elixir);
            player.DarkElixir.SetSilent(saved.DarkElixir);
            player.Gems.SetSilent(saved.Gems);
            player.Trophies.SetSilent(saved.Trophies);
            player.Exp.SetSilent(saved.Exp);
            player.AppliedBattleResultIds.AddRange(receipts);

            var village = new VillageData(resolveBuilding);
            village.Name.SetSilent(saved.VillageName);
            village.MaxBuildings.SetSilent(saved.MaxBuildings);
            foreach (BuildingData building in buildings)
            {
                if (!village.AddBuilding(building))
                {
                    error = "建筑数据无效";
                    return false;
                }
            }
            state = new ValidatedSaveState(player, village);
            return true;
        }

        private static bool TryValidateHeader(
            PlayerSaveData saved,
            int expectedVersion,
            Func<int, BuildingConfig> resolveBuilding,
            int gridColumns,
            int gridRows,
            long nowUnixSeconds,
            out string error)
        {
            error = null;
            if (saved == null || resolveBuilding == null ||
                gridColumns < 1 || gridRows < 1 || nowUnixSeconds < 0)
            {
                error = "验证依赖无效";
                return false;
            }
            if (saved.Version != expectedVersion)
            {
                error = "存档版本不匹配";
                return false;
            }
            if (string.IsNullOrWhiteSpace(saved.PlayerName) ||
                saved.PlayerName.Length > 64 || saved.PlayerLevel < 1 ||
                saved.PlayerLevel > MaxPlayerLevel || saved.TownHallLevel < 1 ||
                saved.TownHallLevel > MaxTownHallLevel || saved.Gold < 0 ||
                saved.Gold > MaxResource || saved.Elixir < 0 ||
                saved.Elixir > MaxResource || saved.DarkElixir < 0 ||
                saved.DarkElixir > MaxResource || saved.Gems < 0 ||
                saved.Gems > MaxResource || saved.Trophies < 0 ||
                saved.Trophies > MaxTrophies || saved.Exp < 0 ||
                saved.Exp > MaxResource)
            {
                error = "玩家数值超出有效范围";
                return false;
            }
            if (string.IsNullOrWhiteSpace(saved.VillageName) ||
                saved.VillageName.Length > 64 || saved.MaxBuildings < 1 ||
                saved.MaxBuildings > MaxBuildingCount || saved.Buildings == null ||
                saved.Buildings.Count > saved.MaxBuildings)
            {
                error = "建筑容量无效";
                return false;
            }
            return true;
        }

        private static bool TryStageBuildings(
            PlayerSaveData saved,
            Func<int, BuildingConfig> resolveBuilding,
            int gridColumns,
            int gridRows,
            long nowUnixSeconds,
            out List<BuildingData> staged,
            out string error)
        {
            staged = null;
            error = null;
            BuildingConfig townHallConfig = resolveBuilding(1);
            if (townHallConfig != null && saved.TownHallLevel > townHallConfig.MaxLevel)
                return Fail("玩家大本营等级超出配置范围", out error);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var origins = new HashSet<long>();
            int? townHallLevel = null;
            foreach (BuildingSaveData building in saved.Buildings)
            {
                bool validTiming = building != null &&
                    (building.State == BuildingState.Upgrading
                        ? building.UpgradeEndTime > 0
                        : building.UpgradeEndTime == 0);
                if (building == null || string.IsNullOrWhiteSpace(building.Id) ||
                    building.Id.Length > 128 || !ids.Add(building.Id) ||
                    building.TypeId < 1 || building.GridX < 0 || building.GridY < 0 ||
                    building.Level < 1 ||
                    !Enum.IsDefined(typeof(BuildingState), building.State) || !validTiming)
                    return Fail("建筑数据无效", out error);
                long origin = ((long)building.GridX << 32) | (uint)building.GridY;
                if (!origins.Add(origin))
                    return Fail("建筑位置重叠", out error);
                if (building.TypeId != 1) continue;
                if (townHallLevel.HasValue)
                    return Fail("村庄包含多个大本营", out error);
                townHallLevel = building.Level;
            }
            if (!townHallLevel.HasValue)
                return Fail("村庄缺少大本营", out error);

            staged = new List<BuildingData>(saved.Buildings.Count);
            var occupied = new HashSet<long>();
            foreach (BuildingSaveData building in saved.Buildings)
            {
                BuildingConfig config = resolveBuilding(building.TypeId);
                if (config == null)
                    return Fail("建筑类型不存在", out error);
                if (config.Id != building.TypeId || config.SizeX < 1 ||
                    config.SizeY < 1 || config.MaxLevel < 1)
                    return Fail("建筑数据无效", out error);
                if (building.Level > config.MaxLevel)
                    return Fail("建筑等级超出配置范围", out error);
                if (building.State == BuildingState.Upgrading &&
                    building.Level >= config.MaxLevel)
                    return Fail("满级建筑不能处于升级状态", out error);
                if (building.State == BuildingState.Upgrading &&
                    building.UpgradeEndTime > nowUnixSeconds + MaxUpgradeDurationSeconds)
                    return Fail("建筑数据无效", out error);
                if ((long)building.GridX + config.SizeX > gridColumns ||
                    (long)building.GridY + config.SizeY > gridRows)
                    return Fail("建筑数据无效", out error);
                for (int x = building.GridX; x < building.GridX + config.SizeX; x++)
                for (int y = building.GridY; y < building.GridY + config.SizeY; y++)
                {
                    long cell = ((long)x << 32) | (uint)y;
                    if (!occupied.Add(cell))
                        return Fail("建筑位置重叠", out error);
                }
                staged.Add(RestoreBuilding(building));
            }
            if (saved.TownHallLevel != townHallLevel.Value)
                return Fail("玩家大本营等级与建筑等级不一致", out error);
            return true;
        }

    }
}
