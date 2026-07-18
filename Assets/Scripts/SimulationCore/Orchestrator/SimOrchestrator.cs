using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>战斗阶段</summary>
    public enum SimCombatPhase
    {
        Idle,
        Deploying,
        Battling,
        Ended,
    }

    /// <summary>
    /// 仿真战斗编排器 — 驱动阶段流转和 Tick 循环。
    /// 纯同步，零 Unity 依赖。命令管线：Drain→Update→Checksum→Record。
    /// </summary>
    public class SimOrchestrator
    {
        /// <summary>当前阶段</summary>
        public SimCombatPhase CurrentPhase { get; private set; } = SimCombatPhase.Idle;

        /// <summary>当前 Tick 号（从 0 开始）</summary>
        public int CurrentTick { get; private set; }

        /// <summary>最近一次 Checksum</summary>
        public ulong LastChecksum { get; private set; }

        /// <summary>获取指定 Tick 的 Checksum（用于 FrameSync 比对）</summary>
        public bool TryGetChecksumForTick(int tick, out ulong checksum)
        {
            var snapshot = _snapshotHistory[tick % 64];
            if (snapshot != null && snapshot.Tick == tick)
            {
                checksum = snapshot.Checksum;
                return true;
            }
            checksum = 0;
            return false;
        }

        /// <summary>性能计数器</summary>
        public SimPerfCounters Perf { get; } = new SimPerfCounters();

        private readonly SimWorld _world;
        private readonly SimSystemGroup _systems;
        private readonly SimDeathSystem _deathSys;
        private readonly CommandBuffer _buffer;
        private readonly CommandRecorder _recorder;
        private readonly ISimInitializer _initializer;
        private readonly SimArchetypeRegistry _registry;
        private readonly ISimLogger _logger;

        // 固定 64 槽环形快照 — 回滚超过 64 Tick 会命中过期快照，生产环境需按需扩容。
        private readonly TickSnapshot[] _snapshotHistory = new TickSnapshot[64];

        private int _deployTimeLimit;   // 部署时间限制（Tick 数）
        private int _deployElapsed;
        private int _battleElapsed;
        private int _battleTimeout;     // 战斗超时（Tick 数）

        /// <param name="world">仿真世界</param>
        /// <param name="systems">ECS 系统管线</param>
        /// <param name="deathSys">死亡系统引用（用于胜负判定）</param>
        /// <param name="buffer">命令缓冲</param>
        /// <param name="recorder">命令录制器（可为 null，LocalMode 不需要）</param>
        /// <param name="initializer">初始布局（用于创建实体）</param>
        /// <param name="registry">实体原型注册表（用于根据 ArchetypeId 创建组件）</param>
        /// <param name="deployTimeLimitSeconds">部署阶段时限（秒）</param>
        /// <param name="battleTimeoutSeconds">战斗超时（秒）</param>
        /// <param name="ticksPerSecond">逻辑帧率</param>
        public SimOrchestrator(
            SimWorld world,
            SimSystemGroup systems,
            SimDeathSystem deathSys,
            CommandBuffer buffer,
            CommandRecorder recorder,
            ISimInitializer initializer,
            SimArchetypeRegistry registry,
            float deployTimeLimitSeconds = 30f,
            float battleTimeoutSeconds = 180f,
            int ticksPerSecond = 20,
            ISimLogger logger = null)
        {
            _world = world;
            _systems = systems;
            _deathSys = deathSys;
            _buffer = buffer;
            _recorder = recorder;
            _initializer = initializer;
            _registry = registry;
            _logger = logger;
            _deployTimeLimit = (int)(deployTimeLimitSeconds * ticksPerSecond);
            _battleTimeout = (int)(battleTimeoutSeconds * ticksPerSecond);
        }

        /// <summary>初始化战斗：创建初始实体，进入部署阶段</summary>
        public void StartBattle()
        {
            CurrentPhase = SimCombatPhase.Idle;
            CurrentTick = 0;
            _deployElapsed = 0;
            _battleElapsed = 0;

            // Clear snapshot ring buffer (Bug #18)
            Array.Clear(_snapshotHistory, 0, _snapshotHistory.Length);

            // 从 ISimInitializer 创建初始实体，使用 Archetype 注册表
            foreach (var info in _initializer.GetInitialEntities())
            {
                var archetype = _registry.Get(info.ArchetypeId);
                if (archetype == null)
                    continue;
                archetype.CreateIn(_world, info.GridX, info.GridY, info.TeamId);
            }

            // 进入部署阶段
            CurrentPhase = SimCombatPhase.Deploying;
        }

        /// <summary>执行一个逻辑 Tick（每 50ms 调用一次）</summary>
        public void Tick()
        {
            if (CurrentPhase == SimCombatPhase.Idle || CurrentPhase == SimCombatPhase.Ended)
                return;

            var sw = new System.Diagnostics.Stopwatch();
            float drainMs = 0, updateMs = 0, hashMs = 0;

            // 1. Drain commands and dispatch
            sw.Restart();
            var cmds = _buffer.Drain(CurrentTick);
            if (cmds != null)
            {
                foreach (var cmd in cmds)
                {
                    DispatchCommand(cmd);
                }
            }
            drainMs = (float)sw.Elapsed.TotalMilliseconds;

            // 2. Update systems
            sw.Restart();
            _systems.UpdateAll();
            updateMs = (float)sw.Elapsed.TotalMilliseconds;

            // 3. Compute checksum
            sw.Restart();
            LastChecksum = _world.ComputeChecksum();
            hashMs = (float)sw.Elapsed.TotalMilliseconds;

            // 4. Record (if recording)
            _recorder?.Record(CurrentTick, LastChecksum);

            // 5. Perf counters
            Perf.RecordTick(CurrentTick, _world.EntityCount, drainMs, updateMs, hashMs);

            // 6. Phase logic
            UpdatePhaseLogic();

            // Snapshot recording AFTER phase transitions, before CurrentTick++ (Bug #6, #10)
            var snap = _world.ExportFullSnapshot();
            snap.Tick = CurrentTick;
            _snapshotHistory[CurrentTick % 64] = snap;

            CurrentTick++;
        }

        private void UpdatePhaseLogic()
        {
            switch (CurrentPhase)
            {
                case SimCombatPhase.Deploying:
                    _deployElapsed++;
                    if (_deployElapsed >= _deployTimeLimit)
                        CurrentPhase = SimCombatPhase.Battling;
                    break;

                case SimCombatPhase.Battling:
                    _battleElapsed++;
                    if (_battleTimeout > 0 && _battleElapsed >= _battleTimeout)
                    {
                        _deathSys.ForceWinner(SimTeam.Defender);
                        CurrentPhase = SimCombatPhase.Ended;
                    }
                    else if (_deathSys.Winner.HasValue)
                    {
                        CurrentPhase = SimCombatPhase.Ended;
                    }
                    break;

                case SimCombatPhase.Ended:
                    // 已结束，不再推进
                    break;
            }
        }

        /// <summary>强制设置阶段（用于测试）</summary>
        public void SetPhase(SimCombatPhase phase) => CurrentPhase = phase;

        /// <summary>
        /// 回滚到指定 Tick 的快照状态。
        /// 用于 FrameSync：权威 Checksum 不一致时回滚修正。
        /// tick=0 表示回滚到初始状态（StartBattle 之后）。
        /// </summary>
        public void RollbackTo(int targetTick)
        {
            if (targetTick == 0)
            {
                Array.Clear(_snapshotHistory, 0, _snapshotHistory.Length);
                _world.ClearPools();
                StartBattle();
                return;
            }

            var snapshot = _snapshotHistory[targetTick % 64];
            // Bug #10: Validate snapshot exists and matches the requested tick
            if (snapshot == null || snapshot.Tick != targetTick)
            {
                return;
            }

            _world.RestoreFromFull(snapshot);
            CurrentTick = targetTick;
            // Bug #5: Restore phase counters
            _battleElapsed = targetTick;
            _deployElapsed = Math.Min(_deployElapsed, targetTick);
            CurrentPhase = targetTick < _deployTimeLimit ? SimCombatPhase.Deploying : SimCombatPhase.Battling;
        }

        /// <summary>
        /// 从快照回滚然后重放到目标 Tick。
        /// 从 replayBuffer 中取出对应 Tick 的命令注入模拟。
        /// </summary>
        public void ReplayFrom(int fromTick, int toTick, CommandBuffer replayBuffer)
        {
            RollbackTo(fromTick);

            for (int t = fromTick; t <= toTick; t++)
            {
                // Inject commands from replay buffer for this tick
                var cmds = replayBuffer.Peek(t);
                foreach (var cmd in cmds)
                    _buffer.AddCommand(cmd);

                Tick();
            }
        }
        /// <summary>分发命令到对应的处理逻辑</summary>
        private void DispatchCommand(ISimCommand cmd)
        {
            switch (cmd.Type)
            {
                case SimCommandType.Deploy:
                    HandleDeploy((DeployCmd)cmd);
                    break;
                case SimCommandType.Move:
                    HandleMove((MoveCmd)cmd);
                    break;
                case SimCommandType.Skill:
                    HandleSkill((SkillCmd)cmd);
                    break;
            }
        }

        private void HandleDeploy(DeployCmd cmd)
        {
            WarnIfStub();
            // TODO: 从配置表获取单位原型，在 _world 中创建 SimEntity
            _logger?.Log($"[SimOrchestrator] DeployCmd: unitType={cmd.UnitTypeId} at ({cmd.GridX},{cmd.GridY}) player={cmd.PlayerId}");
        }

        private void HandleMove(MoveCmd cmd)
        {
            WarnIfStub();
            // TODO: 在 _world 中查找实体并设置其移动目标
            _logger?.Log($"[SimOrchestrator] MoveCmd: entity={cmd.EntityId} to ({cmd.TargetX},{cmd.TargetY})");
        }

        private void HandleSkill(SkillCmd cmd)
        {
            WarnIfStub();
            // TODO: 在 _world 中查找施法者实体并释放技能
            _logger?.Log($"[SimOrchestrator] SkillCmd: caster={cmd.CasterId} skill={cmd.SkillId}");
        }

        private static bool _stubWarned;
        private void WarnIfStub()
        {
            if (_stubWarned) return;
            _stubWarned = true;
            _logger?.Warn("[SimOrchestrator] Command handling is still a placeholder.");
        }
    }
}
