namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>仿真组件标记接口 — 所有逻辑组件必须实现</summary>
    public interface ISimComponent { }

    /// <summary>
    /// 可哈希组件 — 组件自行将字段值混入 FNV-1a 哈希，零装箱、零反射。
    /// 用于 SimWorld.Checksum 的高性能确定性校验。
    /// </summary>
    public interface IHashableComponent : ISimComponent
    {
        /// <summary>将本组件的所有字段混入 hash，返回新 hash</summary>
        ulong HashInto(ulong hash);
    }
}
