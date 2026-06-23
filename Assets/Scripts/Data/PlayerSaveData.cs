using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Data
{
    /// <summary>建筑存档数据（仅供序列化）</summary>
    [Serializable]
    public class BuildingSaveData
    {
        public int TypeId;
        public int GridX;
        public int GridY;
        public int Level;
    }

    /// <summary>
    /// 玩家完整存档 DTO — 离线模式自包含。
    /// 通过 SaveManager.SaveGame/LoadGame 读写。
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        public string PlayerName = "Villager";
        public int PlayerLevel = 1;
        public int TownHallLevel = 1;
        public int Gold = 500;
        public int Elixir = 300;
        public int DarkElixir = 0;
        public int Gems = 50;
        public int Trophies = 0;
        public int Exp = 0;
        public List<BuildingSaveData> Buildings = new List<BuildingSaveData>();
        public string SaveTime;
    }
}
