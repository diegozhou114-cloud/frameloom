using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 实体原型 — 管理拥有相同组件集合的所有实体的 Chunk 列表。
    /// 组件集合确定唯一 Archetype（由 ComponentTypeIds 排序数组标识）。
    /// </summary>
    public class SimArchetype
    {
        /// <summary>组件类型 ID 列表（排序，用于匹配）</summary>
        public int[] ComponentTypeIds { get; }

        /// <summary>所有 Chunk</summary>
        public List<SimChunk> Chunks { get; } = new List<SimChunk>();

        /// <summary>组件类型 ID → 在 ComponentColumns 中的索引</summary>
        public Dictionary<int, int> ColumnIndex { get; } = new Dictionary<int, int>();

        /// <summary>缓存的排序 ID 字符串（避免每次 CreateEntity 重新分配）</summary>
        public string Key { get; }

        public SimArchetype(int[] componentTypeIds)
        {
            ComponentTypeIds = componentTypeIds;
            Key = GetKey(componentTypeIds);
            for (int i = 0; i < componentTypeIds.Length; i++)
                ColumnIndex[componentTypeIds[i]] = i;
        }

        /// <summary>获取最后一个未满的 Chunk，没有则创建新的</summary>
        public SimChunk GetOrCreateChunk()
        {
            if (Chunks.Count > 0 && !Chunks[Chunks.Count - 1].IsFull)
                return Chunks[Chunks.Count - 1];

            var chunk = new SimChunk(ComponentTypeIds);
            Chunks.Add(chunk);
            return chunk;
        }

        /// <summary>计算组件集的排序 ID 字符串（用于 Dictionary Key）</summary>
        public static string GetKey(int[] componentTypeIds)
        {
            return string.Join(",", componentTypeIds);
        }


    }

    /// <summary>
    /// Archetype 匹配工具 — 根据组件类型集合查找/创建 Archetype。
    /// </summary>
    public static class ArchetypeUtils
    {
        /// <summary>从一组组件 Type 派生出排序的 int[] ID</summary>
        public static int[] BuildComponentTypeIds(ComponentTypeRegistry registry, System.Type[] componentTypes)
        {
            var ids = new int[componentTypes.Length];
            for (int i = 0; i < componentTypes.Length; i++)
                ids[i] = registry.GetId(componentTypes[i]);
            System.Array.Sort(ids);
            return ids;
        }
    }
}
