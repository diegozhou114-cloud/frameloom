using System;
using UnityEngine;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 表现层配置 — ArchetypeId → Prefab 映射。
    /// ScriptableObject，策划在 Inspector 中配置。
    /// </summary>
    [CreateAssetMenu(fileName = "ViewConfig", menuName = "ZWebGL/Create ViewConfig", order = 1)]
    public class ViewConfig : ScriptableObject
    {
        [Serializable]
        public struct ArchetypeEntry
        {
            /// <summary>对应 SimEntity 的 ArchetypeId</summary>
            [Tooltip("对应 SimEntitySpawnInfo.ArchetypeId")]
            public int ArchetypeId;
            /// <summary>Unity 预制体</summary>
            public GameObject Prefab;
        }

        [Header("原型 → 预制体映射")]
        public ArchetypeEntry[] Entries;

        /// <summary>根据 ArchetypeId 查找预制体，未找到返回 null</summary>
        public GameObject GetPrefab(int archetypeId)
        {
            foreach (var entry in Entries)
            {
                if (entry.ArchetypeId == archetypeId)
                    return entry.Prefab;
            }
            return null;
        }
    }
}
