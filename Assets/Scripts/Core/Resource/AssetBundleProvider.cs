using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// AssetBundle 资源加载器。
    /// 需要先加载 Manifest 和依赖 Bundle，再加载资源。
    /// </summary>
    public class AssetBundleProvider : IResourceProvider
    {
        public bool SupportsSyncLoad => false;

        private readonly Dictionary<string, AssetBundle> _loadedBundles = new();
        private readonly object _bundleLock = new object();
        private AssetBundleManifest _manifest;

        /// <summary>加载 Manifest（必须最先调用）</summary>
        public async UniTask LoadManifestAsync(string manifestPath)
        {
            var bundle = await LoadBundleAsync(manifestPath);
            if (bundle != null)
                _manifest = bundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }

        /// <summary>异步加载 Bundle</summary>
        public async UniTask<AssetBundle> LoadBundleAsync(string bundleName)
        {
            lock (_bundleLock)
            {
                if (_loadedBundles.TryGetValue(bundleName, out var cached))
                    return cached;
            }

            var request = AssetBundle.LoadFromFileAsync(
                System.IO.Path.Combine(Application.streamingAssetsPath, bundleName));
            await request.ToUniTask();

            var bundle = request.assetBundle;
            if (bundle != null)
            {
                lock (_bundleLock)
                {
                    _loadedBundles[bundleName] = bundle;
                }
            }
            return bundle;
        }

        /// <summary>加载 Bundle 的所有依赖</summary>
        public async UniTask LoadDependenciesAsync(string bundleName)
        {
            if (_manifest == null)
            {
                Debug.LogError("[AssetBundleProvider] Manifest 未加载");
                return;
            }

            var deps = _manifest.GetAllDependencies(bundleName);
            foreach (var dep in deps)
                await LoadBundleAsync(dep);
        }

        public async UniTask<T> LoadAsync<T>(string path) where T : Object
        {
            // path 格式: "bundleName/assetName"
            var parts = path.Split('/', 2);
            if (parts.Length != 2)
            {
                Debug.LogError($"[AssetBundleProvider] 路径格式错误: {path}，应为 bundleName/assetName");
                return null;
            }

            var bundleName = parts[0];
            var assetName = parts[1];

            var bundle = await LoadBundleAsync(bundleName);
            if (bundle == null) return null;

            var request = bundle.LoadAssetAsync<T>(assetName);
            await request.ToUniTask();
            return request.asset as T;
        }

        public T Load<T>(string path) where T : Object
        {
            Debug.LogError("[AssetBundleProvider] 不支持同步加载，请使用 LoadAsync");
            return null;
        }

        public void Unload(string bundleName)
        {
            lock (_bundleLock)
            {
                if (_loadedBundles.TryGetValue(bundleName, out var bundle))
                {
                    bundle.Unload(false);
                    _loadedBundles.Remove(bundleName);
                }
            }
        }

        public void UnloadUnused()
        {
            lock (_bundleLock)
            {
                foreach (var bundle in _loadedBundles.Values)
                    bundle.Unload(false);
                _loadedBundles.Clear();
                _manifest = null;
            }
        }
    }
}
