using System;
using System.Collections.Generic;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Core.Tests
{
    internal sealed class SubscriptionScopeTestBus : IEventBus
    {
        private readonly IEventBus _inner;
        public readonly List<string> RemovedKeys = new();
        public readonly HashSet<string> ThrowOnRemove = new();
        public Action BeforeSubscribe;
        public Action AfterSubscribe;

        public SubscriptionScopeTestBus(IEventBus inner) => _inner = inner;
        public int TotalEventCount => _inner.TotalEventCount;

        public void Subscribe(string key, Action handler)
        {
            BeforeSubscribe?.Invoke();
            _inner.Subscribe(key, handler);
            AfterSubscribe?.Invoke();
        }

        public void Unsubscribe(string key, Action handler)
        {
            RemovedKeys.Add(key);
            if (ThrowOnRemove.Contains(key)) throw new InvalidOperationException(key);
            _inner.Unsubscribe(key, handler);
        }

        public void Publish(string key) => _inner.Publish(key);
        public void Subscribe<T>(string key, Action<T> handler)
        {
            BeforeSubscribe?.Invoke();
            _inner.Subscribe(key, handler);
            AfterSubscribe?.Invoke();
        }
        public void Unsubscribe<T>(string key, Action<T> handler) => _inner.Unsubscribe(key, handler);
        public void Publish<T>(string key, T arg) => _inner.Publish(key, arg);
        public void Subscribe<T1, T2>(string key, Action<T1, T2> handler)
        {
            BeforeSubscribe?.Invoke();
            _inner.Subscribe(key, handler);
            AfterSubscribe?.Invoke();
        }
        public void Unsubscribe<T1, T2>(string key, Action<T1, T2> handler) =>
            _inner.Unsubscribe(key, handler);
        public void Publish<T1, T2>(string key, T1 first, T2 second) =>
            _inner.Publish(key, first, second);
        public void ClearAll() => _inner.ClearAll();
    }
}
