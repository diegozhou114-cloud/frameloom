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
            result.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));
            return result;
        }

        /// <summary>获取指定 Tick 的命令而不移除（用于录制）</summary>
        public List<ISimCommand> Peek(int tick)
        {
            var result = new List<ISimCommand>();
            foreach (var cmd in _pending)
                if (cmd.Tick == tick)
                    result.Add(cmd);
            result.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));
            return result;
        }

        /// <summary>清空所有待处理命令</summary>
        public void Clear()
        {
            _pending.Clear();
        }
    }
}
