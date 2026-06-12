using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.ECS
{
    public class World
    {
        public int EntityCount => _entities.Count;
        public event Action<Entity> OnEntityCreated;
        public event Action<Entity> OnEntityDestroyed;

        private readonly List<Entity> _entities = new List<Entity>();
        private readonly Queue<int> _freeIds = new Queue<int>();
        private int[] _generations;
        private int _nextId;
        private readonly Dictionary<Type, object> _componentPools = new Dictionary<Type, object>();

        public World(int initialCapacity = 256) { _generations = new int[initialCapacity]; }

        public Entity CreateEntity()
        {
            int id;
            if (_freeIds.Count > 0) { id = _freeIds.Dequeue(); _generations[id]++; }
            else
            {
                id = _nextId++;
                if (id >= _generations.Length) Array.Resize(ref _generations, _generations.Length * 2);
                _generations[id] = 1;
            }
            var entity = new Entity(id, _generations[id]);
            _entities.Add(entity);
            OnEntityCreated?.Invoke(entity);
            return entity;
        }

        public void DestroyEntity(Entity entity)
        {
            if (!entity.IsValid) return;
            foreach (var kv in _componentPools)
            {
                var poolType = typeof(ComponentPool<>).MakeGenericType(kv.Key);
                poolType.GetMethod("Remove")?.Invoke(kv.Value, new object[] { entity });
            }
            _entities.RemoveAll(e => e == entity);
            _freeIds.Enqueue(entity.Id);
            OnEntityDestroyed?.Invoke(entity);
        }

        public void AddComponent<T>(Entity entity, T component) where T : struct, IComponent => GetPool<T>().Add(entity, component);
        public ref T GetComponent<T>(Entity entity) where T : struct, IComponent => ref GetPool<T>().Get(entity);
        public bool TryGetComponent<T>(Entity entity, out T component) where T : struct, IComponent => GetPool<T>().TryGet(entity, out component);
        public bool HasComponent<T>(Entity entity) where T : struct, IComponent => GetPool<T>().Has(entity);
        public void RemoveComponent<T>(Entity entity) where T : struct, IComponent => GetPool<T>().Remove(entity);
        public IEnumerable<Entity> GetEntitiesWith<T>() where T : struct, IComponent => GetPool<T>().GetEntities();
        public void ForEachEntities<T>(Action<Entity> action) where T : struct, IComponent => GetPool<T>().ForEach((e, _) => action(e));
        public void ForEachComponent<T>(Action<Entity, ref T> action) where T : struct, IComponent => GetPool<T>().ForEach(action);

        // 事件队列
        private readonly Queue<DamageEvent> _damageEvents = new Queue<DamageEvent>();
        public void EnqueueDamageEvent(DamageEvent evt) => _damageEvents.Enqueue(evt);
        public int ConsumeDamageEvents(Action<DamageEvent> handler)
        {
            int count = _damageEvents.Count;
            while (_damageEvents.Count > 0) handler(_damageEvents.Dequeue());
            return count;
        }

        private ComponentPool<T> GetPool<T>() where T : struct, IComponent
        {
            var type = typeof(T);
            if (!_componentPools.TryGetValue(type, out var pool))
            {
                pool = new ComponentPool<T>(_generations.Length);
                _componentPools[type] = pool;
            }
            return (ComponentPool<T>)pool;
        }
    }

    internal class ComponentPool<T> where T : struct, IComponent
    {
        private int[] _sparse;
        private Entity[] _denseEntities;
        private T[] _denseComponents;
        private int _count;

        public ComponentPool(int initialCapacity)
        {
            _sparse = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++) _sparse[i] = -1;
            _denseEntities = new Entity[initialCapacity];
            _denseComponents = new T[initialCapacity];
        }

        public void Add(Entity entity, T component)
        {
            if (Has(entity)) return;
            int id = entity.Id;
            if (id >= _sparse.Length) ResizeSparse(id + 1);
            if (_count >= _denseEntities.Length) ResizeDense(_count * 2);
            _sparse[id] = _count;
            _denseEntities[_count] = entity;
            _denseComponents[_count] = component;
            _count++;
        }

        public ref T Get(Entity entity)
        {
            int idx = _sparse[entity.Id];
            if (idx < 0 || idx >= _count || _denseEntities[idx] != entity)
                throw new InvalidOperationException($"Entity {entity} 没有组件 {typeof(T).Name}");
            return ref _denseComponents[idx];
        }

        public bool TryGet(Entity entity, out T component)
        {
            int id = entity.Id;
            if (id < _sparse.Length)
            {
                int idx = _sparse[id];
                if (idx >= 0 && idx < _count && _denseEntities[idx] == entity)
                { component = _denseComponents[idx]; return true; }
            }
            component = default; return false;
        }

        public bool Has(Entity entity)
        {
            int id = entity.Id;
            return id < _sparse.Length && _sparse[id] >= 0 && _sparse[id] < _count && _denseEntities[_sparse[id]] == entity;
        }

        public void Remove(Entity entity)
        {
            if (!Has(entity)) return;
            int idx = _sparse[entity.Id], lastIdx = _count - 1;
            if (idx != lastIdx)
            {
                _denseEntities[idx] = _denseEntities[lastIdx];
                _denseComponents[idx] = _denseComponents[lastIdx];
                _sparse[_denseEntities[lastIdx].Id] = idx;
            }
            _sparse[entity.Id] = -1;
            _count--;
        }

        public IEnumerable<Entity> GetEntities() { for (int i = 0; i < _count; i++) yield return _denseEntities[i]; }
        public void ForEach(Action<Entity, ref T> action) { for (int i = 0; i < _count; i++) action(_denseEntities[i], ref _denseComponents[i]); }

        private void ResizeSparse(int newSize) { int old = _sparse.Length; Array.Resize(ref _sparse, newSize); for (int i = old; i < newSize; i++) _sparse[i] = -1; }
        private void ResizeDense(int newSize) { Array.Resize(ref _denseEntities, newSize); Array.Resize(ref _denseComponents, newSize); }
    }
}
