using System;

namespace ZWebGLFrameWork.Data
{
    /// <summary>波次中的敌人配置</summary>
    [Serializable]
    public class WaveEnemy
    {
        /// <summary>兵种 ID</summary>
        public int UnitId;

        /// <summary>数量</summary>
        public int Count;

        /// <summary>等级</summary>
        public int Level;
    }

    /// <summary>关卡/波次配置表行</summary>
    [Serializable]
    public class LevelConfig
    {
        /// <summary>关卡 ID（唯一键）</summary>
        public int Id;

        /// <summary>关卡名称</summary>
        public string Name;

        /// <summary>波次数量</summary>
        public int WaveCount;

        /// <summary>每波敌人配置（JSON 子对象数组）</summary>
        public WaveEnemy[] Waves;

        /// <summary>三星通关摧毁率阈值（0~100），[1星, 2星, 3星]</summary>
        public int[] StarThresholds;

        /// <summary>胜利奖励金币</summary>
        public int GoldReward;

        /// <summary>胜利奖励圣水</summary>
        public int ElixirReward;

        /// <summary>奖杯变化（正=获得，负=失去）</summary>
        public int TrophyChange;

        /// <summary>战斗时间限制（秒）</summary>
        public int TimeLimit;

        /// <summary>从摧毁率计算获得星星数</summary>
        public int GetStars(float destructionPercent)
        {
            if (StarThresholds == null || StarThresholds.Length < 3) return 0;

            int stars = 0;
            if (destructionPercent >= StarThresholds[0]) stars++;
            if (destructionPercent >= StarThresholds[1]) stars++;
            if (destructionPercent >= StarThresholds[2]) stars++;
            return stars;
        }
    }
}
