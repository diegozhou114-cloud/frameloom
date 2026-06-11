using UnityEngine;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>加载状态 — 异步加载场景/资源时的过渡状态</summary>
    public class LoadingState : FSMState<GameState>
    {
        /// <summary>加载进度 (0~1)</summary>
        public float Progress { get; private set; }

        public LoadingState() : base(GameState.Loading) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Progress = 0f;
            Debug.Log("[GameFSM] 进入加载状态");

            // TODO: 显示Loading界面、开始异步加载
        }

        public override void OnUpdate()
        {
            // 更新加载进度
        }

        public override void OnExit()
        {
            Progress = 1f;
            base.OnExit();
            Debug.Log("[GameFSM] 退出加载状态");
        }
    }
}
