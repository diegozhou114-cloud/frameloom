using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// Archetype 驱动仿真世界 — Chunk 连续存储替代 SparseSet。
    /// 同原型实体在同一 Chunk 内连续排列，双组件遍历零跳转。
    /// </summary>
    public class SimWorld
    {
        private readonly ISimLogger _logger;
        private readonly ComponentTypeRegistry _registry;

        public int EntityCount { get; private set; }

        public event Action<SimEntity> OnEntityCreated;
        public event Action<SimEntity> OnEntityDestroyed;

        // Entity 定位：entityId → (archetypeKey, chunkIndex, localIndex)
        private string[] _entityArchetypeKey;
        private int[] _entityChunkIndex;
        private int[] _entityLocalIndex;

        private int _nextId;
        private int[] _generations;
        private int _generationCap;

        // Archetype 存储
        private readonly Dictionary<string, SimArchetype> _archetypes = new Dictionary<string, SimArchetype>();

        // 帧内事件队列
        private readonly Dictionary<Type, List<object>> _eventQueues = new Dictionary<Type, List<object>>();

        public SimWorld(ComponentTypeRegistry registry, int initialCapacity = 256, ISimLogger logger = null)
        {
            _registry = registry;
            _logger = logger;
            _generationCap = initialCapacity;
            _generations = new int[initialCapacity];
            _entityArchetypeKey = new string[initialCapacity];
            _entityChunkIndex = new int[initialCapacity];
            _entityLocalIndex = new int[initialCapacity];
        }

        // ==================== Entity 生命周期 ====================

        /// <summary>
        /// 创建实体并附加指定组件。组件类型集合决定 Archetype，
        /// 同一 Archetype 的实体在 Chunk 内连续排列。
        /// </summary>
        public SimEntity CreateEntity(params Type[] componentTypes)
        {
            // 确保 arrays 足够大
            int id = _nextId++;
            if (id >= _generationCap)
            {
                int newCap = _generationCap * 2;
                Array.Resize(ref _generations, newCap);
                Array.Resize(ref _entityArchetypeKey, newCap);
                Array.Resize(ref _entityChunkIndex, newCap);
                Array.Resize(ref _entityLocalIndex, newCap);
                _generationCap = newCap;
            }
            _generations[id]++;

            var entity = new SimEntity(id, _generations[id]);
            EntityCount++;

            // 确定 Archetype：按类型全名排序确保键确定性
            int[] compTypeIds = ArchetypeUtils.BuildComponentTypeIds(_registry, componentTypes);
            string key = SimArchetype.GetKey(compTypeIds);

            if (!_archetypes.TryGetValue(key, out var archetype))
            {
                archetype = new SimArchetype(compTypeIds);
                _archetypes[key] = archetype;
            }

            SimChunk chunk = archetype.GetOrCreateChunk();

            // 构建组件数组 — 顺序必须与 compTypeIds（已排序）一致
            // compTypeIds 已排序，通过 registry 找回每个 typeId 对应的 Type
            object[] comps = new object[compTypeIds.Length];
            for (int c = 0; c < compTypeIds.Length; c++)
            {
                Type compType = _registry.GetType(compTypeIds[c]);
                comps[c] = Activator.CreateInstance(compType);
            }

            int localIdx = chunk.Count;
            chunk.Add(id, _generations[id], comps);

            _entityArchetypeKey[id] = key;
            _entityChunkIndex[id] = archetype.Chunks.Count - 1;
            _entityLocalIndex[id] = localIdx;

            OnEntityCreated?.Invoke(entity);
            return entity;
        }

        /// <summary>
        /// 销毁实体。使用 swap-remove 保持 Chunk 内连续，O(1)。
        /// 空 Chunk 会被回收（非最后一个时与末尾交换后移除）。
        /// </summary>
        public void DestroyEntity(SimEntity entity)
        {
            if (!entity.IsValid) return;
            int id = entity.Id;
            if (id >= _generationCap || _generations[id] != entity.Generation) return;

            string key = _entityArchetypeKey[id];
            if (key == null) return;

            var archetype = _archetypes[key];
            int chunkIdx = _entityChunkIndex[id];
            int localIdx = _entityLocalIndex[id];

            var chunk = archetype.Chunks[chunkIdx];
            int movedId = chunk.RemoveAt(localIdx);

            // 更新被 swap 移过来的实体的索引
            if (movedId >= 0)
            {
                _entityChunkIndex[movedId] = chunkIdx;
                _entityLocalIndex[movedId] = localIdx;
            }

            // 如果 Chunk 已空且不是最后一个 Chunk，从列表中移除
            if (chunk.Count == 0 && archetype.Chunks.Count > 1)
            {
                int lastChunkIdx = archetype.Chunks.Count - 1;
                if (chunkIdx != lastChunkIdx)
                {
                    // 把最后一个 Chunk 移到当前位置，空 chunk 移到末尾
                    var lastChunk = archetype.Chunks[lastChunkIdx];
                    archetype.Chunks[chunkIdx] = lastChunk;
                    archetype.Chunks[lastChunkIdx] = chunk;
                    // 更新被移动 Chunk 中所有实体的 chunkIndex
                    for (int i = 0; i < lastChunk.Count; i++)
                        _entityChunkIndex[lastChunk.EntityIds[i]] = chunkIdx;
                }
                archetype.Chunks.RemoveAt(archetype.Chunks.Count - 1);
            }

            _entityArchetypeKey[id] = null;
            _generations[id] = 0; // 标记无效
            EntityCount--;
            OnEntityDestroyed?.Invoke(entity);
        }

        // ==================== 组件操作 ====================

        /// <summary>
        /// 增删组件需迁移实体到新 Archetype — 战斗内不触发，抛出异常。
        /// </summary>
        public void AddComponent<T>(SimEntity entity, T component) where T : struct, ISimComponent
        {
            throw new NotSupportedException("AddComponent after entity creation not supported in Archetype mode. " +
                "Use CreateEntity with all component types upfront.");
        }

        /// <summary>获取组件的 ref 引用（支持直接修改）</summary>
        public ref T GetComponentRef<T>(SimEntity entity) where T : struct, ISimComponent
        {
            int id = entity.Id;
            var archetype = _archetypes[_entityArchetypeKey[id]];
            int colIdx = archetype.ColumnIndex[_registry.GetId(typeof(T))];
            var chunk = archetype.Chunks[_entityChunkIndex[id]];
            return ref chunk.GetComponentRef<T>(_entityLocalIndex[id], colIdx);
        }

        /// <summary>尝试获取组件值拷贝</summary>
        public bool TryGetComponent<T>(SimEntity entity, out T component) where T : struct, ISimComponent
        {
            int id = entity.Id;
            if (id >= _generationCap || _entityArchetypeKey[id] == null)
            {
                component = default;
                return false;
            }
            string key = _entityArchetypeKey[id];
            var archetype = _archetypes[key];
            int typeId = _registry.GetId(typeof(T));
            if (!archetype.ColumnIndex.TryGetValue(typeId, out int colIdx))
            {
                component = default;
                return false;
            }
            var chunk = archetype.Chunks[_entityChunkIndex[id]];
            component = chunk.GetComponent<T>(_entityLocalIndex[id], colIdx);
            return true;
        }

        /// <summary>检查实体是否拥有指定组件</summary>
        public bool HasComponent<T>(SimEntity entity) where T : struct, ISimComponent
        {
            int id = entity.Id;
            if (id >= _generationCap || _entityArchetypeKey[id] == null) return false;
            var archetype = _archetypes[_entityArchetypeKey[id]];
            return archetype.ColumnIndex.ContainsKey(_registry.GetId(typeof(T)));
        }

        /// <summary>设置组件值</summary>
        public void SetComponent<T>(SimEntity entity, T component) where T : struct, ISimComponent
        {
            int id = entity.Id;
            var archetype = _archetypes[_entityArchetypeKey[id]];
            int colIdx = archetype.ColumnIndex[_registry.GetId(typeof(T))];
            var chunk = archetype.Chunks[_entityChunkIndex[id]];
            chunk.SetComponent(_entityLocalIndex[id], colIdx, component);
        }

        // ==================== 遍历 ====================

        /// <summary>遍历所有拥有 T 的 Chunk 中的所有实体（含组件值拷贝）</summary>
        public void ForEachComponent<T>(Action<SimEntity, T> action) where T : struct, ISimComponent
        {
            int typeId = _registry.GetId(typeof(T));
            foreach (var kv in _archetypes)
            {
                var archetype = kv.Value;
                if (!archetype.ColumnIndex.TryGetValue(typeId, out int colIdx)) continue;
                if (archetype.Chunks.Count == 0) continue;
                // 第一 Chunk 检测列是否已分配（惰性分配，无此组件的实体不会进入此 Archetype）
                if (archetype.Chunks[0].ComponentColumns[colIdx] == null) continue;

                foreach (var chunk in archetype.Chunks)
                {
                    T[] chunkCol = (T[])chunk.ComponentColumns[colIdx];
                    if (chunkCol == null) continue;
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        action(new SimEntity(chunk.EntityIds[i], chunk.Generations[i]), chunkCol[i]);
                    }
                }
            }
        }

        /// <summary>遍历所有拥有 T 的实体（仅实体，不含组件）</summary>
        public void ForEachEntities<T>(Action<SimEntity> action) where T : struct, ISimComponent
        {
            int typeId = _registry.GetId(typeof(T));
            foreach (var kv in _archetypes)
            {
                var archetype = kv.Value;
                if (!archetype.ColumnIndex.ContainsKey(typeId)) continue;
                foreach (var chunk in archetype.Chunks)
                {
                    for (int i = 0; i < chunk.Count; i++)
                        action(new SimEntity(chunk.EntityIds[i], chunk.Generations[i]));
                }
            }
        }

        /// <summary>双组件联合遍历 — 同 Chunk 内双列顺序扫描，零随机跳转</summary>
        public void ForEachEntities<T1, T2>(Action<SimEntity, T1, T2> action)
            where T1 : struct, ISimComponent where T2 : struct, ISimComponent
        {
            int typeId1 = _registry.GetId(typeof(T1));
            int typeId2 = _registry.GetId(typeof(T2));
            foreach (var kv in _archetypes)
            {
                var archetype = kv.Value;
                if (!archetype.ColumnIndex.TryGetValue(typeId1, out int col1)) continue;
                if (!archetype.ColumnIndex.TryGetValue(typeId2, out int col2)) continue;

                foreach (var chunk in archetype.Chunks)
                {
                    T1[] col1Arr = (T1[])chunk.ComponentColumns[col1];
                    T2[] col2Arr = (T2[])chunk.ComponentColumns[col2];
                    if (col1Arr == null || col2Arr == null) continue;
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        action(new SimEntity(chunk.EntityIds[i], chunk.Generations[i]), col1Arr[i], col2Arr[i]);
                    }
                }
            }
        }

        /// <summary>双组件联合遍历（ref 版本，支持修改）</summary>
        public void ForEachEntities<T1, T2>(RefAction<SimEntity, T1, T2> action)
            where T1 : struct, ISimComponent where T2 : struct, ISimComponent
        {
            int typeId1 = _registry.GetId(typeof(T1));
            int typeId2 = _registry.GetId(typeof(T2));
            foreach (var kv in _archetypes)
            {
                var archetype = kv.Value;
                if (!archetype.ColumnIndex.TryGetValue(typeId1, out int col1)) continue;
                if (!archetype.ColumnIndex.TryGetValue(typeId2, out int col2)) continue;

                foreach (var chunk in archetype.Chunks)
                {
                    T1[] col1Arr = (T1[])chunk.ComponentColumns[col1];
                    T2[] col2Arr = (T2[])chunk.ComponentColumns[col2];
                    if (col1Arr == null || col2Arr == null) continue;
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        action(new SimEntity(chunk.EntityIds[i], chunk.Generations[i]), ref col1Arr[i], ref col2Arr[i]);
                    }
                }
            }
        }

        /// <summary>双组件 ref 遍历委托</summary>
        public delegate void RefAction<TA, TB, TC>(TA arg1, ref TB arg2, ref TC arg3);

        // ==================== 事件队列 ====================

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
            if (!_eventQueues.TryGetValue(type, out var list) || list.Count == 0) return;
            foreach (var obj in list) consumer((T)obj);
            list.Clear();
        }

        // ==================== 校验 ====================

        /// <summary>
        /// 计算当前世界状态的全量哈希 — FNV-1a。
        /// 按 Archetype Key 排序确保确定性，通过 IHashableComponent 零装箱哈希各组件。
        /// </summary>
        public ulong ComputeChecksum()
        {
            ulong hash = 14695981039346656037UL;
            hash ^= (ulong)EntityCount;
            hash *= 1099511628211UL;

            // 按 Archetype Key 排序确保确定性
            var sortedKeys = new List<string>(_archetypes.Keys);
            sortedKeys.Sort();
            foreach (var key in sortedKeys)
            {
                var archetype = _archetypes[key];
                foreach (var chunk in archetype.Chunks)
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        // 哈希 Entity ID + Generation
                        const ulong prime = 1099511628211UL;
                        hash ^= (ulong)chunk.EntityIds[i]; hash *= prime;
                        hash ^= (ulong)chunk.Generations[i]; hash *= prime;

                        // 哈希每个组件（直接类型转换，零装箱）
                        for (int c = 0; c < archetype.ComponentTypeIds.Length; c++)
                        {
                            Array col = chunk.ComponentColumns[c];
                            if (col is SimTransformComponent[] tCol)
                                hash = tCol[i].HashInto(hash);
                            else if (col is SimHealthComponent[] hCol)
                                hash = hCol[i].HashInto(hash);
                            else if (col is SimTeamComponent[] teamCol)
                                hash = teamCol[i].HashInto(hash);
                            else if (col is SimMovementComponent[] mCol)
                                hash = mCol[i].HashInto(hash);
                            else if (col is SimAttackComponent[] aCol)
                                hash = aCol[i].HashInto(hash);
                            else if (col is SimTargetComponent[] tgtCol)
                                hash = tgtCol[i].HashInto(hash);
                        }
                    }
                }
            }
            return hash;
        }

        // ==================== 快照 ====================

        /// <summary>导出完整 Tick 快照（含所有 Chunk 的组件列数据 + Checksum）</summary>
        public TickSnapshot ExportFullSnapshot()
        {
            var snapshot = new TickSnapshot
            {
                NextId = _nextId,
                Generations = (int[])_generations.Clone(),
                Entities = GetAllEntities(),
                Pools = new List<PoolSnapshot>(),
                Checksum = ComputeChecksum(),
                ChunkSnapshots = new List<ChunkSnapshot>(),
            };

            // 按 Archetype Key 排序确保确定性
            var sortedKeys = new List<string>(_archetypes.Keys);
            sortedKeys.Sort();
            foreach (var key in sortedKeys)
            {
                var archetype = _archetypes[key];
                foreach (var chunk in archetype.Chunks)
                {
                    snapshot.ChunkSnapshots.Add(chunk.ExportSnapshot());
                }
            }

            return snapshot;
        }

        private SimEntity[] GetAllEntities()
        {
            var list = new List<SimEntity>();
            foreach (var kv in _archetypes)
                foreach (var chunk in kv.Value.Chunks)
                    for (int i = 0; i < chunk.Count; i++)
                        list.Add(new SimEntity(chunk.EntityIds[i], chunk.Generations[i]));
            return list.ToArray();
        }

        /// <summary>
        /// 从完整快照恢复 — 重建所有 Archetype/Chunk 及实体索引。
        /// </summary>
        public void RestoreFromFull(TickSnapshot snapshot)
        {
            _archetypes.Clear();
            _nextId = snapshot.NextId;
            _generations = (int[])snapshot.Generations.Clone();
            _generationCap = _generations.Length;
            _entityArchetypeKey = new string[_generationCap];
            _entityChunkIndex = new int[_generationCap];
            _entityLocalIndex = new int[_generationCap];
            EntityCount = 0;
            foreach (var list in _eventQueues.Values) list.Clear();

            // 从 Chunk 快照重建 Archetype 和 Chunk
            if (snapshot.ChunkSnapshots != null)
            {
                foreach (var chunkSnap in snapshot.ChunkSnapshots)
                {
                    string key = SimArchetype.GetKey(chunkSnap.ComponentTypeIds);
                    if (!_archetypes.TryGetValue(key, out var archetype))
                    {
                        archetype = new SimArchetype(chunkSnap.ComponentTypeIds);
                        _archetypes[key] = archetype;
                    }

                    var chunk = new SimChunk(chunkSnap.ComponentTypeIds);
                    chunk.RestoreFrom(chunkSnap);
                    archetype.Chunks.Add(chunk);

                    // 重建实体索引
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        int entityId = chunk.EntityIds[i];
                        if (entityId >= _generationCap) continue;
                        _entityArchetypeKey[entityId] = key;
                        _entityChunkIndex[entityId] = archetype.Chunks.Count - 1;
                        _entityLocalIndex[entityId] = i;
                    }

                    EntityCount += chunk.Count;
                }
            }
        }

        // ==================== 清理 ====================

        /// <summary>清空所有 Archetype、Chunk 和事件队列</summary>
        public void ClearPools()
        {
            _archetypes.Clear();
            EntityCount = 0;
            foreach (var list in _eventQueues.Values) list.Clear();
        }
    }
}
