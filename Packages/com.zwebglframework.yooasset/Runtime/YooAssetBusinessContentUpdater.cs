using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ZWebGLFrameWork.Core;
using global::YooAsset;

namespace ZWebGLFrameWork.YooAsset
{
    /// <summary>
    /// 准备业务清单。调用方预先配置并初始化包；本类不初始化 YooAssets 或下载全部资源。
    /// </summary>
    public sealed class YooAssetBusinessContentUpdater : IBusinessContentUpdater
    {
        private readonly ResourcePackage _package;
        private AsyncOperationBase _runningOperation;
        private bool _preparing;

        /// <summary>注入已初始化包；允许尚未激活资源清单。</summary>
        public YooAssetBusinessContentUpdater(ResourcePackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            if (package.InitializeStatus != EOperationStatus.Succeed)
                throw new ArgumentException("The resource package must be initialized.", nameof(package));
        }

        /// <summary>请求版本并更新清单；只有成功后返回业务 provider，取消原样传播。</summary>
        public async UniTask<BusinessContentUpdateResult> PrepareAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_preparing || (_runningOperation != null && !_runningOperation.IsDone))
                return BusinessContentUpdateResult.Failed("A YooAsset preparation operation is still running.");
            _preparing = true;
            try
            {
                var version = _package.RequestPackageVersionAsync();
                _runningOperation = version;
                await UniTask.WaitUntil(() => version.IsDone, cancellationToken: token);
                token.ThrowIfCancellationRequested();
                if (version.Status != EOperationStatus.Succeed)
                    return BusinessContentUpdateResult.Failed(version.Error);

                var manifest = _package.UpdatePackageManifestAsync(version.PackageVersion);
                _runningOperation = manifest;
                await UniTask.WaitUntil(() => manifest.IsDone, cancellationToken: token);
                token.ThrowIfCancellationRequested();
                if (manifest.Status != EOperationStatus.Succeed)
                    return BusinessContentUpdateResult.Failed(manifest.Error);

                return BusinessContentUpdateResult.Updated(new YooAssetProvider(_package));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return BusinessContentUpdateResult.Failed(ex.Message);
            }
            finally
            {
                _preparing = false;
            }
        }
    }
}
