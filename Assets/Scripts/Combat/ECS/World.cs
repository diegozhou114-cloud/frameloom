using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.Combat.ECS
{
    /// <summary>
    /// ECS 世界 — 管理所有 Entity 和 Component。
    /// 每个组件类型独立存储（SparseSet：Entity → Component，O(1) 增删查）。
    /// 无 Archetype，简单高效，适合中小规模战斗（< 500 单位）。
    /// </summary>
    public class World
    {
        /// <summary>当前实体总数</summary>
        public int EntityCount => _entities.Count;

        /// <summary>实体创建事件</summary>
        public event Action<Entity> OnEntityCreated;
        /// <summary>实体销毁事件</summary>
        public event Action<Entity> OnEntityDestroyed;

        // Entity 池
        private readonly List<Entity> _entities = new List<Entity>();
        private readonly Queue<int> _freeIds = new Queue<int>();
        private int[] _generations;
        private int _nextId;

        // Component 存储：每个类型独立 SparseSet
        private readonly Dictionary<Type, object> _componentPools = new Dictionary<Type, object>();
        // componentPools[type] = ComponentPool<T>

        public World(int initialCapacity = 256)
        {
            _generations = new int[initialCapacity];
        }

        // ==================== Entity ====================

        /// <summary>创建实体</summary>
        public Entity CreateEntity()
        {
            int id;
            if (_freeIds.Count > 0)
            {
                id = _freeIds.Dequeue();
                _generations[id]++;
            }
            else
            {
                id = _nextId++;
                if (id >= _generations.Length)
                    Array.Resize(ref _generations, _generations.Length * 2);
                _generations[id] = 1;
            }

            var entity = new Entity(id, _generations[id]);
            _entities.Add(entity);
            OnEntityCreated?.Invoke(entity);
            return entity;
        }

        /// <summary>销毁实体（回收所有组件）</summary>
        public void DestroyEntity(Entity entity)
        {
            if (!entity.IsValid) return;

            // 移除所有组件
            foreach (var kv in _componentPools)
            {
                var poolType = typeof(ComponentPool<>).MakeGenericType(kv.Key);
                var removeMethod = poolType.GetMethod("Remove");
                removeMethod?.Invoke(kv.Value, new object[] { entity });
            }

            _entities.RemoveAll(e => e == entity);
            _freeIds.Enqueue(entity.Id);
            OnEntityDestroyed?.Invoke(entity);
        }

        // ==================== Component ====================

        /// <summary>添加组件</summary>
        public void AddComponent<T>(Entity entity, T component) where T : struct, IComponent
        {
            GetPool<T>().Add(entity, component);
        }

        /// <summary>获取组件</summary>
        public ref T GetComponent<T>(Entity entity) where T : struct, IComponent
        {
            return ref GetPool<T>().Get(entity);
        }

        /// <summary>尝试获取组件</summary>
        public bool TryGetComponent<T>(Entity entity, out T component) where T : struct, IComponent
        {
            return GetPool<T>().TryGet(entity, out component);
        }

        /// <summary>是否有组件</summary>
        public bool HasComponent<T>(Entity entity) where T : struct, IComponent
        {
            return GetPool<T>().Has(entity);
        }

        /// <summary>移除组件</summary>
        public void RemoveComponent<T>(Entity entity) where T : struct, IComponent
        {
            GetPool<T>().Remove(entity);
        }

        /// <summary>遍历拥有某组件的所有实体</summary>
        public IEnumerable<Entity> GetEntitiesWith<T>() where T : struct, IComponent
        {
            return GetPool<T>().GetEntities();
        }

        // ==================== 内部 ====================

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

    /// <summary>
    /// SparseSet 组件池 — Entity ID 到 Component 的映射。
    /// 密排存储，O(1) 增删查，迭代时跳过空洞。
    /// </summary>
    internal class ComponentPool<T> where T : struct, IComponent
    {
        // Sparse: entityId → denseIndex (sparse[3] = 1 表示 entity 3 在 dense[1])
        private int[] _sparse;
        // Dense: 密排 Entity
        private Entity[] _denseEntities;
        // Dense: 密排 Component
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
                {
                    component = _denseComponents[idx];
                    return true;
                }
            }
            component = default;
            return false;
        }

        public bool Has(Entity entity)
        {
            int id = entity.Id;
            return id < _sparse.Length && _sparse[id] >= 0 &&
                   _sparse[id] < _count && _denseEntities[_sparse[id]] == entity;
        }

        public void Remove(Entity entity)
        {
            if (!Has(entity)) return;

            int idx = _sparse[entity.Id];
            int lastIdx = _count - 1;

            // Swap-remove：用最后一个覆盖当前
            if (idx != lastIdx)
            {
                _denseEntities[idx] = _denseEntities[lastIdx];
                _denseComponents[idx] = _denseComponents[lastIdx];
                _sparse[_denseEntities[lastIdx].Id] = idx;
            }

            _sparse[entity.Id] = -1;
            _count--;
        }

        public IEnumerable<Entity> GetEntities()
        {
            for (int i = 0; i < _count; i++)
                yield return _denseEntities[i];
        }

        /// <summary>遍历所有组件引用（高性能迭代）</summary>
        public void ForEach(Action<Entity, ref T> action)
        {
            for (int i = 0; i < _count; i++)
                action(_denseEntities[i], ref _denseComponents[i]);
        }

        private void ResizeSparse(int newSize)
        {
            int oldSize = _sparse.Length;
            Array.Resize(ref _sparse, newSize);
            for (int i = oldSize; i < newSize; i++) _sparse[i] = -1;
        }

        private void ResizeDense(int newSize)
        {
            Array.Resize(ref _denseEntities, newSize);
            Array.Resize(ref _denseComponents, newSize);
        }
    }
}
