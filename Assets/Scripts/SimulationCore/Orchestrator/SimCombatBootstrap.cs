namespace ZWebGLFrameWork.SimulationCore
{
    public static class SimCombatBootstrap
    {
        public static SimSystemGroup Build(SimWorld world)
        {
            var systems = new SimSystemGroup();
            var deathSys = new SimDeathSystem();

            systems.Add(
                new SimTargetSelectionSystem(), // Order 5: stable target acquisition
                new SimPathPlanningSystem(),    // Order 8: deterministic path planning
                new SimTargetCacheSystem(),      // Order 9: cache target positions
                new SimMovementSystem(),         // Order 10: move toward target
                new SimAttackSystem(),
                new SimDamageSystem(),
                deathSys
            );

            systems.InitAll(world);
            return systems;
        }
    }
}
