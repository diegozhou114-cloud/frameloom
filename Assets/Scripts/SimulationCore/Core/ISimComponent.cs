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
}
