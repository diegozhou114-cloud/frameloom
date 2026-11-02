using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ZWebGLFrameWork.Core;
using global::YooAsset;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.YooAsset
{
    /// <summary>
    /// 主线程异步资源适配器。租约各自拥有独立句柄；资源包生命周期由调用方管理。
    /// </summary>
    public sealed partial class YooAssetProvider : IResourceLeaseProvider, IDisposable
    {
        private readonly ResourcePackage _package;
        private bool _disposed;

        /// <summary>接入已经初始化且已加载资源清单的包，不初始化或接管全局 YooAssets。</summary>
        public YooAssetProvider(ResourcePackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            if (package.InitializeStatus != EOperationStatus.Succeed || !package.PackageValid)
                throw new ArgumentException("The resource package must have an initialized manifest.", nameof(package));
        }

        /// <summary>仅提供异步加载，避免 WebGL 主线程同步等待。</summary>
        public bool SupportsSyncLoad => false;

        /// <summary>加载独立租约；失败、取消或适配器退休均释放本次句柄。</summary>
        public async UniTask<ResourceLease<T>> AcquireAsync<T>(
            string path, CancellationToken token = default) where T : Object
        {
            ThrowIfDisposed();
            ValidatePath(path);
            token.ThrowIfCancellationRequested();

            AssetHandle handle = _package.LoadAssetAsync<T>(path);
            try
            {
                await UniTask.WaitUntil(() => _disposed || handle.IsDone, cancellationToken: token);
                token.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                if (handle.Status != EOperationStatus.Succeed)
                    throw new InvalidOperationException($"YooAsset failed to load '{path}': {handle.LastError}");
                T asset = handle.AssetObject as T;
                if (asset == null)
                    throw new InvalidOperationException($"YooAsset returned no {typeof(T).Name} for '{path}'.");

                var lease = new ResourceLease<T>(asset, handle.Dispose);
                handle = null;
                return lease;
            }
            finally
            {
                handle?.Dispose();
            }
        }

        /// <summary>同步加载不受支持；请使用 AcquireAsync 或 LoadAsync。</summary>
        public T Load<T>(string path) where T : Object =>
            throw new NotSupportedException("YooAssetProvider only supports asynchronous loading.");

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(YooAssetProvider));
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("An asset location is required.", nameof(path));
        }
    }
}
