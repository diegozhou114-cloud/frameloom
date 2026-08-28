using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真系统组 — 管理多个 SimSystemBase，按 Order 排序执行。
    /// </summary>
    public class SimSystemGroup
    {
        private readonly List<SimSystemBase> _systems = new List<SimSystemBase>();
        private readonly HashSet<SimSystemBase> _initializedSystems =
            new HashSet<SimSystemBase>();
        private bool _isSorted = true;
        private bool _isInitialized;
        private SimWorld _world;

        public void Add(SimSystemBase system)
        {
            _systems.Add(system);
            _isSorted = false;
            InitializeIfReady(system);
        }

        public void Add(params SimSystemBase[] systems)
        {
            foreach (SimSystemBase system in systems)
                Add(system);
        }

        /// <summary>初始化所有系统</summary>
        public void InitAll(SimWorld world)
        {
            _world = world;
            _isInitialized = true;
            Sort();
            foreach (var sys in _systems)
            {
                if (!sys.Enabled) continue;
                Initialize(sys);
            }
        }

        /// <summary>按 Order 更新所有系统</summary>
        public void UpdateAll()
        {
            Sort();
            foreach (var sys in _systems)
            {
                if (!sys.Enabled) continue;
                InitializeIfReady(sys);
                sys.Update();
            }
        }

        /// <summary>清理所有系统</summary>
        public void CleanupAll()
        {
            Sort();
            foreach (var sys in _systems)
            {
                if (_initializedSystems.Remove(sys))
                    sys.Cleanup();
            }
            _isInitialized = false;
            _world = null;
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

        private void InitializeIfReady(SimSystemBase system)
        {
            if (_isInitialized && system.Enabled && !_initializedSystems.Contains(system))
                Initialize(system);
        }

        private void Initialize(SimSystemBase system)
        {
            system.SetWorld(_world);
            system.Init();
            _initializedSystems.Add(system);
        }
    }
}
