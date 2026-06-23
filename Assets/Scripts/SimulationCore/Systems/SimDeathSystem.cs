using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 确定性死亡系统 — 清理死亡单位，统计存活数，判定胜负。
    /// Order: 90（最后执行）
    /// </summary>
    public class SimDeathSystem : SimSystemBase
    {
        public SimDeathSystem() { Order = 90; }

        /// <summary>攻击方存活数</summary>
        public int AttackerAlive { get; private set; }
        /// <summary>防守方存活数</summary>
        public int DefenderAlive { get; private set; }
        /// <summary>攻击方死亡数（本 Tick）</summary>
        public int AttackerDead { get; private set; }
        /// <summary>防守方死亡数（本 Tick）</summary>
        public int DefenderDead { get; private set; }
        /// <summary>攻击方摧毁防守方百分比（0~1）</summary>
        public float AttackerDestroyedPercent { get; private set; }
        /// <summary>胜者（null=战斗继续，0=None不应出现，1=Attacker，2=Defender）</summary>
        public int? Winner { get; private set; }

        private readonly List<SimEntity> _deadList = new List<SimEntity>();

        public override void Update()
        {
            AttackerAlive = 0;
            DefenderAlive = 0;
            AttackerDead = 0;
            DefenderDead = 0;
            Winner = null;
            _deadList.Clear();

            World.ForEachEntities<SimHealthComponent>(entity =>
            {
                if (!World.TryGetComponent<SimHealthComponent>(entity, out var health)) return;

                if (!health.IsAlive)
                {
                    _deadList.Add(entity);
                    return;
                }

                if (World.TryGetComponent<SimTeamComponent>(entity, out var team))
                {
                    if (team.TeamId == SimTeam.Attacker) AttackerAlive++;
                    else if (team.TeamId == SimTeam.Defender) DefenderAlive++;
                }
            });

            foreach (var dead in _deadList)
            {
                if (World.TryGetComponent<SimTeamComponent>(dead, out var team))
                {
                    if (team.TeamId == SimTeam.Attacker) AttackerDead++;
                    else if (team.TeamId == SimTeam.Defender) DefenderDead++;
                }
                World.DestroyEntity(dead);
            }

            int defenderTotal = DefenderDead + DefenderAlive;
            AttackerDestroyedPercent = defenderTotal > 0
                ? (float)DefenderDead / defenderTotal : 0f;

            if (AttackerAlive == 0 && DefenderAlive > 0)
                Winner = SimTeam.Defender;
            else if (DefenderAlive == 0 && AttackerAlive > 0)
                Winner = SimTeam.Attacker;
        }

        /// <summary>强制设置胜者（超时/外部干预）</summary>
        public void ForceWinner(int teamId) { Winner = teamId; }
    }
}
