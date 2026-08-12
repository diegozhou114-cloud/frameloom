using System;
using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.SimulationCore;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Combat
{
    public sealed class UnityEntityViewFactory : IEntityViewFactory, IDisposable
    {
        private const int MaxRetainedPerArchetype = 64;
        private readonly Dictionary<int, UnityEntityViewPoolSlot> _configuredPools =
            new Dictionary<int, UnityEntityViewPoolSlot>();
        private readonly ViewConfig _viewConfig;
        private UnityEntityViewPoolSlot _fallbackPool;
        private GameObject _fallbackTemplate;
        private GameObject _poolRoot;
        private bool _disposed;

        public UnityEntityViewFactory(ViewConfig viewConfig)
        {
            _viewConfig = viewConfig;
        }

        public IEntityView Create(EntityPresentationKey key, int archetypeId)
        {
            ThrowIfDisposed();
            return GetPool(archetypeId).Get(this);
        }

        public void Destroy(IEntityView view)
        {
            if (!(view is PooledUnityEntityView pooled) ||
                !ReferenceEquals(pooled.Owner, this)) return;
            if (_disposed)
            {
                pooled.Invalidate(this);
                return;
            }
            if (pooled.TryReturn(this, out UnityEntityViewPoolSlot pool))
                pool.Release(pooled);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (UnityEntityViewPoolSlot pool in _configuredPools.Values)
                pool.Dispose();
            _configuredPools.Clear();
            _fallbackPool?.Dispose();
            _fallbackPool = null;
            if (_fallbackTemplate != null) Object.Destroy(_fallbackTemplate);
            _fallbackTemplate = null;
            if (_poolRoot != null) Object.Destroy(_poolRoot);
            _poolRoot = null;
        }

        private UnityEntityViewPoolSlot GetPool(int archetypeId)
        {
            if (_configuredPools.TryGetValue(archetypeId, out var pool)) return pool;
            GameObject prefab = FindConfiguredPrefab(archetypeId);
            if (prefab == null) return GetFallbackPool();
            pool = new UnityEntityViewPoolSlot(prefab, PoolRoot,
                "[CombatView:" + archetypeId + "]", MaxRetainedPerArchetype);
            _configuredPools.Add(archetypeId, pool);
            return pool;
        }

        private UnityEntityViewPoolSlot GetFallbackPool()
        {
            if (_fallbackPool != null) return _fallbackPool;
            _fallbackTemplate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _fallbackTemplate.name = "[CombatView:FallbackTemplate]";
            _fallbackTemplate.transform.SetParent(PoolRoot, false);
            _fallbackTemplate.SetActive(false);
            _fallbackPool = new UnityEntityViewPoolSlot(_fallbackTemplate,
                PoolRoot, "[CombatView:Fallback]", MaxRetainedPerArchetype);
            return _fallbackPool;
        }

        private GameObject FindConfiguredPrefab(int archetypeId)
        {
            ViewConfig.ArchetypeEntry[] entries = _viewConfig?.Entries;
            if (entries == null) return null;
            for (int index = 0; index < entries.Length; index++)
                if (entries[index].ArchetypeId == archetypeId)
                    return entries[index].Prefab;
            return null;
        }

        private Transform PoolRoot
        {
            get
            {
                if (_poolRoot != null) return _poolRoot.transform;
                _poolRoot = new GameObject("[CombatViewPool]");
                Transform transform = _poolRoot.transform;
                transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                transform.localScale = Vector3.one;
                Object.DontDestroyOnLoad(_poolRoot);
                return transform;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }
    }
}
