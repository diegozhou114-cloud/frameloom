namespace ZWebGLFrameWork.ECS
{
    /// <summary>ECS 组件标记接口 — 所有组件必须实现</summary>
    public interface IComponent { }

    /// <summary>
    /// 组件池内部接口 — 提供非泛型 Remove，消除 World.DestroyEntity 的反射调用。
    /// 由 ComponentPool&lt;T&gt; 实现，不对外暴露。
    /// </summary>
    internal interface IComponentPool
    {
        void Remove(Entity entity);
    }
}
