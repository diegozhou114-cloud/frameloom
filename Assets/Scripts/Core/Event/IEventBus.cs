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
}
