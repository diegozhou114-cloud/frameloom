using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// KCP 传输层 — 在可靠 UDP 上发送/接收 InputPacket。
    /// Unity 侧由 CombatManager 驱动 Update()。
    /// </summary>
    public class KcpTransport
    {
        /// <summary>当收到完整 InputPacket 时触发</summary>
        public event Action<InputPacket> OnPacketReceived;

        private readonly Action<byte[]> _sendCallback;  // 底层发送回调
        private readonly List<byte[]> _receiveQueue = new List<byte[]>();
        private readonly object _lock = new object();

        /// <param name="sendCallback">底层发送方法（由 KcpClient.OnDataReceived 联动）</param>
        public KcpTransport(Action<byte[]> sendCallback)
        {
            _sendCallback = sendCallback;
        }

        /// <summary>发送 InputPacket</summary>
        public void Send(InputPacket packet)
        {
            byte[] data = packet.ToBytes();
            _sendCallback(data);
        }

        /// <summary>接收原始字节（由 KcpClient 回调调用）</summary>
        public void OnRawDataReceived(byte[] data)
        {
            lock (_lock)
            {
                _receiveQueue.Add(data);
            }
        }

        /// <summary>每帧处理接收队列</summary>
        public void Update()
        {
            byte[][] toProcess;
            lock (_lock)
            {
                toProcess = _receiveQueue.ToArray();
                _receiveQueue.Clear();
            }

            foreach (var data in toProcess)
            {
                try
                {
                    var packet = InputPacket.FromBytes(data);
                    OnPacketReceived?.Invoke(packet);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[KcpTransport] 丢弃损坏的数据包: {ex.Message}");
                }
            }
        }
    }
}
