using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 兼容资源加载接口。Resources 为默认实现，YooAsset 由独立适配包提供。
    /// 这里返回的裸资源由提供者缓存管理；需要独立释放或取消时使用
    /// IResourceLeaseProvider。所有 Unity 操作均在主线程执行。
    /// </summary>
    public interface IResourceProvider
    {
        /// <summary>异步加载资源</summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="path">资源路径（格式取决于实现）</param>
        UniTask<T> LoadAsync<T>(string path) where T : Object;

        /// <summary>同步加载资源（仅 Resources 模式支持）</summary>
        T Load<T>(string path) where T : Object;

        /// <summary>卸载资源</summary>
        void Unload(string path);

        /// <summary>卸载所有未使用资源</summary>
        void UnloadUnused();

        /// <summary>是否支持同步加载</summary>
        bool SupportsSyncLoad { get; }
    }
}
