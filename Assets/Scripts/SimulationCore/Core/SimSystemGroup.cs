using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真系统组 — 管理多个 SimSystemBase，按 Order 排序执行。
    /// </summary>
    public class SimSystemGroup
    {
        private readonly List<SimSystemBase> _systems = new List<SimSystemBase>();
        private bool _isSorted = true;

        public void Add(SimSystemBase system)
        {
            _systems.Add(system);
            _isSorted = false;
        }

        public void Add(params SimSystemBase[] systems)
        {
            _systems.AddRange(systems);
            _isSorted = false;
        }

        /// <summary>初始化所有系统</summary>
        public void InitAll(SimWorld world)
        {
            Sort();
            foreach (var sys in _systems)
            {
                if (!sys.Enabled) continue;
                sys.SetWorld(world);
                sys.Init();
            }
        }

        /// <summary>按 Order 更新所有系统</summary>
        public void UpdateAll()
        {
            Sort();
            foreach (var sys in _systems)
            {
                if (sys.Enabled)
                    sys.Update();
            }
        }

        /// <summary>清理所有系统</summary>
        public void CleanupAll()
        {
            Sort();
            foreach (var sys in _systems)
            {
                if (sys.Enabled)
                    sys.Cleanup();
            }
        }

        /// <summary>查找指定类型的系统</summary>
        public T Find<T>() where T : SimSystemBase
        {
            foreach (var sys in _systems)
                if (sys is T t) return t;
            return null;
        }

        private void Sort()
        {
            if (_isSorted) return;
            _systems.Sort((a, b) => a.Order.CompareTo(b.Order));
            _isSorted = true;
        }
    }
}
