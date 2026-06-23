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

        /// <summary>性能计数器</summary>
        public SimPerfCounters Perf { get; } = new SimPerfCounters();

        private readonly SimWorld _world;
        private readonly SimSystemGroup _systems;
        private readonly SimDeathSystem _deathSys;
        private readonly CommandBuffer _buffer;
        private readonly CommandRecorder _recorder;
        private readonly ISimInitializer _initializer;

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
            float deployTimeLimitSeconds = 30f,
            float battleTimeoutSeconds = 180f,
            int ticksPerSecond = 20)
        {
            _world = world;
            _systems = systems;
            _deathSys = deathSys;
            _buffer = buffer;
            _recorder = recorder;
            _initializer = initializer;
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

            // 从 ISimInitializer 创建初始实体
            foreach (var info in _initializer.GetInitialEntities())
            {
                var entity = _world.CreateEntity();
                _world.AddComponent(entity, SimTransformComponent.Create(
                    info.GridX, info.GridY, LogicInt.FromInt(1)));
                _world.AddComponent(entity, SimHealthComponent.Create(
                    info.TeamId == SimTeam.Defender ? 500 : 100));
                _world.AddComponent(entity, SimTeamComponent.Create(info.TeamId));

                if (info.TeamId == SimTeam.Attacker)
                {
                    _world.AddComponent(entity, SimMovementComponent.Create(2f, 1f));
                    _world.AddComponent(entity, SimAttackComponent.Create(20, 3f, 1f, 20));
                    _world.AddComponent(entity, SimTargetComponent.Create());
                }
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

            // 1. Drain commands
            sw.Restart();
            var cmds = _buffer.Drain(CurrentTick);
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

            CurrentTick++;
        }

        /// <summary>强制设置阶段（用于测试）</summary>
        public void SetPhase(SimCombatPhase phase) => CurrentPhase = phase;
    }
}
