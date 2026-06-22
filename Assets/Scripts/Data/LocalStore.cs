using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 本地持久化键值存储 — 支持任意可序列化类型（struct / class / 基础类型）。
    /// 数据以 JSON 文件存储在 persistentDataPath，进程重启后仍有效。
    ///
    /// 用法：
    ///   LocalStore.Set("player_name", "Alice");
    ///   LocalStore.Set("settings", new AudioSettings { Volume = 0.8f });
    ///   var v = LocalStore.Get("settings", new AudioSettings());
    ///   LocalStore.ClearAll(); // 一键清空
    /// </summary>
    public static class LocalStore
    {
        private const string FileName = "local_store.json";

        private static string FilePath =>
            Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>当前存储的键数量</summary>
        public static int Count
        {
            get { EnsureLoaded(); return _entries.Count; }
        }

        private static Dictionary<string, StoreEntry> _entries;
        private static bool _loaded;

        // ==================== 公开 API ====================

        /// <summary>写入一个值（自动序列化 + 落盘）</summary>
        /// <typeparam name="T">值类型（需可被 JsonUtility 序列化）</typeparam>
        public static void Set<T>(string key, T value)
        {
            EnsureLoaded();
            if (value == null) { Remove(key); return; }

            var json = JsonUtility.ToJson(value);
            _entries[key] = new StoreEntry
            {
                TypeName = typeof(T).FullName,
                JsonData = json,
            };

            FlushToDisk();
        }

        /// <summary>读取一个值（不存在或反序列化失败返回 defaultValue）</summary>
        public static T Get<T>(string key, T defaultValue = default)
        {
            EnsureLoaded();

            if (!_entries.TryGetValue(key, out var entry)) return defaultValue;

            try
            {
                return JsonUtility.FromJson<T>(entry.JsonData);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[LocalStore] 反序列化失败 key={key}, type={typeof(T).Name}: {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>安全读取，返回是否成功</summary>
        public static bool TryGet<T>(string key, out T value)
        {
            EnsureLoaded();

            if (!_entries.TryGetValue(key, out var entry))
            {
                value = default;
                return false;
            }

            try
            {
                value = JsonUtility.FromJson<T>(entry.JsonData);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>键是否存在</summary>
        public static bool Has(string key)
        {
            EnsureLoaded();
            return _entries.ContainsKey(key);
        }

        /// <summary>删除指定键（自动落盘）</summary>
        public static bool Remove(string key)
        {
            EnsureLoaded();
            if (!_entries.Remove(key)) return false;

            FlushToDisk();
            return true;
        }

        /// <summary>一键清空所有本地缓存（方便覆盖本地修改）</summary>
        public static void ClearAll()
        {
            _entries = new Dictionary<string, StoreEntry>();
            FlushToDisk();
            Debug.Log("[LocalStore] ✅ 所有本地缓存已清空");
        }

        /// <summary>强制从磁盘重新加载（丢弃内存中的未保存修改）</summary>
        public static void ReloadFromDisk()
        {
            _loaded = false;
            _entries = null;
            EnsureLoaded();
            Debug.Log($"[LocalStore] 已从磁盘重新加载，当前 {_entries.Count} 条记录");
        }

        // ==================== 内部：加载 / 落盘 ====================

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var data = JsonUtility.FromJson<StoreFileData>(json);
                    _entries = data?.ToDictionary() ?? new Dictionary<string, StoreEntry>();
                }
                else
                {
                    _entries = new Dictionary<string, StoreEntry>();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStore] 加载文件失败，使用空存储: {ex.Message}");
                _entries = new Dictionary<string, StoreEntry>();
            }
        }

        private static void FlushToDisk()
        {
            try
            {
                var data = StoreFileData.FromDictionary(_entries);
                var json = JsonUtility.ToJson(data, prettyPrint: false);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStore] 写入文件失败: {ex.Message}");
            }
        }

        // ==================== 内部数据结构 ====================

        [Serializable]
        private class StoreEntry
        {
            public string TypeName;
            public string JsonData;
        }

        /// <summary>持久化文件格式 — 适配 JsonUtility</summary>
        [Serializable]
        private class StoreFileData
        {
            public List<string> Keys = new List<string>();
            public List<string> TypeNames = new List<string>();
            public List<string> JsonValues = new List<string>();

            public static StoreFileData FromDictionary(Dictionary<string, StoreEntry> dict)
            {
                var data = new StoreFileData();
                if (dict == null) return data;

                foreach (var kv in dict)
                {
                    data.Keys.Add(kv.Key);
                    data.TypeNames.Add(kv.Value.TypeName);
                    data.JsonValues.Add(kv.Value.JsonData);
                }
                return data;
            }

            public Dictionary<string, StoreEntry> ToDictionary()
            {
                var dict = new Dictionary<string, StoreEntry>(Keys.Count);
                for (int i = 0; i < Keys.Count && i < TypeNames.Count && i < JsonValues.Count; i++)
                {
                    dict[Keys[i]] = new StoreEntry
                    {
                        TypeName = TypeNames[i],
                        JsonData = JsonValues[i],
                    };
                }
                return dict;
            }
        }
    }
}
