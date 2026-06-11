using ZWebGLFrameWork.Combat.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>阵营</summary>
    public enum Team
    {
        None = 0,
        Attacker = 1,   // 进攻方（玩家）
        Defender = 2,   // 防守方（AI/敌方村庄）
    }

    /// <summary>阵营组件</summary>
    public struct TeamComponent : IComponent
    {
        public Team TeamId;

        /// <summary>是否是敌人</summary>
        public static bool IsEnemy(Team a, Team b) =>
            a != Team.None && b != Team.None && a != b;

        public static TeamComponent Create(Team team) => new TeamComponent { TeamId = team };
    }
}
