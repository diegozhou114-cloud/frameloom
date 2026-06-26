namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// Archetype 仿真系统基类 — 纯同步管线，使用 SimArchetypeWorld。
    /// </summary>
    public abstract class SimArchetypeSystemBase
    {
        public string SystemName => GetType().Name;
        public bool Enabled { get; set; } = true;
        public int Order { get; set; }

        protected SimArchetypeWorld World { get; private set; }

        internal void SetWorld(SimArchetypeWorld world) => World = world;

        public virtual void Init() { }
        public virtual void Update() { }
        public virtual void Cleanup() { }
    }
}
