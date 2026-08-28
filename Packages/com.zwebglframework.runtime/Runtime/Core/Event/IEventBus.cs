using System;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 事件总线接口 — 可 Mock 的抽象。
    /// 默认实现：EventBus.Instance（静态代理）。
    /// 单测中可通过 EventBus.SetInstance(mock) 替换。
    /// </summary>
    public interface IEventBus
    {
        void Subscribe(string eventKey, Action handler);
        void Unsubscribe(string eventKey, Action handler);
        void Publish(string eventKey);

        void Subscribe<T>(string eventKey, Action<T> handler);
        void Unsubscribe<T>(string eventKey, Action<T> handler);
        void Publish<T>(string eventKey, T arg);

        void Subscribe<T1, T2>(string eventKey, Action<T1, T2> handler);
        void Unsubscribe<T1, T2>(string eventKey, Action<T1, T2> handler);
        void Publish<T1, T2>(string eventKey, T1 arg1, T2 arg2);

        void ClearAll();
        int TotalEventCount { get; }
    }

    /// <summary>
    /// 类型安全的事件键 — 编译期校验参数类型，避免字符串拼写错误。
    /// 用法：public static readonly EventKey<GameStateChangedEvent> StateChanged = new("GameStateChanged");
    ///      EventBus.Publish(StateChanged, payload);
    /// </summary>
    public readonly struct EventKey<T>
    {
        public readonly string Key;

        public EventKey(string key) => Key = key;

        public static implicit operator string(EventKey<T> ek) => ek.Key;

        public override string ToString() => Key;
    }

    /// <summary>EventBus 类型安全扩展方法</summary>
    public static class EventBusExtensions
    {
        public static void Subscribe<T>(this IEventBus bus, EventKey<T> key, Action<T> handler)
            => bus.Subscribe(key.Key, handler);

        public static void Unsubscribe<T>(this IEventBus bus, EventKey<T> key, Action<T> handler)
            => bus.Unsubscribe(key.Key, handler);

        public static void Publish<T>(this IEventBus bus, EventKey<T> key, T arg)
            => bus.Publish(key.Key, arg);

    }
}
