using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>加载状态 — 异步加载场景/资源时的过渡状态</summary>
    public class LoadingState : FSMState<GameState>
    {
        public float Progress { get; private set; }

        /// <summary>当前正在执行的加载协程（OnExit 时停止以避免孤儿协程）</summary>
        private Coroutine _activeCoroutine;

        public LoadingState() : base(GameState.Loading) { }

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
                Debug.LogWarning("[GameFSM] LoadingState 收到非LoadingPayload数据，回退到主城");
                ChangeState(GameState.MainVillage);
            }
        }

        private IEnumerator LoadSequence(LoadingPayload payload)
        {
            if (!string.IsNullOrEmpty(payload.TargetScene))
            {
                var loadTask = SceneLoader.Instance.LoadSceneAsync(payload.TargetScene);
                while (loadTask.Status == UniTaskStatus.Pending)
                {
                    Progress = SceneLoader.Instance.Progress;
                    yield return null;
                }

                // 场景加载失败处理
                if (loadTask.Status == UniTaskStatus.Faulted)
                {
                    Debug.LogError($"[LoadingState] ❌ 场景加载失败: {payload.TargetScene}");
                    Progress = 0f;
                    EventBus.Publish("LoadingFailed");
                    ChangeState(GameState.MainVillage); // 回退到主城
                    _activeCoroutine = null;
                    yield break;
                }
            }
            else
            {
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
            // 停止未完成的加载协程，防止在退出后继续执行并覆盖 FSM 状态
            if (_activeCoroutine != null)
            {
                CoroutineManager.StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
                Debug.Log("[GameFSM] 加载协程已取消");
            }
            Progress = 1f;
            base.OnExit();
            Debug.Log("[GameFSM] 退出加载状态");
        }
    }
}
