namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 目标位置缓存系统 — 在 Attack 系统之前将目标 Transform 复制到 TargetComponent。
    /// 消除 AttackSystem 中的 3 层间接寻址。
    /// Order: 9（索敌和路径规划之后、移动和攻击之前）
    /// </summary>
    public class SimTargetCacheSystem : SimSystemBase
    {
        public SimTargetCacheSystem() { Order = 9; }

        public override void Update()
        {
            World.ForEachComponent<SimTargetComponent>((entity, target) =>
            {
                if (!target.HasTarget) return;

                if (World.TryGetComponent<SimTransformComponent>(target.TargetEntity, out var targetTransform))
                {
                    target.TargetPosX = targetTransform.PosX;
                    target.TargetPosY = targetTransform.PosY;
                    World.SetComponent(entity, target);
                }
            });
        }
    }
}
