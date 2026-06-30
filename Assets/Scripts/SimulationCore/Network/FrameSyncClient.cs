using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 帧同步客户端状态
    /// </summary>
    public enum FrameSyncState
    {
        Idle,
        Connecting,
        Syncing,      // 等待双方输入就绪
        Predicting,   // 本地预测执行中
        RollingBack,  // Checksum 不一致，回滚中
    }

    /// <summary>
    /// 帧同步客户端 — 预测+回滚状态机。
    /// 本地立即预测执行，收到服务器权威数据后比对 Checksum。
    /// </summary>
    public class FrameSyncClient
    {
        public FrameSyncState State { get; private set; } = FrameSyncState.Idle;

        /// <summary>当前权威 Tick（服务器已确认的最新 Tick）</summary>
        public int AuthorityTick { get; private set; }

        /// <summary>本地预测 Tick（总是 ≥ AuthorityTick）</summary>
        public int PredictedTick { get; private set; }

        /// <summary>回滚次数（统计用）</summary>
        public int RollbackCount { get; private set; }

        private readonly SimOrchestrator _orchestrator;
        private readonly CommandBuffer _buffer;
        private readonly InputSender _sender;
        private readonly InputReceiver _receiver;
        private readonly int _playerId;
        private readonly ISimLogger _logger;

        public FrameSyncClient(
            SimOrchestrator orchestrator,
            CommandBuffer buffer,
            InputSender sender,
            InputReceiver receiver,
            int playerId,
            ISimLogger logger = null)
        {
            _orchestrator = orchestrator;
            _buffer = buffer;
            _sender = sender;
            _receiver = receiver;
            _playerId = playerId;
            _logger = logger;
        }

        /// <summary>开始帧同步</summary>
        public void Start()
        {
            _orchestrator.StartBattle();
            _orchestrator.SetPhase(SimCombatPhase.Battling);
            State = FrameSyncState.Syncing;
            AuthorityTick = 0;
            PredictedTick = 0;
        }

        /// <summary>每逻辑 Tick 调用（20Hz）</summary>
        public void Tick()
        {
            if (State == FrameSyncState.Idle) return;

            // ── 1. 发送本地输入 ──
            SendLocalInput();

            // ── 2. 尝试推进权威 Tick ──
            int advanced = AdvanceAuthority();
            if (advanced > 0)
            {
                _logger?.Log($"Authority advanced by {advanced} ticks to {AuthorityTick}");
            }

            // ── 3. 本地预测 ──
            if (State != FrameSyncState.RollingBack)
            {
                PredictAhead();
            }
        }

        /// <summary>收集本地输入并发送</summary>
        private void SendLocalInput()
        {
            // 消费已从 InputReceiver 排出的输入 → 转为 SimCommand → 放入 Buffer
            var remoteInputs = _receiver.DrainReadyInputs();
            foreach (var input in remoteInputs)
            {
                var cmd = input.ToCommand(1 - _playerId); // 对手的 PlayerId
                if (cmd != null)
                    _buffer.AddCommand(cmd);
            }
        }

        /// <summary>推进权威 Tick：服务器回传的数据已通过 InputReceiver 到达</summary>
        private int AdvanceAuthority()
        {
            int advanced = 0;
            while (_buffer.PendingCount > 0)
            {
                // 执行一个权威 Tick
                _orchestrator.Tick();
                AuthorityTick++;
                advanced++;

                if (_orchestrator.CurrentPhase == SimCombatPhase.Ended)
                {
                    State = FrameSyncState.Idle;
                    break;
                }
            }
            return advanced;
        }

        /// <summary>本地预测：在权威 Tick 之前预先执行</summary>
        private void PredictAhead()
        {
            // 简化：只预测 AuthorityTick+1
            // 完整实现需接收服务器回传的双方输入 + Checksum 后比对
            // TODO: Implement actual prediction simulation
            PredictedTick = AuthorityTick + 1;
        }

        /// <summary>
        /// 收到服务器权威数据 — 比对 Checksum，不一致则回滚。
        /// </summary>
        /// <param name="authorityTick">服务器确认的 Tick</param>
        /// <param name="authorityChecksum">服务器计算的权威 Checksum</param>
        public void OnAuthorityData(int authorityTick, ulong authorityChecksum)
        {
            // 从快照历史中查找对应 Tick 的本地 Checksum
            if (!_orchestrator.TryGetChecksumForTick(authorityTick, out ulong localChecksum))
            {
                _logger?.Warn($"No local checksum for tick {authorityTick}, accepting authority");
                AuthorityTick = authorityTick;
                return;
            }

            if (localChecksum != authorityChecksum)
            {
                _logger?.Warn($"Checksum mismatch at tick {authorityTick}! Local={localChecksum:X16} Auth={authorityChecksum:X16}. Rolling back...");
                RollbackCount++;

                // 回滚到上一个一致 Tick
                int rollbackTarget = Math.Max(0, authorityTick - 1);
                State = FrameSyncState.RollingBack;
                _orchestrator.RollbackTo(rollbackTarget);
                AuthorityTick = rollbackTarget;
                PredictedTick = rollbackTarget + 1;
                State = FrameSyncState.Syncing;
            }
            else
            {
                // 一致 — 释放该 Tick 之前的快照
                AuthorityTick = Math.Max(AuthorityTick, authorityTick);
                State = FrameSyncState.Predicting;
            }
        }

        /// <summary>获取统计摘要</summary>
        public string GetStats()
        {
            return $"State={State} AuthTick={AuthorityTick} PredTick={PredictedTick} Rollbacks={RollbackCount}";
        }
    }
}
