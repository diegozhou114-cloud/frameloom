using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 全局事件总线 — 基于字符串键的发布/订阅，支持 0~2 参数。
    /// 线程安全。实现 IEventBus 接口，可通过 SetInstance 注入 Mock。
    /// </summary>
    public static class EventBus
    {
        private static IEventBus _instance;
        private static readonly DefaultEventBus _default = new DefaultEventBus();
        private static readonly object _instanceLock = new object();

        /// <summary>当前实例（默认 = 内置实现）</summary>
        public static IEventBus Instance
        {
            get => _instance ?? _default;
            private set => _instance = value;
        }

        /// <summary>注入自定义实现（单测用）</summary>
        public static void SetInstance(IEventBus instance) { lock (_instanceLock) _instance = instance; }

        /// <summary>重置为默认实现</summary>
        public static void ResetInstance() { lock (_instanceLock) _instance = null; }

        // ==================== 委托方法 ====================
        public static void Subscribe(string eventKey, Action handler)
            => Instance.Subscribe(eventKey, handler);
        public static void Unsubscribe(string eventKey, Action handler)
            => Instance.Unsubscribe(eventKey, handler);
        public static void Publish(string eventKey)
            => Instance.Publish(eventKey);
        public static void Subscribe<T>(string eventKey, Action<T> handler)
            => Instance.Subscribe(eventKey, handler);
        public static void Unsubscribe<T>(string eventKey, Action<T> handler)
            => Instance.Unsubscribe(eventKey, handler);
        public static void Publish<T>(string eventKey, T arg)
            => Instance.Publish(eventKey, arg);
        public static void Subscribe<T1, T2>(string eventKey, Action<T1, T2> handler)
            => Instance.Subscribe(eventKey, handler);
        public static void Unsubscribe<T1, T2>(string eventKey, Action<T1, T2> handler)
            => Instance.Unsubscribe(eventKey, handler);
        public static void Publish<T1, T2>(string eventKey, T1 arg1, T2 arg2)
            => Instance.Publish(eventKey, arg1, arg2);
        public static void ClearAll() => Instance.ClearAll();
        public static int TotalEventCount => Instance.TotalEventCount;

        // ==================== 类型安全 EventKey 重载 ====================

        public static void Subscribe<T>(EventKey<T> key, Action<T> handler)
            => Instance.Subscribe(key.Key, handler);
        public static void Unsubscribe<T>(EventKey<T> key, Action<T> handler)
            => Instance.Unsubscribe(key.Key, handler);
        public static void Publish<T>(EventKey<T> key, T arg)
            => Instance.Publish(key.Key, arg);
    }

    /// <summary>EventBus 默认实现</summary>
    internal class DefaultEventBus : IEventBus
    {
        private readonly Dictionary<string, Action> _events0 = new();
        private readonly Dictionary<string, Delegate> _events1 = new();
        private readonly Dictionary<string, Delegate> _events2 = new();

        public void Subscribe(string k, Action h) { lock (_events0) _events0[k] = _events0.TryGetValue(k, out var e) ? e + h : h; }
        public void Unsubscribe(string k, Action h) { lock (_events0) { if (_events0.TryGetValue(k, out var e)) { var r = e - h; if (r == null) _events0.Remove(k); else _events0[k] = r; } } }
        public void Publish(string k) { Action h; lock (_events0) _events0.TryGetValue(k, out h); h?.Invoke(); }

        public void Subscribe<T>(string k, Action<T> h) { lock (_events1) _events1[k] = _events1.TryGetValue(k, out var e) ? Delegate.Combine(e, h) : h; }
        public void Unsubscribe<T>(string k, Action<T> h) { lock (_events1) { if (_events1.TryGetValue(k, out var e)) { var r = Delegate.Remove(e, h); if (r == null) _events1.Remove(k); else _events1[k] = r; } } }
        public void Publish<T>(string k, T a) { Delegate d; lock (_events1) _events1.TryGetValue(k, out d); (d as Action<T>)?.Invoke(a); }

        public void Subscribe<T1, T2>(string k, Action<T1, T2> h) { lock (_events2) _events2[k] = _events2.TryGetValue(k, out var e) ? Delegate.Combine(e, h) : h; }
        public void Unsubscribe<T1, T2>(string k, Action<T1, T2> h) { lock (_events2) { if (_events2.TryGetValue(k, out var e)) { var r = Delegate.Remove(e, h); if (r == null) _events2.Remove(k); else _events2[k] = r; } } }
        public void Publish<T1, T2>(string k, T1 a1, T2 a2) { Delegate d; lock (_events2) _events2.TryGetValue(k, out d); (d as Action<T1, T2>)?.Invoke(a1, a2); }

        public void ClearAll() { lock (_events0) _events0.Clear(); lock (_events1) _events1.Clear(); lock (_events2) _events2.Clear(); }
        public int TotalEventCount { get { int c = 0; lock (_events0) c += _events0.Count; lock (_events1) c += _events1.Count; lock (_events2) c += _events2.Count; return c; } }
    }
}
