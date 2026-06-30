using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 输入接收器 — 缓冲到达的输入，按序交付，发送 Ack。
    /// </summary>
    public class InputReceiver
    {
        /// <summary>缓冲的输入（可能乱序到达）</summary>
        private readonly SortedDictionary<uint, FrameInput> _buffer = new SortedDictionary<uint, FrameInput>();
        /// <summary>期望的下一个序列号</summary>
        public uint ExpectedSequence { get; private set; }
        /// <summary>已收到的最大序列号（用于 Ack）</summary>
        public uint MaxReceivedSequence { get; private set; }

        /// <summary>接收一个输入包，返回其中所有输入
        /// 注意：packet.NewestSequence 与 packet.AckSequence 当前未使用 — 可靠性协议（丢包重传/拥塞控制）需要这两个字段。
        /// </summary>
        public void ReceivePacket(InputPacket packet)
        {
            foreach (var input in packet.Inputs)
            {
                if (!_buffer.ContainsKey(input.Sequence))
                    _buffer[input.Sequence] = input;
                if (input.Sequence > MaxReceivedSequence)
                    MaxReceivedSequence = input.Sequence;
            }
        }

        /// <summary>取出所有已就绪（连续）的输入</summary>
        public List<FrameInput> DrainReadyInputs()
        {
            var ready = new List<FrameInput>();
            while (_buffer.TryGetValue(ExpectedSequence, out var input))
            {
                ready.Add(input);
                _buffer.Remove(ExpectedSequence);
                ExpectedSequence++;
            }
            return ready;
        }

        /// <summary>缓冲区是否为空</summary>
        public bool HasBuffered => _buffer.Count > 0;
    }
}
