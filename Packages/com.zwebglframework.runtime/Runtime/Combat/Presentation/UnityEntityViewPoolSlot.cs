using System;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Combat
{
    internal sealed class UnityEntityViewPoolSlot : IDisposable
    {
        private readonly GameObjectPool _pool;
        private readonly string _hierarchyName;
        private bool _disposed;

        internal UnityEntityViewPoolSlot(GameObject prefab, Transform parent,
            string hierarchyName, int maxRetained)
        {
            _hierarchyName = hierarchyName;
            _pool = new GameObjectPool(prefab, parent, 0, maxRetained);
        }

        internal PooledUnityEntityView Get(UnityEntityViewFactory owner)
        {
            GameObject instance = _pool.Get();
            PooledUnityEntityView view = instance.GetComponent<PooledUnityEntityView>();
            if (view == null)
            {
                instance.name = _hierarchyName;
                view = instance.AddComponent<PooledUnityEntityView>();
            }
            view.Checkout(owner, this);
            return view;
        }

        internal void Release(PooledUnityEntityView view)
        {
            if (_disposed || ReferenceEquals(view, null) || view == null) return;
            _pool.Release(view.gameObject);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pool.Dispose();
        }
    }
}
