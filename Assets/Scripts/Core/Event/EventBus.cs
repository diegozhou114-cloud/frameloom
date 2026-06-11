using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 全局事件总线 — 基于字符串键的发布/订阅，支持 0~2 参数。
    /// 线程安全，自动清理空回调。
    /// </summary>
    public static class EventBus
    {
        // --- 无参事件 ---
        private static readonly Dictionary<string, Action> _events0 = new Dictionary<string, Action>();

        public static void Subscribe(string eventKey, Action handler)
        {
            if (handler == null) return;

            lock (_events0)
            {
                if (_events0.TryGetValue(eventKey, out var existing))
                {
                    _events0[eventKey] = existing + handler;
                }
                else
                {
                    _events0[eventKey] = handler;
                }
            }
        }

        public static void Unsubscribe(string eventKey, Action handler)
        {
            if (handler == null) return;

            lock (_events0)
            {
                if (_events0.TryGetValue(eventKey, out var existing))
                {
                    var result = existing - handler;
                    if (result == null)
                        _events0.Remove(eventKey);
                    else
                        _events0[eventKey] = result;
                }
            }
        }

        public static void Publish(string eventKey)
        {
            Action handler = null;
            lock (_events0)
            {
                _events0.TryGetValue(eventKey, out handler);
            }

            handler?.Invoke();
        }

        // --- 单参事件 ---
        private static readonly Dictionary<string, Delegate> _events1 = new Dictionary<string, Delegate>();

        public static void Subscribe<T>(string eventKey, Action<T> handler)
        {
            if (handler == null) return;

            lock (_events1)
            {
                if (_events1.TryGetValue(eventKey, out var existing))
                {
                    _events1[eventKey] = Delegate.Combine(existing, handler);
                }
                else
                {
                    _events1[eventKey] = handler;
                }
            }
        }

        public static void Unsubscribe<T>(string eventKey, Action<T> handler)
        {
            if (handler == null) return;

            lock (_events1)
            {
                if (_events1.TryGetValue(eventKey, out var existing))
                {
                    var result = Delegate.Remove(existing, handler);
                    if (result == null)
                        _events1.Remove(eventKey);
                    else
                        _events1[eventKey] = result;
                }
            }
        }

        public static void Publish<T>(string eventKey, T arg)
        {
            Delegate del = null;
            lock (_events1)
            {
                _events1.TryGetValue(eventKey, out del);
            }

            (del as Action<T>)?.Invoke(arg);
        }

        // --- 双参事件 ---
        private static readonly Dictionary<string, Delegate> _events2 = new Dictionary<string, Delegate>();

        public static void Subscribe<T1, T2>(string eventKey, Action<T1, T2> handler)
        {
            if (handler == null) return;

            lock (_events2)
            {
                if (_events2.TryGetValue(eventKey, out var existing))
                {
                    _events2[eventKey] = Delegate.Combine(existing, handler);
                }
                else
                {
                    _events2[eventKey] = handler;
                }
            }
        }

        public static void Unsubscribe<T1, T2>(string eventKey, Action<T1, T2> handler)
        {
            if (handler == null) return;

            lock (_events2)
            {
                if (_events2.TryGetValue(eventKey, out var existing))
                {
                    var result = Delegate.Remove(existing, handler);
                    if (result == null)
                        _events2.Remove(eventKey);
                    else
                        _events2[eventKey] = result;
                }
            }
        }

        public static void Publish<T1, T2>(string eventKey, T1 arg1, T2 arg2)
        {
            Delegate del = null;
            lock (_events2)
            {
                _events2.TryGetValue(eventKey, out del);
            }

            (del as Action<T1, T2>)?.Invoke(arg1, arg2);
        }

        /// <summary>清除所有事件注册（场景切换时调用）</summary>
        public static void ClearAll()
        {
            lock (_events0) { _events0.Clear(); }
            lock (_events1) { _events1.Clear(); }
            lock (_events2) { _events2.Clear(); }
        }

        /// <summary>当前注册的事件总数（调试用）</summary>
        public static int TotalEventCount
        {
            get
            {
                int count = 0;
                lock (_events0) { count += _events0.Count; }
                lock (_events1) { count += _events1.Count; }
                lock (_events2) { count += _events2.Count; }
                return count;
            }
        }
    }
}
