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
            {
                var val = kv.Value;
                list.Add(new SerializableKVP
                {
                    Key = kv.Key,
                    Value = val?.ToString() ?? "",
                    TypeName = val?.GetType().Name ?? "null",
                });
            }
            return list;
        }

        private static Dictionary<string, object> FromSerializable(List<SerializableKVP> list)
        {
            var dict = new Dictionary<string, object>();
            if (list != null)
                foreach (var kv in list)
                    dict[kv.Key] = ParseTyped(kv.Value, kv.TypeName);
            return dict;
        }

        /// <summary>按类型名还原值（int/float/bool/enum 等常见类型）</summary>
        private static object ParseTyped(string value, string typeName)
        {
            if (string.IsNullOrEmpty(value)) return null;

            return typeName switch
            {
                "Int32" => int.TryParse(value, out var i) ? i : 0,
                "Single" => float.TryParse(value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f,
                "Int64" => long.TryParse(value, out var l) ? l : 0L,
                "Boolean" => bool.TryParse(value, out var b) && b,
                "String" => value,
                _ => value, // 枚举和其他类型由 ObservableModel.RestoreSnapshot 处理
            };
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

        /// <summary>从存档槽位加载 PlayerSaveData 并写入 DataManager（离线模式入口）</summary>
        public static bool LoadToDataManager(int? slot = null)
        {
            int targetSlot = slot ?? CurrentSlot;
            var data = Load<PlayerSaveData>(targetSlot);
            if (data == null) return false;

            var player = DataManager.Instance.Player;
            player.Name.Value = data.PlayerName;
            player.Level.Value = data.PlayerLevel;
            player.TownHallLevel.Value = data.TownHallLevel;
            player.Gold.Value = data.Gold;
            player.Elixir.Value = data.Elixir;
            player.DarkElixir.Value = data.DarkElixir;
            player.Gems.Value = data.Gems;
            player.Trophies.Value = data.Trophies;
            player.Exp.Value = data.Exp;

            var village = DataManager.Instance.Village;
            village.Buildings.Clear();
            if (data.Buildings != null)
            {
                foreach (var b in data.Buildings)
                    village.AddBuilding(BuildingData.Create(b.TypeId, b.GridX, b.GridY));
            }

            Debug.Log($"[SaveManager] 数据写入 DataManager: slot={targetSlot}");
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
        public string TypeName;
    }
}
