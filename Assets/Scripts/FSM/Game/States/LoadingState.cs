using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>加载状态 — 异步加载场景/资源时的过渡状态</summary>
    public class LoadingState : FSMState<GameState>
    {
        public float Progress { get; private set; }

        /// <summary>当前正在执行的加载协程（OnExit 时停止以避免孤儿协程）</summary>
        private Coroutine _activeCoroutine;
        private readonly ISceneLoadService _sceneLoader;

        public LoadingState() : this(null) { }

        public LoadingState(ISceneLoadService sceneLoader) : base(GameState.Loading)
        {
            _sceneLoader = sceneLoader;
        }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Progress = 0f;
            Debug.Log("[GameFSM] 进入加载状态");

            if (data is LoadingPayload payload)
            {
                EventBus.Publish("LoadingStarted");
                _activeCoroutine = CoroutineManager.StartCoroutine(LoadSequence(payload));
            }
            else
            {
                // 优雅降级：收到非预期 payload 类型时回退到主城，避免卡死在加载屏
                Debug.LogWarning("[GameFSM] LoadingState 收到非LoadingPayload数据，回退到主城");
                ChangeState(GameState.MainVillage);
            }
        }

        private IEnumerator LoadSequence(LoadingPayload payload)
        {
            if (!string.IsNullOrEmpty(payload.TargetScene))
            {
                ISceneLoadService sceneLoader = _sceneLoader ?? SceneLoader.Instance;
                UniTask<SceneLoadResult> loadTask = sceneLoader.LoadSceneAsync(
                    payload.TargetScene,
                    LoadSceneMode.Single,
                    LifetimeToken);
                while (loadTask.Status == UniTaskStatus.Pending)
                {
                    if (sceneLoader is SceneLoader unitySceneLoader)
                        Progress = unitySceneLoader.Progress;
                    yield return null;
                }

                SceneLoadResult result;
                try
                {
                    result = loadTask.GetAwaiter().GetResult();
                }
                catch (System.OperationCanceledException)
                {
                    result = new SceneLoadResult(SceneLoadStatus.Cancelled);
                }
                catch (System.Exception exception)
                {
                    result = new SceneLoadResult(SceneLoadStatus.Failed, exception.Message);
                }

                if (!result.Succeeded)
                {
                    Debug.LogWarning(
                        $"[LoadingState] ❌ 场景加载失败: {payload.TargetScene} ({result.Status})");
                    Progress = 0f;
                    EventBus.Publish("LoadingFailed", result.Status);
                    ChangeState(GameState.MainVillage); // 回退到主城
                    _activeCoroutine = null;
                    yield break;
                }
            }
            else
            {
                // 伪 0.1s 延迟：纯 UI 体验打磨，确保加载画面至少显示一瞬，避免闪烁
                yield return new WaitForSeconds(0.1f);
            }

            // 检查协程是否已被外部停止（防止孤儿协程在状态切换后仍然变更 FSM）
            if (_activeCoroutine == null)
            {
                Debug.LogWarning("[LoadingState] 加载协程已被取消，跳过状态切换");
                yield break;
            }

            Progress = 1.0f;
            EventBus.Publish("LoadingComplete");
            _activeCoroutine = null;
            ChangeState(payload.TargetState, payload.TargetData);
        }

        public override void OnExit()
        {
            base.OnExit();
            // 停止未完成的加载协程，防止在退出后继续执行并覆盖 FSM 状态
            if (_activeCoroutine != null)
            {
                CoroutineManager.StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
                Debug.Log("[GameFSM] 加载协程已取消");
            }
            Progress = 1f;
            Debug.Log("[GameFSM] 退出加载状态");
        }
    }
}
