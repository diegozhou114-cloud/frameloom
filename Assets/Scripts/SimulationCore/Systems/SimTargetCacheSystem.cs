namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 目标位置缓存系统 — 在 Attack 系统之前将目标 Transform 复制到 TargetComponent。
    /// 消除 AttackSystem 中的 3 层间接寻址。
    /// Order: 5（最优先执行）
    /// </summary>
    public class SimTargetCacheSystem : SimSystemBase
    {
        public SimTargetCacheSystem() { Order = 5; }

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
