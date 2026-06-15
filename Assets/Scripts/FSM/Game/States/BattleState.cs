using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>战斗状态 — 攻击/防御场景，单位控制和战斗逻辑</summary>
    public class BattleState : FSMState<GameState>
    {
        public BattleState() : base(GameState.Battle) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入战斗状态");

            EventBus.Publish("BattleEntered");
        }

        public override void OnUpdate()
        {
            // 战斗每帧逻辑
        }

        public override void OnExit()
        {
            base.OnExit();
            Debug.Log("[GameFSM] 退出战斗状态");
        }
    }
}
