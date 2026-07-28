using System;
using System.Collections.Generic;
using System.Text;

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
        private int _worldEpoch = 1;
        private int[] _generations;
        private int _generationCap;

        // Archetype 存储
        private readonly Dictionary<string, SimArchetype> _archetypes = new Dictionary<string, SimArchetype>();

        // 帧内事件队列
        private readonly Dictionary<Type, object> _eventQueues = new Dictionary<Type, object>();

        // 排序键缓存（避免每 Tick 分配 List<string>）
        private readonly List<string> _sortedKeys = new List<string>();
        private readonly List<EntityLocation> _sortedLocations = new List<EntityLocation>();

        private struct EntityLocation
        {
            public SimEntity Entity;
            public SimChunk Chunk;
            public int LocalIndex;
            public int ColumnIndex;
            public int SecondaryColumnIndex;
        }

        public SimWorld(ComponentTypeRegistry registry, int initialCapacity = 256, ISimLogger logger = null)
        {
            _registry = registry;
            _logger = logger;
            _generationCap = Math.Max(1, initialCapacity);
            _generations = new int[_generationCap];
            _entityArchetypeKey = new string[_generationCap];
            _entityChunkIndex = new int[_generationCap];
            _entityLocalIndex = new int[_generationCap];
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

            var entity = new SimEntity(id, _generations[id], _worldEpoch);
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
                int typeId = compTypeIds[c];
                comps[c] = SimChunkHelper.CreateDefault(typeId);
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
            if (!IsAlive(entity)) return;
            int id = entity.Id;

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
            EntityCount--;
            OnEntityDestroyed?.Invoke(entity);
        }

        public bool IsAlive(SimEntity entity)
        {
            int id = entity.Id;
            return entity.IsValid && entity.WorldEpoch == _worldEpoch &&
                id < _generationCap && _generations[id] == entity.Generation &&
                _entityArchetypeKey[id] != null;
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
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {id} is not valid or has been destroyed.");
            var archetype = _archetypes[_entityArchetypeKey[id]];
            int colIdx = archetype.ColumnIndex[_registry.GetId(typeof(T))];
            var chunk = archetype.Chunks[_entityChunkIndex[id]];
            return ref chunk.GetComponentRef<T>(_entityLocalIndex[id], colIdx);
        }

        /// <summary>尝试获取组件值拷贝</summary>
        public bool TryGetComponent<T>(SimEntity entity, out T component) where T : struct, ISimComponent
        {
            int id = entity.Id;
            if (!IsAlive(entity))
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
            if (!IsAlive(entity)) return false;
            var archetype = _archetypes[_entityArchetypeKey[id]];
            return archetype.ColumnIndex.ContainsKey(_registry.GetId(typeof(T)));
        }

        /// <summary>设置组件值</summary>
        public void SetComponent<T>(SimEntity entity, T component) where T : struct, ISimComponent
        {
            int id = entity.Id;
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {id} is not valid or has been destroyed.");
            var archetype = _archetypes[_entityArchetypeKey[id]];
            int colIdx = archetype.ColumnIndex[_registry.GetId(typeof(T))];
            var chunk = archetype.Chunks[_entityChunkIndex[id]];
            chunk.SetComponent(_entityLocalIndex[id], colIdx, component);
        }

        // ==================== 遍历 ====================

        /// <summary>遍历所有拥有 T 的 Chunk 中的所有实体（含组件值拷贝）</summary>
        public void ForEachComponent<T>(Action<SimEntity, T> action) where T : struct, ISimComponent
        {
            CollectLocations(_registry.GetId(typeof(T)));
            foreach (var location in _sortedLocations)
                action(location.Entity, location.Chunk.GetComponent<T>(location.LocalIndex, location.ColumnIndex));
        }

        /// <summary>遍历所有拥有 T 的实体（仅实体，不含组件）</summary>
        public void ForEachEntities<T>(Action<SimEntity> action) where T : struct, ISimComponent
        {
            CollectLocations(_registry.GetId(typeof(T)));
            foreach (var location in _sortedLocations)
                action(location.Entity);
        }

        /// <summary>双组件联合遍历 — 同 Chunk 内双列顺序扫描，零随机跳转</summary>
        public void ForEachEntities<T1, T2>(Action<SimEntity, T1, T2> action)
            where T1 : struct, ISimComponent where T2 : struct, ISimComponent
        {
            CollectLocations(_registry.GetId(typeof(T1)), _registry.GetId(typeof(T2)));
            foreach (var location in _sortedLocations)
                action(location.Entity,
                    location.Chunk.GetComponent<T1>(location.LocalIndex, location.ColumnIndex),
                    location.Chunk.GetComponent<T2>(location.LocalIndex, location.SecondaryColumnIndex));
        }

        /// <summary>双组件联合遍历（ref 版本，支持修改）</summary>
        public void ForEachEntities<T1, T2>(RefAction<SimEntity, T1, T2> action)
            where T1 : struct, ISimComponent where T2 : struct, ISimComponent
        {
            CollectLocations(_registry.GetId(typeof(T1)), _registry.GetId(typeof(T2)));
            foreach (var location in _sortedLocations)
            {
                T1[] first = (T1[])location.Chunk.ComponentColumns[location.ColumnIndex];
                T2[] second = (T2[])location.Chunk.ComponentColumns[location.SecondaryColumnIndex];
                action(location.Entity, ref first[location.LocalIndex], ref second[location.LocalIndex]);
            }
        }

        private void CollectLocations(int typeId1, int typeId2 = -1)
        {
            _sortedLocations.Clear();
            foreach (var archetype in _archetypes.Values)
            {
                if (!archetype.ColumnIndex.TryGetValue(typeId1, out int col1)) continue;
                int col2 = -1;
                if (typeId2 >= 0 && !archetype.ColumnIndex.TryGetValue(typeId2, out col2)) continue;
                foreach (var chunk in archetype.Chunks)
                {
                    if (chunk.ComponentColumns[col1] == null ||
                        (col2 >= 0 && chunk.ComponentColumns[col2] == null)) continue;
                    for (int index = 0; index < chunk.Count; index++)
                    {
                        _sortedLocations.Add(new EntityLocation
                        {
                            Entity = new SimEntity(chunk.EntityIds[index], chunk.Generations[index], _worldEpoch),
                            Chunk = chunk,
                            LocalIndex = index,
                            ColumnIndex = col1,
                            SecondaryColumnIndex = col2,
                        });
                    }
                }
            }
            _sortedLocations.Sort((left, right) =>
            {
                int byId = left.Entity.Id.CompareTo(right.Entity.Id);
                return byId != 0 ? byId : left.Entity.Generation.CompareTo(right.Entity.Generation);
            });
        }

        /// <summary>双组件 ref 遍历委托</summary>
        public delegate void RefAction<TA, TB, TC>(TA arg1, ref TB arg2, ref TC arg3);

        // ==================== 事件队列 ====================

        /// <summary>入队一个事件（本 Tick 内消费）</summary>
        public void EnqueueEvent<T>(T evt) where T : struct
        {
            var type = typeof(T);
            if (!_eventQueues.TryGetValue(type, out var obj))
            {
                _eventQueues[type] = new List<T>();
            }
            ((List<T>)_eventQueues[type]).Add(evt);
        }

        /// <summary>消费所有已入队的 T 类型事件（消费后清空）</summary>
        public void ConsumeEvents<T>(Action<T> consumer) where T : struct
        {
            var type = typeof(T);
            if (!_eventQueues.TryGetValue(type, out var obj)) return;
            var list = (List<T>)obj;
            if (list.Count == 0) return;
            foreach (var item in list) consumer(item);
            list.Clear();
        }

        // ==================== 校验 ====================

        /// <summary>
        /// 计算当前世界状态的全量哈希 — FNV-1a。
        /// 按 Archetype Key 排序确保确定性，通过 IHashableComponent 零装箱哈希各组件。
        /// </summary>
        public ulong ComputeChecksum() => HashInto(DeterministicHash64.OffsetBasis);

        public ulong HashInto(ulong hash)
        {
            var allocator = new DeterministicHash64(hash);
            allocator.AddInt32(SimComponentSchema.Version);
            allocator.AddInt32(EntityCount);
            allocator.AddInt32(_nextId);
            for (int entityId = 0; entityId < _nextId; entityId++)
                allocator.AddInt32(_generations[entityId]);
            hash = allocator.Value;

            // 按 Archetype Key 排序确保确定性
            _sortedKeys.Clear();
            _sortedKeys.AddRange(_archetypes.Keys);
            _sortedKeys.Sort();
            foreach (var key in _sortedKeys)
            {
                var archetype = _archetypes[key];
                var schema = new DeterministicHash64(hash);
                schema.AddInt32(archetype.ComponentTypeIds.Length);
                foreach (int componentTypeId in archetype.ComponentTypeIds)
                    schema.AddInt32(componentTypeId);
                hash = schema.Value;
                // 按 Chunk 首个 Entity ID 排序确保确定性（避免 swap-remove 导致的顺序变化）
                var chunks = new List<SimChunk>(archetype.Chunks);
                chunks.Sort((a, b) =>
                {
                    int aId = a.Count > 0 ? a.EntityIds[0] : int.MaxValue;
                    int bId = b.Count > 0 ? b.EntityIds[0] : int.MaxValue;
                    return aId.CompareTo(bId);
                });
                foreach (var chunk in chunks)
                {
                    hash = HashChunk(archetype, chunk, hash);
                }
            }
            return HashPendingEvents(hash);
        }

        private ulong HashPendingEvents(ulong hash)
        {
            var types = new List<Type>();
            foreach (var pair in _eventQueues)
            {
                if (pair.Value is System.Collections.ICollection values && values.Count > 0)
                    types.Add(pair.Key);
            }
            types.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));

            var state = new DeterministicHash64(hash);
            state.AddInt32(types.Count);
            foreach (Type type in types)
            {
                state.AddBytes(Encoding.UTF8.GetBytes(type.FullName ?? type.Name));
                if (type == typeof(SimDamageEvent))
                {
                    var events = (List<SimDamageEvent>)_eventQueues[type];
                    state.AddInt32(events.Count);
                    foreach (SimDamageEvent damage in events)
                    {
                        state.AddInt32(damage.Amount);
                        AddEntity(ref state, damage.Attacker);
                        AddEntity(ref state, damage.Target);
                    }
                    continue;
                }
                throw new NotSupportedException($"No deterministic event hash registered for {type.FullName}.");
            }
            return state.Value;
        }

        private static void AddEntity(ref DeterministicHash64 state, SimEntity entity)
        {
            state.AddInt32(entity.Id);
            state.AddInt32(entity.Generation);
            state.AddInt32(entity.WorldEpoch);
        }

        private static ulong HashChunk(SimArchetype archetype, SimChunk chunk, ulong hash)
        {
            var indices = new List<int>(chunk.Count);
            for (int index = 0; index < chunk.Count; index++) indices.Add(index);
            indices.Sort((left, right) =>
            {
                int byId = chunk.EntityIds[left].CompareTo(chunk.EntityIds[right]);
                return byId != 0 ? byId : chunk.Generations[left].CompareTo(chunk.Generations[right]);
            });
            foreach (int i in indices)
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
                    else if (col is SimIdentityComponent[] identityCol)
                        hash = identityCol[i].HashInto(hash);
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
                Checksum = ComputeChecksum(),
                ChunkSnapshots = new List<ChunkSnapshot>(),
                PendingDamageEvents = new List<SimDamageEvent>(),
            };

            if (_eventQueues.TryGetValue(typeof(SimDamageEvent), out object pendingDamage))
                snapshot.PendingDamageEvents.AddRange((List<SimDamageEvent>)pendingDamage);

            // 按 Archetype Key 排序确保确定性
            _sortedKeys.Clear();
            _sortedKeys.AddRange(_archetypes.Keys);
            _sortedKeys.Sort();
            foreach (var key in _sortedKeys)
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
                        list.Add(new SimEntity(chunk.EntityIds[i], chunk.Generations[i], _worldEpoch));
            return list.ToArray();
        }

        /// <summary>
        /// 从完整快照恢复 — 重建所有 Archetype/Chunk 及实体索引。
        /// </summary>
        public void RestoreFromFull(TickSnapshot snapshot)
        {
            ValidateSnapshot(snapshot);
            _worldEpoch = unchecked(_worldEpoch + 1);
            if (_worldEpoch == 0) _worldEpoch = 1;
            _archetypes.Clear();
            _nextId = snapshot.NextId;
            _generations = (int[])snapshot.Generations.Clone();
            _generationCap = _generations.Length;
            _entityArchetypeKey = new string[_generationCap];
            _entityChunkIndex = new int[_generationCap];
            _entityLocalIndex = new int[_generationCap];
            EntityCount = 0;
            _eventQueues.Clear();

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

            // Snapshot component values may contain handles from the previous epoch.
            ForEachComponent<SimTargetComponent>((entity, target) =>
            {
                if (target.TargetEntity.IsValid)
                    target.TargetEntity = new SimEntity(
                        target.TargetEntity.Id, target.TargetEntity.Generation, _worldEpoch);
                SetComponent(entity, target);
            });

            if (snapshot.PendingDamageEvents != null)
            {
                foreach (SimDamageEvent damage in snapshot.PendingDamageEvents)
                {
                    EnqueueEvent(new SimDamageEvent
                    {
                        Amount = damage.Amount,
                        Attacker = RemapEntity(damage.Attacker),
                        Target = RemapEntity(damage.Target),
                    });
                }
            }
        }

        private SimEntity RemapEntity(SimEntity entity)
        {
            return entity.IsValid
                ? new SimEntity(entity.Id, entity.Generation, _worldEpoch)
                : SimEntity.Invalid;
        }

        private void ValidateSnapshot(TickSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Generations == null ||
                snapshot.NextId < 0 || snapshot.NextId > snapshot.Generations.Length ||
                snapshot.ChunkSnapshots == null)
                throw new ArgumentException("Invalid world snapshot.", nameof(snapshot));
            var entityIds = new HashSet<int>();
            foreach (var chunk in snapshot.ChunkSnapshots)
            {
                if (chunk == null || chunk.ComponentTypeIds == null ||
                    chunk.EntityIds == null || chunk.Generations == null ||
                    chunk.Columns == null || chunk.Count < 0 ||
                    chunk.Count > SimChunk.Capacity ||
                    chunk.EntityIds.Length < chunk.Count ||
                    chunk.Generations.Length < chunk.Count ||
                    chunk.Columns.Length != chunk.ComponentTypeIds.Length)
                    throw new ArgumentException("Invalid world snapshot chunk.", nameof(snapshot));
                int previousTypeId = 0;
                for (int column = 0; column < chunk.Columns.Length; column++)
                {
                    int typeId = chunk.ComponentTypeIds[column];
                    Array expected = SimChunkHelper.CreateColumn(typeId, 0);
                    if (typeId <= previousTypeId || expected == null || chunk.Columns[column] == null ||
                        chunk.Columns[column].Length < chunk.Count ||
                        chunk.Columns[column].GetType() != expected.GetType())
                        throw new ArgumentException("Invalid world snapshot chunk.", nameof(snapshot));
                    previousTypeId = typeId;
                }
                for (int index = 0; index < chunk.Count; index++)
                {
                    int id = chunk.EntityIds[index];
                    if (id < 0 || id >= snapshot.NextId ||
                        snapshot.Generations[id] != chunk.Generations[index] ||
                        !entityIds.Add(id))
                        throw new ArgumentException("Snapshot entity metadata is inconsistent.", nameof(snapshot));
                }
            }
        }

        // ==================== 清理 ====================

        /// <summary>清空所有 Archetype、Chunk 和事件队列</summary>
        public void ClearPools()
        {
            _worldEpoch = unchecked(_worldEpoch + 1);
            if (_worldEpoch == 0) _worldEpoch = 1;
            _archetypes.Clear();
            EntityCount = 0;
            _eventQueues.Clear();
        }
    }
}
