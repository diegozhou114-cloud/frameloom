using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Core
{
    public partial class ResourceManager
    {
        private readonly Dictionary<string, WeakReference<UnityEngine.Object>> _frameworkCache = new();
        private readonly Dictionary<string, WeakReference<UnityEngine.Object>> _businessCache = new();
        private readonly object _cacheLock = new object();
        private int _businessProviderVersion;

        private bool TryGetCache<T>(ResourceLayer layer, string key, out T asset)
            where T : UnityEngine.Object
        {
            lock (_cacheLock)
            {
                Dictionary<string, WeakReference<UnityEngine.Object>> cache = GetCache(layer);
                if (cache.TryGetValue(key, out var weakRef) && weakRef.TryGetTarget(out var obj))
                {
                    if (obj is T t && t != null)
                    {
                        asset = t;
                        return true;
                    }
                    cache.Remove(key);
                }
                asset = null;
                return false;
            }
        }

        private void SetCacheIfCurrent<T>(ResourceLayer layer, string key, T asset,
            IResourceProvider provider, int providerVersion) where T : UnityEngine.Object
        {
            lock (_cacheLock)
            {
                if (!ReferenceEquals(GetProvider(layer), provider) ||
                    GetProviderVersion(layer) != providerVersion)
                    return;
                GetCache(layer)[key] = new WeakReference<UnityEngine.Object>(asset);
            }
        }

        private IResourceProvider GetProvider(ResourceLayer layer) =>
            layer == ResourceLayer.Framework ? FrameworkProvider : BusinessProvider;

        private int GetProviderVersion(ResourceLayer layer) =>
            layer == ResourceLayer.Business ? _businessProviderVersion : 0;

        private Dictionary<string, WeakReference<UnityEngine.Object>> GetCache(ResourceLayer layer) =>
            layer == ResourceLayer.Framework ? _frameworkCache : _businessCache;
    }
}
