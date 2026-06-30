using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.ECS
{
    /// <summary>支持 ref 参数的双参数委托</summary>
    public delegate void RefAction<T1, T2>(T1 arg1, ref T2 arg2);

    /// <summary>支持 ref 参数的三参数委托</summary>
    public delegate void RefAction<T1, T2, T3>(T1 arg1, ref T2 arg2, ref T3 arg3);

    /// <summary>值传递双参数委托</summary>
    public delegate void ValAction<T1, T2>(T1 arg1, T2 arg2);

    /// <summary>值传递三参数委托</summary>
    public delegate void ValAction<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3);

    public class World
    {
        public int EntityCount => _entities.Count;
        public event Action<Entity> OnEntityCreated;
        public event Action<Entity> OnEntityDestroyed;

        /// <summary>组件变更事件（Entity, ComponentType, Added=true表示新增/false表示移除）</summary>
        public event Action<Entity, Type, bool> OnComponentChanged;

        private readonly List<Entity> _entities = new List<Entity>();
        private readonly Queue<int> _freeIds = new Queue<int>();
        private int[] _generations;
        private int _nextId;
        private readonly Dictionary<Type, IComponentPool> _componentPools = new Dictionary<Type, IComponentPool>();
        private readonly Dictionary<int, int> _entityIndexMap = new Dictionary<int, int>();

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
            _entityIndexMap[id] = _entities.Count - 1;
            OnEntityCreated?.Invoke(entity);
            return entity;
        }

        public void DestroyEntity(Entity entity)
        {
            // 验证 entity 句柄有效性（ID 范围 + Generation 匹配）
            if (entity.Id < 0 || entity.Id >= _generations.Length) return;
            if (entity.Generation != _generations[entity.Id]) return;

            // 通过 IComponentPool 接口移除所有组件 — 零反射
            foreach (var pool in _componentPools.Values)
            {
                pool.Remove(entity);
            }

            // O(1) swap-remove 从 _entities 列表移除
            if (_entityIndexMap.TryGetValue(entity.Id, out int index))
            {
                int lastIdx = _entities.Count - 1;
                if (index != lastIdx)
                {
                    _entities[index] = _entities[lastIdx];
                    _entityIndexMap[_entities[index].Id] = index;
                }
                _entities.RemoveAt(lastIdx);
                _entityIndexMap.Remove(entity.Id);
            }

            _freeIds.Enqueue(entity.Id);
            OnEntityDestroyed?.Invoke(entity);
        }

        public void AddComponent<T>(Entity entity, T component) where T : struct, IComponent
        {
            bool added = !HasComponent<T>(entity);
            GetPool<T>().Add(entity, component);
            if (added) OnComponentChanged?.Invoke(entity, typeof(T), true);
        }
        public ref T GetComponent<T>(Entity entity) where T : struct, IComponent => ref GetPool<T>().Get(entity);
        public bool TryGetComponent<T>(Entity entity, out T component) where T : struct, IComponent => GetPool<T>().TryGet(entity, out component);
        public bool HasComponent<T>(Entity entity) where T : struct, IComponent => GetPool<T>().Has(entity);
        public void RemoveComponent<T>(Entity entity) where T : struct, IComponent
        {
            bool removed = HasComponent<T>(entity);
            GetPool<T>().Remove(entity);
            if (removed) OnComponentChanged?.Invoke(entity, typeof(T), false);
        }

        /// <summary>设置组件（存在则覆盖，不存在则新增）— 解决 TryGetComponent 副本修改后无法写回的问题</summary>
        public void SetComponent<T>(Entity entity, T component) where T : struct, IComponent => GetPool<T>().Set(entity, component);
        public IEnumerable<Entity> GetEntitiesWith<T>() where T : struct, IComponent => GetPool<T>().GetEntities();
        public void ForEachEntities<T>(Action<Entity> action) where T : struct, IComponent => GetPool<T>().ForEach(action);
        public void ForEachComponent<T>(ValAction<Entity, T> action) where T : struct, IComponent => GetPool<T>().ForEach(action);

        /// <summary>两组件联合遍历（ref 版本，支持写入）</summary>
        public void ForEachEntities<TA, TB>(RefAction<Entity, TA, TB> action)
            where TA : struct, IComponent where TB : struct, IComponent
        {
            GetPool<TA>().ForEachRefPair(GetPool<TB>(), action);
        }

        /// <summary>两组件联合遍历（值传递版本）</summary>
        public void ForEachEntities<TA, TB>(ValAction<Entity, TA, TB> action)
            where TA : struct, IComponent where TB : struct, IComponent
        {
            GetPool<TA>().ForEachRefPair(GetPool<TB>(), action);
        }

        // ==================== 通用事件队列（泛型，不耦合业务类型） ====================

        private readonly Dictionary<Type, object> _eventQueues = new Dictionary<Type, object>();

        /// <summary>入队一个事件</summary>
        public void EnqueueEvent<T>(T evt) where T : struct
        {
            GetEventQueue<T>().Enqueue(evt);
        }

        /// <summary>消费所有事件，返回消费数量</summary>
        public int ConsumeEvents<T>(Action<T> handler) where T : struct
        {
            var queue = GetEventQueue<T>();
            int count = queue.Count;
            while (queue.Count > 0)
                handler(queue.Dequeue());
            return count;
        }

        private Queue<T> GetEventQueue<T>() where T : struct
        {
            var type = typeof(T);
            if (!_eventQueues.TryGetValue(type, out var q))
            {
                q = new Queue<T>();
                _eventQueues[type] = q;
            }
            return (Queue<T>)q;
        }

        // ==================== 兼容旧 API ====================

        /// <summary>清空所有组件池（场景切换时调用）</summary>
        public void ClearPools()
        {
            foreach (var pool in _componentPools.Values)
                pool.Clear();
        }

        /// <summary>完全重置 World（清空所有实体、组件、事件队列）</summary>
        public void Reset()
        {
            _entities.Clear();
            _entityIndexMap.Clear();
            _freeIds.Clear();
            _nextId = 0;
            foreach (var pool in _componentPools.Values)
            {
                pool.Clear();
            }
            // 清理事件队列（清空字典即可，GetEventQueue 会按需重建）
            _eventQueues.Clear();
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

    internal class ComponentPool<T> : IComponentPool where T : struct, IComponent
    {
        private int[] _sparse;
        private Entity[] _denseEntities;
        private T[] _denseComponents;
        private int _count;

        /// <summary>当前存活的组件数</summary>
        public int Count => _count;

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

        /// <summary>设置组件 — 存在则覆盖，不存在则新增</summary>
        public void Set(Entity entity, T component)
        {
            int id = entity.Id;
            if (id < _sparse.Length)
            {
                int idx = _sparse[id];
                if (idx >= 0 && idx < _count && _denseEntities[idx] == entity)
                {
                    _denseComponents[idx] = component;
                    return;
                }
            }
            Add(entity, component);
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

        public void Clear()
        {
            for (int i = 0; i < _count; i++)
                _sparse[_denseEntities[i].Id] = -1;
            _count = 0;
        }

        public IEnumerable<Entity> GetEntities() { for (int i = 0; i < _count; i++) yield return _denseEntities[i]; }
        public void ForEach(Action<Entity> action) { for (int i = 0; i < _count; i++) action(_denseEntities[i]); }
        public void ForEach(ValAction<Entity, T> action) { for (int i = 0; i < _count; i++) action(_denseEntities[i], _denseComponents[i]); }
        public void ForEach(RefAction<Entity, T> action) { for (int i = 0; i < _count; i++) action(_denseEntities[i], ref _denseComponents[i]); }

        /// <summary>
        /// 联合遍历：遍历本池所有实体，对同时存在于 otherPool 的实体执行 action。
        /// 两个 ref 都直接引用各池的密集数组，支持写入。
        /// </summary>
        internal void ForEachRefPair<TOther>(ComponentPool<TOther> otherPool,
            RefAction<Entity, T, TOther> action) where TOther : struct, IComponent
        {
            // 优化：遍历较小的池以减少迭代次数
            if (_count <= otherPool._count)
            {
                for (int i = 0; i < _count; i++)
                {
                    var entity = _denseEntities[i];
                    int id = entity.Id;
                    if (id < otherPool._sparse.Length)
                    {
                        int otherIdx = otherPool._sparse[id];
                        if (otherIdx >= 0 && otherIdx < otherPool._count &&
                            otherPool._denseEntities[otherIdx] == entity)
                        {
                            action(entity, ref _denseComponents[i],
                                ref otherPool._denseComponents[otherIdx]);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < otherPool._count; i++)
                {
                    var entity = otherPool._denseEntities[i];
                    int id = entity.Id;
                    if (id < _sparse.Length)
                    {
                        int thisIdx = _sparse[id];
                        if (thisIdx >= 0 && thisIdx < _count &&
                            _denseEntities[thisIdx] == entity)
                        {
                            action(entity, ref _denseComponents[thisIdx],
                                ref otherPool._denseComponents[i]);
                        }
                    }
                }
            }
        }

        internal void ForEachRefPair<TOther>(ComponentPool<TOther> otherPool,
            ValAction<Entity, T, TOther> action) where TOther : struct, IComponent
        {
            // 优化：遍历较小的池以减少迭代次数
            if (_count <= otherPool._count)
            {
                for (int i = 0; i < _count; i++)
                {
                    var entity = _denseEntities[i];
                    int id = entity.Id;
                    if (id < otherPool._sparse.Length)
                    {
                        int otherIdx = otherPool._sparse[id];
                        if (otherIdx >= 0 && otherIdx < otherPool._count &&
                            otherPool._denseEntities[otherIdx] == entity)
                        {
                            action(entity, _denseComponents[i],
                                otherPool._denseComponents[otherIdx]);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < otherPool._count; i++)
                {
                    var entity = otherPool._denseEntities[i];
                    int id = entity.Id;
                    if (id < _sparse.Length)
                    {
                        int thisIdx = _sparse[id];
                        if (thisIdx >= 0 && thisIdx < _count &&
                            _denseEntities[thisIdx] == entity)
                        {
                            action(entity, _denseComponents[thisIdx],
                                otherPool._denseComponents[i]);
                        }
                    }
                }
            }
        }

        private void ResizeSparse(int newSize) { int old = _sparse.Length; Array.Resize(ref _sparse, newSize); for (int i = old; i < newSize; i++) _sparse[i] = -1; }
        private void ResizeDense(int newSize) { Array.Resize(ref _denseEntities, newSize); Array.Resize(ref _denseComponents, newSize); }
    }
}
