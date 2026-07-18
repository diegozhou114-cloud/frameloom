using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimTargetSelectionSystem : SimSystemBase
    {
        private struct Candidate
        {
            public SimEntity Entity;
            public int TeamId;
            public int GridX;
            public int GridY;
        }

        private readonly List<Candidate> _candidates = new List<Candidate>();
        private readonly Dictionary<SimEntity, int> _assignments = new Dictionary<SimEntity, int>();

        public SimTargetSelectionSystem() { Order = 5; }

        public override void Update()
        {
            _candidates.Clear();
            _assignments.Clear();
            World.ForEachComponent<SimTransformComponent>((entity, transform) =>
            {
                if (!World.TryGetComponent(entity, out SimTeamComponent team) ||
                    !World.TryGetComponent(entity, out SimHealthComponent health) ||
                    !health.IsAlive)
                    return;
                _candidates.Add(new Candidate
                {
                    Entity = entity,
                    TeamId = team.TeamId,
                    GridX = transform.GridX,
                    GridY = transform.GridY,
                });
            });
            _candidates.Sort((left, right) => left.Entity.Id.CompareTo(right.Entity.Id));

            World.ForEachEntities<SimAttackComponent, SimTransformComponent>(
                (entity, attack, transform) =>
                {
                    if (!World.TryGetComponent(entity, out SimTeamComponent team) ||
                        !World.TryGetComponent(entity, out SimTargetComponent target))
                        return;

                    Candidate best = default;
                    bool found = false;
                    int bestScore = int.MaxValue;
                    foreach (var candidate in _candidates)
                    {
                        if (candidate.TeamId == team.TeamId) continue;
                        int distance = Math.Abs(transform.GridX - candidate.GridX) +
                                       Math.Abs(transform.GridY - candidate.GridY);
                        _assignments.TryGetValue(candidate.Entity, out int assigned);
                        int score = distance + assigned * 1000;
                        if (!found || score < bestScore ||
                            (score == bestScore && candidate.Entity.Id < best.Entity.Id))
                        {
                            best = candidate;
                            bestScore = score;
                            found = true;
                        }
                    }

                    target.TargetEntity = found ? best.Entity : SimEntity.Invalid;
                    if (found)
                    {
                        _assignments.TryGetValue(best.Entity, out int assigned);
                        _assignments[best.Entity] = assigned + 1;
                    }
                    World.SetComponent(entity, target);
                });
        }
    }
}
