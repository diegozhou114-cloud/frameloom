using System.Collections.Generic;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    public struct ReplayResult
    {
        public bool IsValid;
        public int FailedTick;
        public ulong ExpectedChecksum;
        public ulong ActualChecksum;
    }

    /// <summary>
    /// 重放验证器 — 接受 ReplayFile，用确定性仿真重放并逐 Tick 比对 Checksum。
    /// 可在 .NET Core 服务器上运行，共享 SimulationCore 源码。
    /// </summary>
    public class ReplayValidator
    {
        private readonly ISimLogger _logger;

        public ReplayValidator(ISimLogger logger = null)
        {
            _logger = logger;
        }

        public ReplayResult Validate(ReplayFile replay)
        {
            var world = new SimWorld(ComponentTypeRegistry.CreateCombatRegistry(), 256, _logger);
            var buffer = new CommandBuffer();
            var systems = SimCombatBootstrap.Build(world);
            var deathSys = systems.Find<SimDeathSystem>();
            var registry = SimArchetypeRegistry.CreateDefault();

            // 用 ReplayFile 中的数据构造 ISimInitializer
            var initializer = new ReplayInitializer(replay);
            var orchestrator = new SimOrchestrator(
                world, systems, deathSys, buffer, null, initializer, registry, logger: _logger);

            orchestrator.StartBattle();
            // 跳过部署阶段，直接进战斗
            orchestrator.SetPhase(SimCombatPhase.Battling);

            for (int tick = 0; tick < replay.TotalTicks; tick++)
            {
                // 从 ReplayFile 注入命令到 Buffer
                var tickRecord = replay.Records.Find(r => r.Tick == tick);
                if (tickRecord != null)
                {
                    foreach (var sc in tickRecord.Commands)
                    {
                        var cmd = CommandRecorder.DeserializeCommand(sc);
                        if (cmd != null)
                            buffer.AddCommand(cmd);
                    }
                }

                orchestrator.Tick();

                if (orchestrator.CurrentPhase == SimCombatPhase.Ended)
                    break;

                // 比对 Checksum
                ulong expected = tickRecord?.Checksum ?? 0;
                ulong actual = orchestrator.LastChecksum;
                if (expected != actual)
                {
                    _logger?.Error($"Tick {tick}: checksum mismatch! Expected={expected:X16} Actual={actual:X16}");
                    return new ReplayResult
                    {
                        IsValid = false,
                        FailedTick = tick,
                        ExpectedChecksum = expected,
                        ActualChecksum = actual,
                    };
                }
            }

            return new ReplayResult { IsValid = true, FailedTick = -1 };
        }

        /// <summary>从 ReplayFile 构造 ISimInitializer</summary>
        private class ReplayInitializer : ISimInitializer
        {
            private readonly ReplayFile _replay;
            public int Seed => _replay.Seed;

            public ReplayInitializer(ReplayFile replay) { _replay = replay; }

            public IEnumerable<SimEntitySpawnInfo> GetInitialEntities()
            {
                return _replay.InitialLayout;
            }
        }
    }
}
