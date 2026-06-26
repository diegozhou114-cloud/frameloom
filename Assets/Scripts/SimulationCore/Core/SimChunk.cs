using System;

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
                    // 延迟分配列（按组件类型创建数组）
                    Type compType = components[c].GetType();
                    ComponentColumns[c] = Array.CreateInstance(compType, Capacity);
                }
                ComponentColumns[c].SetValue(components[c], idx);
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
                col.SetValue(col.GetValue(lastIdx), localIndex);
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
    }
}
