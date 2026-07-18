using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    public class SimDeathSystem : SimSystemBase
    {
        public SimDeathSystem() { Order = 90; }

        public int AttackerAlive { get; private set; }
        public int DefenderAlive { get; private set; }
        public int AttackerDead { get; private set; }
        public int DefenderDead { get; private set; }
        public float AttackerDestroyedPercent { get; private set; }
        public int? Winner { get; private set; }

        private readonly List<SimEntity> _deadList = new List<SimEntity>();

        public override void Update()
        {
            AttackerAlive = 0; DefenderAlive = 0;
            AttackerDead = 0; DefenderDead = 0;
            Winner = null;
            _deadList.Clear();

            World.ForEachComponent<SimHealthComponent>((entity, health) =>
            {
                if (!health.IsAlive) { _deadList.Add(entity); return; }

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
            AttackerDestroyedPercent = defenderTotal > 0 ? (float)DefenderDead / defenderTotal : 0f;

            if (AttackerAlive == 0 && DefenderAlive > 0) Winner = SimTeam.Defender;
            else if (DefenderAlive == 0 && AttackerAlive > 0) Winner = SimTeam.Attacker;
        }

        public void ForceWinner(int teamId) { Winner = teamId; }

        public ulong HashInto(ulong hash)
        {
            var state = new DeterministicHash64(hash);
            state.AddInt32(AttackerAlive);
            state.AddInt32(DefenderAlive);
            state.AddInt32(AttackerDead);
            state.AddInt32(DefenderDead);
            state.AddBool(Winner.HasValue);
            if (Winner.HasValue) state.AddInt32(Winner.Value);
            return state.Value;
        }
    }
}
