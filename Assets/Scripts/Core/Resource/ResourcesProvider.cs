using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// Resources 资源加载器 — 基于 Unity Resources API。
    /// 路径相对于 Resources 目录，不带扩展名。
    /// 例：LoadAsync<GameObject>("UI/Panels/MainPanel")
    /// </summary>
    public class ResourcesProvider : IResourceProvider
    {
        public bool SupportsSyncLoad => true;

        public T Load<T>(string path) where T : Object
        {
            var asset = Resources.Load<T>(path);
            if (asset == null)
                Debug.LogWarning($"[ResourcesProvider] 资源不存在: Resources/{path}");
            return asset;
        }

        public async UniTask<T> LoadAsync<T>(string path) where T : Object
        {
            var request = Resources.LoadAsync<T>(path);
            await request.ToUniTask();
            if (request.asset == null)
                Debug.LogWarning($"[ResourcesProvider] 资源不存在: Resources/{path}");
            return request.asset as T;
        }

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
