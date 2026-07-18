using System;
using System.Collections.Generic;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Network
{
    /// <summary>Queues encoded simulation input packets over a host transport.</summary>
    public sealed class KcpTransport
    {
        public event Action<InputPacket> OnPacketReceived;

        private readonly Action<byte[]> _sendCallback;
        private readonly ISimLogger _logger;
        private readonly List<byte[]> _receiveQueue = new List<byte[]>();
        private readonly object _lock = new object();

        public KcpTransport(Action<byte[]> sendCallback, ISimLogger logger = null)
        {
            _sendCallback = sendCallback ?? throw new ArgumentNullException(nameof(sendCallback));
            _logger = logger;
        }

        public void Send(InputPacket packet)
        {
            _sendCallback(packet.ToBytes());
        }

        public void OnRawDataReceived(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            lock (_lock)
            {
                _receiveQueue.Add(data);
            }
        }

        public void Update()
        {
            byte[][] toProcess;
            lock (_lock)
            {
                toProcess = _receiveQueue.ToArray();
                _receiveQueue.Clear();
            }

            foreach (byte[] data in toProcess)
            {
                try
                {
                    OnPacketReceived?.Invoke(InputPacket.FromBytes(data));
                }
                catch (Exception exception)
                {
                    _logger?.Warn($"[KcpTransport] Dropped malformed packet: {exception.Message}");
                }
            }
        }
    }
}
