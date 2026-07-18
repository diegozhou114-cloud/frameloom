namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public static partial class SessionTestDriver
    {
        public static byte[] ManifestBytes()
        {
            return new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
                new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
                new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 20, 10000, 20)
            }).ToCanonicalBytes();
        }

        public static BattleInit BattleInit(int seed, InitialEntitySpec[] entities = null)
        {
            byte[] manifest = ManifestBytes();
            byte[] hash = DeterministicContentManifest.Parse(manifest).ContentHash;
            return new BattleInit(1, 1, "test-match", "test-session", seed, 20,
                SimCombatMode.VillageAttack, hash,
                new[] { new SimPlayerIdentity(0, "attacker"), new SimPlayerIdentity(1, "defender") },
                entities ?? new[]
                {
                    new InitialEntitySpec(10, 2, 2, SimTeam.Attacker, 0),
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1)
                }, 6);
        }

        public static SimBattleSession StartedBattle(int seed = 42)
        {
            var session = (SimBattleSession)new SimBattleSessionFactory().Create(ManifestBytes());
            StartResult result = session.Start(BattleInit(seed));
            if (!result.Accepted) throw new System.InvalidOperationException(result.Code.ToString());
            return session;
        }

        public static BattleInit DeployingBattleInit(int seed = 42) =>
            BattleInit(seed, new[]
            {
                new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1)
            });

        public static SimBattleSession StartedDeployingBattle()
        {
            var session = (SimBattleSession)new SimBattleSessionFactory().Create(ManifestBytes());
            BattleInit init = DeployingBattleInit();
            StartResult result = session.Start(init);
            if (!result.Accepted) throw new System.InvalidOperationException(result.Code.ToString());
            return session;
        }
    }
}
