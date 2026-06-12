using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZWebGLFrameWork.ECS
{
    public class SystemGroup
    {
        private readonly List<SystemBase> _systems = new List<SystemBase>();
        private bool _isSorted = true;

        public void Add(SystemBase system) { _systems.Add(system); _isSorted = false; }
        public void Add(params SystemBase[] systems) { _systems.AddRange(systems); _isSorted = false; }

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

        public void UpdateAll()
        {
            foreach (var sys in _systems)
                if (sys.Enabled) sys.Update();
        }

        public async Task CleanupAllAsync()
        {
            foreach (var sys in _systems)
                if (sys.Enabled) await sys.CleanupAsync();
        }

        /// <summary>查找指定类型的系统</summary>
        public T Find<T>() where T : SystemBase
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
