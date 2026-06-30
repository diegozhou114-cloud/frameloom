using System;

namespace ZWebGLFrameWork.Data
{
    /// <summary>建筑配置表行 — 定义每种建筑的基础属性</summary>
    [Serializable]
    public class BuildingConfig
    {
        /// <summary>建筑类型 ID（唯一键）</summary>
        public int Id;

        /// <summary>建筑名称</summary>
        public string Name;

        /// <summary>最大等级</summary>
        public int MaxLevel;

        /// <summary>建造/升级消耗金币（数组，按等级索引）</summary>
        public int[] BuildCost;

        /// <summary>建造/升级时间（秒，按等级索引）</summary>
        public int[] BuildTime;

        /// <summary>生命值（按等级索引）</summary>
        public int[] HP;

        /// <summary>占用的网格大小（宽, 高）</summary>
        public int SizeX;
        public int SizeY;

        /// <summary>建筑类型：Resource/Defense/Army/Other</summary>
        public string Category;

        /// <summary>获取指定等级的建筑消耗（超出范围返回 -1）</summary>
        public int GetBuildCost(int level)
        {
            int idx = level - 1;
            if (BuildCost == null || idx < 0 || idx >= BuildCost.Length)
                return -1;
            return BuildCost[idx];
        }

        /// <summary>获取指定等级的建筑时间（秒）。超出范围返回 -1。</summary>
        public int GetBuildTime(int level)
        {
            int idx = level - 1;
            if (BuildTime == null || idx < 0 || idx >= BuildTime.Length)
                return -1;
            return BuildTime[idx];
        }

        /// <summary>获取指定等级的生命值。超出范围返回 -1。</summary>
        public int GetHP(int level)
        {
            int idx = level - 1;
            if (HP == null || idx < 0 || idx >= HP.Length)
                return -1;
            return HP[idx];
        }
    }
}
