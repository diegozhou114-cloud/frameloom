using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 固定容量 Chunk — 同原型实体连续存储，SoA 列布局。
    /// 128 实体/块 ≈ 16KB（按 6 组件 × 16B 估算），适合 L1 Cache。
    /// </summary>
    public class SimChunk
    {
        public const int Capacity = 128;

        /// <summary>当前存活实体数</summary>
        public int Count { get; private set; }

        /// <summary>该 Chunk 的组件类型 ID 列表（与 Archetype 共享引用）</summary>
        public int[] ComponentTypeIds;

        /// <summary>Entity ID 列</summary>
        public int[] EntityIds;

        /// <summary>Generation 列</summary>
        public int[] Generations;

        /// <summary>组件列数组（索引 = 组件在 Archetype 中的位置）</summary>
        public Array[] ComponentColumns;

        public SimChunk(int[] componentTypeIds)
        {
            ComponentTypeIds = componentTypeIds;
            EntityIds = new int[Capacity];
            Generations = new int[Capacity];
            ComponentColumns = new Array[componentTypeIds.Length];
        }

        /// <summary>该 Chunk 是否已满</summary>
        public bool IsFull => Count >= Capacity;

        /// <summary>添加实体及其组件。调用者保证未满且组件数量匹配。</summary>
        public void Add(int entityId, int generation, object[] components)
        {
            int idx = Count;
            EntityIds[idx] = entityId;
            Generations[idx] = generation;

            for (int c = 0; c < ComponentTypeIds.Length; c++)
            {
                if (ComponentColumns[c] == null)
                {
                    int typeId = ComponentTypeIds[c];
                    ComponentColumns[c] = SimChunkHelper.CreateColumn(typeId, Capacity);
                }
                SimChunkHelper.SetValue(ComponentColumns[c], idx, components[c], ComponentTypeIds[c]);
            }

            Count++;
        }

        /// <summary>移除实体（swap-remove，O(1)）。返回被交换到该位置的实体 ID（用于更新外部索引），-1 表示移除的是最后一个</summary>
        public int RemoveAt(int localIndex)
        {
            int lastIdx = Count - 1;
            if (localIndex == lastIdx)
            {
                Count--;
                return -1;
            }

            // Swap last → removed position
            EntityIds[localIndex] = EntityIds[lastIdx];
            Generations[localIndex] = Generations[lastIdx];
            for (int c = 0; c < ComponentColumns.Length; c++)
            {
                Array col = ComponentColumns[c];
                int typeId = ComponentTypeIds[c];
                object val = SimChunkHelper.GetValue(col, lastIdx, typeId);
                SimChunkHelper.SetValue(col, localIndex, val, typeId);
            }

            Count--;
            return EntityIds[localIndex]; // 被移到 localIndex 的实体 ID
        }

        /// <summary>获取组件的 ref 引用（用于修改）</summary>
        public ref T GetComponentRef<T>(int localIndex, int columnIndex) where T : struct
        {
            T[] col = (T[])ComponentColumns[columnIndex];
            return ref col[localIndex];
        }

        /// <summary>获取组件的值拷贝</summary>
        public T GetComponent<T>(int localIndex, int columnIndex) where T : struct
        {
            T[] col = (T[])ComponentColumns[columnIndex];
            return col[localIndex];
        }

        /// <summary>设置组件值</summary>
        public void SetComponent<T>(int localIndex, int columnIndex, T value) where T : struct
        {
            T[] col = (T[])ComponentColumns[columnIndex];
            col[localIndex] = value;
        }

        /// <summary>导出 Chunk 快照 — 每列 Array.Copy，零装箱</summary>
        public ChunkSnapshot ExportSnapshot()
        {
            var snap = new ChunkSnapshot
            {
                ComponentTypeIds = (int[])ComponentTypeIds.Clone(),
                EntityIds = new int[Count],
                Generations = new int[Count],
                Count = Count,
                Columns = new Array[ComponentColumns.Length],
            };

            Array.Copy(EntityIds, snap.EntityIds, Count);
            Array.Copy(Generations, snap.Generations, Count);

            for (int c = 0; c < ComponentColumns.Length; c++)
            {
                if (ComponentColumns[c] != null)
                {
                    int typeId = ComponentTypeIds[c];
                    Array copy = SimChunkHelper.CreateColumn(typeId, Count);
                    CopyColumn(typeId, ComponentColumns[c], copy, Count);
                    snap.Columns[c] = copy;
                }
            }

            return snap;
        }

        /// <summary>从快照恢复 Chunk（覆盖当前数据）</summary>
        public void RestoreFrom(ChunkSnapshot snap)
        {
            Count = snap.Count;
            Array.Copy(snap.EntityIds, EntityIds, Count);
            Array.Copy(snap.Generations, Generations, Count);

            for (int c = 0; c < ComponentColumns.Length; c++)
            {
                if (snap.Columns[c] != null)
                {
                    if (ComponentColumns[c] == null)
                    {
                        int typeId = ComponentTypeIds[c];
                        ComponentColumns[c] = SimChunkHelper.CreateColumn(typeId, Capacity);
                    }
                    CopyColumn(ComponentTypeIds[c], snap.Columns[c], ComponentColumns[c], snap.Count);
                }
            }
        }

        private static void CopyColumn(int typeId, Array source, Array destination, int count)
        {
            Array.Copy(source, destination, count);
            if (typeId != SimComponentTypeId.Movement) return;

            var sourceMovement = (SimMovementComponent[])source;
            var destinationMovement = (SimMovementComponent[])destination;
            for (int index = 0; index < count; index++)
            {
                SimMovementComponent movement = sourceMovement[index];
                movement.Path = movement.Path == null ? null : new List<int>(movement.Path);
                destinationMovement[index] = movement;
            }
        }
    }
}
