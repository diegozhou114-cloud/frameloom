using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Data
{
    /// <summary>建筑存档数据（仅供序列化）</summary>
    [Serializable]
    public class BuildingSaveData
    {
        public string Id;
        public int TypeId;
        public int GridX;
        public int GridY;
        public int Level;
        public BuildingState State;
        public long UpgradeEndTime;
    }

    /// <summary>
    /// 玩家完整存档 DTO — 离线模式自包含。
    /// 通过 SaveManager.SaveGame/LoadGame 读写。
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;
        public string PlayerName = "Villager";
        public int PlayerLevel = 1;
        public int TownHallLevel = 1;
        public int Gold = 500;
        public int Elixir = 300;
        public int DarkElixir = 0;
        public int Gems = 50;
        public int Trophies = 0;
        public int Exp = 0;
        public List<string> AppliedBattleResultIds = new List<string>();
        public string VillageName = "My Village";
        public int MaxBuildings = 50;
        public List<BuildingSaveData> Buildings = new List<BuildingSaveData>();
        // NOTE: DateTime stored as formatted string — lossy without timezone offset.
        // Acceptable for local saves where all timestamps share the device's local zone;
        // if cross-device sync is added, switch to DateTimeOffset or UTC ISO-8601.
        public string SaveTime;
    }
}
