using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public class SimBattleDeterminismTests
    {
        private SimWorld CreateWorld()
        {
            return new SimWorld(ComponentTypeRegistry.CreateCombatRegistry(), 256);
        }

        /// <summary>创建简单战斗场景：2个攻击方 + 3个防守方，在网格上</summary>
        private void SetupBattle(SimWorld world, DeterministicRandom rng)
        {
            // 防守方建筑（不移动，高血量）
            for (int i = 0; i < 3; i++)
            {
                int gx = 10 + i * 8;
                int gy = 15;
                var e = world.CreateEntity(typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent));
                world.SetComponent(e, SimTransformComponent.Create(gx, gy, LogicInt.FromInt(1)));
                world.SetComponent(e, SimHealthComponent.Create(200));
                world.SetComponent(e, SimTeamComponent.Create(SimTeam.Defender));
            }

            // 攻击方单位（可移动，低血量）
            for (int i = 0; i < 2; i++)
            {
                int gx = 5;
                int gy = 5 + i * 5;
                var e = world.CreateEntity(
                    typeof(SimTransformComponent), typeof(SimMovementComponent), typeof(SimHealthComponent),
                    typeof(SimTeamComponent), typeof(SimAttackComponent), typeof(SimTargetComponent));
                world.SetComponent(e, SimTransformComponent.Create(gx, gy, LogicInt.FromInt(1)));
                world.SetComponent(e, SimMovementComponent.Create(3f, 1f)); // speed=3
                world.SetComponent(e, SimHealthComponent.Create(100));
                world.SetComponent(e, SimTeamComponent.Create(SimTeam.Attacker));
                world.SetComponent(e, SimAttackComponent.Create(25, 3f, 1f, 20)); // damage=25, range=3, cooldown=1s
                world.SetComponent(e, SimTargetComponent.Create());
            }
        }

        /// <summary>简单的 AI：每 Tick 找最近的敌人，设置为目标+移动路径</summary>
        private void SimpleAI(SimWorld world, int tick)
        {
            // 仅每 10 Tick 重新索敌
            if (tick % 10 != 0) return;

            var attackers = new List<(SimEntity entity, int x, int y)>();
            var defenders = new List<(SimEntity entity, int x, int y)>();

            world.ForEachComponent<SimTransformComponent>((entity, transform) =>
            {
                if (world.TryGetComponent<SimTeamComponent>(entity, out var team))
                {
                    if (team.TeamId == SimTeam.Attacker)
                        attackers.Add((entity, transform.GridX, transform.GridY));
                    else if (team.TeamId == SimTeam.Defender)
                        defenders.Add((entity, transform.GridX, transform.GridY));
                }
            });

            var isWalkable = new Func<int, int, bool>((x, y) =>
            {
                // 简化：所有格可通行
                return x >= 0 && x < 50 && y >= 0 && y < 50;
            });
            var pathfinder = new IntAStarPathfinding(50, 50, isWalkable);

            foreach (var (attacker, ax, ay) in attackers)
            {
                // 找最近防守方
                (SimEntity entity, int x, int y)? closest = null;
                int bestDist = int.MaxValue;
                foreach (var d in defenders)
                {
                    int dist = Math.Abs(ax - d.x) + Math.Abs(ay - d.y);
                    if (dist < bestDist) { bestDist = dist; closest = d; }
                }

                if (closest == null) continue;

                if (world.TryGetComponent<SimTargetComponent>(attacker, out var target))
                {
                    target.TargetEntity = closest.Value.entity;
                    world.SetComponent(attacker, target);
                }

                var path = pathfinder.FindPath(ax, ay, closest.Value.x, closest.Value.y);
                if (path.Count > 0)
                {
                    if (world.TryGetComponent<SimMovementComponent>(attacker, out var mov))
                    {
                        mov.Path = new List<int>();
                        foreach (var (px, py) in path)
                            mov.Path.Add((px << 16) | py);
                        mov.PathIndex = 0;
                        world.SetComponent(attacker, mov);
                    }
                }
            }
        }

        [Test]
        public void SameSeedSameInput_TenRuns_SameChecksum()
        {
            ulong firstChecksum = 0;

            for (int run = 0; run < 10; run++)
            {
                var world = CreateWorld();
                var rng = new DeterministicRandom(42);
                SetupBattle(world, rng);

                var systems = SimCombatBootstrap.Build(world);
                var deathSys = systems.Find<SimDeathSystem>();

                for (int tick = 0; tick < 100; tick++)
                {
                    SimpleAI(world, tick);
                    systems.UpdateAll();
                    if (deathSys.Winner.HasValue) break;
                }

                ulong cs = world.ComputeChecksum();

                if (run == 0)
                    firstChecksum = cs;
                else
                    Assert.That(cs, Is.EqualTo(firstChecksum),
                        $"Run {run}: checksum differs from run 0. Expected={firstChecksum:X16}, Got={cs:X16}");
            }
        }

        [Test]
        public void DifferentSeed_DifferentChecksum()
        {
            ulong? cs42 = null;
            ulong? cs99 = null;

            for (int run = 0; run < 2; run++)
            {
                int seed = run == 0 ? 42 : 99;
                var world = CreateWorld();
                var rng = new DeterministicRandom(seed);
                SetupBattle(world, rng);

                var systems = SimCombatBootstrap.Build(world);
                var deathSys = systems.Find<SimDeathSystem>();

                for (int tick = 0; tick < 100; tick++)
                {
                    SimpleAI(world, tick);
                    systems.UpdateAll();
                    if (deathSys.Winner.HasValue) break;
                }

                if (seed == 42) cs42 = world.ComputeChecksum();
                else cs99 = world.ComputeChecksum();
            }

            Assert.That(cs42, Is.Not.EqualTo(cs99),
                "不同 seed 应产生不同 checksum");
        }

        [Test]
        public void Benchmark_100Entities_100Ticks()
        {
            var world = new SimWorld(ComponentTypeRegistry.CreateCombatRegistry(), 512);
            var rng = new DeterministicRandom(1);

            // 创建 100 个实体
            for (int i = 0; i < 50; i++)
            {
                SimEntity e = i % 2 == 0
                    ? world.CreateEntity(
                        typeof(SimTransformComponent), typeof(SimHealthComponent),
                        typeof(SimTeamComponent), typeof(SimAttackComponent),
                        typeof(SimTargetComponent))
                    : world.CreateEntity(
                        typeof(SimTransformComponent), typeof(SimHealthComponent),
                        typeof(SimTeamComponent));
                world.SetComponent(e, SimTransformComponent.Create(i % 20, i / 20, LogicInt.FromInt(1)));
                world.SetComponent(e, SimHealthComponent.Create(100));
                world.SetComponent(e, SimTeamComponent.Create(i < 25 ? SimTeam.Attacker : SimTeam.Defender));
                if (i % 2 == 0)
                {
                    world.SetComponent(e, SimAttackComponent.Create(10, 2f, 2f, 20));
                    world.SetComponent(e, SimTargetComponent.Create());
                }
            }

            var systems = SimCombatBootstrap.Build(world);
            var deathSys = systems.Find<SimDeathSystem>();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int tick = 0; tick < 100; tick++)
            {
                systems.UpdateAll();
                if (deathSys.Winner.HasValue) break;
            }
            sw.Stop();

            TestContext.Progress.WriteLine($"100 entities × {100} ticks: {sw.ElapsedMilliseconds}ms total, {sw.ElapsedMilliseconds / 100.0:F2}ms/tick");
        }

        [Test]
        public void AStar_DeterministicPath_SameEveryTime()
        {
            var isWalkable = new Func<int, int, bool>((x, y) => x >= 0 && x < 50 && y >= 0 && y < 50);
            var pathfinder = new IntAStarPathfinding(50, 50, isWalkable);

            List<(int, int)> firstPath = null;

            for (int run = 0; run < 5; run++)
            {
                var path = pathfinder.FindPath(5, 5, 40, 40);
                if (run == 0)
                    firstPath = path;
                else
                    Assert.That(path, Is.EqualTo(firstPath),
                        $"Run {run}: A* path differs from run 0");
            }
        }
    }
}
