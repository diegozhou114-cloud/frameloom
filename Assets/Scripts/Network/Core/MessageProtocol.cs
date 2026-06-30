using System;

namespace ZWebGLFrameWork.Network
{
    /// <summary>
    /// 长度前缀帧协议 — 解决 TCP 粘包/半包问题。
    /// 帧格式：[4字节大端长度][消息体]
    /// 最大消息体 16MB。
    /// </summary>
    public class MessageProtocol
    {
        private byte[] _receiveBuffer;
        private int _bufferLength;

        /// <summary>最大消息大小（默认 16MB）</summary>
        public int MaxMessageSize { get; set; } = 16 * 1024 * 1024;

        public MessageProtocol(int bufferSize = NetworkDefine.ReceiveBufferSize)
        {
            _receiveBuffer = new byte[bufferSize];
        }

        // ==================== 编码（发送方向） ====================

        /// <summary>将消息体编码为带长度前缀的完整帧</summary>
        public static byte[] Encode(byte[] body)
        {
            return Encode(body, 0, body.Length);
        }

        public static byte[] Encode(byte[] body, int offset, int length)
        {
            if (length > 16 * 1024 * 1024)
                throw new ArgumentException($"消息体过大: {length} bytes, 最大 16MB");

            var frame = new byte[4 + length];
            frame[0] = (byte)((length >> 24) & 0xFF);
            frame[1] = (byte)((length >> 16) & 0xFF);
            frame[2] = (byte)((length >> 8) & 0xFF);
            frame[3] = (byte)(length & 0xFF);

            Buffer.BlockCopy(body, offset, frame, 4, length);
            return frame;
        }

        // ==================== 解码（接收方向） ====================

        /// <summary>
        /// 喂入原始数据到内部缓冲区（一次 receive 调用一次）。
        /// 分离喂入与提取，避免循环调用 TryDecode 时重复拷贝数据。
        /// </summary>
        public void Feed(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0) return;

            int totalLen = _bufferLength + dataLength;
            if (totalLen > _receiveBuffer.Length)
                ExpandBuffer(totalLen);

            Buffer.BlockCopy(data, 0, _receiveBuffer, _bufferLength, dataLength);
            _bufferLength = totalLen;
        }

        /// <summary>
        /// 从内部缓冲区提取一条完整消息体。
        /// 返回 null = 需要更多数据（等待下一次 Feed）。
        /// 可在 Feed 后循环调用直到返回 null。
        /// </summary>
        public byte[] TryExtract()
        {
            if (_bufferLength < 4) return null;

            int bodyLength =
                (_receiveBuffer[0] << 24) |
                (_receiveBuffer[1] << 16) |
                (_receiveBuffer[2] << 8) |
                _receiveBuffer[3];

            if (bodyLength <= 0 || bodyLength > MaxMessageSize)
            {
                // Corrupt frame: reset the entire buffer to resync on the next packet boundary
                // instead of shifting by 1 byte which causes cascading desync.
                Reset();
                return null;
            }

            int frameLength = 4 + bodyLength;
            if (_bufferLength < frameLength) return null;

            var body = new byte[bodyLength];
            Buffer.BlockCopy(_receiveBuffer, 4, body, 0, bodyLength);
            RemoveBytes(frameLength);

            return body;
        }

        /// <summary>
        /// [兼容旧 API] 喂入数据并尝试提取一条消息。
        /// 推荐使用 Feed + 循环 TryExtract 替代循环调用此方法。
        /// </summary>
        public byte[] TryDecode(byte[] data, int dataLength)
        {
            Feed(data, dataLength);
            return TryExtract();
        }

        public void Reset()
        {
            _bufferLength = 0;
        }

        private void RemoveBytes(int count)
        {
            int remaining = _bufferLength - count;
            if (remaining > 0)
                Buffer.BlockCopy(_receiveBuffer, count, _receiveBuffer, 0, remaining);
            _bufferLength = remaining;
        }

        private void ExpandBuffer(int required)
        {
            int newSize = _receiveBuffer.Length;
            while (newSize < required) newSize *= 2;
            var newBuffer = new byte[newSize];
            Buffer.BlockCopy(_receiveBuffer, 0, newBuffer, 0, _bufferLength);
            _receiveBuffer = newBuffer;
        }
    }
}
