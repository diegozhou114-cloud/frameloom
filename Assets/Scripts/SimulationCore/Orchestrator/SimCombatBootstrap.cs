namespace ZWebGLFrameWork.SimulationCore
{
    public static class SimCombatBootstrap
    {
        public static SimSystemGroup Build(SimWorld world)
        {
            var systems = new SimSystemGroup();
            var deathSys = new SimDeathSystem();

            systems.Add(
                new SimTargetCacheSystem(),  // Order 5: cache target positions before attack
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
