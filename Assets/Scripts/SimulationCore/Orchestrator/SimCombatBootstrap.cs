namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真战斗引导器 — 组装 SimSystem 管线。
    /// 纯同步初始化，无 UniTask 依赖。
    /// </summary>
    public static class SimCombatBootstrap
    {
        /// <summary>创建并初始化仿真 ECS 管线</summary>
        public static SimSystemGroup Build(SimWorld world)
        {
            var systems = new SimSystemGroup();
            var deathSys = new SimDeathSystem();

            systems.Add(
                new SimMovementSystem(),
                new SimAttackSystem(),
                new SimDamageSystem(),
                deathSys
            );

            systems.InitAll(world);
            return systems;
        }
    }
}
