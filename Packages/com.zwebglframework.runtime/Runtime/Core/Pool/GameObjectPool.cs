using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>GameObject 专用对象池，管理 Prefab 实例的复用。</summary>
    public class GameObjectPool : IDisposable
    {
        private const int DefaultMaxRetained = 256;
        private readonly Stack<GameObject> _availableStack;
        private readonly List<GameObject> _deadScratch;
        private readonly HashSet<GameObject> _owned;
        private readonly HashSet<GameObject> _available;
        private readonly HashSet<GameObject> _checkedOut;
        private readonly object _poolLock = new();
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly int _maxRetained;
        private bool _disposed;

        public int Count
        {
            get { lock (_poolLock) { PruneDestroyed(_available); return _available.Count; } }
        }
        public GameObjectPool(GameObject prefab, Transform parent, int preload)
            : this(prefab, parent, preload, DefaultMaxRetained) { }

        public GameObjectPool(GameObject prefab, Transform parent = null,
            int preload = 0, int maxRetained = DefaultMaxRetained)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (preload < 0) throw new ArgumentOutOfRangeException(nameof(preload));
            if (maxRetained < preload)
                throw new ArgumentOutOfRangeException(nameof(maxRetained));
            _prefab = prefab;
            _parent = parent;
            _maxRetained = maxRetained;
            int capacity = Math.Max(preload, 4);
            _availableStack = new Stack<GameObject>(capacity);
            _deadScratch = new List<GameObject>(capacity);
            _owned = NewIdentitySet();
            _available = NewIdentitySet();
            _checkedOut = NewIdentitySet();
            try
            {
                for (int index = 0; index < preload; index++) Retain(CreateInactive());
            }
            catch
            {
                DestroyAvailable();
                throw;
            }
        }
        public GameObject Get()
        {
            lock (_poolLock)
            {
                ThrowIfDisposed();
                while (_availableStack.Count > 0)
                {
                    GameObject item = _availableStack.Pop();
                    _available.Remove(item);
                    if (item == null) { _owned.Remove(item); continue; }
                    return Checkout(item);
                }
                GameObject created = UnityEngine.Object.Instantiate(_prefab, LiveParent());
                _owned.Add(created);
                return Checkout(created);
            }
        }
        public GameObject Get(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            GameObject item = Get();
            try
            {
                item.transform.SetParent(parent == null ? LiveParent() : parent);
                item.transform.SetPositionAndRotation(position, rotation);
                return item;
            }
            catch
            {
                ForgetAndDestroy(item);
                throw;
            }
        }
        public void Release(GameObject item)
        {
            lock (_poolLock)
            {
                ThrowIfDisposed();
                if (ReferenceEquals(item, null)) throw new ArgumentNullException(nameof(item));
                if (!_owned.Contains(item))
                    throw new ArgumentException("Object does not belong to this pool.", nameof(item));
                if (item == null) { Forget(item); return; }
                if (_available.Contains(item))
                    throw new InvalidOperationException("Object has already been released.");
                if (!_checkedOut.Contains(item))
                    throw new InvalidOperationException("Object is not checked out.");
                item.SetActive(false);
                item.transform.SetParent(LiveParent());
                _checkedOut.Remove(item);
                PruneDestroyed(_available);
                if (_available.Count < _maxRetained) Retain(item);
                else
                {
                    _owned.Remove(item);
                    UnityEngine.Object.Destroy(item);
                }
            }
        }
        public void Clear()
        {
            lock (_poolLock)
            {
                ThrowIfDisposed();
                DestroyAvailable();
            }
        }
        public void Dispose()
        {
            lock (_poolLock)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (GameObject item in _owned)
                    if (item != null) UnityEngine.Object.Destroy(item);
                _availableStack.Clear();
                _deadScratch.Clear();
                _owned.Clear();
                _available.Clear();
                _checkedOut.Clear();
            }
        }
        private GameObject CreateInactive()
        {
            GameObject item = UnityEngine.Object.Instantiate(_prefab, LiveParent());
            item.SetActive(false);
            _owned.Add(item);
            return item;
        }

        private GameObject Checkout(GameObject item)
        {
            _checkedOut.Add(item);
            try { item.SetActive(true); return item; }
            catch { ForgetAndDestroy(item); throw; }
        }

        private void Retain(GameObject item)
        {
            _available.Add(item);
            _availableStack.Push(item);
        }

        private void Forget(GameObject item)
        {
            _available.Remove(item);
            _checkedOut.Remove(item);
            _owned.Remove(item);
        }

        private void ForgetAndDestroy(GameObject item)
        {
            Forget(item);
            if (item != null) UnityEngine.Object.Destroy(item);
        }

        private void DestroyAvailable()
        {
            while (_availableStack.Count > 0)
            {
                GameObject item = _availableStack.Pop();
                _available.Remove(item);
                _owned.Remove(item);
                if (item != null) UnityEngine.Object.Destroy(item);
            }
        }

        private void PruneDestroyed(HashSet<GameObject> tracked)
        {
            _deadScratch.Clear();
            foreach (GameObject item in tracked)
                if (item == null) _deadScratch.Add(item);
            for (int index = 0; index < _deadScratch.Count; index++)
                Forget(_deadScratch[index]);
            _deadScratch.Clear();
        }

        private Transform LiveParent() => _parent == null ? null : _parent;

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }

        private static HashSet<GameObject> NewIdentitySet() =>
            new(ReferenceIdentityComparer<GameObject>.Instance);
    }
}
