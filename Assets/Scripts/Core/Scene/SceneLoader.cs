using System;
using System.Threading;
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
    public class SceneLoader : SingletonDontDestroy<SceneLoader>, ISceneLoadService
    {
        /// <summary>是否已初始化</summary>
        public bool IsReady { get; private set; }
        private bool _initialized = false;

        /// <summary>当前正在加载的场景名（null 表示无加载任务）</summary>
        public string LoadingSceneName { get; private set; }

        /// <summary>加载进度（0~1）</summary>
        public float Progress { get; private set; }

        // ==================== 初始化 ====================

        /// <summary>
        /// 初始化场景加载器。
        /// </summary>
        public bool Init()
        {
            if (_initialized) return true;
            try
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                _initialized = true;
                IsReady = true;
                Debug.Log("[SceneLoader] ✅ 初始化完成");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SceneLoader] ❌ 初始化失败: {ex.Message}");
                return false;
            }
        }

        // ==================== 公开 API ====================

        /// <summary>异步加载场景（叠加模式）</summary>
        /// <param name="sceneName">场景名（需在 Build Settings 中注册）</param>
        private sealed class UnitySceneLoadOperation : ISceneLoadOperation
        {
            private readonly AsyncOperation _operation;

            public UnitySceneLoadOperation(AsyncOperation operation) =>
                _operation = operation ?? throw new ArgumentNullException(nameof(operation));

            public float Progress => _operation.progress;
            public bool IsDone => _operation.isDone;
            public bool AllowSceneActivation
            {
                get => _operation.allowSceneActivation;
                set => _operation.allowSceneActivation = value;
            }
        }

        public async UniTask<SceneLoadResult> LoadSceneAsync(
            string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken token = default)
        {
            LoadingSceneName = sceneName;
            Progress = 0f;
            EventBus.Publish("SceneLoadStarted", sceneName);
            try
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, mode);
                if (operation == null)
                {
                    return new SceneLoadResult(
                        SceneLoadStatus.NotFound,
                        $"Scene is missing from Build Settings: {sceneName}");
                }

                operation.allowSceneActivation = false;
                SceneLoadResult result = await SceneLoadRunner.RunAsync(
                    new UnitySceneLoadOperation(operation),
                    30f,
                    token,
                    reportProgress: value => Progress = value);
                if (!result.Succeeded)
                    Debug.LogError($"[SceneLoader] Load failed: {sceneName} ({result.Status})");
                return result;
            }
            catch (ArgumentException exception)
            {
                return new SceneLoadResult(SceneLoadStatus.NotFound, exception.Message);
            }
            catch (OperationCanceledException)
            {
                return new SceneLoadResult(SceneLoadStatus.Cancelled);
            }
            catch (Exception exception)
            {
                return new SceneLoadResult(SceneLoadStatus.Failed, exception.Message);
            }
            finally
            {
                LoadingSceneName = null;
            }
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
            if (!_initialized) return;
            _initialized = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            base.OnDestroy();
        }
    }
}
