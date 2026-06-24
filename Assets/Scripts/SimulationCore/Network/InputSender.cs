using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 输入发送器 — 累积本地输入，冗余发送直到被 Ack。
    /// 基于 Glenn Fiedler 的 Deterministic Lockstep 方案。
    /// </summary>
    public class InputSender
    {
        /// <summary>已发送但未被确认的输入（按 Sequence 索引）</summary>
        private readonly Dictionary<uint, FrameInput> _unacked = new Dictionary<uint, FrameInput>();
        /// <summary>对端确认已收到的最大序列号</summary>
        public uint RemoteAckSequence { get; private set; }
        /// <summary>本地已生成的下一个序列号</summary>
        public uint NextSequence { get; private set; }

        /// <summary>添加一个新输入</summary>
        public void AddInput(FrameInput input)
        {
            _unacked[input.Sequence] = input;
            if (input.Sequence >= NextSequence)
                NextSequence = input.Sequence + 1;
        }

        /// <summary>生成待发送的 InputPacket（冗余携带所有未确认输入）</summary>
        public InputPacket BuildPacket()
        {
            var packet = new InputPacket
            {
                NewestSequence = NextSequence > 0 ? NextSequence - 1 : 0,
                AckSequence = RemoteAckSequence,
            };
            foreach (var kv in _unacked)
            {
                packet.Inputs.Add(kv.Value);
            }
            return packet;
        }

        /// <summary>处理对端的 Ack，丢弃已确认的旧输入</summary>
        public void OnAckReceived(uint ackSequence)
        {
            var toRemove = new List<uint>();
            foreach (var kv in _unacked)
            {
                if (kv.Key <= ackSequence)
                    toRemove.Add(kv.Key);
            }
            foreach (var key in toRemove)
                _unacked.Remove(key);
            if (ackSequence > RemoteAckSequence)
                RemoteAckSequence = ackSequence;
        }

        /// <summary>还有未确认的输入</summary>
        public bool HasUnacked => _unacked.Count > 0;
    }
}
