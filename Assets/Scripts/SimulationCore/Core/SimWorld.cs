using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 确定性仿真世界 — 管理所有 SimEntity 和 SimComponentPool。
    /// 零 UnityEngine 依赖，可在 .NET Core 服务器上运行。
    /// </summary>
    public class SimWorld
    {
        private readonly ISimLogger _logger;

        public int EntityCount => _entities.Count;

        /// <summary>实体创建/销毁事件 — 表现层同步用</summary>
        public event Action<SimEntity> OnEntityCreated;
        public event Action<SimEntity> OnEntityDestroyed;

        private readonly List<SimEntity> _entities = new List<SimEntity>();
        private int _nextId;
        private int[] _generations;
        private readonly Dictionary<Type, ISimComponentPool> _componentPools = new Dictionary<Type, ISimComponentPool>();

        // ---- 事件队列（系统间通信，如 SimAttackSystem → SimDamageSystem） ----
        private readonly Dictionary<Type, List<object>> _eventQueues = new Dictionary<Type, List<object>>();

        public SimWorld(int initialCapacity = 256, ISimLogger logger = null)
        {
            _logger = logger;
            _generations = new int[initialCapacity];
        }

        // ==================== Entity 生命周期 ====================

        /// <summary>
        /// 创建实体。ID 单调递增，确保确定性顺序。
        /// （不复用 freeIds — Queue 的出队顺序取决于销毁顺序，破坏确定性。）
        /// </summary>
        public SimEntity CreateEntity()
        {
            int id = _nextId++;
            if (id >= _generations.Length)
                Array.Resize(ref _generations, _generations.Length * 2);
            _generations[id] = _generations[id] + 1;

            var entity = new SimEntity(id, _generations[id]);
            _entities.Add(entity);
            OnEntityCreated?.Invoke(entity);
            return entity;
        }

        public void DestroyEntity(SimEntity entity)
        {
            if (!entity.IsValid) return;
            foreach (var pool in _componentPools.Values)
                pool.Remove(entity);
            _entities.RemoveAll(e => e == entity);
            OnEntityDestroyed?.Invoke(entity);
        }

        // ==================== 组件操作 ====================

        public void AddComponent<T>(SimEntity entity, T component) where T : struct, ISimComponent
            => GetPool<T>().Add(entity, component);

        public ref T GetComponent<T>(SimEntity entity) where T : struct, ISimComponent
            => ref GetPool<T>().Get(entity);

        public bool TryGetComponent<T>(SimEntity entity, out T component) where T : struct, ISimComponent
            => GetPool<T>().TryGet(entity, out component);

        public bool HasComponent<T>(SimEntity entity) where T : struct, ISimComponent
            => GetPool<T>().Has(entity);

        public void RemoveComponent<T>(SimEntity entity) where T : struct, ISimComponent
            => GetPool<T>().Remove(entity);

        public void SetComponent<T>(SimEntity entity, T component) where T : struct, ISimComponent
            => GetPool<T>().Set(entity, component);

        // ==================== 遍历 ====================

        public IEnumerable<SimEntity> GetEntitiesWith<T>() where T : struct, ISimComponent
            => GetPool<T>().GetEntities();

        /// <summary>遍历拥有组件 T 的所有实体，传递实体+组件值（不修改组件）</summary>
        public void ForEachComponent<T>(SimComponentPool<T>.ValAction<SimEntity, T> action) where T : struct, ISimComponent
            => GetPool<T>().ForEach(action);

        /// <summary>遍历拥有组件 T 的所有实体（仅实体，不含组件数据）</summary>
        public void ForEachEntities<T>(Action<SimEntity> action) where T : struct, ISimComponent
            => GetPool<T>().ForEach(action);

        /// <summary>联合遍历同时拥有 T1 和 T2 的实体，ref 传递双组件（可用于修改）</summary>
        public void ForEachEntities<T1, T2>(SimComponentPool<T1>.RefPairAction<SimEntity, T1, T2> action)
            where T1 : struct, ISimComponent where T2 : struct, ISimComponent
            => GetPool<T1>().ForEachRefPair(GetPool<T2>(), action);

        // ==================== 帧内事件队列 ====================

        /// <summary>入队一个事件（本 Tick 内消费）</summary>
        public void EnqueueEvent<T>(T evt) where T : struct
        {
            var type = typeof(T);
            if (!_eventQueues.TryGetValue(type, out var list))
            {
                list = new List<object>();
                _eventQueues[type] = list;
            }
            list.Add(evt);
        }

        /// <summary>消费所有已入队的 T 类型事件（消费后清空）</summary>
        public void ConsumeEvents<T>(Action<T> consumer) where T : struct
        {
            var type = typeof(T);
            if (!_eventQueues.TryGetValue(type, out var list) || list.Count == 0)
                return;
            foreach (var obj in list)
                consumer((T)obj);
            list.Clear();
        }

        // ==================== 状态校验 ====================

        /// <summary>
        /// 计算当前世界状态的全量哈希 — FNV-1a 遍历所有组件池。
        /// 用于帧同步 Checksum 比对。
        /// </summary>
        public ulong ComputeChecksum()
        {
            ulong hash = 14695981039346656037UL; // FNV-1a offset basis
            hash ^= (ulong)_entities.Count;
            hash *= 1099511628211UL;
            // Iterate in sorted order by type name for deterministic checksum
            var sortedPools = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Type, ISimComponentPool>>(_componentPools);
            sortedPools.Sort((a, b) => string.CompareOrdinal(a.Key.FullName, b.Key.FullName));
            foreach (var kv in sortedPools)
            {
                hash = kv.Value.AccumulateHash(hash);
            }
            return hash;
        }

        // ==================== 快照（预留 FrameSync 回滚） ====================

        /// <summary>导出当前实体元数据快照</summary>
        public SimWorldSnapshot ExportSnapshot()
        {
            return new SimWorldSnapshot(_nextId, (int[])_generations.Clone(), _entities.ToArray());
        }

        /// <summary>从快照恢复实体元数据</summary>
        public void RestoreFrom(SimWorldSnapshot snapshot)
        {
            ClearPools();
            _nextId = snapshot.NextId;
            _generations = (int[])snapshot.Generations.Clone();
            _entities.Clear();
            _entities.AddRange(snapshot.Entities);
            // 组件数据恢复在 Phase 5c (FrameSync) 完善
        }

        /// <summary>导出完整快照（含所有组件数据）</summary>
        public TickSnapshot ExportFullSnapshot()
        {
            var snapshot = new TickSnapshot
            {
                NextId = _nextId,
                Generations = (int[])_generations.Clone(),
                Entities = _entities.ToArray(),
                Pools = new List<PoolSnapshot>(),
                Checksum = ComputeChecksum(),
            };
            // Iterate in sorted order by type name for deterministic snapshot
            var sortedPools = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Type, ISimComponentPool>>(_componentPools);
            sortedPools.Sort((a, b) => string.CompareOrdinal(a.Key.FullName, b.Key.FullName));
            foreach (var kv in sortedPools)
            {
                snapshot.Pools.Add(kv.Value.ExportDense());
            }
            return snapshot;
        }

        /// <summary>从完整快照恢复（含所有组件数据）</summary>
        public void RestoreFromFull(TickSnapshot snapshot)
        {
            // 清空所有组件池
            foreach (var pool in _componentPools.Values)
                pool.Clear();

            _nextId = snapshot.NextId;
            _generations = (int[])snapshot.Generations.Clone();
            _entities.Clear();
            _entities.AddRange(snapshot.Entities);

            // 恢复各组件池
            foreach (var poolSnapshot in snapshot.Pools)
            {
                var type = Type.GetType(poolSnapshot.ComponentTypeName);
                if (type != null && _componentPools.TryGetValue(type, out var pool))
                {
                    pool.RestoreDense(poolSnapshot);
                }
            }
        }

        // ==================== 清理 ====================

        public void ClearPools()
        {
            foreach (var pool in _componentPools.Values)
                pool.Clear();
            foreach (var list in _eventQueues.Values)
                list.Clear();
        }

        // ==================== 内部 ====================

        private SimComponentPool<T> GetPool<T>() where T : struct, ISimComponent
        {
            var type = typeof(T);
            if (!_componentPools.TryGetValue(type, out var pool))
            {
                pool = new SimComponentPool<T>(_generations.Length);
                _componentPools[type] = pool;
            }
            return (SimComponentPool<T>)pool;
        }
    }

    /// <summary>仿真世界快照 — 用于回滚恢复（Phase 5c 完善）</summary>
    public struct SimWorldSnapshot
    {
        public int NextId;
        public int[] Generations;
        public SimEntity[] Entities;

        public SimWorldSnapshot(int nextId, int[] generations, SimEntity[] entities)
        {
            NextId = nextId;
            Generations = generations;
            Entities = entities;
        }
    }
}
