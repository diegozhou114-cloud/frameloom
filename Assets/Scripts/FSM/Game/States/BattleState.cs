using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 战斗状态 — 完整战斗闭环。
    /// OnEnter: 解析 BattleParams → 创建 CombatMode → 启动战斗 → 打开部署面板。
    /// OnUpdate: 轮询战斗阶段变更（CombatManager 自驱动 Tick）。
    /// OnExit: 清理面板 + 反注册事件。
    /// </summary>
    public class BattleState : FSMState<GameState>
    {
        private bool _battleStarted;

        public BattleState() : base(GameState.Battle) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入战斗状态");

            var bp = data as BattleParams;
            if (bp == null)
            {
                Debug.LogError("[BattleState] 未传入 BattleParams，返回主城");
                ChangeState(GameState.MainVillage);
                return;
            }

            // ① 解析战斗模式
            var mode = CreateMode(bp.ModeId);
            if (mode == null)
            {
                Debug.LogError($"[BattleState] 未知模式: {bp.ModeId}");
                ChangeState(GameState.MainVillage);
                return;
            }

            // ② 监听战斗阶段 + 结束事件 + 返回主城
            EventBus.Subscribe<CombatPhase>(CombatManager.EventPhaseChanged, OnPhaseChanged);
            EventBus.Subscribe<CombatEndEvent>(CombatManager.EventCombatEnded, OnCombatEnded);
            EventBus.Subscribe("Battle_ReturnToVillage", OnReturnToVillage);

            // ③ 启动战斗（异步，由 CombatManager.Update 驱动 Tick）
            _battleStarted = false;
            CombatManager.Instance.StartBattleAsync(mode).Forget();
        }

        public override void OnUpdate()
        {
            // CombatManager.Update() 自驱动，此处仅做首次启动后的 UI 响应
            if (!_battleStarted && CombatManager.Instance.CurrentPhase == CombatPhase.Deploying)
            {
                _battleStarted = true;
                UIManager.Instance.Open<DeployPanel>();
            }
        }

        public override void OnExit()
        {
            EventBus.Unsubscribe<CombatPhase>(CombatManager.EventPhaseChanged, OnPhaseChanged);
            EventBus.Unsubscribe<CombatEndEvent>(CombatManager.EventCombatEnded, OnCombatEnded);
            EventBus.Unsubscribe("Battle_ReturnToVillage", OnReturnToVillage);

            UIManager.Instance.Close<DeployPanel>();
            UIManager.Instance.Close<BattleHUD>();
            UIManager.Instance.Close<ResultPanel>();

            base.OnExit();
            Debug.Log("[GameFSM] 退出战斗状态");
        }

        // ==================== 事件回调 ====================

        private void OnPhaseChanged(CombatPhase phase)
        {
            Debug.Log($"[BattleState] 阶段变更: {phase}");

            switch (phase)
            {
                case CombatPhase.Battling:
                    UIManager.Instance.Close<DeployPanel>();
                    UIManager.Instance.Open<BattleHUD>();
                    break;
                case CombatPhase.Ended:
                    UIManager.Instance.Close<BattleHUD>();
                    break;
            }
        }

        private void OnCombatEnded(CombatEndEvent result)
        {
            Debug.Log($"[BattleState] 战斗结束: winner={result.Winner}, stars={result.Stars}");
            UIManager.Instance.Open<ResultPanel>(result);
        }

        private void OnReturnToVillage()
        {
            Debug.Log("[BattleState] 返回主城");
            ChangeState(GameState.MainVillage);
        }

        // ==================== 内部 ====================

        private CombatMode CreateMode(string modeId)
        {
            switch (modeId)
            {
                case "village_attack":
                    return new VillageAttackMode();
                case "builder_base":
                    return new BuilderBaseMode();
                case "clan_war":
                    return new ClanWarMode();
                case "campaign":
                    return new CampaignMode();
                default:
                    return null;
            }
        }
    }
}
