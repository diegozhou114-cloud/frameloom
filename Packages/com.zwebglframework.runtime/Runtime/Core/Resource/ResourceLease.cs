using System;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 一次资源借用。调用方在 Unity 主线程释放；与其他借用和管理器缓存独立。
    /// Dispose 后 Asset 为 null，底层释放操作最多执行一次。
    /// </summary>
    public sealed class ResourceLease<T> : IDisposable where T : UnityEngine.Object
    {
        private Action _release;

        /// <summary>当前持有的资源；释放后为 null。</summary>
        public T Asset { get; private set; }

        /// <summary>是否已经释放。</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>创建借用；release 只能释放本次持有的底层引用。</summary>
        public ResourceLease(T asset, Action release)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            Asset = asset;
            _release = release;
        }

        /// <summary>释放本次借用，不影响其他调用者。</summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Asset = null;
            Action release = _release;
            _release = null;
            release?.Invoke();
        }
    }
}
