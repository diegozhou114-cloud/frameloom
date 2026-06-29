using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>暂停状态 — 游戏暂停时进入，恢复时退出</summary>
    public class PauseState : FSMState<GameState>
    {
        public PauseState() : base(GameState.Pause) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Time.timeScale = 0f;
            Debug.Log("[GameFSM] ⏸ 游戏暂停");
        }

        public override void OnExit()
        {
            Time.timeScale = 1f;
            base.OnExit();
            Debug.Log("[GameFSM] ▶ 游戏恢复");
        }
    }
}
