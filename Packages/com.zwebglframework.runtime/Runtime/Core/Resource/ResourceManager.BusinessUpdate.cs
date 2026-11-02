using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ZWebGLFrameWork.Core
{
    public partial class ResourceManager
    {
        private int _businessUpdateVersion;

        /// <summary>准备业务内容；仅当前、未取消的成功结果可以安装提供者。</summary>
        public async UniTask<BusinessContentUpdateResult> PrepareBusinessContentAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_resourcesDisposed) throw new ObjectDisposedException(nameof(ResourceManager));
            int updateVersion = ++_businessUpdateVersion;
            int providerVersion = _businessProviderVersion;
            IBusinessContentUpdater updater = _businessContentUpdater ??=
                new UnconfiguredBusinessContentUpdater();
            BusinessContentUpdateResult result = await updater.PrepareAsync(token);
            bool installed = false;
            try
            {
                token.ThrowIfCancellationRequested();
                if (_resourcesDisposed) throw new ObjectDisposedException(nameof(ResourceManager));
                if (updateVersion != _businessUpdateVersion || providerVersion != _businessProviderVersion)
                    throw new InvalidOperationException("业务更新在准备期间已被替换。");
                if (result == null)
                    return BusinessContentUpdateResult.Failed("业务内容更新器未返回结果。");
                if (result.Status != BusinessContentUpdateStatus.Updated) return result;
                if (result.BusinessProvider == null)
                    return BusinessContentUpdateResult.Failed("业务内容更新成功但未提供资源加载器。");
                SetBusinessProvider(result.BusinessProvider);
                installed = true;
                return result;
            }
            finally
            {
                if (!installed && result?.BusinessProvider is IDisposable disposable &&
                    !ReferenceEquals(result.BusinessProvider, _businessProvider))
                    DisposeSafely(disposable);
            }
        }
    }
}
