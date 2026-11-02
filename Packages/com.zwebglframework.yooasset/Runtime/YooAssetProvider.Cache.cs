using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ZWebGLFrameWork.Core;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.YooAsset
{
    public sealed partial class YooAssetProvider
    {
        private sealed class CachedResource
        {
            public Object Asset;
            public IDisposable Lease;
            public int PendingLoads;
        }

        private readonly Dictionary<(Type type, string path), CachedResource> _cache = new();

        /// <summary>
        /// 兼容旧加载入口：缓存一个自有租约，直到 Unload、UnloadUnused 或 Dispose。
        /// </summary>
        public async UniTask<T> LoadAsync<T>(string path) where T : Object
        {
            ThrowIfDisposed();
            ValidatePath(path);
            var key = (typeof(T), path);
            if (!_cache.TryGetValue(key, out var entry))
                _cache.Add(key, entry = new CachedResource());
            if (entry.Asset != null) return entry.Asset as T;

            entry.PendingLoads++;
            ResourceLease<T> lease = null;
            try
            {
                lease = await AcquireAsync<T>(path);
                if (!_cache.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
                    throw new OperationCanceledException("The cached load was unloaded before completion.");
                if (entry.Asset == null)
                {
                    entry.Lease?.Dispose();
                    entry.Asset = lease.Asset;
                    entry.Lease = lease;
                    lease = null;
                }
                return entry.Asset as T;
            }
            finally
            {
                lease?.Dispose();
                entry.PendingLoads--;
                if (entry.PendingLoads == 0 && entry.Lease == null &&
                    _cache.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _cache.Remove(key);
            }
        }

        /// <summary>释放此路径的全部旧接口缓存；独立租约不受影响。</summary>
        public void Unload(string path)
        {
            ThrowIfDisposed();
            ValidatePath(path);
            var keys = new List<(Type type, string path)>();
            foreach (var key in _cache.Keys)
                if (key.path == path) keys.Add(key);
            foreach (var key in keys)
            {
                var entry = _cache[key];
                _cache.Remove(key);
                entry.Lease?.Dispose();
            }
        }

        /// <summary>清理旧接口缓存，并请求 YooAsset 回收没有任何句柄引用的资源。</summary>
        public void UnloadUnused()
        {
            ThrowIfDisposed();
            ClearCache();
            _package.UnloadUnusedAssetsAsync();
        }

        /// <summary>退休适配器；已发出的独立租约继续有效，资源包保持存活。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearCache();
        }

        private void ClearCache()
        {
            var entries = new List<CachedResource>(_cache.Values);
            _cache.Clear();
            foreach (var entry in entries) entry.Lease?.Dispose();
        }
    }
}
