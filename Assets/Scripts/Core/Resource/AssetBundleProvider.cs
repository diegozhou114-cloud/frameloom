using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

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
        private readonly Dictionary<string, int> _bundleRefCounts = new();
        private readonly Dictionary<string, string[]> _retainedDependencies = new();
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
        public virtual async UniTask<AssetBundle> LoadBundleAsync(string bundleName)
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
        public virtual async UniTask LoadDependenciesAsync(string bundleName)
        {
            foreach (string dependency in GetDependencies(bundleName))
                await LoadBundleAsync(dependency);
        }

        protected virtual async UniTask<AssetBundle> EnsureBundleReadyAsync(string bundleName)
        {
            await LoadDependenciesAsync(bundleName);
            return await LoadBundleAsync(bundleName);
        }

        protected virtual IReadOnlyList<string> GetDependencies(string bundleName)
        {
            if (_manifest == null) return Array.Empty<string>();
            string[] dependencies = _manifest.GetAllDependencies(bundleName);
            Array.Sort(dependencies, StringComparer.Ordinal);
            return dependencies;
        }

        protected void RetainBundleReferences(string bundleName)
        {
            lock (_bundleLock)
            {
                if (!_retainedDependencies.TryGetValue(bundleName, out string[] dependencies))
                {
                    dependencies = GetDependencies(bundleName)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    _retainedDependencies[bundleName] = dependencies;
                }

                IncrementReference(bundleName);
                foreach (string dependency in dependencies)
                    IncrementReference(dependency);
            }
        }

        public async UniTask<T> LoadAsync<T>(string path) where T : Object
        {
            // path 格式: "bundleName/assetName"
            var parts = path.Split('/', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
                string.IsNullOrWhiteSpace(parts[1]))
            {
                Debug.LogError($"[AssetBundleProvider] 路径格式错误: {path}，应为 bundleName/assetName");
                return null;
            }

            var bundleName = parts[0];
            var assetName = parts[1];

            AssetBundle bundle = await EnsureBundleReadyAsync(bundleName);
            if (bundle == null) return null;
            RetainBundleReferences(bundleName);

            try
            {
                AssetBundleRequest request = bundle.LoadAssetAsync<T>(assetName);
                await request.ToUniTask();
                T asset = request.asset as T;
                if (asset != null) return asset;

                ReleaseBundleReferences(bundleName);
                return null;
            }
            catch
            {
                ReleaseBundleReferences(bundleName);
                throw;
            }
        }

        public T Load<T>(string path) where T : Object
        {
            Debug.LogError("[AssetBundleProvider] 不支持同步加载，请使用 LoadAsync");
            return null;
        }

        public void Unload(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return;
            int separator = assetPath.IndexOf('/');
            string bundleName = separator < 0
                ? assetPath
                : assetPath.Substring(0, separator);
            ReleaseBundleReferences(bundleName);
        }

        protected void ReleaseBundleReferences(string bundleName)
        {
            lock (_bundleLock)
            {
                if (!_bundleRefCounts.ContainsKey(bundleName))
                {
                    UnloadBundle(bundleName);
                    return;
                }

                string[] dependencies = _retainedDependencies.TryGetValue(
                    bundleName, out string[] retained)
                    ? retained
                    : Array.Empty<string>();
                DecrementAndUnload(bundleName);
                foreach (string dependency in dependencies)
                    DecrementAndUnload(dependency);
                if (!_bundleRefCounts.ContainsKey(bundleName))
                    _retainedDependencies.Remove(bundleName);
            }
        }

        private void IncrementReference(string bundleName)
        {
            _bundleRefCounts[bundleName] = _bundleRefCounts.TryGetValue(
                bundleName, out int count)
                ? checked(count + 1)
                : 1;
        }

        private void DecrementAndUnload(string bundleName)
        {
            int count = _bundleRefCounts[bundleName] - 1;
            if (count > 0)
            {
                _bundleRefCounts[bundleName] = count;
                return;
            }

            _bundleRefCounts.Remove(bundleName);
            UnloadBundle(bundleName);
        }

        protected virtual void UnloadBundle(string bundleName)
        {
            if (!_loadedBundles.TryGetValue(bundleName, out AssetBundle bundle)) return;
            bundle.Unload(false);
            _loadedBundles.Remove(bundleName);
        }

        public void UnloadUnused()
        {
            lock (_bundleLock)
            {
                foreach (var bundle in _loadedBundles.Values)
                    bundle.Unload(false);
                _loadedBundles.Clear();
                _bundleRefCounts.Clear();
                _retainedDependencies.Clear();
                _manifest = null;
            }
        }
    }
}
