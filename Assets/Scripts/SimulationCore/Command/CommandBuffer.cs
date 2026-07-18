using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 命令缓冲 — 收集玩家输入，按 Tick 分组。
    /// 每渲染帧调用 AddCommand，每逻辑 Tick 调用 Drain 消费。
    /// </summary>
    public class CommandBuffer
    {
        /// <summary>已缓冲的命令数</summary>
        public int PendingCount => _pending.Count;

        private readonly List<ISimCommand> _pending = new List<ISimCommand>();

        /// <summary>添加一条命令（渲染帧调用）</summary>
        public void AddCommand(ISimCommand cmd)
        {
            _pending.Add(cmd);
        }

        /// <summary>取出指定 Tick 的所有命令（逻辑 Tick 调用）</summary>
        public List<ISimCommand> Drain(int tick)
        {
            var result = new List<ISimCommand>();
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Tick == tick)
                {
                    result.Add(_pending[i]);
                    _pending.RemoveAt(i);
                }
            }
            // 保持确定性：按 PlayerId 排序（同一 Tick 内多玩家命令的执行顺序）
            result.Sort(CompareWithinTick);
            return result;
        }

        /// <summary>获取指定 Tick 的命令而不移除（用于录制）</summary>
        public List<ISimCommand> Peek(int tick)
        {
            var result = new List<ISimCommand>();
            foreach (var cmd in _pending)
                if (cmd.Tick == tick)
                    result.Add(cmd);
            result.Sort(CompareWithinTick);
            return result;
        }

        public ulong HashInto(ulong hash)
        {
            var ordered = new List<ISimCommand>(_pending);
            ordered.Sort(CompareAll);
            var state = new DeterministicHash64(hash);
            state.AddInt32(ordered.Count);
            foreach (ISimCommand command in ordered)
            {
                state.AddByte((byte)command.Type);
                state.AddInt32(command.Tick);
                state.AddInt32(command.PlayerId);
                state.AddInt32(command.Sequence);
                switch (command)
                {
                    case DeployCmd deploy:
                        state.AddInt32(deploy.UnitTypeId);
                        state.AddInt32(deploy.GridX);
                        state.AddInt32(deploy.GridY);
                        break;
                    case MoveCmd move:
                        state.AddInt32(move.EntityId);
                        state.AddInt32(move.TargetX);
                        state.AddInt32(move.TargetY);
                        break;
                    case SkillCmd skill:
                        state.AddInt32(skill.CasterId);
                        state.AddInt32(skill.SkillId);
                        state.AddInt32(skill.TargetX);
                        state.AddInt32(skill.TargetY);
                        break;
                }
            }
            return state.Value;
        }

        public ISimCommand[] ExportPendingCommands()
        {
            var ordered = new List<ISimCommand>(_pending);
            ordered.Sort(CompareAll);
            return ordered.ToArray();
        }

        public void RestorePendingCommands(IEnumerable<ISimCommand> commands)
        {
            _pending.Clear();
            if (commands == null) return;
            foreach (ISimCommand command in commands)
            {
                if (command == null) throw new System.ArgumentException("Pending command cannot be null.");
                _pending.Add(command);
            }
        }

        private static int CompareWithinTick(ISimCommand left, ISimCommand right)
        {
            int player = left.PlayerId.CompareTo(right.PlayerId);
            if (player != 0) return player;
            int sequence = left.Sequence.CompareTo(right.Sequence);
            return sequence != 0 ? sequence : left.Type.CompareTo(right.Type);
        }

        private static int CompareAll(ISimCommand left, ISimCommand right)
        {
            int tick = left.Tick.CompareTo(right.Tick);
            return tick != 0 ? tick : CompareWithinTick(left, right);
        }

        /// <summary>清空所有待处理命令</summary>
        public void Clear()
        {
            _pending.Clear();
        }
    }
}
