using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor
{
    public static class SimulationBenchmark
    {
        [MenuItem("Tools/ZWebGL/Benchmarks/Simulation 100x100")]
        public static void Run100By100()
        {
            using (ISimBattleSession session = CreateBenchmarkSession(100))
            {
                var stopwatch = Stopwatch.StartNew();
                for (int tick = 0; tick < 100; tick++)
                    session.Tick();
                stopwatch.Stop();
                UnityEngine.Debug.Log($"[SimulationBenchmark] entities=100 ticks=100 " +
                    $"milliseconds={stopwatch.ElapsedMilliseconds} " +
                    $"checksum={session.State.Checksum:X16}");
            }
        }

        private static ISimBattleSession CreateBenchmarkSession(int entityCount)
        {
            if (entityCount < 0)
                throw new ArgumentOutOfRangeException(nameof(entityCount));

            byte[] manifestBytes = ManifestBytes();
            byte[] contentHash = DeterministicContentManifest.Parse(manifestBytes).ContentHash;
            var entities = new InitialEntitySpec[entityCount];
            for (int index = 0; index < entityCount; index++)
            {
                bool attacker = index % 2 == 0;
                entities[index] = new InitialEntitySpec(
                    attacker ? 10 : 2, index % 20, index / 20,
                    attacker ? SimTeam.Attacker : SimTeam.Defender,
                    attacker ? 0 : 1);
            }

            var init = new BattleInit(1, 1, "benchmark-match", "benchmark-session", 42,
                20, SimCombatMode.VillageAttack, contentHash,
                new[]
                {
                    new SimPlayerIdentity(0, "attacker"),
                    new SimPlayerIdentity(1, "defender")
                }, entities, 6);
            ISimBattleSession session = new SimBattleSessionFactory().Create(manifestBytes);
            StartResult started = session.Start(init);
            if (!started.Accepted)
            {
                session.Dispose();
                throw new InvalidOperationException(started.Code.ToString());
            }
            return session;
        }

        private static byte[] ManifestBytes()
        {
            return new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
                new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
                new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 20, 10000, 20)
            }, new[]
            {
                new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 3600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 3600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 3600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400,
                    new[] { 0, 100, 200, 300, 400 })
            }).ToCanonicalBytes();
        }
    }
}
