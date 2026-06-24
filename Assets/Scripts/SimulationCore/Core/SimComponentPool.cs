using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// SparseSet 组件池 — 确定性遍历顺序（= 创建顺序），O(1) 增删查。
    /// 与现有 ComponentPool&lt;T&gt; 完全一致，但 AccumulateHash 用于状态校验。
    /// </summary>
    internal class SimComponentPool<T> : ISimComponentPool where T : struct, ISimComponent
    {
        private int[] _sparse;
        private SimEntity[] _denseEntities;
        private T[] _denseComponents;
        private int _count;

        public int Count => _count;

        public SimComponentPool(int initialCapacity)
        {
            _sparse = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++) _sparse[i] = -1;
            _denseEntities = new SimEntity[initialCapacity];
            _denseComponents = new T[initialCapacity];
        }

        public void Add(SimEntity entity, T component)
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

        public void Set(SimEntity entity, T component)
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

        public ref T Get(SimEntity entity)
        {
            int idx = _sparse[entity.Id];
            if (idx < 0 || idx >= _count || _denseEntities[idx] != entity)
                throw new InvalidOperationException($"SimEntity {entity} does not have component {typeof(T).Name}");
            return ref _denseComponents[idx];
        }

        public bool TryGet(SimEntity entity, out T component)
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

        public bool Has(SimEntity entity)
        {
            int id = entity.Id;
            return id < _sparse.Length && _sparse[id] >= 0
                && _sparse[id] < _count && _denseEntities[_sparse[id]] == entity;
        }

        public void Remove(SimEntity entity)
        {
            if (!Has(entity)) return;
            int idx = _sparse[entity.Id];
            int lastIdx = _count - 1;
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

        // ==================== 遍历 ====================

        public IEnumerable<SimEntity> GetEntities()
        {
            for (int i = 0; i < _count; i++)
                yield return _denseEntities[i];
        }

        public void ForEach(Action<SimEntity> action)
        {
            for (int i = 0; i < _count; i++)
                action(_denseEntities[i]);
        }

        public delegate void ValAction<TA, TB>(TA arg1, TB arg2);
        public delegate void RefAction<TA, TB>(TA arg1, ref TB arg2);
        public delegate void RefPairAction<TA, TB, TC>(TA arg1, ref TB arg2, ref TC arg3);

        public void ForEach(ValAction<SimEntity, T> action)
        {
            for (int i = 0; i < _count; i++)
                action(_denseEntities[i], _denseComponents[i]);
        }

        public void ForEach(RefAction<SimEntity, T> action)
        {
            for (int i = 0; i < _count; i++)
                action(_denseEntities[i], ref _denseComponents[i]);
        }

        /// <summary>
        /// 联合遍历：遍历本池所有实体，对同时存在于 otherPool 的实体执行 action。
        /// 用于双组件系统（如 MovementSystem 需要 Position + Velocity）。
        /// </summary>
        internal void ForEachRefPair<TOther>(SimComponentPool<TOther> otherPool,
            RefPairAction<SimEntity, T, TOther> action) where TOther : struct, ISimComponent
        {
            for (int i = 0; i < _count; i++)
            {
                var entity = _denseEntities[i];
                int id = entity.Id;
                if (id < otherPool._sparse.Length)
                {
                    int otherIdx = otherPool._sparse[id];
                    if (otherIdx >= 0 && otherIdx < otherPool._count
                        && otherPool._denseEntities[otherIdx] == entity)
                    {
                        action(entity, ref _denseComponents[i],
                            ref otherPool._denseComponents[otherIdx]);
                    }
                }
            }
        }

        // ==================== 状态哈希 ====================

        private static System.Reflection.FieldInfo[] _cachedFields;
        private static Type _cachedType;

        public ulong AccumulateHash(ulong hash)
        {
            const ulong FNVPrime = 1099511628211UL;

            // Cache field info per T (once per component type)
            if (_cachedType != typeof(T))
            {
                _cachedType = typeof(T);
                _cachedFields = typeof(T).GetFields(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                // Sort by name for deterministic order across platforms
                System.Array.Sort(_cachedFields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            }

            for (int i = 0; i < _count; i++)
            {
                // Hash entity identity
                hash ^= (ulong)_denseEntities[i].Id;
                hash *= FNVPrime;
                hash ^= (ulong)_denseEntities[i].Generation;
                hash *= FNVPrime;

                // Hash each field value deterministically
                object boxed = _denseComponents[i]; // boxing — acceptable at 20Hz
                foreach (var field in _cachedFields)
                {
                    object value = field.GetValue(boxed);
                    if (value is int iv)
                    {
                        hash ^= (ulong)iv;
                        hash *= FNVPrime;
                    }
                    else if (value is uint uv)
                    {
                        hash ^= (ulong)uv;
                        hash *= FNVPrime;
                    }
                    else if (value is long lv)
                    {
                        hash ^= (ulong)lv;
                        hash *= FNVPrime;
                    }
                    else if (value is float fv)
                    {
                        // Floats should not exist in SimComponents, but handle anyway
                        byte[] floatBytes = System.BitConverter.GetBytes(fv);
                        int bits = System.BitConverter.ToInt32(floatBytes, 0);
                        hash ^= (uint)bits;
                        hash *= FNVPrime;
                    }
                    else if (value is bool bv)
                    {
                        hash ^= bv ? 1UL : 0UL;
                        hash *= FNVPrime;
                    }
                    else if (value is LogicInt li)
                    {
                        hash ^= (ulong)li.RawValue;
                        hash *= FNVPrime;
                    }
                    // Ignore reference types (List, etc.) — components are pure value types
                }
            }
            return hash;
        }

        // ==================== 快照导出/恢复 ====================

        public PoolSnapshot ExportDense()
        {
            int compSize = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            byte[] compBytes = new byte[_count * compSize];
            var snapshot = new PoolSnapshot
            {
                ComponentTypeName = typeof(T).FullName,
                Sparse = new int[_sparse.Length],
                DenseEntities = new SimEntity[_count],
                DenseComponentsBytes = compBytes,
                Count = _count,
                ComponentByteSize = compSize,
            };

            // 复制 sparse 数组
            Array.Copy(_sparse, snapshot.Sparse, _sparse.Length);

            // 复制 dense entities
            Array.Copy(_denseEntities, snapshot.DenseEntities, _count);

            // 复制 dense components → 字节数组
            IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(compSize);
            try
            {
                for (int i = 0; i < _count; i++)
                {
                    System.Runtime.InteropServices.Marshal.StructureToPtr(_denseComponents[i], ptr, false);
                    System.Runtime.InteropServices.Marshal.Copy(ptr, compBytes, i * compSize, compSize);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
            }

            return snapshot;
        }

        public void RestoreDense(PoolSnapshot snapshot)
        {
            if (snapshot == null || snapshot.ComponentTypeName != typeof(T).FullName)
                return;

            _count = snapshot.Count;
            int compSize = snapshot.ComponentByteSize;

            // 恢复 sparse
            if (_sparse.Length < snapshot.Sparse.Length)
                _sparse = new int[snapshot.Sparse.Length];
            Array.Copy(snapshot.Sparse, _sparse, snapshot.Sparse.Length);

            // 恢复 dense entities + components
            if (_denseEntities.Length < _count)
                _denseEntities = new SimEntity[_count];
            Array.Copy(snapshot.DenseEntities, _denseEntities, _count);

            if (_denseComponents.Length < _count)
                _denseComponents = new T[_count];

            IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(compSize);
            try
            {
                for (int i = 0; i < _count; i++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(snapshot.DenseComponentsBytes, i * compSize, ptr, compSize);
                    _denseComponents[i] = System.Runtime.InteropServices.Marshal.PtrToStructure<T>(ptr);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
            }
        }

        // ==================== 内部 ====================

        private void ResizeSparse(int newSize)
        {
            int old = _sparse.Length;
            Array.Resize(ref _sparse, newSize);
            for (int i = old; i < newSize; i++) _sparse[i] = -1;
        }

        private void ResizeDense(int newSize)
        {
            Array.Resize(ref _denseEntities, newSize);
            Array.Resize(ref _denseComponents, newSize);
        }
    }
}
