using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 死亡系统 — 清理死亡单位，检查胜负条件。
    /// Order: 90（最后执行）
    /// </summary>
    public class DeathSystem : SystemBase
    {
        public DeathSystem() { Order = 90; }

        /// <summary>攻击方存活数</summary>
        public int AttackerAlive { get; private set; }

        /// <summary>防守方存活数</summary>
        public int DefenderAlive { get; private set; }

        /// <summary>攻击方死亡数（本帧累计）</summary>
        public int AttackerDead { get; private set; }

        /// <summary>防守方死亡数（本帧累计）</summary>
        public int DefenderDead { get; private set; }

        /// <summary>是否有阵营全灭</summary>
        public Team? Winner { get; private set; }

        private readonly System.Collections.Generic.List<Entity> _deadList =
            new System.Collections.Generic.List<Entity>();

        public override void Update()
        {
            AttackerAlive = 0;
            DefenderAlive = 0;
            AttackerDead = 0;
            DefenderDead = 0;
            Winner = null;
            _deadList.Clear();

            World.ForEachComponent<HealthComponent>((entity, ref health) =>
            {
                if (!health.IsAlive)
                {
                    _deadList.Add(entity);
                    return;
                }

                if (World.TryGetComponent<TeamComponent>(entity, out var team))
                {
                    if (team.TeamId == Team.Attacker) AttackerAlive++;
                    else if (team.TeamId == Team.Defender) DefenderAlive++;
                }
            });

            // 按阵营分别计数死亡单位
            foreach (var dead in _deadList)
            {
                if (World.TryGetComponent<TeamComponent>(dead, out var team))
                {
                    if (team.TeamId == Team.Attacker) AttackerDead++;
                    else if (team.TeamId == Team.Defender) DefenderDead++;
                }

                Debug.Log($"[DeathSystem] 单位死亡: {dead}");
                World.DestroyEntity(dead);
            }

            // 攻击方摧毁防守方的百分比 = 防守方死亡数 / 防守方总数
            int defenderTotal = DefenderDead + DefenderAlive;
            AttackerDestroyedPercent = defenderTotal > 0
                ? (float)DefenderDead / defenderTotal : 0f;

            if (AttackerAlive == 0 && DefenderAlive > 0)
                Winner = Team.Defender;
            else if (DefenderAlive == 0 && AttackerAlive > 0)
                Winner = Team.Attacker;
        }

        /// <summary>攻击方摧毁防守方的百分比（0~1）</summary>
        public float AttackerDestroyedPercent { get; private set; }

        /// <summary>强制设置胜者（超时/外部干预）</summary>
        public void ForceWinner(Team team) { Winner = team; }
    }
}
