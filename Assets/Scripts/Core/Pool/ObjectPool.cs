using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    // ============================================================
    // 泛型对象池 — 适用于普通 C# 对象
    // ============================================================

    /// <summary>
    /// 通用对象池，适用于 C# 对象（非 MonoBehaviour）。
    /// 用法：var pool = new ObjectPool&lt;MyClass&gt;(factory, resetAction, initialCapacity);
    /// </summary>
    public class ObjectPool<T> where T : class
    {
        private readonly Stack<T> _pool;
        private readonly object _poolLock = new object();
        private readonly Func<T> _createFunc;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;

        public int Count => _pool.Count;

        /// <param name="createFunc">创建新实例的工厂方法</param>
        /// <param name="onGet">从池中取出时的回调</param>
        /// <param name="onRelease">放回池中时的回调</param>
        /// <param name="onDestroy">销毁时的回调</param>
        /// <param name="preload">预热数量</param>
        public ObjectPool(
            Func<T> createFunc,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            Action<T> onDestroy = null,
            int preload = 0)
        {
            _createFunc = createFunc ?? throw new ArgumentNullException(nameof(createFunc));
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            _pool = new Stack<T>(preload > 0 ? preload : 4);

            for (int i = 0; i < preload; i++)
            {
                _pool.Push(_createFunc());
            }
        }

        /// <summary>从池中获取一个实例（池空时自动创建）</summary>
        public T Get()
        {
            lock (_poolLock)
            {
                T obj = _pool.Count > 0 ? _pool.Pop() : _createFunc();
                _onGet?.Invoke(obj);
                return obj;
            }
        }

        /// <summary>将实例放回池中</summary>
        public void Release(T obj)
        {
            if (obj == null) return;
            lock (_poolLock)
            {
                _onRelease?.Invoke(obj);
                _pool.Push(obj);
            }
        }

        /// <summary>清空池并销毁所有实例</summary>
        public void Clear()
        {
            lock (_poolLock)
            {
                while (_pool.Count > 0)
                {
                    _onDestroy?.Invoke(_pool.Pop());
                }
            }
        }
    }

    // ============================================================
    // GameObject 对象池 — 适用于 Unity Prefab 实例
    // ============================================================

    /// <summary>
    /// GameObject 专用对象池，管理 Prefab 实例的复用。
    /// </summary>
    public class GameObjectPool
    {
        private readonly Stack<GameObject> _pool = new Stack<GameObject>();
        private readonly object _poolLock = new object();
        private readonly GameObject _prefab;
        private readonly Transform _parent;

        public int Count => _pool.Count;

        /// <param name="prefab">预制体</param>
        /// <param name="parent">池中对象的父节点</param>
        /// <param name="preload">预热数量</param>
        public GameObjectPool(GameObject prefab, Transform parent = null, int preload = 0)
        {
            _prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            _parent = parent;

            for (int i = 0; i < preload; i++)
            {
                var obj = UnityEngine.Object.Instantiate(_prefab, _parent);
                obj.SetActive(false);
                _pool.Push(obj);
            }
        }

        /// <summary>从池中取出（激活状态），池空时自动 Instantiate</summary>
        public GameObject Get()
        {
            lock (_poolLock)
            {
                GameObject obj;
                if (_pool.Count > 0)
                {
                    obj = _pool.Pop();
                }
                else
                {
                    obj = UnityEngine.Object.Instantiate(_prefab, _parent);
                }

                obj.SetActive(true);
                return obj;
            }
        }

        /// <summary>从池中取出并设置位置/旋转/父节点</summary>
        public GameObject Get(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var obj = Get();
            obj.transform.SetParent(parent ?? _parent);
            obj.transform.SetPositionAndRotation(position, rotation);
            return obj;
        }

        /// <summary>
        /// 放回池中（失活状态）。
        /// 注意：出于性能考虑，此方法不验证传入的 GameObject 是否源自本池；
        /// 调用方需确保只归还从本池 Get() 取出的对象，否则可能导致父节点错乱或重复入池。
        /// </summary>
        public void Release(GameObject obj)
        {
            if (obj == null) return;

            lock (_poolLock)
            {
                obj.SetActive(false);
                obj.transform.SetParent(_parent);
                _pool.Push(obj);
            }
        }

        /// <summary>清空池并销毁所有对象</summary>
        public void Clear()
        {
            lock (_poolLock)
            {
                while (_pool.Count > 0)
                {
                    var obj = _pool.Pop();
                    if (obj != null)
                    {
                        UnityEngine.Object.Destroy(obj);
                    }
                }
            }
        }
    }
}
