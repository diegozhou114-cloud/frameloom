using UnityEngine;
using ZWebGLFrameWork.Combat.ECS;

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

        /// <summary>是否有阵营全灭</summary>
        public Team? Winner { get; private set; }

        public override void Update()
        {
            AttackerAlive = 0;
            DefenderAlive = 0;
            Winner = null;

            var deadList = new System.Collections.Generic.List<Entity>();

            foreach (var entity in World.GetEntitiesWith<HealthComponent>())
            {
                var health = World.GetComponent<HealthComponent>(entity);
                if (!health.IsAlive)
                {
                    deadList.Add(entity);
                    continue;
                }

                // 统计存活数
                if (World.TryGetComponent<TeamComponent>(entity, out var team))
                {
                    if (team.TeamId == Team.Attacker) AttackerAlive++;
                    else if (team.TeamId == Team.Defender) DefenderAlive++;
                }
            }

            // 清理死亡单位
            foreach (var dead in deadList)
            {
                Debug.Log($"[DeathSystem] 单位死亡: {dead}");
                World.DestroyEntity(dead);
            }

            // 胜负判定
            if (AttackerAlive == 0 && DefenderAlive > 0)
                Winner = Team.Defender;
            else if (DefenderAlive == 0 && AttackerAlive > 0)
                Winner = Team.Attacker;
        }
    }
}
