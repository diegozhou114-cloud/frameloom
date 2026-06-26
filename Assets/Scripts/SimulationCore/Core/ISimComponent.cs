namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>仿真组件标记接口 — 所有逻辑组件必须实现</summary>
    public interface ISimComponent { }

    /// <summary>
    /// 组件池内部接口 — 提供非泛型 Remove/Clear/AccumulateHash，
    /// 消除 SimWorld.DestroyEntity 和 ComputeChecksum 的反射调用。
    /// </summary>
    internal interface ISimComponentPool
    {
        void Remove(SimEntity entity);
        void Clear();
        ulong AccumulateHash(ulong hash);
        /// <summary>导出 dense 数组快照</summary>
        PoolSnapshot ExportDense();
        /// <summary>从快照恢复</summary>
        void RestoreDense(PoolSnapshot snapshot);
    }

    /// <summary>
    /// 可哈希组件 — 组件自行将字段值混入 FNV-1a 哈希，零装箱、零反射。
    /// 用于 SimArchetypeWorld.Checksum 的高性能确定性校验。
    /// </summary>
    public interface IHashableComponent : ISimComponent
    {
        /// <summary>将本组件的所有字段混入 hash，返回新 hash</summary>
        ulong HashInto(ulong hash);
    }
}
