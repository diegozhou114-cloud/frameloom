using System.Collections;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>加载状态 — 异步加载场景/资源时的过渡状态</summary>
    public class LoadingState : FSMState<GameState>
    {
        public float Progress { get; private set; }

        public LoadingState() : base(GameState.Loading) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Progress = 0f;
            Debug.Log("[GameFSM] 进入加载状态");

            if (data is LoadingPayload payload)
            {
                EventBus.Publish("LoadingStarted");
                // 使用 CoroutineManager 替代 async void，保持 FSM 生命周期同步
                CoroutineManager.Instance.StartCoroutine(LoadSequence(payload));
            }
            else
            {
                Debug.LogWarning("[GameFSM] LoadingState 收到非LoadingPayload数据，回退到主城");
                ChangeState(GameState.MainVillage);
            }
        }

        private IEnumerator LoadSequence(LoadingPayload payload)
        {
            yield return new WaitForSeconds(0.15f);
            Progress = 0.5f;
            yield return new WaitForSeconds(0.15f);
            Progress = 1.0f;
            EventBus.Publish("LoadingComplete");
            ChangeState(payload.TargetState, payload.TargetData);
        }

        public override void OnExit()
        {
            Progress = 1f;
            base.OnExit();
            Debug.Log("[GameFSM] 退出加载状态");
        }
    }
}
