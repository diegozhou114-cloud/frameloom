using UnityEngine;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>登录状态 — 账号认证、选区、角色选择</summary>
    public class LoginState : FSMState<GameState>
    {
        public LoginState() : base(GameState.Login) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入登录状态");

            // TODO: 打开登录UI、连接网关服务器
        }

        public override void OnExit()
        {
            base.OnExit();
            Debug.Log("[GameFSM] 退出登录状态");
        }
    }
}
