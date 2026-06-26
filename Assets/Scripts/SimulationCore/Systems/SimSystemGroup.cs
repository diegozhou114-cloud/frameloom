using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    public class SimSystemGroup
    {
        private readonly List<SimSystemBase> _systems = new List<SimSystemBase>();
        private bool _isSorted = true;

        public void Add(SimSystemBase system) { _systems.Add(system); _isSorted = false; }
        public void Add(params SimSystemBase[] systems) { _systems.AddRange(systems); _isSorted = false; }

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

        public void UpdateAll()
        {
            foreach (var sys in _systems)
                if (sys.Enabled) sys.Update();
        }

        public void CleanupAll()
        {
            foreach (var sys in _systems)
                if (sys.Enabled) sys.Cleanup();
        }

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
