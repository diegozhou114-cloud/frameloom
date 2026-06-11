using UnityEngine;

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

            // TODO: SDK初始化、资源热更检查、版本校验
            // 完成后切换到登录
            // ChangeState(GameState.Login);
        }

        public override void OnUpdate()
        {
            // 模拟启动完成
        }

        public override void OnExit()
        {
            base.OnExit();
            Debug.Log("[GameFSM] 退出启动状态");
        }
    }
}
