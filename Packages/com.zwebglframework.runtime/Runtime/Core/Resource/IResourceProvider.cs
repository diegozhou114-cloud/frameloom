using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 资源加载抽象接口。当前使用 Resources；正式项目接入资源更新和分包时迁移至 YooAsset。
    /// TODO(YooAsset): 不在当前框架阶段接入 AssetBundle 或 Addressables。
    /// 所有异步方法返回 UniTask，Unity 主线程安全。
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
