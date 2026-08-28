namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>目标组件 — SimEntity 版本</summary>
    public struct SimTargetComponent : IHashableComponent
    {
        public SimEntity TargetEntity;
        public bool HasTarget => TargetEntity.IsValid;

        /// <summary>缓存的目标位置（避免远程 Transform 查询）</summary>
        public LogicInt TargetPosX, TargetPosY;

        public static SimTargetComponent Create()
        {
            return new SimTargetComponent { TargetEntity = SimEntity.Invalid };
        }

        public ulong HashInto(ulong hash)
        {
            const ulong FNVPrime = 1099511628211UL;
            hash ^= (ulong)TargetEntity.Id; hash *= FNVPrime;
            hash ^= (ulong)TargetEntity.Generation; hash *= FNVPrime;
            hash ^= (ulong)TargetPosX.RawValue; hash *= FNVPrime;
            hash ^= (ulong)TargetPosY.RawValue; hash *= FNVPrime;
            return hash;
        }
    }
}
