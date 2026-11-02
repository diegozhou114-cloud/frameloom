using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 拥有一组事件订阅，在结束时统一解除。由拥有者串行使用，不执行每帧工作。
    /// 捕获创建时的实际总线；切换 EventBus.Instance 不会改变解除订阅的目标。
    /// 每次注册使用独立转发委托，注册失败时只回滚本次监听，不影响其他拥有者。
    /// </summary>
    public sealed class SubscriptionScope : IDisposable
    {
        private readonly IEventBus _bus;
        private readonly List<Action> _unsubscribe = new();
        private bool _disposed;

        /// <summary>使用当前事件总线创建作用域。</summary>
        public SubscriptionScope() : this(EventBus.Instance) { }

        /// <summary>使用指定事件总线创建作用域。</summary>
        public SubscriptionScope(IEventBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        /// <summary>订阅无参数事件，直到作用域结束。</summary>
        public void Subscribe(string key, Action handler)
        {
            Action ownedHandler = () => handler();
            Register(handler, () => _bus.Subscribe(key, ownedHandler),
                () => _bus.Unsubscribe(key, ownedHandler));
        }

        /// <summary>订阅单参数事件，直到作用域结束。</summary>
        public void Subscribe<T>(string key, Action<T> handler)
        {
            Action<T> ownedHandler = value => handler(value);
            Register(handler, () => _bus.Subscribe(key, ownedHandler),
                () => _bus.Unsubscribe(key, ownedHandler));
        }

        /// <summary>通过类型化事件键订阅，直到作用域结束。</summary>
        public void Subscribe<T>(EventKey<T> key, Action<T> handler) =>
            Subscribe(key.Key, handler);

        /// <summary>订阅双参数事件，直到作用域结束。</summary>
        public void Subscribe<T1, T2>(string key, Action<T1, T2> handler)
        {
            Action<T1, T2> ownedHandler = (first, second) => handler(first, second);
            Register(handler, () => _bus.Subscribe(key, ownedHandler),
                () => _bus.Unsubscribe(key, ownedHandler));
        }

        private void Register(Delegate handler, Action subscribe, Action unsubscribe)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SubscriptionScope));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            try { subscribe(); }
            catch (Exception registrationFailure)
            {
                try { unsubscribe(); }
                catch (Exception cleanupFailure)
                {
                    if (!_disposed) _unsubscribe.Add(unsubscribe);
                    throw new AggregateException(registrationFailure, cleanupFailure);
                }
                throw;
            }
            if (_disposed)
            {
                unsubscribe();
                throw new ObjectDisposedException(nameof(SubscriptionScope));
            }
            _unsubscribe.Add(unsubscribe);
        }

        /// <summary>
        /// 逆序解除全部订阅；重复调用无效。单项失败仍尝试其余项目，最后汇总抛出。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception> failures = null;
            for (int index = _unsubscribe.Count - 1; index >= 0; index--)
            {
                try { _unsubscribe[index](); }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }
            _unsubscribe.Clear();
            if (failures != null) throw new AggregateException(failures);
        }
    }
}
