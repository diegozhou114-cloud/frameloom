using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Core
{
    /// <summary>通用对象池，适用于普通 C# 引用对象。</summary>
    public class ObjectPool<T> : IDisposable where T : class
    {
        private const int DefaultMaxRetained = 256;
        private readonly Stack<T> _availableStack;
        private readonly HashSet<T> _owned;
        private readonly HashSet<T> _available;
        private readonly HashSet<T> _checkedOut;
        private readonly object _poolLock = new();
        private readonly Func<T> _createFunc;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly int _maxRetained;
        private bool _disposed;

        public int Count { get { lock (_poolLock) return _availableStack.Count; } }

        public ObjectPool(Func<T> createFunc, Action<T> onGet,
            Action<T> onRelease, Action<T> onDestroy, int preload)
            : this(createFunc, onGet, onRelease, onDestroy, preload,
                DefaultMaxRetained) { }

        public ObjectPool(Func<T> createFunc, Action<T> onGet = null,
            Action<T> onRelease = null, Action<T> onDestroy = null, int preload = 0,
            int maxRetained = DefaultMaxRetained)
        {
            if (createFunc == null) throw new ArgumentNullException(nameof(createFunc));
            if (preload < 0) throw new ArgumentOutOfRangeException(nameof(preload));
            if (maxRetained < preload)
                throw new ArgumentOutOfRangeException(nameof(maxRetained));
            _createFunc = createFunc;
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            _maxRetained = maxRetained;
            _availableStack = new Stack<T>(Math.Max(preload, 4));
            _owned = NewIdentitySet();
            _available = NewIdentitySet();
            _checkedOut = NewIdentitySet();
            try
            {
                for (int index = 0; index < preload; index++)
                {
                    T item = CreateOwned();
                    _available.Add(item);
                    _availableStack.Push(item);
                }
            }
            catch (Exception creationFailure)
            {
                Exception cleanupFailure = DestroyAvailable();
                if (cleanupFailure != null)
                    throw new AggregateException(creationFailure, cleanupFailure);
                throw;
            }
        }

        public T Get()
        {
            lock (_poolLock)
            {
                ThrowIfDisposed();
                bool reused = _availableStack.Count > 0;
                T item = reused ? _availableStack.Pop() : CreateOwned();
                if (reused) _available.Remove(item);
                _checkedOut.Add(item);
                try
                {
                    _onGet?.Invoke(item);
                    return item;
                }
                catch (Exception callbackFailure)
                {
                    _checkedOut.Remove(item);
                    if (reused)
                    {
                        _available.Add(item);
                        _availableStack.Push(item);
                    }
                    else
                    {
                        _owned.Remove(item);
                        try { _onDestroy?.Invoke(item); }
                        catch (Exception destroyFailure)
                        {
                            throw new AggregateException(callbackFailure, destroyFailure);
                        }
                    }
                    throw;
                }
            }
        }

        public void Release(T item)
        {
            lock (_poolLock)
            {
                ThrowIfDisposed();
                if (ReferenceEquals(item, null)) throw new ArgumentNullException(nameof(item));
                if (!_owned.Contains(item))
                    throw new ArgumentException("Object does not belong to this pool.", nameof(item));
                if (_available.Contains(item))
                    throw new InvalidOperationException("Object has already been released.");
                if (!_checkedOut.Contains(item))
                    throw new InvalidOperationException("Object is not checked out.");
                _onRelease?.Invoke(item);
                _checkedOut.Remove(item);
                if (_availableStack.Count < _maxRetained)
                {
                    _available.Add(item);
                    _availableStack.Push(item);
                }
                else
                {
                    _owned.Remove(item);
                    _onDestroy?.Invoke(item);
                }
            }
        }

        public void Clear()
        {
            lock (_poolLock)
            {
                ThrowIfDisposed();
                Exception failure = DestroyAvailable();
                if (failure != null) throw failure;
            }
        }

        public void Dispose()
        {
            lock (_poolLock)
            {
                if (_disposed) return;
                _disposed = true;
                _availableStack.Clear();
                _available.Clear();
                _checkedOut.Clear();
                Exception failure = null;
                foreach (T item in _owned) failure = Destroy(item, failure);
                _owned.Clear();
                if (failure != null) throw failure;
            }
        }

        private T CreateOwned()
        {
            T item = _createFunc();
            if (ReferenceEquals(item, null))
                throw new InvalidOperationException("Pool factory returned null.");
            if (!_owned.Add(item))
                throw new InvalidOperationException("Pool factory returned an owned object.");
            return item;
        }

        private Exception DestroyAvailable()
        {
            Exception failure = null;
            while (_availableStack.Count > 0)
            {
                T item = _availableStack.Pop();
                _available.Remove(item);
                _owned.Remove(item);
                failure = Destroy(item, failure);
            }
            return failure;
        }

        private Exception Destroy(T item, Exception failure)
        {
            try { _onDestroy?.Invoke(item); }
            catch (Exception exception) { return failure ?? exception; }
            return failure;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }

        private static HashSet<T> NewIdentitySet() =>
            new(ReferenceIdentityComparer<T>.Instance);
    }
}
