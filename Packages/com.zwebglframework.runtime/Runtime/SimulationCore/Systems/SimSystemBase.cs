namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// Archetype 仿真系统基类 — 纯同步管线，使用 SimWorld。
    /// </summary>
    public abstract class SimSystemBase
    {
        public string SystemName => GetType().Name;
        public bool Enabled { get; set; } = true;
        public int Order { get; set; }

        protected SimWorld World { get; private set; }

        internal void SetWorld(SimWorld world) => World = world;

        public virtual void Init() { }
        public virtual void Update() { }
        public virtual void Cleanup() { }
    }
}
