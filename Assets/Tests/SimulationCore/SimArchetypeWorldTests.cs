using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public class SimArchetypeWorldTests
    {
        private ComponentTypeRegistry _registry;
        private SimArchetypeWorld CreateWorld()
        {
            return new SimArchetypeWorld(ComponentTypeRegistry.CreateCombatRegistry(), 256);
        }

        [SetUp]
        public void Setup()
        {
            _registry = ComponentTypeRegistry.CreateCombatRegistry();
        }

        [Test]
        public void CreateEntity_AllComponents_EntityCountIncrements()
        {
            var world = CreateWorld();
            var e = world.CreateEntity(
                typeof(SimTransformComponent),
                typeof(SimHealthComponent),
                typeof(SimTeamComponent));
            Assert.That(world.EntityCount, Is.EqualTo(1));
            Assert.That(e.IsValid, Is.True);
        }

        [Test]
        public void GetSetComponent_RoundTrips()
        {
            var world = CreateWorld();
            var e = world.CreateEntity(
                typeof(SimTransformComponent),
                typeof(SimHealthComponent));

            Assert.That(world.HasComponent<SimHealthComponent>(e), Is.True);
            Assert.That(world.HasComponent<SimMovementComponent>(e), Is.False);

            var health = new SimHealthComponent { CurrentHP = 50, MaxHP = 100 };
            world.SetComponent(e, health);

            Assert.That(world.TryGetComponent<SimHealthComponent>(e, out var h), Is.True);
            Assert.That(h.CurrentHP, Is.EqualTo(50));
        }

        [Test]
        public void EntityID_Monotonic()
        {
            var world = CreateWorld();
            var e1 = world.CreateEntity(typeof(SimHealthComponent));
            var e2 = world.CreateEntity(typeof(SimHealthComponent));
            Assert.That(e1.Id, Is.EqualTo(0));
            Assert.That(e2.Id, Is.EqualTo(1));
        }

        [Test]
        public void DestroyEntity_DecrementsCount()
        {
            var world = CreateWorld();
            var e = world.CreateEntity(typeof(SimHealthComponent));
            Assert.That(world.EntityCount, Is.EqualTo(1));
            world.DestroyEntity(e);
            Assert.That(world.EntityCount, Is.EqualTo(0));
            Assert.That(world.TryGetComponent<SimHealthComponent>(e, out _), Is.False);
        }

        [Test]
        public void SameOperations_SameChecksum()
        {
            var w1 = CreateWorld();
            var w2 = CreateWorld();

            foreach (var world in new[] { w1, w2 })
            {
                var e1 = world.CreateEntity(
                    typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent));
                world.SetComponent(e1, new SimTransformComponent { GridX = 10, GridY = 20 });
                world.SetComponent(e1, new SimHealthComponent { CurrentHP = 100, MaxHP = 100 });
                world.SetComponent(e1, new SimTeamComponent { TeamId = SimTeam.Defender });

                var e2 = world.CreateEntity(
                    typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent));
                world.SetComponent(e2, new SimTransformComponent { GridX = 30, GridY = 40 });
                world.SetComponent(e2, new SimHealthComponent { CurrentHP = 50, MaxHP = 80 });
                world.SetComponent(e2, new SimTeamComponent { TeamId = SimTeam.Attacker });
            }

            Assert.That(w1.ComputeChecksum(), Is.EqualTo(w2.ComputeChecksum()));
        }

        [Test]
        public void TenRuns_SameOps_SameChecksum()
        {
            ulong expected = 0;
            for (int run = 0; run < 10; run++)
            {
                var world = CreateWorld();
                for (int i = 0; i < 10; i++)
                {
                    var e = world.CreateEntity(
                        typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent));
                    world.SetComponent(e, new SimTransformComponent { GridX = i, GridY = i * 2 });
                    world.SetComponent(e, new SimHealthComponent { CurrentHP = 100 - i * 3, MaxHP = 100 });
                    world.SetComponent(e, new SimTeamComponent { TeamId = i % 2 == 0 ? SimTeam.Attacker : SimTeam.Defender });
                }
                ulong cs = world.ComputeChecksum();
                if (run == 0) expected = cs;
                else Assert.That(cs, Is.EqualTo(expected), $"Run {run}: checksum mismatch");
            }
        }

        [Test]
        public void FullBattle_WithArchetypeSystems_RunsToEnd()
        {
            var world = CreateWorld();
            var rng = new DeterministicRandom(42);

            // Create defenders (buildings)
            for (int i = 0; i < 3; i++)
            {
                var e = world.CreateEntity(
                    typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent));
                world.SetComponent(e, SimTransformComponent.Create(10 + i * 8, 15, LogicInt.FromInt(1)));
                world.SetComponent(e, SimHealthComponent.Create(200));
                world.SetComponent(e, SimTeamComponent.Create(SimTeam.Defender));
            }

            // Create attackers
            for (int i = 0; i < 2; i++)
            {
                var e = world.CreateEntity(
                    typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent),
                    typeof(SimMovementComponent), typeof(SimAttackComponent), typeof(SimTargetComponent));
                world.SetComponent(e, SimTransformComponent.Create(5, 5 + i * 5, LogicInt.FromInt(1)));
                world.SetComponent(e, SimHealthComponent.Create(100));
                world.SetComponent(e, SimTeamComponent.Create(SimTeam.Attacker));
                world.SetComponent(e, SimMovementComponent.Create(3f, 1f));
                world.SetComponent(e, SimAttackComponent.Create(25, 3f, 1f, 20));
                world.SetComponent(e, SimTargetComponent.Create());
            }

            var systems = SimArchetypeCombatBootstrap.Build(world);
            var deathSys = systems.Find<SimArchetypeDeathSystem>();

            int tick;
            for (tick = 0; tick < 200; tick++)
            {
                SimpleAI(world, tick);
                systems.UpdateAll();
                if (deathSys.Winner.HasValue) break;
            }

            Assert.That(tick, Is.LessThan(200), "Battle should end before timeout");
            Assert.That(deathSys.Winner.HasValue, Is.True);
        }

        // Simple AI adapted for SimArchetypeWorld
        private void SimpleAI(SimArchetypeWorld world, int tick)
        {
            if (tick % 10 != 0) return;

            var defenders = new List<(SimEntity entity, int x, int y)>();
            world.ForEachComponent<SimTransformComponent>((entity, transform) =>
            {
                if (world.TryGetComponent<SimTeamComponent>(entity, out var team)
                    && team.TeamId == SimTeam.Defender)
                    defenders.Add((entity, transform.GridX, transform.GridY));
            });

            world.ForEachComponent<SimTransformComponent>((entity, transform) =>
            {
                if (!world.TryGetComponent<SimTeamComponent>(entity, out var team)
                    || team.TeamId != SimTeam.Attacker || defenders.Count == 0)
                    return;

                var closest = defenders[0];
                int bestDist = Math.Abs(transform.GridX - closest.x) + Math.Abs(transform.GridY - closest.y);
                foreach (var d in defenders)
                {
                    int dist = Math.Abs(transform.GridX - d.x) + Math.Abs(transform.GridY - d.y);
                    if (dist < bestDist) { bestDist = dist; closest = d; }
                }

                if (world.TryGetComponent<SimTargetComponent>(entity, out var target))
                {
                    target.TargetEntity = closest.entity;
                    world.SetComponent(entity, target);
                }

                if (world.TryGetComponent<SimMovementComponent>(entity, out var mov))
                {
                    int dx = closest.x - transform.GridX;
                    int dy = closest.y - transform.GridY;
                    int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
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
        public void Benchmark_ArchetypeWorld_100Entities_100Ticks()
        {
            var world = CreateWorld();
            for (int i = 0; i < 100; i++)
            {
                if (i < 30)
                {
                    var e = world.CreateEntity(
                        typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent));
                    world.SetComponent(e, new SimHealthComponent { CurrentHP = 500, MaxHP = 500 });
                    world.SetComponent(e, new SimTeamComponent { TeamId = SimTeam.Defender });
                }
                else
                {
                    var e = world.CreateEntity(
                        typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent),
                        typeof(SimMovementComponent), typeof(SimAttackComponent), typeof(SimTargetComponent));
                    world.SetComponent(e, new SimHealthComponent { CurrentHP = 100, MaxHP = 100 });
                    world.SetComponent(e, new SimTeamComponent { TeamId = SimTeam.Attacker });
                    world.SetComponent(e, SimMovementComponent.Create(2f, 1f));
                    world.SetComponent(e, SimAttackComponent.Create(20, 3f, 1f, 20));
                }
            }

            var systems = SimArchetypeCombatBootstrap.Build(world);
            var deathSys = systems.Find<SimArchetypeDeathSystem>();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int t = 0; t < 100; t++)
            {
                SimpleAI(world, t);
                systems.UpdateAll();
                if (deathSys.Winner.HasValue) break;
            }
            sw.Stop();

            TestContext.Progress.WriteLine($"[ArchetypeWorld] 100e × 100t: {sw.ElapsedMilliseconds}ms total, {sw.ElapsedMilliseconds / 100.0:F3}ms/tick");
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThan(0)); // just ensure it ran
        }
    }
}
