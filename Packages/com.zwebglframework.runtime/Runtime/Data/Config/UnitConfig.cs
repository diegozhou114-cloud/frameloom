using System;

namespace ZWebGLFrameWork.Data
{
    /// <summary>兵种配置表行 — 定义每种单位的战斗属性</summary>
    [Serializable]
    public class UnitConfig
    {
        /// <summary>兵种 ID（唯一键）</summary>
        public int Id;

        /// <summary>兵种名称</summary>
        public string Name;

        /// <summary>生命值</summary>
        public int HP;

        /// <summary>每秒伤害</summary>
        public int Damage;

        /// <summary>攻击速度（秒/次）</summary>
        public float AttackSpeed;

        /// <summary>移动速度（单位/秒）</summary>
        public float MoveSpeed;

        /// <summary>攻击范围</summary>
        public float Range;

        /// <summary>训练消耗（圣水/暗黑重油）</summary>
        public int TrainingCost;

        /// <summary>训练时间（秒）</summary>
        public int TrainingTime;

        /// <summary>占用人口（兵营空间）</summary>
        public int HousingSpace;

        /// <summary>优先攻击目标类型：Any/Defense/Resource/Wall</summary>
        public string PreferredTarget;

        /// <summary>单位类型：Ground/Air</summary>
        public string UnitType;

        /// <summary>移动类型：Ground/Flying</summary>
        public string MovementType;

        /// <summary>所需大本营等级</summary>
        public int RequiredTownHall;
    }
}
