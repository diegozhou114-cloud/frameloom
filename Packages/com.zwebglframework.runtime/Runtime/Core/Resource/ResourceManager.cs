using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 资源管理器 — 跨场景单例，统一资源加载入口。
    /// 框架资源固定本地加载，业务资源可由后续内容更新系统替换。
    /// </summary>
    public class ResourceManager : SingletonDontDestroy<ResourceManager>
    {
        private IResourceProvider _frameworkProvider;
        private IResourceProvider _businessProvider;
        private IBusinessContentUpdater _businessContentUpdater;

        /// <summary>是否已初始化</summary>
        public bool IsReady { get; private set; }

        /// <summary>固定的框架资源加载器。没有公开替换入口。</summary>
        public IResourceProvider FrameworkProvider => _frameworkProvider ??= new ResourcesProvider();

        /// <summary>当前业务资源加载器。</summary>
        public IResourceProvider BusinessProvider => _businessProvider ??= new ResourcesProvider();

        /// <summary>
        /// 初始化资源管理器。
        /// 默认使用 ResourcesProvider。
        /// TODO(YooAsset): 正式项目接入资源更新与分包管理时，使用 YooAsset Provider 替换当前加载策略；
        /// 目前不构建或加载 AssetBundle。
        /// </summary>
        public bool Init()
        {
            try
            {
                _frameworkProvider ??= new ResourcesProvider();
                _businessProvider ??= new ResourcesProvider();
                _businessContentUpdater ??= new UnconfiguredBusinessContentUpdater();

                IsReady = true;
                Debug.Log("[ResourceManager] ✅ 初始化完成 — Framework: ResourcesProvider, Business: ResourcesProvider");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ResourceManager] ❌ 初始化失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>替换业务资源加载器（自动清理业务缓存）。</summary>
        public void SetBusinessProvider(IResourceProvider newProvider)
        {
            if (newProvider == null)
                throw new ArgumentNullException(nameof(newProvider));

            IResourceProvider previousProvider;
            lock (_cacheLock)
            {
                if (ReferenceEquals(_businessProvider, newProvider)) return;
                _businessCache.Clear();
                _businessProviderVersion++;
                previousProvider = _businessProvider;
                _businessProvider = newProvider;
            }

            previousProvider?.UnloadUnused();
            IsReady = true;
        }

        /// <summary>配置业务内容更新器。更新器无法访问框架资源加载器。</summary>
        public void SetBusinessContentUpdater(IBusinessContentUpdater updater)
        {
            _businessContentUpdater = updater ?? throw new ArgumentNullException(nameof(updater));
        }

        /// <summary>准备业务内容；仅成功结果可以替换业务资源加载器。</summary>
        public async UniTask<BusinessContentUpdateResult> PrepareBusinessContentAsync(
            CancellationToken token)
        {
            IBusinessContentUpdater updater = _businessContentUpdater ??=
                new UnconfiguredBusinessContentUpdater();
            BusinessContentUpdateResult result = await updater.PrepareAsync(token);
            if (result == null)
                return BusinessContentUpdateResult.Failed("业务内容更新器未返回结果。");

            if (result.Status != BusinessContentUpdateStatus.Updated)
                return result;

            if (result.BusinessProvider == null)
                return BusinessContentUpdateResult.Failed("业务内容更新成功但未提供资源加载器。");

            SetBusinessProvider(result.BusinessProvider);
            return result;
        }

        // ==================== 公开 API ====================

        /// <summary>异步加载资源（带缓存）</summary>
        public UniTask<T> LoadAsync<T>(string path) where T : UnityEngine.Object =>
            LoadAsync<T>(ResourceLayer.Framework, path);

        /// <summary>按资源层级异步加载资源（带独立缓存）。</summary>
        public async UniTask<T> LoadAsync<T>(ResourceLayer layer, string path)
            where T : UnityEngine.Object
        {
            if (TryGetCache<T>(layer, path, out var cached))
                return cached;

            IResourceProvider provider = GetProvider(layer);
            int providerVersion = GetProviderVersion(layer);
            var asset = await provider.LoadAsync<T>(path);
            if (asset != null)
                SetCacheIfCurrent(layer, path, asset, provider, providerVersion);
            return asset;
        }

        /// <summary>同步加载资源（仅 Resources 模式）</summary>
        public T Load<T>(string path) where T : UnityEngine.Object =>
            Load<T>(ResourceLayer.Framework, path);

        /// <summary>按资源层级同步加载资源。</summary>
        public T Load<T>(ResourceLayer layer, string path) where T : UnityEngine.Object
        {
            if (TryGetCache<T>(layer, path, out var cached))
                return cached;

            IResourceProvider provider = GetProvider(layer);
            int providerVersion = GetProviderVersion(layer);
            var asset = provider.Load<T>(path);
            if (asset != null)
                SetCacheIfCurrent(layer, path, asset, provider, providerVersion);
            return asset;
        }

        /// <summary>卸载指定路径的资源</summary>
        public void Unload(string path) => Unload(ResourceLayer.Framework, path);

        /// <summary>卸载指定层级中指定路径的资源。</summary>
        public void Unload(ResourceLayer layer, string path)
        {
            lock (_cacheLock)
            {
                GetCache(layer).Remove(path);
            }
            GetProvider(layer).Unload(path);
        }

        /// <summary>清空所有缓存并卸载未使用资源</summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _frameworkCache.Clear();
                _businessCache.Clear();
            }
            FrameworkProvider.UnloadUnused();
            if (!ReferenceEquals(FrameworkProvider, BusinessProvider))
                BusinessProvider.UnloadUnused();
        }

        // ==================== 缓存 ====================

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
                    if (obj is T t)
                    {
                        asset = t;
                        return true;
                    }
                    cache.Remove(key); // 类型不匹配，移除
                }
                asset = null;
                return false;
            }
        }

        private void SetCacheIfCurrent<T>(ResourceLayer layer, string key, T asset, IResourceProvider provider,
            int providerVersion) where T : UnityEngine.Object
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
