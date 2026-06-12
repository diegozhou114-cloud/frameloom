using System.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 战斗编排器 — 管理阶段流转。所有规则参数从 CombatMode 读取。
    /// </summary>
    public class CombatOrchestrator
    {
        public const string EventPhaseChanged = "CombatPhaseChanged";
        public const string EventCombatEnded = "CombatEnded";

        public CombatPhase CurrentPhase { get; private set; } = CombatPhase.Idle;

        public async Task RunDeployPhase(float timeLimit)
        {
            if (timeLimit <= 0) return;
            SetPhase(CombatPhase.Deploying);
            Debug.Log($"[CombatOrchestrator] 部署阶段，{timeLimit}s");

            float elapsed = 0f;
            while (elapsed < timeLimit)
            {
                await AsyncManager.Delay(100);
                elapsed += 0.1f;
                await Task.Yield();
            }
        }

        public async Task RunBattlePhase(
            SystemGroup systems, UnitAI ai, DeathSystem deathSys,
            CombatMode mode, UnitSpawner spawner)
        {
            SetPhase(CombatPhase.Battling);
            Debug.Log($"[CombatOrchestrator] 战斗开始 [{mode.DisplayName}]");

            int wave = 0;
            float waveTimer = mode.FirstWaveDelay;
            float battleElapsed = 0f;

            while (CurrentPhase == CombatPhase.Battling)
            {
                battleElapsed += Time.deltaTime;

                // 超时保护
                if (mode.BattleTimeout > 0 && battleElapsed > mode.BattleTimeout)
                {
                    Debug.LogWarning("[CombatOrchestrator] 战斗超时，强制结束");
                    deathSys.ForceWinner(Team.Defender);
                    break;
                }

                // 波次生成
                if (mode.UseWaves)
                {
                    waveTimer += Time.deltaTime;
                    if (waveTimer >= mode.WaveInterval)
                    {
                        waveTimer = 0f;
                        wave++;
                        mode.SpawnAttackerWave(spawner, wave);
                    }
                }

                ai.Update();
                systems.UpdateAll();

                // 模式自定义胜负判定
                var winner = mode.CheckWinCondition(deathSys, battleElapsed);
                if (winner.HasValue)
                {
                    Debug.Log($"[CombatOrchestrator] 战斗结束 [{mode.DisplayName}], 胜者: {winner}");
                    break;
                }

                await Task.Yield();
            }
        }

        public async Task RunEndPhase(CombatEndEvent result)
        {
            SetPhase(CombatPhase.Ended);
            Debug.Log($"[CombatOrchestrator] 结算: winner={result.Winner}, stars={result.Stars}");
            await AsyncManager.DelaySeconds(2f);
        }

        public async Task CleanupAsync(SystemGroup systems)
        {
            await systems.CleanupAllAsync();
            SetPhase(CombatPhase.Idle);
        }

        private void SetPhase(CombatPhase phase)
        {
            CurrentPhase = phase;
            EventBus.Publish(EventPhaseChanged, phase);
        }
    }
}
