namespace ZWebGLFrameWork.SimulationCore
{
    public static class SimArchetypeCombatBootstrap
    {
        public static SimArchetypeSystemGroup Build(SimArchetypeWorld world)
        {
            var systems = new SimArchetypeSystemGroup();
            var deathSys = new SimArchetypeDeathSystem();

            systems.Add(
                new SimArchetypeTargetCacheSystem(),  // Order 5: cache target positions before attack
                new SimArchetypeMovementSystem(),
                new SimArchetypeAttackSystem(),
                new SimArchetypeDamageSystem(),
                deathSys
            );

            systems.InitAll(world);
            return systems;
        }
    }
}
