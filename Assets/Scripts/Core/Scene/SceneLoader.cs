using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 场景加载器 — 跨场景单例，统一场景加载/卸载/过渡。
    /// 后续与 UIManager 的 Loading 面板联动，实现带进度的异步切换。
    ///
    /// 用法：
    ///   SceneLoader.Instance.Init();
    ///   await SceneLoader.Instance.LoadSceneAsync("Battle");
    /// </summary>
    public class SceneLoader : SingletonDontDestroy<SceneLoader>
    {
        /// <summary>是否已初始化</summary>
        public bool IsReady { get; private set; }

        /// <summary>当前正在加载的场景名（null 表示无加载任务）</summary>
        public string LoadingSceneName { get; private set; }

        /// <summary>加载进度（0~1）</summary>
        public float Progress { get; private set; }

        // ==================== 初始化 ====================

        /// <summary>
        /// 初始化场景加载器。
        /// TODO: 注册 SceneManager.sceneLoaded 事件，用于追踪场景切换。
        /// </summary>
        public void Init()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            IsReady = true;
            Debug.Log("[SceneLoader] ✅ 初始化完成");
        }

        // ==================== 公开 API ====================

        /// <summary>异步加载场景（叠加模式）</summary>
        /// <param name="sceneName">场景名（需在 Build Settings 中注册）</param>
        public async Cysharp.Threading.Tasks.UniTask LoadSceneAsync(string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single)
        {
            LoadingSceneName = sceneName;
            Progress = 0f;
            Debug.Log($"[SceneLoader] 开始加载场景: {sceneName} (mode={mode})");

            // TODO: 触发 UIManager 显示 Loading 面板
            // TODO: 使用 Addressables 或 AssetBundle 加载场景

            var op = SceneManager.LoadSceneAsync(sceneName, mode);
            if (op == null)
            {
                Debug.LogError($"[SceneLoader] 场景不存在: {sceneName}");
                LoadingSceneName = null;
                return;
            }

            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
            {
                Progress = op.progress;
                await Cysharp.Threading.Tasks.UniTask.Yield();
            }

            Progress = 1f;
            op.allowSceneActivation = true;

            await Cysharp.Threading.Tasks.UniTask.Yield();
            LoadingSceneName = null;
            Debug.Log($"[SceneLoader] 场景加载完成: {sceneName}");
        }

        /// <summary>卸载场景</summary>
        public async Cysharp.Threading.Tasks.UniTask UnloadSceneAsync(string sceneName)
        {
            Debug.Log($"[SceneLoader] 开始卸载场景: {sceneName}");
            var op = SceneManager.UnloadSceneAsync(sceneName);
            if (op != null)
                await op.ToUniTask();
        }

        /// <summary>获取当前活跃场景名</summary>
        public string GetActiveSceneName() => SceneManager.GetActiveScene().name;

        // ==================== 内部 ====================

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[SceneLoader] 场景已加载: {scene.name} (mode={mode})");
        }

        private void OnSceneUnloaded(Scene scene)
        {
            Debug.Log($"[SceneLoader] 场景已卸载: {scene.name}");
        }

        protected override void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            base.OnDestroy();
        }
    }
}
