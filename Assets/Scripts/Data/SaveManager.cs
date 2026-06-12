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
        private const int MaxSaveSlots = 5;

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

        /// <summary>保存数据到当前槽位</summary>
        public static bool Save<T>(T data, int? slot = null)
        {
            int targetSlot = slot ?? CurrentSlot;
            try
            {
                string json = JsonUtility.ToJson(data, prettyPrint: true);
                string path = GetFilePath(targetSlot);

                File.WriteAllText(path, json);
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
            int targetSlot = slot ?? CurrentSlot;

            var saveData = new GameSaveData
            {
                PlayerEntries = ToSerializable(DataManager.Instance.Player.GetSnapshot()),
                VillageEntries = ToSerializable(DataManager.Instance.Village.GetSnapshot()),
                SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };

            return Save(saveData, targetSlot);
        }

        private static List<SerializableKVP> ToSerializable(Dictionary<string, object> dict)
        {
            var list = new List<SerializableKVP>();
            foreach (var kv in dict)
                list.Add(new SerializableKVP { Key = kv.Key, Value = kv.Value?.ToString() ?? "" });
            return list;
        }

        private static Dictionary<string, object> FromSerializable(List<SerializableKVP> list)
        {
            var dict = new Dictionary<string, object>();
            if (list != null)
                foreach (var kv in list)
                    dict[kv.Key] = kv.Value;
            return dict;
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

        /// <summary>加载游戏完整状态</summary>
        public static bool LoadGame(int? slot = null)
        {
            var saveData = Load<GameSaveData>(slot);
            if (saveData == null) return false;

            DataManager.Instance.Player.RestoreSnapshot(FromSerializable(saveData.PlayerEntries));
            DataManager.Instance.Village.RestoreSnapshot(FromSerializable(saveData.VillageEntries));

            Debug.Log($"[SaveManager] 加载成功: slot={slot ?? CurrentSlot}, time={saveData.SaveTime}");
            return true;
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

    /// <summary>游戏完整存档数据</summary>
    [Serializable]
    public class GameSaveData
    {
        public List<SerializableKVP> PlayerEntries;
        public List<SerializableKVP> VillageEntries;
        public string SaveTime;
    }

    [Serializable]
    public class SerializableKVP
    {
        public string Key;
        public string Value;
    }
}
