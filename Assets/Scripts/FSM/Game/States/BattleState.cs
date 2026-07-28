using System;
using System.Threading;
using UnityEngine;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.SimulationCore;
using ZWebGLFrameWork.UI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(
    "ZWebGLFrameWork.Combat.PlayModeTests")]

namespace ZWebGLFrameWork.FSM
{
    public class BattleState : FSMState<GameState>
    {
        private readonly CombatManager _combatManager;
        private readonly BattleRewardService _rewardService = new BattleRewardService();
        private CancellationTokenSource _battleLifetime;
        private bool _battleStarted;
        private string _expectedMatchId;

        public BattleState() : this(CombatManager.Instance) { }

        internal BattleState(CombatManager combatManager) : base(GameState.Battle) =>
            _combatManager = combatManager ?? throw new ArgumentNullException(nameof(combatManager));

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入战斗状态");

            if (!(data is BattleParams parameters))
            {
                Debug.LogError("[BattleState] 未传入 BattleParams，返回主城");
                ChangeState(GameState.MainVillage);
                return;
            }

            EventBus.Subscribe<SimCombatPhase>(CombatManager.EventPhaseChanged, OnPhaseChanged);
            EventBus.Subscribe("Battle_ReturnToVillage", OnReturnToVillage);

            _battleLifetime = new CancellationTokenSource();
            _expectedMatchId = parameters.MatchId;
            SaveManager.SaveGame();
            _battleStarted = false;
            SafeStartBattle(parameters, _battleLifetime);
        }

        public override void OnUpdate()
        {
            if (!_battleStarted && _combatManager.CurrentPhase == SimCombatPhase.Deploying)
            {
                _battleStarted = true;
                UIManager.Instance.Open<DeployPanel>();
            }
        }

        public override void OnExit()
        {
            CancellationTokenSource lifetime = _battleLifetime;
            _battleLifetime = null;
            lifetime?.Cancel();
            lifetime?.Dispose();
            _combatManager.StopBattle();

            EventBus.Unsubscribe<SimCombatPhase>(CombatManager.EventPhaseChanged, OnPhaseChanged);
            EventBus.Unsubscribe("Battle_ReturnToVillage", OnReturnToVillage);

            UIManager.Instance.Close<DeployPanel>();
            UIManager.Instance.Close<BattleHUD>();
            UIManager.Instance.Close<ResultPanel>();

            base.OnExit();
            Debug.Log("[GameFSM] 退出战斗状态");
        }

        private void OnPhaseChanged(SimCombatPhase phase)
        {
            Debug.Log($"[BattleState] 阶段变更: {phase}");
            switch (phase)
            {
                case SimCombatPhase.Battling:
                    UIManager.Instance.Close<DeployPanel>();
                    UIManager.Instance.Open<BattleHUD>();
                    break;
                case SimCombatPhase.Ended:
                    UIManager.Instance.Close<BattleHUD>();
                    break;
            }
        }

        private void OnReturnToVillage()
        {
            Debug.Log("[BattleState] 返回主城");
            ChangeState(GameState.MainVillage);
        }

        private async void SafeStartBattle(BattleParams parameters,
            CancellationTokenSource lifetime)
        {
            try
            {
                SimBattleResult result = await _combatManager.StartBattleAsync(parameters, lifetime.Token);
                if (lifetime.IsCancellationRequested || !ReferenceEquals(_battleLifetime, lifetime) ||
                    !IsActive)
                    return;

                BattleRewardCommitStatus status = _rewardService.Commit(_expectedMatchId, result,
                    BattleResultTrust.Accepted, DataManager.Instance.Player,
                    () => SaveManager.SaveGame(), committed => UIManager.Instance.Open<ResultPanel>(committed));
                if (status != BattleRewardCommitStatus.Applied &&
                    status != BattleRewardCommitStatus.Duplicate)
                {
                    EventBus.Publish("BattleRewardRejected", status);
                    Debug.LogError($"[BattleState] Result commit rejected: {status}");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (ReferenceEquals(_battleLifetime, lifetime) &&
                    !lifetime.IsCancellationRequested && IsActive)
                    ChangeState(GameState.MainVillage);
            }
        }
    }
}
