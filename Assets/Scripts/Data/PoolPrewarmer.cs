using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 对象池预热器 — 从配置表读取常用单位/特效 ID，
    /// 预创建实例放入对象池，避免首场战斗的 Instantiate 卡顿。
    /// </summary>
    public static class PoolPrewarmer
    {
        /// <summary>每个单位预创建的实例数</summary>
        private const int PrewarmCountPerUnit = 2;

        /// <summary>已预热的池（UnitId → GameObjectPool）</summary>
        private static readonly Dictionary<int, GameObjectPool> _pools = new();

        /// <summary>是否已预热</summary>
        public static bool IsPrewarmed { get; private set; }

        /// <summary>
        /// 从 ConfigTableManager 异步读取单位列表并预创建对象池实例。
        /// 使用 LoadAsync 避免主线程同步 IO 卡顿。
        /// </summary>
        public static async UniTask PrewarmFromConfigAsync(ConfigTableManager config)
        {
            if (IsPrewarmed)
            {
                Debug.Log("[PoolPrewarmer] 已预热，跳过");
                return;
            }

            // 清空旧池，防止重复预热导致内存泄漏
            Clear();

            if (config == null || !config.IsReady)
            {
                Debug.LogWarning("[PoolPrewarmer] ConfigTableManager 未就绪，跳过预热");
                return;
            }

            int successCount = 0;
            foreach (var unit in config.GetAllUnits())
            {
                // NOTE: prefab path $"Prefabs/Units/{unit.Name}" is fragile — if a prefab
                // is renamed the lookup silently returns null and the unit is skipped.
                // Consider storing the prefab GUID or using Addressables with a direct reference.
                string prefabPath = $"Prefabs/Units/{unit.Name}";
                var prefab = await ResourceManager.Instance.LoadAsync<GameObject>(prefabPath);

                if (prefab == null)
                {
                    Debug.LogWarning(
                        $"[PoolPrewarmer] Prefab 不存在: {prefabPath}，跳过 {unit.Name}");
                    continue;
                }

                var poolRoot = GetOrCreatePoolRoot();
                var pool = new GameObjectPool(prefab, poolRoot, PrewarmCountPerUnit);
                _pools[unit.Id] = pool;
                successCount++;

                Debug.Log(
                    $"[PoolPrewarmer] ✅ {unit.Name} ×{PrewarmCountPerUnit} (池中:{pool.Count})");
            }

            IsPrewarmed = true;
            Debug.Log(
                $"[PoolPrewarmer] 预热完成: {successCount}/{config.UnitCount} 种单位");
        }

        /// <summary>从池中获取指定单位的实例（未预热的单位返回 null）</summary>
        public static GameObject Get(int unitId)
        {
            if (_pools.TryGetValue(unitId, out var pool))
                return pool.Get();
            return null;
        }

        /// <summary>放回池中</summary>
        public static void Release(int unitId, GameObject obj)
        {
            if (_pools.TryGetValue(unitId, out var pool))
                pool.Release(obj);
            else
                Object.Destroy(obj);
        }

        /// <summary>清空所有预热池</summary>
        public static void Clear()
        {
            foreach (var pool in _pools.Values)
                pool.Clear();
            _pools.Clear();
            IsPrewarmed = false;
        }

        // ==================== 内部 ====================

        private static Transform _poolRoot;

        private static Transform GetOrCreatePoolRoot()
        {
            if (_poolRoot == null)
            {
                var go = new GameObject("[PoolPrewarmer_Root]");
                Object.DontDestroyOnLoad(go);
                go.SetActive(false); // 池根节点隐藏
                _poolRoot = go.transform;
            }
            return _poolRoot;
        }
    }
}
