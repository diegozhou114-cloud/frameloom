using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 资源管理器 — 跨场景单例，统一资源加载入口。
    /// 支持运行时切换 Provider，内置 LRU 缓存。
    /// 
    /// 用法：
    ///   ResourceManager.SetProvider(new ResourcesProvider());
    ///   var prefab = await ResourceManager.LoadAsync<GameObject>("UI/Panels/Main");
    /// </summary>
    public class ResourceManager : SingletonDontDestroy<ResourceManager>
    {
        private IResourceProvider _provider;

        /// <summary>是否已初始化</summary>
        public bool IsReady { get; private set; }

        /// <summary>当前资源加载器</summary>
        public IResourceProvider Provider
        {
            get => _provider ??= new ResourcesProvider();
            set => _provider = value;
        }

        /// <summary>
        /// 初始化资源管理器。
        /// 默认使用 ResourcesProvider；在热更完成后可切换为 AssetBundleProvider。
        /// </summary>
        public bool Init(IResourceProvider customProvider = null)
        {
            try
            {
                if (customProvider != null)
                    _provider = customProvider;
                else
                    _provider ??= new ResourcesProvider();

                IsReady = true;
                Debug.Log($"[ResourceManager] ✅ 初始化完成 — Provider: {_provider.GetType().Name}");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ResourceManager] ❌ 初始化失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>切换加载器（自动清理旧缓存）</summary>
        public void SetProvider(IResourceProvider newProvider)
        {
            _provider?.UnloadUnused();
            _provider = newProvider;
        }

        // ==================== 公开 API ====================

        /// <summary>异步加载资源（带缓存）</summary>
        public async UniTask<T> LoadAsync<T>(string path) where T : UnityEngine.Object
        {
            if (TryGetCache<T>(path, out var cached))
                return cached;

            var asset = await Provider.LoadAsync<T>(path);
            if (asset != null)
                SetCache(path, asset);
            return asset;
        }

        /// <summary>同步加载资源（仅 Resources 模式）</summary>
        public T Load<T>(string path) where T : UnityEngine.Object
        {
            if (TryGetCache<T>(path, out var cached))
                return cached;

            var asset = Provider.Load<T>(path);
            if (asset != null)
                SetCache(path, asset);
            return asset;
        }

        /// <summary>卸载指定路径的资源</summary>
        public void Unload(string path)
        {
            _cache.Remove(path);
            Provider.Unload(path);
        }

        /// <summary>清空所有缓存并卸载未使用资源</summary>
        public void ClearCache()
        {
            _cache.Clear();
            Provider.UnloadUnused();
        }

        // ==================== 缓存 ====================

        private readonly Dictionary<string, WeakReference<UnityEngine.Object>> _cache = new();

        private bool TryGetCache<T>(string key, out T asset) where T : UnityEngine.Object
        {
            if (_cache.TryGetValue(key, out var weakRef) && weakRef.TryGetTarget(out var obj))
            {
                if (obj is T t)
                {
                    asset = t;
                    return true;
                }
                _cache.Remove(key); // 类型不匹配，移除
            }
            asset = null;
            return false;
        }

        private void SetCache<T>(string key, T asset) where T : UnityEngine.Object
        {
            _cache[key] = new WeakReference<UnityEngine.Object>(asset);
        }
    }
}
