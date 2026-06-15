using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>启动状态 — 初始化SDK、检查更新、加载基础资源</summary>
    public class LaunchState : FSMState<GameState>
    {
        public LaunchState() : base(GameState.Launch) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入启动状态");

            EventBus.Publish("LaunchStarted");

            // 使用 CoroutineManager 替代 async void，保持 FSM 生命周期同步
            CoroutineManager.Delay(0.5f, () => ChangeState(GameState.Login));
        }

        public override void OnExit()
        {
            base.OnExit();
            Debug.Log("[GameFSM] 退出启动状态");
        }
    }
}
