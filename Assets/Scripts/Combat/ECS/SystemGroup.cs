using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZWebGLFrameWork.Combat.ECS
{
    /// <summary>
    /// 系统组 — 按 Order 排序执行的一组系统。
    /// 支持异步初始化（InitAllAsync）和清理（CleanupAllAsync）。
    /// </summary>
    public class SystemGroup
    {
        private readonly List<SystemBase> _systems = new List<SystemBase>();
        private bool _isSorted = true;

        /// <summary>添加系统</summary>
        public void Add(SystemBase system)
        {
            _systems.Add(system);
            _isSorted = false;
        }

        /// <summary>添加多个系统</summary>
        public void Add(params SystemBase[] systems)
        {
            _systems.AddRange(systems);
            _isSorted = false;
        }

        /// <summary>异步初始化所有系统</summary>
        public async Task InitAllAsync(World world)
        {
            Sort();

            foreach (var sys in _systems)
            {
                if (!sys.Enabled) continue;
                sys.SetWorld(world);
                await sys.InitAsync();
            }
        }

        /// <summary>每帧更新所有系统</summary>
        public void UpdateAll()
        {
            foreach (var sys in _systems)
            {
                if (!sys.Enabled) continue;
                sys.Update();
            }
        }

        /// <summary>异步清理所有系统</summary>
        public async Task CleanupAllAsync()
        {
            foreach (var sys in _systems)
            {
                if (!sys.Enabled) continue;
                await sys.CleanupAsync();
            }
        }

        private void Sort()
        {
            if (_isSorted) return;
            _systems.Sort((a, b) => a.Order.CompareTo(b.Order));
            _isSorted = true;
        }
    }
}
