namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>部署单位命令 — 在指定网格放置单位</summary>
    public struct DeployCmd : ISimCommand
    {
        public SimCommandType Type => SimCommandType.Deploy;
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int Sequence { get; set; }
        /// <summary>单位原型 ID</summary>
        public int UnitTypeId;
        /// <summary>部署网格 X</summary>
        public int GridX;
        /// <summary>部署网格 Y</summary>
        public int GridY;
    }

    /// <summary>移动命令 — 命令单位移动到目标网格</summary>
    public struct MoveCmd : ISimCommand
    {
        public SimCommandType Type => SimCommandType.Move;
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int Sequence { get; set; }
        /// <summary>目标实体 ID</summary>
        public int EntityId;
        /// <summary>目标网格 X</summary>
        public int TargetX;
        /// <summary>目标网格 Y</summary>
        public int TargetY;
    }

    /// <summary>技能命令 — 释放技能到目标</summary>
    public struct SkillCmd : ISimCommand
    {
        public SimCommandType Type => SimCommandType.Skill;
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int Sequence { get; set; }
        /// <summary>施法者实体 ID</summary>
        public int CasterId;
        /// <summary>技能 ID</summary>
        public int SkillId;
        /// <summary>目标网格 X（-1 表示无目标）</summary>
        public int TargetX;
        /// <summary>目标网格 Y（-1 表示无目标）</summary>
        public int TargetY;
    }

    public struct EndDeploymentCmd : ISimCommand
    {
        public SimCommandType Type => SimCommandType.EndDeployment;
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int Sequence { get; set; }
    }
}
