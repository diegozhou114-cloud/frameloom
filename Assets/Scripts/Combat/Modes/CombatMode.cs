using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 战斗模式抽象基类 — 定义一种玩法的全部规则。
    /// 每个具体模式定义：部署规则、波次规则、胜负判定、结算计算、单位生成。
    /// 
    /// 用法：CombatManager.StartBattle(new VillageAttackMode());
    /// </summary>
    public abstract class CombatMode
    {
        /// <summary>模式唯一 ID</summary>
        public abstract string ModeId { get; }

        /// <summary>显示名称</summary>
        public abstract string DisplayName { get; }

        // ==================== 部署 ====================

        /// <summary>部署时间限制（秒），0 = 不限时</summary>
        public virtual float DeployTimeLimit => 30f;

        /// <summary>是否允许部署阶段</summary>
        public virtual bool AllowDeployment => true;

        /// <summary>可用单位类型 ID 列表（空 = 全部可用）</summary>
        public virtual int[] AllowedUnitTypes => null;

        /// <summary>最大部署单位数（0 = 不限）</summary>
        public virtual int MaxDeployCount => 0;

        // ==================== 战斗 ====================

        /// <summary>是否使用波次生成</summary>
        public virtual bool UseWaves => true;

        /// <summary>波次间隔（秒）</summary>
        public virtual float WaveInterval => 5f;

        /// <summary>战斗超时（秒），到期触发 ForceWinner</summary>
        public virtual float BattleTimeout => 180f;

        /// <summary>第一波延迟（秒）</summary>
        public virtual float FirstWaveDelay => 3f;

        // ==================== 胜负判定 ====================

        /// <summary>
        /// 检查胜负条件。每帧调用。
        /// 返回 null = 继续战斗，非 null = 胜者已决。
        /// </summary>
        public abstract Team? CheckWinCondition(DeathSystem deathSys, float elapsedTime);

        // ==================== 结算 ====================

        /// <summary>计算战斗结果</summary>
        public abstract CombatEndEvent CalculateResult(
            Team winner, int totalBuildings, int destroyedBuildings, float battleDuration);

        // ==================== 单位生成 ====================

        /// <summary>生成防守方</summary>
        public abstract void SpawnDefenders(UnitSpawner spawner, VillageData village);

        /// <summary>生成攻击方波次</summary>
        public abstract void SpawnAttackerWave(UnitSpawner spawner, int wave);
    }
}
