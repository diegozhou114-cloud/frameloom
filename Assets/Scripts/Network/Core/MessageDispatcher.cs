using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Network
{
    /// <summary>消息处理器委托</summary>
    public delegate void MessageHandler(byte[] data);

    /// <summary>
    /// 消息分发器 — 根据消息 ID 路由到注册的处理器。
    /// 后台线程安全入队，主线程 Update 消费，避免 Unity 主线程安全问题。
    /// </summary>
    public class MessageDispatcher
    {
        /// <summary>消息 ID 在消息体中的偏移（默认 0 = 前 2 字节为消息 ID）</summary>
        public int MessageIdOffset { get; set; } = 0;

        /// <summary>消息 ID 字节数（默认 2 = ushort）</summary>
        public int MessageIdSize { get; set; } = 2;

        private readonly Dictionary<ushort, MessageHandler> _handlers = 
            new Dictionary<ushort, MessageHandler>();

        // 线程安全队列：后台线程 Enqueue → 主线程 Dequeue
        private readonly ConcurrentQueue<byte[]> _messageQueue = 
            new ConcurrentQueue<byte[]>();

        // ==================== 注册 ====================

        /// <summary>注册消息处理器</summary>
        public void Register(ushort messageId, MessageHandler handler)
        {
            if (handler == null) return;

            lock (_handlers)
            {
                if (_handlers.ContainsKey(messageId))
                {
                    _handlers[messageId] += handler;
                }
                else
                {
                    _handlers[messageId] = handler;
                }
            }
        }

        /// <summary>取消注册</summary>
        public void Unregister(ushort messageId, MessageHandler handler)
        {
            if (handler == null) return;

            lock (_handlers)
            {
                if (_handlers.TryGetValue(messageId, out var existing))
                {
                    var result = existing - handler;
                    if (result == null)
                        _handlers.Remove(messageId);
                    else
                        _handlers[messageId] = result;
                }
            }
        }

        /// <summary>取消指定消息 ID 的所有处理器</summary>
        public void UnregisterAll(ushort messageId)
        {
            lock (_handlers)
            {
                _handlers.Remove(messageId);
            }
        }

        /// <summary>清空所有注册</summary>
        public void Clear()
        {
            lock (_handlers)
            {
                _handlers.Clear();
            }

            while (_messageQueue.TryDequeue(out _)) { }
        }

        // ==================== 入队（任意线程安全） ====================

        /// <summary>将消息入队（来自网络线程），等待主线程消费</summary>
        public void Enqueue(byte[] rawData)
        {
            _messageQueue.Enqueue(rawData);
        }

        // ==================== 消费（仅主线程） ====================

        /// <summary>在主线程 Update 中调用，消费队列中的消息</summary>
        public void Dispatch()
        {
            while (_messageQueue.TryDequeue(out var data))
            {
                DispatchMessage(data);
            }
        }

        private void DispatchMessage(byte[] data)
        {
            if (data.Length < MessageIdOffset + MessageIdSize) return;

            // 从消息体中提取消息 ID（大端）
            ushort messageId = ReadMessageId(data);

            MessageHandler handler;
            lock (_handlers)
            {
                _handlers.TryGetValue(messageId, out handler);
            }

            if (handler != null)
            {
                try
                {
                    handler(data);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError(
                        $"[MessageDispatcher] 消息 {messageId} 处理异常: {ex}");
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning(
                    $"[MessageDispatcher] 未注册的消息 ID: {messageId}");
            }
        }

        private ushort ReadMessageId(byte[] data)
        {
            int offset = MessageIdOffset;
            if (MessageIdSize == 2)
            {
                return (ushort)((data[offset] << 8) | data[offset + 1]);
            }
            else if (MessageIdSize == 4)
            {
                return (ushort)((data[offset] << 24) | (data[offset + 1] << 16) |
                                (data[offset + 2] << 8) | data[offset + 3]);
            }
            return 0;
        }

        // ==================== 工具方法 ====================

        /// <summary>构建消息帧（长度前缀 + 消息体），消息体前 2 字节为消息 ID</summary>
        public static byte[] BuildMessage(ushort messageId, byte[] body)
        {
            var data = new byte[2 + body.Length];
            data[0] = (byte)((messageId >> 8) & 0xFF);
            data[1] = (byte)(messageId & 0xFF);
            Buffer.BlockCopy(body, 0, data, 2, body.Length);

            return MessageProtocol.Encode(data);
        }

        /// <summary>构建消息帧（仅消息 ID，无消息体）</summary>
        public static byte[] BuildMessage(ushort messageId)
        {
            var data = new byte[2];
            data[0] = (byte)((messageId >> 8) & 0xFF);
            data[1] = (byte)(messageId & 0xFF);

            return MessageProtocol.Encode(data);
        }
    }
}
