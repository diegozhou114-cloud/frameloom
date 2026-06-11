using System.Threading.Tasks;

namespace ZWebGLFrameWork.Combat.ECS
{
    /// <summary>
    /// 系统基类 — 所有战斗系统的父类。
    /// Init：战斗开始前初始化
    /// Update：每帧调用（主线程）
    /// Cleanup：战斗结束后清理
    /// 
    /// 异步支持：系统的 Init/Cleanup 可以是 async Task，
    /// 与 UniTask API 对齐（后续替换为 UniTask 只需改类型名）。
    /// </summary>
    public abstract class SystemBase
    {
        /// <summary>系统名称</summary>
        public string SystemName => GetType().Name;

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>执行顺序（越小越先执行）</summary>
        public int Order { get; set; }

        /// <summary>所属 World</summary>
        protected World World { get; private set; }

        internal void SetWorld(World world) => World = world;

        /// <summary>初始化（战斗开始前，支持 async）</summary>
        public virtual Task InitAsync() => Task.CompletedTask;

        /// <summary>每帧更新</summary>
        public virtual void Update() { }

        /// <summary>清理（战斗结束后，支持 async）</summary>
        public virtual Task CleanupAsync() => Task.CompletedTask;
    }
}
