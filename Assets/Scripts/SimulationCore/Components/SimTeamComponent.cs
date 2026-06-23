namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>阵营常量</summary>
    public static class SimTeam
    {
        public const int None = 0;
        public const int Attacker = 1;
        public const int Defender = 2;

        public static bool IsEnemy(int a, int b) => a != None && b != None && a != b;
    }

    /// <summary>阵营组件 — 纯整数</summary>
    public struct SimTeamComponent : ISimComponent
    {
        public int TeamId;

        public static SimTeamComponent Create(int teamId) => new SimTeamComponent { TeamId = teamId };
    }
}
