namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>命令类型枚举</summary>
    public enum SimCommandType : byte
    {
        None = 0,
        Deploy = 1,   // 部署单位
        Move = 2,     // 移动命令（点击目的地）
        Skill = 3,    // 释放技能
        EndDeployment = 4,
    }

    /// <summary>仿真命令接口 — 所有命令实现此接口</summary>
    public interface ISimCommand
    {
        /// <summary>命令类型</summary>
        SimCommandType Type { get; }
        /// <summary>所属 Tick</summary>
        int Tick { get; }
        /// <summary>玩家 ID（0=Player1, 1=Player2）</summary>
        int PlayerId { get; }
        int Sequence { get; }
    }
}
