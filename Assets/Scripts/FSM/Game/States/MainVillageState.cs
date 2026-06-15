using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>主城状态 — 核心玩法：建造、训练、社交、商店</summary>
    public class MainVillageState : FSMState<GameState>
    {
        public MainVillageState() : base(GameState.MainVillage) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入主城状态");

            EventBus.Publish("MainVillageEntered");
        }

        public override void OnUpdate()
        {
            // 主城逻辑每帧更新：建筑计时、资源产出
        }

        public override void OnExit()
        {
            base.OnExit();
            Debug.Log("[GameFSM] 退出主城状态");
        }
    }
}
