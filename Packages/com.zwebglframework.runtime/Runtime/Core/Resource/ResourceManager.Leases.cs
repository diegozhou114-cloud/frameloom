using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ZWebGLFrameWork.Core
{
    public partial class ResourceManager
    {
        private bool _resourcesDisposed;

        /// <summary>借用本地框架资源；释放借用不影响其他使用者。</summary>
        public UniTask<ResourceLease<T>> AcquireAsync<T>(string path,
            CancellationToken token = default) where T : UnityEngine.Object =>
            AcquireAsync<T>(ResourceLayer.Framework, path, token);

        /// <summary>
        /// 获取独立资源借用，绕过裸资源缓存。提供者不支持借用时明确失败；
        /// 取消、管理器销毁或业务提供者在等待期间变更时释放迟到结果。
        /// </summary>
        public async UniTask<ResourceLease<T>> AcquireAsync<T>(ResourceLayer layer,
            string path, CancellationToken token = default) where T : UnityEngine.Object
        {
            token.ThrowIfCancellationRequested();
            if (_resourcesDisposed) throw new ObjectDisposedException(nameof(ResourceManager));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("资源路径不能为空。", nameof(path));
            IResourceProvider provider = GetProvider(layer);
            int version = GetProviderVersion(layer);
            if (!(provider is IResourceLeaseProvider leaseProvider))
                throw new NotSupportedException($"{provider.GetType().Name} 不支持独立资源借用。");

            ResourceLease<T> lease = await leaseProvider.AcquireAsync<T>(path, token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (_resourcesDisposed) throw new ObjectDisposedException(nameof(ResourceManager));
                if (!ReferenceEquals(GetProvider(layer), provider) || GetProviderVersion(layer) != version)
                    throw new InvalidOperationException("资源提供者在加载期间已替换，请重新加载。");
                if (lease == null || lease.IsDisposed || lease.Asset == null)
                    throw new InvalidOperationException("资源提供者未返回有效借用。");
                return lease;
            }
            catch
            {
                DisposeSafely(lease);
                throw;
            }
        }

        private static void RetireProvider(IResourceProvider provider)
        {
            try { provider?.UnloadUnused(); }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
            DisposeSafely(provider as IDisposable);
        }

        private static void DisposeSafely(IDisposable resource)
        {
            try { resource?.Dispose(); }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
        }

        protected override void OnDestroy()
        {
            try
            {
                _resourcesDisposed = true;
                IsReady = false;
                _frameworkCache.Clear();
                _businessCache.Clear();
                DisposeSafely(_businessProvider as IDisposable);
            }
            finally { base.OnDestroy(); }
        }
    }
}
