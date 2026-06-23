using NUnit.Framework;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public class SimReplayRoundTripTests
    {
        /// <summary>创建简单的初始布局</summary>
        private class TestInitializer : ISimInitializer
        {
            private readonly int _seed;
            public int Seed => _seed;

            public TestInitializer(int seed) { _seed = seed; }

            public IEnumerable<SimEntitySpawnInfo> GetInitialEntities()
            {
                // 1 个防守方建筑
                yield return new SimEntitySpawnInfo(1, 15, 15, SimTeam.Defender);
                // 2 个攻击方单位
                yield return new SimEntitySpawnInfo(2, 5, 5, SimTeam.Attacker);
                yield return new SimEntitySpawnInfo(2, 5, 10, SimTeam.Attacker);
            }
        }

        /// <summary>简易 AI：每 10 Tick 给攻击方设置目标</summary>
        private void SimpleAI(SimWorld world, int tick)
        {
            if (tick % 10 != 0) return;

            var defenders = new List<(SimEntity entity, int x, int y)>();
            world.ForEachComponent<SimTransformComponent>((entity, transform) =>
            {
                if (world.TryGetComponent<SimTeamComponent>(entity, out var team)
                    && team.TeamId == SimTeam.Defender)
                {
                    defenders.Add((entity, transform.GridX, transform.GridY));
                }
            });

            world.ForEachComponent<SimTransformComponent>((entity, transform) =>
            {
                if (!world.TryGetComponent<SimTeamComponent>(entity, out var team)
                    || team.TeamId != SimTeam.Attacker)
                    return;
                if (defenders.Count == 0) return;

                var closest = defenders[0];
                int bestDist = System.Math.Abs(transform.GridX - closest.x)
                             + System.Math.Abs(transform.GridY - closest.y);
                foreach (var d in defenders)
                {
                    int dist = System.Math.Abs(transform.GridX - d.x)
                             + System.Math.Abs(transform.GridY - d.y);
                    if (dist < bestDist) { bestDist = dist; closest = d; }
                }

                // 设置目标
                if (world.TryGetComponent<SimTargetComponent>(entity, out var target))
                {
                    target.TargetEntity = closest.entity;
                    world.SetComponent(entity, target);
                }

                // 设置移动路径（简单直线逼近）
                if (world.TryGetComponent<SimMovementComponent>(entity, out var mov))
                {
                    int dx = closest.x - transform.GridX;
                    int dy = closest.y - transform.GridY;
                    int steps = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
                    if (steps > 0)
                    {
                        mov.Path = new List<int>();
                        int cx = transform.GridX, cy = transform.GridY;
                        for (int s = 0; s < steps; s++)
                        {
                            if (cx != closest.x) cx += dx > 0 ? 1 : -1;
                            if (cy != closest.y) cy += dy > 0 ? 1 : -1;
                            mov.Path.Add((cx << 16) | cy);
                        }
                        mov.PathIndex = 0;
                        world.SetComponent(entity, mov);
                    }
                }
            });
        }

        [Test]
        public void RoundTrip_SameBattle_SameChecksums()
        {
            const int seed = 42;

            // ── 第一遍：录制 ──
            var recorderChecksums = new List<ulong>();
            {
                var world = new SimWorld(256);
                var buffer = new CommandBuffer();
                var initializer = new TestInitializer(seed);
                var recorder = new CommandRecorder(seed, initializer.GetInitialEntities(), buffer);
                var systems = SimCombatBootstrap.Build(world);
                var deathSys = systems.Find<SimDeathSystem>();
                var orch = new SimOrchestrator(world, systems, deathSys, buffer, recorder, initializer,
                    deployTimeLimitSeconds: 5f, battleTimeoutSeconds: 60f, ticksPerSecond: 20);

                orch.StartBattle();
                // 提前结束部署
                orch.SetPhase(SimCombatPhase.Battling);

                for (int tick = 0; tick < 200; tick++)
                {
                    SimpleAI(world, tick);
                    orch.Tick();
                    recorderChecksums.Add(orch.LastChecksum);
                    if (orch.CurrentPhase == SimCombatPhase.Ended) break;
                }
            }

            // ── 第二遍：从 Replay 重放（同一个 seed+布局，应产生相同 checksum） ──
            {
                var world = new SimWorld(256);
                var initializer = new TestInitializer(seed);
                var systems = SimCombatBootstrap.Build(world);
                var deathSys = systems.Find<SimDeathSystem>();
                var buffer = new CommandBuffer();
                var orch = new SimOrchestrator(world, systems, deathSys, buffer, null, initializer,
                    deployTimeLimitSeconds: 5f, battleTimeoutSeconds: 60f, ticksPerSecond: 20);

                orch.StartBattle();
                orch.SetPhase(SimCombatPhase.Battling);

                for (int tick = 0; tick < recorderChecksums.Count; tick++)
                {
                    SimpleAI(world, tick);
                    orch.Tick();
                    Assert.That(orch.LastChecksum, Is.EqualTo(recorderChecksums[tick]),
                        $"Tick {tick}: checksum mismatch");
                    if (orch.CurrentPhase == SimCombatPhase.Ended) break;
                }
            }
        }

        [Test]
        public void EmptyCommandTick_ProducesValidChecksum()
        {
            var world = new SimWorld(64);
            var buffer = new CommandBuffer();
            var initializer = new TestInitializer(42);
            var systems = SimCombatBootstrap.Build(world);
            var deathSys = systems.Find<SimDeathSystem>();
            var orch = new SimOrchestrator(world, systems, deathSys, buffer, null, initializer,
                ticksPerSecond: 20);

            orch.StartBattle();
            orch.SetPhase(SimCombatPhase.Battling);

            // 空命令 Tick — Checksum 仍然产生
            orch.Tick();
            Assert.That(orch.LastChecksum, Is.Not.EqualTo(0UL),
                "空命令 Tick 应产生非零 checksum");
            Assert.That(orch.CurrentTick, Is.EqualTo(1));
        }

        [Test]
        public void ReplayFile_VersionField_Exists()
        {
            var replay = new ReplayFile();
            Assert.That(replay.Version, Is.EqualTo(1),
                "ReplayFile 必须有 Version 字段做格式兼容");
        }
    }
}
