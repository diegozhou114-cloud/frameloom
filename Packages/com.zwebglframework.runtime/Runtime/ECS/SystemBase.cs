using Cysharp.Threading.Tasks;

namespace ZWebGLFrameWork.ECS
{
    public abstract class SystemBase
    {
        public string SystemName => GetType().Name;
        public bool Enabled { get; set; } = true;
        public int Order { get; set; }
        protected World World { get; private set; }
        internal void SetWorld(World world) => World = world;
        // NOTE: InitAsync/CleanupAsync return UniTask, forcing every system to depend on
        // UniTask even when it doesn't need async init/cleanup. Consider splitting into
        // IAsyncSystem { InitAsync; CleanupAsync } so sync-only systems avoid the dependency.
        public virtual UniTask InitAsync() => UniTask.CompletedTask;
        public virtual void Update() { }
        public virtual UniTask CleanupAsync() => UniTask.CompletedTask;
    }
}
