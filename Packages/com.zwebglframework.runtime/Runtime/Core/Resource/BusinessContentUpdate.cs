using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ZWebGLFrameWork.Core
{
    public enum BusinessContentUpdateStatus
    {
        NotConfigured,
        NoUpdate,
        Updated,
        Failed,
    }

    /// <summary>业务内容更新结果。只描述客户端结果，不包含传输或服务端协议。</summary>
    public sealed class BusinessContentUpdateResult
    {
        public BusinessContentUpdateStatus Status { get; }
        public string Message { get; }
        public IResourceProvider BusinessProvider { get; }

        private BusinessContentUpdateResult(BusinessContentUpdateStatus status, string message,
            IResourceProvider businessProvider)
        {
            Status = status;
            Message = message;
            BusinessProvider = businessProvider;
        }

        public static BusinessContentUpdateResult NotConfigured() =>
            new(BusinessContentUpdateStatus.NotConfigured, "业务热更未配置，使用本地业务内容。", null);

        public static BusinessContentUpdateResult NoUpdate(string message = null) =>
            new(BusinessContentUpdateStatus.NoUpdate, message, null);

        public static BusinessContentUpdateResult Updated(IResourceProvider businessProvider,
            string message = null) =>
            new(BusinessContentUpdateStatus.Updated, message, businessProvider);

        public static BusinessContentUpdateResult Failed(string message) =>
            new(BusinessContentUpdateStatus.Failed,
                string.IsNullOrWhiteSpace(message) ? "业务内容更新失败。" : message, null);
    }

    /// <summary>
    /// 业务热更集成边界。TODO(YooAsset): 接入资源分包；TODO(Huatuo): 接入业务热更代码。
    /// </summary>
    public interface IBusinessContentUpdater
    {
        UniTask<BusinessContentUpdateResult> PrepareAsync(CancellationToken token);
    }

    internal sealed class UnconfiguredBusinessContentUpdater : IBusinessContentUpdater
    {
        public UniTask<BusinessContentUpdateResult> PrepareAsync(CancellationToken token) =>
            UniTask.FromResult(BusinessContentUpdateResult.NotConfigured());
    }
}
