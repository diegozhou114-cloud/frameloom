using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 战斗参数 — 从 MainVillageState 传入 BattleState。
    /// 战斗模式类型 + 对手村庄数据。
    /// </summary>
    public class BattleParams
    {
        /// <summary>战斗模式 ID：village_attack / clan_war / builder_base / campaign</summary>
        public string ModeId;

        /// <summary>攻击方村庄数据</summary>
        public VillageData AttackerVillage;

        /// <summary>防守方村庄数据</summary>
        public VillageData DefenderVillage;

        public static BattleParams VillageAttack(VillageData attacker, VillageData defender)
        {
            return new BattleParams
            {
                ModeId = "village_attack",
                AttackerVillage = attacker,
                DefenderVillage = defender,
            };
        }
    }
}
