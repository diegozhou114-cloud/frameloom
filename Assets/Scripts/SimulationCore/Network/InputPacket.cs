using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// UDP 输入包 — 冗余携带最近 N 个输入 + Ack 确认。
    /// 格式：[newestSeq:4] [count:1] [input0:8] [input1:8] ... [ackSeq:4]
    /// </summary>
    public class InputPacket
    {
        public const int MaxInputsPerPacket = 32;

        /// <summary>包中最新的输入序列号</summary>
        public uint NewestSequence;
        /// <summary>冗余携带的输入列表（从旧到新）</summary>
        public List<FrameInput> Inputs;
        /// <summary>确认已收到的对端最新序列号</summary>
        public uint AckSequence;

        public InputPacket()
        {
            Inputs = new List<FrameInput>();
        }

        /// <summary>序列化为字节数组（显式小端序，跨平台一致）</summary>
        public byte[] ToBytes()
        {
            int count = Math.Min(Inputs.Count, MaxInputsPerPacket);
            int size = 4 + 1 + count * 8 + 4; // seq:4 + count:1 + inputs:count*8 + ack:4
            byte[] bytes = new byte[size];
            int offset = 0;

            // NewestSequence — little-endian
            bytes[offset]     = (byte)(NewestSequence & 0xFF);
            bytes[offset + 1] = (byte)((NewestSequence >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((NewestSequence >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((NewestSequence >> 24) & 0xFF);
            offset += 4;

            bytes[offset++] = (byte)count;
            for (int i = 0; i < count; i++)
            {
                var input = Inputs[i];
                // input.Sequence — little-endian
                bytes[offset]     = (byte)(input.Sequence & 0xFF);
                bytes[offset + 1] = (byte)((input.Sequence >> 8) & 0xFF);
                bytes[offset + 2] = (byte)((input.Sequence >> 16) & 0xFF);
                bytes[offset + 3] = (byte)((input.Sequence >> 24) & 0xFF);
                offset += 4;
                // input.Data — little-endian
                bytes[offset]     = (byte)(input.Data & 0xFF);
                bytes[offset + 1] = (byte)((input.Data >> 8) & 0xFF);
                bytes[offset + 2] = (byte)((input.Data >> 16) & 0xFF);
                bytes[offset + 3] = (byte)((input.Data >> 24) & 0xFF);
                offset += 4;
            }
            // AckSequence — little-endian
            bytes[offset]     = (byte)(AckSequence & 0xFF);
            bytes[offset + 1] = (byte)((AckSequence >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((AckSequence >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((AckSequence >> 24) & 0xFF);

            return bytes;
        }

        /// <summary>从字节数组反序列化（显式小端序，跨平台一致）</summary>
        public static InputPacket FromBytes(byte[] bytes)
        {
            var packet = new InputPacket();
            int offset = 0;
            // NewestSequence — little-endian
            packet.NewestSequence = (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
            offset += 4;
            int count = bytes[offset++];
            if (offset + count * 8 + 4 > bytes.Length) return null; // not enough bytes for inputs + ack
            for (int i = 0; i < count; i++)
            {
                packet.Inputs.Add(new FrameInput
                {
                    Sequence = (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24)),
                    Data = (uint)(bytes[offset + 4] | (bytes[offset + 5] << 8) | (bytes[offset + 6] << 16) | (bytes[offset + 7] << 24)),
                });
                offset += 8;
            }
            // AckSequence — little-endian
            packet.AckSequence = (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
            return packet;
        }
    }
}
