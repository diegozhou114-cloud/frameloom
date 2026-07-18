using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Network.Contracts.Dispatch
{
    public sealed class NetworkMessage
    {
        public uint MessageId { get; }
        public byte[] Payload { get; }

        public NetworkMessage(uint messageId, byte[] payload)
        {
            MessageId = messageId;
            Payload = (byte[])(payload ?? Array.Empty<byte>()).Clone();
        }
    }

    public sealed class BoundedMessagePump
    {
        private readonly object _gate = new object();
        private readonly Queue<NetworkMessage> _queue = new Queue<NetworkMessage>();
        private readonly Dictionary<uint, Action<NetworkMessage>> _handlers =
            new Dictionary<uint, Action<NetworkMessage>>();
        private readonly int _capacity;
        private readonly int _maxMessagesPerPump;

        public int DroppedCount { get; private set; }

        public int PendingCount
        {
            get
            {
                lock (_gate) return _queue.Count;
            }
        }

        public BoundedMessagePump(int capacity, int maxMessagesPerPump)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (maxMessagesPerPump <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxMessagesPerPump));
            _capacity = capacity;
            _maxMessagesPerPump = maxMessagesPerPump;
        }

        public void Register(uint messageId, Action<NetworkMessage> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_gate)
            {
                if (_handlers.TryGetValue(messageId, out Action<NetworkMessage> existing))
                    _handlers[messageId] = existing + handler;
                else
                    _handlers.Add(messageId, handler);
            }
        }

        public void Unregister(uint messageId, Action<NetworkMessage> handler)
        {
            if (handler == null) return;
            lock (_gate)
            {
                if (!_handlers.TryGetValue(messageId, out Action<NetworkMessage> existing)) return;
                Action<NetworkMessage> remaining = existing - handler;
                if (remaining == null) _handlers.Remove(messageId);
                else _handlers[messageId] = remaining;
            }
        }

        public bool TryEnqueue(NetworkMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            lock (_gate)
            {
                if (_queue.Count >= _capacity)
                {
                    DroppedCount++;
                    return false;
                }
                _queue.Enqueue(message);
                return true;
            }
        }

        public int Pump()
        {
            int dispatched = 0;
            while (dispatched < _maxMessagesPerPump)
            {
                NetworkMessage message;
                Action<NetworkMessage> handler;
                lock (_gate)
                {
                    if (_queue.Count == 0) break;
                    message = _queue.Dequeue();
                    _handlers.TryGetValue(message.MessageId, out handler);
                }
                handler?.Invoke(message);
                dispatched++;
            }
            return dispatched;
        }
    }
}
