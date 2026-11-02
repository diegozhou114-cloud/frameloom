using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// Resources 资源加载器 — 基于 Unity Resources API。
    /// 路径相对于 Resources 目录，不带扩展名。
    /// 例：LoadAsync<GameObject>("UI/Panels/MainPanel")
    /// </summary>
    public class ResourcesProvider : IResourceLeaseProvider
    {
        // Unity 的未使用资源扫描会检查静态字段，不扫描普通执行栈。
        private static readonly Dictionary<Object, int> _activeLeases = new();

        public bool SupportsSyncLoad => true;

        public T Load<T>(string path) where T : Object
        {
            var asset = Resources.Load<T>(path);
            if (asset == null)
                Debug.LogWarning($"[ResourcesProvider] 资源不存在: Resources/{path}");
            return asset;
        }

        public UniTask<T> LoadAsync<T>(string path) where T : Object =>
            LoadAsync<T>(path, CancellationToken.None);

        /// <summary>可取消等待；Unity Resources 的底层请求本身不可中止。</summary>
        public async UniTask<T> LoadAsync<T>(string path, CancellationToken token)
            where T : Object
        {
            token.ThrowIfCancellationRequested();
            var request = Resources.LoadAsync<T>(path);
            await request.ToUniTask(cancellationToken: token);
            token.ThrowIfCancellationRequested();
            if (request.asset == null)
                Debug.LogWarning($"[ResourcesProvider] 资源不存在: Resources/{path}");
            return request.asset as T;
        }

        /// <summary>
        /// 持有资源强引用。Resources 由 Unity 回收未使用资源，释放借用不调用
        /// UnloadAsset，以免影响同资源的其他使用者。
        /// </summary>
        public async UniTask<ResourceLease<T>> AcquireAsync<T>(string path,
            CancellationToken token = default) where T : Object
        {
            T asset = await LoadAsync<T>(path, token);
            if (asset == null)
                throw new KeyNotFoundException($"Resources/{path} ({typeof(T).Name})");
            _activeLeases[asset] = _activeLeases.TryGetValue(asset, out int count) ? count + 1 : 1;
            return new ResourceLease<T>(asset, () => ReleaseLease(asset));
        }

        private static void ReleaseLease(Object asset)
        {
            int remaining = _activeLeases[asset] - 1;
            if (remaining == 0) _activeLeases.Remove(asset);
            else _activeLeases[asset] = remaining;
        }

        /// <summary>
        /// [无操作] Resources.UnloadAsset 不支持按路径卸载单个资源；
        /// 调用方应使用 <see cref="UnloadUnused"/> 批量释放未使用的资源。
        /// </summary>
        public void Unload(string path)
        {
            // Resources 模式无单资源卸载，调用方应使用 UnloadUnused
        }

        public void UnloadUnused()
        {
            Resources.UnloadUnusedAssets();
        }
    }
}
