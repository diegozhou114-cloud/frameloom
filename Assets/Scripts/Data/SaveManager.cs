using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 存档管理器 — JSON 序列化，支持多存档槽位。
    /// 使用 PlayerPrefs 作为存档索引，Application.persistentDataPath 存储文件。
    /// </summary>
    public static class SaveManager
    {
        private const string SaveIndexKey = "Save_Index";     // 当前选中的存档槽位
        private const string SaveFilePrefix = "save_";        // 文件名前缀
        private const string SaveExtension = ".json";
        public const int MaxSaveSlots = 5;
        private static readonly IAtomicTextWriter SaveWriter = new AtomicFileWriter();

        /// <summary>存档目录</summary>
        public static string SavePath => Application.persistentDataPath;

        // ==================== 槽位管理 ====================

        /// <summary>当前存档槽位（0~4）</summary>
        public static int CurrentSlot
        {
            get => PlayerPrefs.GetInt(SaveIndexKey, 0);
            set => PlayerPrefs.SetInt(SaveIndexKey, Mathf.Clamp(value, 0, MaxSaveSlots - 1));
        }

        /// <summary>获取存档文件路径</summary>
        public static string GetFilePath(int slot)
        {
            return Path.Combine(SavePath, $"{SaveFilePrefix}{slot}{SaveExtension}");
        }

        /// <summary>存档是否存在</summary>
        public static bool Exists(int slot)
        {
            return File.Exists(GetFilePath(slot));
        }

        // ==================== 保存 ====================

        /// <summary>
        /// 保存数据到当前槽位。
        /// 注意：当前保存为明文 JSON，无加密保护。
        /// 生产环境建议对敏感数据（如玩家金币、宝石等）加密后再存储。
        /// </summary>
        public static bool Save<T>(T data, int? slot = null)
        {
            int targetSlot = slot ?? CurrentSlot;
            try
            {
                string json = JsonUtility.ToJson(data, prettyPrint: true);
                string path = GetFilePath(targetSlot);
                FileWriteResult result = SaveWriter.Write(path, json);
                if (!result.Succeeded)
                {
                    Debug.LogError($"[SaveManager] 保存失败: {result.Error}");
                    return false;
                }

                Debug.Log($"[SaveManager] 保存成功: slot={targetSlot}, path={path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] 保存失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>保存游戏完整状态（Player + Village）</summary>
        public static bool SaveGame(int? slot = null)
        {
            var dm = DataManager.Instance;
            if (dm?.Player == null || dm?.Village == null)
            {
                Debug.LogError("[SaveManager] DataManager 未就绪，保存中止");
                return false;
            }

            int targetSlot = slot ?? CurrentSlot;

            var player = dm.Player;
            var village = dm.Village;

            PlayerSaveData saveData = SaveDataMapper.Capture(player, village);

            return Save(saveData, targetSlot);
        }

        // ==================== 加载 ====================

        /// <summary>从指定槽位加载</summary>
        public static T Load<T>(int? slot = null) where T : class
        {
            int targetSlot = slot ?? CurrentSlot;
            string path = GetFilePath(targetSlot);

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SaveManager] 存档不存在: slot={targetSlot}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] 加载失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>从存档槽位加载 PlayerSaveData 并写入 DataManager（离线模式入口）</summary>
        public static bool LoadToDataManager(int? slot = null)
        {
            int targetSlot = slot ?? CurrentSlot;
            var data = Load<PlayerSaveData>(targetSlot);
            if (data == null) return false;

            if (data.Version != PlayerSaveData.CurrentVersion)
                Debug.LogWarning($"[SaveManager] 存档版本不匹配: 存档={data.Version}, 当前={PlayerSaveData.CurrentVersion}");

            var dm = DataManager.Instance;
            if (dm?.Player == null || dm?.Village == null)
            {
                Debug.LogError("[SaveManager] DataManager 未就绪，无法加载存档");
                return false;
            }

            var player = dm.Player;
            RestorePlayer(player, data);

            var village = dm.Village;
            RestoreBuildings(village, data, targetSlot);

            Debug.Log($"[SaveManager] 数据写入 DataManager: slot={targetSlot}");
            return true;
        }

        private static void RestorePlayer(PlayerData player, PlayerSaveData data)
        {
            player.Name.Value = data.PlayerName;
            player.Level.Value = data.PlayerLevel;
            player.TownHallLevel.Value = data.TownHallLevel;
            player.Gold.Value = data.Gold;
            player.Elixir.Value = data.Elixir;
            player.DarkElixir.Value = data.DarkElixir;
            player.Gems.Value = data.Gems;
            player.Trophies.Value = data.Trophies;
            player.Exp.Value = data.Exp;
        }

        private static void RestoreBuildings(VillageData village, PlayerSaveData data, int targetSlot)
        {
            village.Name.Value = data.VillageName ?? village.Name.Value;
            if (data.MaxBuildings > 0)
                village.MaxBuildings.Value = data.MaxBuildings;
            village.Buildings.Clear();
            if (data.Buildings != null)
            {
                foreach (var b in data.Buildings)
                {
                    // 校验 TypeId 在配置表中存在
                    if (ConfigTableManager.Instance.IsReady)
                    {
                        if (ConfigTableManager.Instance.GetBuilding(b.TypeId) == null)
                        {
                            Debug.LogWarning($"[SaveManager] 存档中的建筑类型 {b.TypeId} 在配置表中不存在，跳过");
                            continue;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[SaveManager] Configs not ready, skipping building validation for slot {targetSlot}");
                    }
                    village.AddBuilding(SaveDataMapper.RestoreBuilding(b));
                }
            }
        }

        // ==================== 删除 ====================

        /// <summary>删除指定槽位的存档</summary>
        public static bool Delete(int slot)
        {
            string path = GetFilePath(slot);
            if (!File.Exists(path)) return false;

            try
            {
                File.Delete(path);
                Debug.Log($"[SaveManager] 删除存档: slot={slot}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] 删除失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>删除所有存档</summary>
        public static void DeleteAll()
        {
            for (int i = 0; i < MaxSaveSlots; i++)
            {
                Delete(i);
            }
        }
    }
}
