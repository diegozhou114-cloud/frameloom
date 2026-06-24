using NUnit.Framework;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

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
                // 1 个防守方建筑 (Cannon)
                yield return new SimEntitySpawnInfo(2, 15, 15, SimTeam.Defender);
                // 2 个攻击方单位 (Barbarian)
                yield return new SimEntitySpawnInfo(10, 5, 5, SimTeam.Attacker);
                yield return new SimEntitySpawnInfo(10, 5, 10, SimTeam.Attacker);
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
                    SimArchetypeRegistry.CreateDefault(),
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
                    SimArchetypeRegistry.CreateDefault(),
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
                SimArchetypeRegistry.CreateDefault(),
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

        [Test]
        public void ReplayFile_JsonRoundTrip_PreservesData()
        {
            var replay = new ReplayFile
            {
                Version = 1,
                Seed = 42,
                InitialLayout = new[]
                {
                    new SimEntitySpawnInfo(2, 10, 10, SimTeam.Defender),
                    new SimEntitySpawnInfo(10, 5, 5, SimTeam.Attacker),
                },
                Records = new List<TickRecord>
                {
                    new TickRecord { Tick = 0, Checksum = 12345UL, Commands = new List<SerializableCommand>() },
                    new TickRecord { Tick = 1, Checksum = 67890UL, Commands = new List<SerializableCommand>
                    {
                        new SerializableCommand { Type = "Deploy", Tick = 1, PlayerId = 0, UnitTypeId = 3, GridX = 7, GridY = 8 }
                    }},
                },
            };

            string json = replay.ToJson();
            var restored = ReplayFile.FromJson(json);

            Assert.That(restored.Version, Is.EqualTo(1));
            Assert.That(restored.Seed, Is.EqualTo(42));
            Assert.That(restored.InitialLayout.Length, Is.EqualTo(2));
            Assert.That(restored.InitialLayout[0].ArchetypeId, Is.EqualTo(2));
            Assert.That(restored.Records.Count, Is.EqualTo(2));
            Assert.That(restored.Records[1].Commands[0].Type, Is.EqualTo("Deploy"));
            Assert.That(restored.Records[1].Commands[0].UnitTypeId, Is.EqualTo(3));
        }

        [Test]
        public void FullBattle_ExportJson_ValidatorPasses()
        {
            const int seed = 42;
            string json;
            int totalTicks;

            // ── Phase 1: Run battle with recording ──
            {
                var world = new SimWorld(256);
                var buffer = new CommandBuffer();
                var initializer = new TestInitializer(seed);
                var recorder = new CommandRecorder(seed, initializer.GetInitialEntities(), buffer);
                var registry = SimArchetypeRegistry.CreateDefault();
                var systems = SimCombatBootstrap.Build(world);
                var deathSys = systems.Find<SimDeathSystem>();
                var orch = new SimOrchestrator(world, systems, deathSys, buffer, recorder, initializer, registry,
                    ticksPerSecond: 20);

                orch.StartBattle();
                orch.SetPhase(SimCombatPhase.Battling);

                for (int tick = 0; tick < 200; tick++)
                {
                    SimpleAI(world, tick);
                    orch.Tick();
                    if (orch.CurrentPhase == SimCombatPhase.Ended) break;
                }

                json = recorder.ExportJson();
                totalTicks = orch.CurrentTick;
                Assert.That(json, Is.Not.Null.And.Not.Empty, "JSON export should not be empty");
            }

            // ── Phase 2: Validate JSON directly ──
            var replay = ReplayFile.FromJson(json);
            Assert.That(replay.Version, Is.EqualTo(1));
            Assert.That(replay.Seed, Is.EqualTo(seed));
            Assert.That(replay.Records.Count, Is.GreaterThan(0));

            // ── Phase 3: Validate through replay validator ──
            // (in-process, same as server would do)
            var world2 = new SimWorld(256);
            var buffer2 = new CommandBuffer();
            var initializer2 = new TestInitializer(seed);
            var registry2 = SimArchetypeRegistry.CreateDefault();
            var systems2 = SimCombatBootstrap.Build(world2);
            var deathSys2 = systems2.Find<SimDeathSystem>();
            var orch2 = new SimOrchestrator(world2, systems2, deathSys2, buffer2, null, initializer2, registry2,
                ticksPerSecond: 20);

            orch2.StartBattle();
            orch2.SetPhase(SimCombatPhase.Battling);

            for (int tick = 0; tick < replay.Records.Count; tick++)
            {
                // Inject commands from replay
                var tickRecord = replay.Records.Find(r => r.Tick == tick);
                if (tickRecord != null)
                {
                    foreach (var sc in tickRecord.Commands)
                    {
                        var cmd = CommandRecorder.DeserializeCommand(sc);
                        if (cmd != null) buffer2.AddCommand(cmd);
                    }
                }

                orch2.Tick();

                if (orch2.CurrentPhase == SimCombatPhase.Ended) break;

                ulong expected = tickRecord?.Checksum ?? 0;
                ulong actual = orch2.LastChecksum;
                Assert.That(actual, Is.EqualTo(expected),
                    $"Tick {tick}: replay checksum mismatch. Expected={expected:X16}, Actual={actual:X16}");
            }
        }

        [Test]
        public void TamperedReplay_FailsValidation()
        {
            const int seed = 42;

            // Run a battle and export
            ReplayFile replay;
            {
                var world = new SimWorld(256);
                var buffer = new CommandBuffer();
                var initializer = new TestInitializer(seed);
                var recorder = new CommandRecorder(seed, initializer.GetInitialEntities(), buffer);
                var registry = SimArchetypeRegistry.CreateDefault();
                var systems = SimCombatBootstrap.Build(world);
                var deathSys = systems.Find<SimDeathSystem>();
                var orch = new SimOrchestrator(world, systems, deathSys, buffer, recorder, initializer, registry,
                    ticksPerSecond: 20);

                orch.StartBattle();
                orch.SetPhase(SimCombatPhase.Battling);

                for (int tick = 0; tick < 100; tick++)
                {
                    SimpleAI(world, tick);
                    orch.Tick();
                    if (orch.CurrentPhase == SimCombatPhase.Ended) break;
                }

                replay = recorder.Export();
            }

            Assert.That(replay.Records.Count, Is.GreaterThan(1), "Need at least 2 ticks to tamper");

            // Tamper: change a mid-battle checksum to a wrong value
            int tamperTick = replay.Records.Count / 2;
            replay.Records[tamperTick].Checksum = 0xDEADBEEFUL;

            // Replay the tampered file
            var world2 = new SimWorld(256);
            var buffer2 = new CommandBuffer();
            var initializer2 = new TestInitializer(seed);
            var registry2 = SimArchetypeRegistry.CreateDefault();
            var systems2 = SimCombatBootstrap.Build(world2);
            var deathSys2 = systems2.Find<SimDeathSystem>();
            var orch2 = new SimOrchestrator(world2, systems2, deathSys2, buffer2, null, initializer2, registry2,
                ticksPerSecond: 20);

            orch2.StartBattle();
            orch2.SetPhase(SimCombatPhase.Battling);

            bool mismatchFound = false;
            for (int tick = 0; tick < replay.Records.Count; tick++)
            {
                var tickRecord = replay.Records.Find(r => r.Tick == tick);
                if (tickRecord?.Commands != null)
                {
                    foreach (var sc in tickRecord.Commands)
                    {
                        var cmd = CommandRecorder.DeserializeCommand(sc);
                        if (cmd != null) buffer2.AddCommand(cmd);
                    }
                }

                orch2.Tick();
                if (orch2.CurrentPhase == SimCombatPhase.Ended) break;

                if (tickRecord != null && orch2.LastChecksum != tickRecord.Checksum)
                {
                    mismatchFound = true;
                    Assert.That(tick, Is.EqualTo(tamperTick),
                        $"Mismatch should occur at tampered tick {tamperTick}, but occurred at {tick}");
                    break;
                }
            }

            Assert.That(mismatchFound, Is.True, "Tampered replay should fail checksum validation");
        }

        [Test]
        public void Rollback_MidBattle_RestoresCorrectState()
        {
            const int seed = 42;
            var world = new SimWorld(256);
            var buffer = new CommandBuffer();
            var initializer = new TestInitializer(seed);
            var registry = SimArchetypeRegistry.CreateDefault();
            var systems = SimCombatBootstrap.Build(world);
            var deathSys = systems.Find<SimDeathSystem>();
            var orch = new SimOrchestrator(world, systems, deathSys, buffer, null, initializer, registry,
                ticksPerSecond: 20);

            orch.StartBattle();
            orch.SetPhase(SimCombatPhase.Battling);

            // Run 20 ticks
            for (int t = 0; t < 20; t++)
            {
                SimpleAI(world, t);
                orch.Tick();
            }
            ulong csAt20 = orch.LastChecksum;

            // Run 10 more ticks
            for (int t = 20; t < 30; t++)
            {
                SimpleAI(world, t);
                orch.Tick();
            }

            // Rollback to tick 20
            orch.RollbackTo(20);

            // Verify checksum matches original tick 20
            Assert.That(orch.LastChecksum, Is.EqualTo(csAt20),
                "Rollback to tick 20 should restore checksum from original tick 20");

            // Replay ticks 20–29 with same AI
            for (int t = 20; t < 30; t++)
            {
                SimpleAI(world, t);
                orch.Tick();
            }

            // After replay, checksum should be non-zero (deterministic replay)
            Assert.That(orch.LastChecksum, Is.Not.EqualTo(0UL),
                "After rollback + replay, checksum should be non-zero");
            Assert.That(orch.CurrentTick, Is.EqualTo(30),
                "After replay of 10 ticks from tick 20, CurrentTick should be 30");
        }

        [Test]
        public void Rollback_ToTickZero_RestartsBattle()
        {
            const int seed = 42;
            var world = new SimWorld(256);
            var buffer = new CommandBuffer();
            var initializer = new TestInitializer(seed);
            var registry = SimArchetypeRegistry.CreateDefault();
            var systems = SimCombatBootstrap.Build(world);
            var deathSys = systems.Find<SimDeathSystem>();
            var orch = new SimOrchestrator(world, systems, deathSys, buffer, null, initializer, registry,
                ticksPerSecond: 20);

            orch.StartBattle();
            orch.SetPhase(SimCombatPhase.Battling);

            // Run 5 ticks
            for (int t = 0; t < 5; t++)
            {
                SimpleAI(world, t);
                orch.Tick();
            }

            // Rollback to tick 0
            orch.RollbackTo(0);

            Assert.That(orch.CurrentTick, Is.EqualTo(0),
                "Rollback to tick 0 should reset CurrentTick to 0");
            Assert.That(orch.CurrentPhase, Is.EqualTo(SimCombatPhase.Deploying),
                "Rollback to tick 0 should restart battle in Deploying phase");
            Assert.That(world.EntityCount, Is.GreaterThan(0),
                "World should have entities after restart");
        }

        [Test]
        public void SingleTick_EmptyCommands_ProducesConsistentChecksum()
        {
            var world = new SimWorld(64);
            var buffer = new CommandBuffer();
            var initializer = new TestInitializer(42);
            var registry = SimArchetypeRegistry.CreateDefault();
            var systems = SimCombatBootstrap.Build(world);
            var deathSys = systems.Find<SimDeathSystem>();
            var orch = new SimOrchestrator(world, systems, deathSys, buffer, null, initializer, registry,
                ticksPerSecond: 20);

            orch.StartBattle();
            orch.SetPhase(SimCombatPhase.Battling);

            orch.Tick();
            ulong cs1 = orch.LastChecksum;

            // Replay same setup
            var world2 = new SimWorld(64);
            var buffer2 = new CommandBuffer();
            var initializer2 = new TestInitializer(42);
            var registry2 = SimArchetypeRegistry.CreateDefault();
            var systems2 = SimCombatBootstrap.Build(world2);
            var deathSys2 = systems2.Find<SimDeathSystem>();
            var orch2 = new SimOrchestrator(world2, systems2, deathSys2, buffer2, null, initializer2, registry2,
                ticksPerSecond: 20);
            orch2.StartBattle();
            orch2.SetPhase(SimCombatPhase.Battling);
            orch2.Tick();

            Assert.That(orch2.LastChecksum, Is.EqualTo(cs1),
                "Empty-command single tick should produce consistent checksum");
        }

        [Test]
        public void FrameSyncClient_RollbackOnChecksumMismatch()
        {
            const int seed = 42;
            var world = new SimWorld(256);
            var buffer = new CommandBuffer();
            var initializer = new TestInitializer(seed);
            var registry = SimArchetypeRegistry.CreateDefault();
            var systems = SimCombatBootstrap.Build(world);
            var deathSys = systems.Find<SimDeathSystem>();
            var orch = new SimOrchestrator(world, systems, deathSys, buffer, null, initializer, registry,
                ticksPerSecond: 20);

            var sender = new InputSender();
            var receiver = new InputReceiver();
            var client = new FrameSyncClient(orch, buffer, sender, receiver, 0);

            client.Start();

            // Run 10 ticks locally
            for (int t = 0; t < 10; t++)
            {
                SimpleAI(world, t);
                orch.Tick();
            }
            ulong csAt10 = orch.LastChecksum;

            // Simulate server sending WRONG checksum → should trigger rollback
            client.OnAuthorityData(10, 0xDEADBEEFUL);

            Assert.That(client.RollbackCount, Is.EqualTo(1),
                "Wrong checksum should trigger exactly 1 rollback");
            Assert.That(client.AuthorityTick, Is.EqualTo(9),
                "After rollback, authority should be at tick 9 (safe point)");
        }
    }
}
