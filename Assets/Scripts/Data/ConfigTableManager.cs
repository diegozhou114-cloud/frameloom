using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 配置表管理器 — 跨场景单例，预加载并缓存所有游戏配置表。
    /// 在 LaunchState Phase 3 阶段调用 PreloadAllAsync()，
    /// 之后通过 GetBuilding/GetUnit/GetLevel 同步查询。
    /// </summary>
    public class ConfigTableManager : SingletonDontDestroy<ConfigTableManager>
    {
        /// <summary>配置表是否已全部加载完毕</summary>
        public bool IsReady { get; private set; }

        // ==================== 配置表存储 ====================

        private Dictionary<int, BuildingConfig> _buildings;
        private Dictionary<int, UnitConfig> _units;
        private Dictionary<int, LevelConfig> _levels;

        // ==================== 公开属性 ====================

        /// <summary>建筑配置表行数</summary>
        public int BuildingCount => _buildings?.Count ?? 0;

        /// <summary>兵种配置表行数</summary>
        public int UnitCount => _units?.Count ?? 0;

        /// <summary>关卡配置表行数</summary>
        public int LevelCount => _levels?.Count ?? 0;

        // ==================== 预加载入口 ====================

        /// <summary>
        /// 异步预加载全部配置表。
        /// 返回 false 表示部分表加载失败（已记录错误日志）。
        /// </summary>
        public UniTask<bool> PreloadAllAsync()
        {
            if (IsReady) return UniTask.FromResult(true);

            Debug.Log("[ConfigTableManager] 开始预加载配置表...");
            bool allOk = true;

            // ─── 建筑表 ───
            _buildings = LoadConfigTable<BuildingConfig>("Config/Buildings");
            if (_buildings == null || _buildings.Count == 0)
            {
                Debug.LogError("[ConfigTableManager] ❌ 建筑配置表加载失败或为空");
                allOk = false;
                _buildings = null;
            }
            else
            {
                Debug.Log($"[ConfigTableManager] ✅ 建筑配置表: {_buildings.Count} 条");
            }

            // ─── 兵种表 ───
            _units = LoadConfigTable<UnitConfig>("Config/Units");
            if (_units == null || _units.Count == 0)
            {
                Debug.LogError("[ConfigTableManager] ❌ 兵种配置表加载失败或为空");
                allOk = false;
                _units = null;
            }
            else
            {
                Debug.Log($"[ConfigTableManager] ✅ 兵种配置表: {_units.Count} 条");
            }

            // ─── 关卡表 ───
            _levels = LoadConfigTable<LevelConfig>("Config/Levels");
            if (_levels == null || _levels.Count == 0)
            {
                Debug.LogError("[ConfigTableManager] ❌ 关卡配置表加载失败或为空");
                allOk = false;
                _levels = null;
            }
            else
            {
                Debug.Log($"[ConfigTableManager] ✅ 关卡配置表: {_levels.Count} 条");
            }

            IsReady = allOk;
            Debug.Log($"[ConfigTableManager] 预加载完成 (Ready={IsReady})");
            return UniTask.FromResult(allOk);
        }

        // ==================== 查询 API — 建筑 ====================

        /// <summary>按 ID 获取建筑配置（未加载返回 null）</summary>
        public BuildingConfig GetBuilding(int id)
        {
            if (_buildings == null) return null;
            _buildings.TryGetValue(id, out var cfg);
            return cfg;
        }

        /// <summary>安全获取建筑配置</summary>
        public bool TryGetBuilding(int id, out BuildingConfig cfg)
        {
            cfg = null;
            return _buildings != null && _buildings.TryGetValue(id, out cfg);
        }

        /// <summary>获取所有建筑配置</summary>
        public IEnumerable<BuildingConfig> GetAllBuildings()
        {
            return _buildings != null ? _buildings.Values : Array.Empty<BuildingConfig>();
        }

        // ==================== 查询 API — 兵种 ====================

        /// <summary>按 ID 获取兵种配置</summary>
        public UnitConfig GetUnit(int id)
        {
            if (_units == null) return null;
            _units.TryGetValue(id, out var cfg);
            return cfg;
        }

        /// <summary>安全获取兵种配置</summary>
        public bool TryGetUnit(int id, out UnitConfig cfg)
        {
            cfg = null;
            return _units != null && _units.TryGetValue(id, out cfg);
        }

        /// <summary>获取所有兵种配置</summary>
        public IEnumerable<UnitConfig> GetAllUnits()
        {
            return _units != null ? _units.Values : Array.Empty<UnitConfig>();
        }

        /// <summary>获取指定大本营等级可用的兵种</summary>
        public List<UnitConfig> GetUnitsByTownHall(int townHallLevel)
        {
            var result = new List<UnitConfig>();
            if (_units == null) return result;

            foreach (var unit in _units.Values)
            {
                if (unit.RequiredTownHall <= townHallLevel)
                    result.Add(unit);
            }
            return result;
        }

        // ==================== 查询 API — 关卡 ====================

        /// <summary>按 ID 获取关卡配置</summary>
        public LevelConfig GetLevel(int id)
        {
            if (_levels == null) return null;
            _levels.TryGetValue(id, out var cfg);
            return cfg;
        }

        /// <summary>安全获取关卡配置</summary>
        public bool TryGetLevel(int id, out LevelConfig cfg)
        {
            cfg = null;
            return _levels != null && _levels.TryGetValue(id, out cfg);
        }

        /// <summary>获取所有关卡配置</summary>
        public IEnumerable<LevelConfig> GetAllLevels()
        {
            return _levels != null ? _levels.Values : Array.Empty<LevelConfig>();
        }

        // ==================== 缓存管理 ====================

        /// <summary>清空所有配置表缓存</summary>
        public void Clear()
        {
            _buildings?.Clear();
            _units?.Clear();
            _levels?.Clear();
            IsReady = false;
        }

        // ==================== 内部实现 ====================

        /// <summary>泛型静态缓存 Id 字段反射（只反射一次）</summary>
        private static class IdFieldCache<T>
        {
            public static readonly System.Reflection.FieldInfo Field = typeof(T).GetField("Id");
        }

        /// <summary>从 Resources 异步加载 JSON 数组配置表，按 Id 建立索引</summary>
        private Dictionary<int, T> LoadConfigTable<T>(string path)
            where T : class
        {
            // 静态缓存 Id 字段反射（只反射一次）
            var idField = IdFieldCache<T>.Field;
            if (idField == null)
            {
                Debug.LogError($"[ConfigTableManager] {typeof(T).Name} 缺少 Id 字段");
                return null;
            }

            // Resources.Load 必须在主线程调用
            T[] items;
            try
            {
                items = ConfigLoader.LoadJsonArrayFromResources<T>(path);
            }
            finally
            {
                // 已在主线程，无需切换
            }

            if (items == null || items.Length == 0)
            {
                return null;
            }

            var dict = new Dictionary<int, T>(items.Length);
            foreach (var item in items)
            {
                if (item == null)
                {
                    Debug.LogError($"[ConfigTableManager] {typeof(T).Name} contains a null configuration item");
                    return null;
                }

                int id = (int)idField.GetValue(item);
                if (dict.ContainsKey(id))
                {
                    Debug.LogWarning(
                        $"[ConfigTableManager] {typeof(T).Name} Id={id} 重复，跳过");
                    continue;
                }

                dict[id] = item;
            }

            return dict;
        }
    }
}
