using UnityEngine;
using ZWebGLFrameWork.Combat.ECS;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 单位 AI 行为 — 简单的决策树：索敌 → 寻路 → 攻击。
    /// 运行在 ECS 系统之外，每 N 帧更新一次（降低计算压力）。
    /// </summary>
    public class UnitAI
    {
        /// <summary>AI 更新间隔（帧）</summary>
        public int UpdateInterval { get; set; } = 6;

        private readonly World _world;
        private int _frameCounter;
        private GridManager _grid;

        public UnitAI(World world)
        {
            _world = world;
            _grid = GridManager.Instance;
        }

        /// <summary>逐帧调用，按间隔执行 AI 决策</summary>
        public void Update()
        {
            _frameCounter++;
            if (_frameCounter % UpdateInterval != 0) return;

            foreach (var entity in _world.GetEntitiesWith<TransformComponent>())
            {
                if (!_world.TryGetComponent<AttackComponent>(entity, out _)) continue;
                if (!_world.TryGetComponent<HealthComponent>(entity, out var health) || !health.IsAlive) continue;

                ProcessEntity(entity);
            }
        }

        private void ProcessEntity(Entity entity)
        {
            var transform = _world.GetComponent<TransformComponent>(entity);
            var team = _world.GetComponent<TeamComponent>(entity);
            var attack = _world.GetComponent<AttackComponent>(entity);

            // 1. 已有目标 + 在射程内 → 不需要移动
            if (_world.TryGetComponent<TargetComponent>(entity, out var target) && target.HasTarget)
            {
                if (_world.TryGetComponent<TransformComponent>(target.TargetEntity, out var targetTransform))
                {
                    float dist = Vector3.Distance(transform.Position, targetTransform.Position);
                    if (dist <= attack.Range)
                    {
                        // 在射程内，清除移动路径
                        if (_world.HasComponent<MovementComponent>(entity))
                        {
                            var mov = _world.GetComponent<MovementComponent>(entity);
                            mov.Path = null;
                            _world.AddComponent(entity, mov);
                        }
                        return;
                    }
                }
                else
                {
                    // 目标已死亡
                    _world.RemoveComponent<TargetComponent>(entity);
                }
            }

            // 2. 索敌
            Entity? enemy = FindBestTarget(transform, team.TeamId);
            if (!enemy.HasValue) return;

            // 3. 设置目标
            if (!_world.HasComponent<TargetComponent>(entity))
                _world.AddComponent(entity, TargetComponent.Create());
            ref var t = ref _world.GetComponent<TargetComponent>(entity);
            t.TargetEntity = enemy.Value;

            // 4. 寻路
            var enemyTransform = _world.GetComponent<TransformComponent>(enemy.Value);
            var path = AStarPathfinding.FindPath(
                transform.GridPosition, enemyTransform.GridPosition);

            if (path.Count == 0) return;

            // 5. 设置移动路径
            if (!_world.HasComponent<MovementComponent>(entity))
                _world.AddComponent(entity, MovementComponent.Create(3f)); // 默认速度

            ref var mov = ref _world.GetComponent<MovementComponent>(entity);
            mov.Path = path;
            mov.PathIndex = 0;
        }

        private Entity? FindBestTarget(TransformComponent selfTransform, Team selfTeam)
        {
            Entity? bestTarget = null;
            float bestScore = float.MaxValue;

            foreach (var entity in _world.GetEntitiesWith<TransformComponent>())
            {
                if (!_world.TryGetComponent<TeamComponent>(entity, out var otherTeam)) continue;
                if (!_world.TryGetComponent<HealthComponent>(entity, out var hp) || !hp.IsAlive) continue;
                if (!TeamComponent.IsEnemy(selfTeam, otherTeam.TeamId)) continue;

                var otherTransform = _world.GetComponent<TransformComponent>(entity);
                float dist = Vector3.Distance(selfTransform.Position, otherTransform.Position);

                // 优先距离近 + 血量低
                float score = dist - hp.CurrentHP * 0.1f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = entity;
                }
            }

            return bestTarget;
        }
    }
}
