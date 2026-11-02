using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 资源管理器 — 跨场景单例，统一资源加载入口。
    /// 框架资源固定本地加载，业务资源可由后续内容更新系统替换。
    /// </summary>
    public partial class ResourceManager : SingletonDontDestroy<ResourceManager>
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
        /// YooAsset 通过独立适配包安装到业务层，框架层始终使用本地资源。
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

        /// <summary>
        /// 接管业务提供者，替换时释放旧提供者的兼容缓存及 IDisposable 资源。
        /// 提供者须保证已交给调用方的 ResourceLease 仍有效，直到调用方释放。
        /// </summary>
        public void SetBusinessProvider(IResourceProvider newProvider)
        {
            if (newProvider == null)
                throw new ArgumentNullException(nameof(newProvider));
            if (_resourcesDisposed) throw new ObjectDisposedException(nameof(ResourceManager));

            IResourceProvider previousProvider;
            lock (_cacheLock)
            {
                if (ReferenceEquals(_businessProvider, newProvider)) return;
                _businessCache.Clear();
                _businessProviderVersion++;
                previousProvider = _businessProvider;
                _businessProvider = newProvider;
            }

            IsReady = true;
            RetireProvider(previousProvider);
        }

        /// <summary>配置业务内容更新器。更新器无法访问框架资源加载器。</summary>
        public void SetBusinessContentUpdater(IBusinessContentUpdater updater)
        {
            if (_resourcesDisposed) throw new ObjectDisposedException(nameof(ResourceManager));
            _businessContentUpdater = updater ?? throw new ArgumentNullException(nameof(updater));
            _businessUpdateVersion++;
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

    }
}
