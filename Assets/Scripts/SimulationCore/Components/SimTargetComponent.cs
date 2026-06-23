namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>目标组件 — SimEntity 版本</summary>
    public struct SimTargetComponent : ISimComponent
    {
        public SimEntity TargetEntity;
        public bool HasTarget => TargetEntity.IsValid;

        public static SimTargetComponent Create()
        {
            return new SimTargetComponent { TargetEntity = SimEntity.Invalid };
        }
    }
}
