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
        public virtual UniTask InitAsync() => UniTask.CompletedTask;
        public virtual void Update() { }
        public virtual UniTask CleanupAsync() => UniTask.CompletedTask;
    }
}
