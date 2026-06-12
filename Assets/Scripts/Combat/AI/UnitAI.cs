using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 单位 AI — 轻量编排层。
    /// 评估目标 → 无效则索敌 → 路径规划。
    /// 每 N 帧更新一次，降低计算压力。
    /// </summary>
    public class UnitAI
    {
        public int UpdateInterval { get; set; } = 6;

        private readonly World _world;
        private readonly TargetEvaluator _targetEvaluator;
        private readonly TargetSelector _targetSelector;
        private readonly PathPlanner _pathPlanner;
        private int _frameCounter;

        public UnitAI(World world, AStarPathfinding pathfinding)
        {
            _world = world;
            _targetEvaluator = new TargetEvaluator(world);
            _targetSelector = new TargetSelector(world);
            _pathPlanner = new PathPlanner(world, pathfinding);
        }

        public void Update()
        {
            _frameCounter++;
            if (_frameCounter % UpdateInterval != 0) return;

            _world.ForEachEntities<TransformComponent>(entity =>
            {
                if (!_world.TryGetComponent<AttackComponent>(entity, out _)) return;
                if (!_world.TryGetComponent<HealthComponent>(entity, out var health) || !health.IsAlive) return;

                ProcessEntity(entity);
            });
        }

        private void ProcessEntity(Entity entity)
        {
            var transform = _world.GetComponent<TransformComponent>(entity);
            var attack = _world.GetComponent<AttackComponent>(entity);
            var team = _world.GetComponent<TeamComponent>(entity);

            // 1. 评估当前目标
            var hasTarget = _world.TryGetComponent<TargetComponent>(entity, out var target);
            if (hasTarget)
            {
                var status = _targetEvaluator.Evaluate(entity, target, attack, transform);
                switch (status)
                {
                    case TargetStatus.InRange:
                        _pathPlanner.ClearPath(entity);
                        return;
                    case TargetStatus.OutOfRange:
                        break; // 继续寻路
                    case TargetStatus.NoTarget:
                    case TargetStatus.TargetDead:
                        _world.RemoveComponent<TargetComponent>(entity);
                        hasTarget = false;
                        break;
                }
            }

            // 2. 索敌
            if (!hasTarget)
            {
                var enemy = _targetSelector.FindTarget(transform, team.TeamId);
                if (!enemy.HasValue) return;

                _world.AddComponent(entity, TargetComponent.Create());
                ref var t = ref _world.GetComponent<TargetComponent>(entity);
                t.TargetEntity = enemy.Value;
            }

            // 3. 寻路到目标（CellSize 由 Spawner 注入 MovementComponent，默认 1f）
            target = _world.GetComponent<TargetComponent>(entity);
            var targetTransform = _world.GetComponent<TransformComponent>(target.TargetEntity);
            float cellSize = _world.TryGetComponent<MovementComponent>(entity, out var mov)
                ? mov.CellSize : 1f;

            _pathPlanner.PlanPath(entity, transform.GridPosition,
                targetTransform.GridPosition, 3f, cellSize);
        }
    }
}
