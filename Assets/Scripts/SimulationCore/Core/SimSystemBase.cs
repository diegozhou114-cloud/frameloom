namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真系统基类 — 纯同步管线，去掉 UniTask 异步。
    /// Order 决定 UpdateAll 执行顺序。
    /// </summary>
    public abstract class SimSystemBase
    {
        public string SystemName => GetType().Name;
        public bool Enabled { get; set; } = true;
        public int Order { get; set; }

        protected SimWorld World { get; private set; }

        internal void SetWorld(SimWorld world) => World = world;

        /// <summary>初始化（同步）</summary>
        public virtual void Init() { }

        /// <summary>每 Tick 执行</summary>
        public virtual void Update() { }

        /// <summary>清理（同步）</summary>
        public virtual void Cleanup() { }
    }
}
