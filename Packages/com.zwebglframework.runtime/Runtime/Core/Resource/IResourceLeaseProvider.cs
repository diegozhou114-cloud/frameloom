using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 支持独立资源借用的可选能力。每次成功调用转交一份独立引用；
    /// 失败或取消必须释放未转交的引用。提供者退休不能使已转交的借用失效。
    /// </summary>
    public interface IResourceLeaseProvider : IResourceProvider
    {
        /// <summary>在 Unity 主线程异步加载；调用方负责 Dispose 返回的借用。</summary>
        UniTask<ResourceLease<T>> AcquireAsync<T>(string path,
            CancellationToken token = default) where T : Object;
    }
}
