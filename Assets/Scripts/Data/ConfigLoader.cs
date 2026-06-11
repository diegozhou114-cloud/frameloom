using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 配置表加载器 — 从 Resources 或 StreamingAssets 加载 JSON/CSV 配置。
    /// 泛型加载 + 缓存，避免重复读取。
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>配置缓存（路径 → 反序列化对象）</summary>
        private static readonly Dictionary<string, object> _cache = new Dictionary<string, object>();

        // ==================== JSON 配置 ====================

        /// <summary>从 Resources 加载 JSON 配置（单对象）</summary>
        public static T LoadJsonFromResources<T>(string path) where T : class
        {
            // 去掉 .json 后缀（Resources.Load 不需要）
            string key = $"res:{path}";

            if (_cache.TryGetValue(key, out var cached))
                return cached as T;

            var asset = Resources.Load<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogError($"[ConfigLoader] 资源不存在: Resources/{path}");
                return null;
            }

            try
            {
                var result = JsonUtility.FromJson<T>(asset.text);
                _cache[key] = result;
                Resources.UnloadAsset(asset);
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfigLoader] JSON 解析失败: {path}, {ex.Message}");
                return null;
            }
        }

        /// <summary>从 Resources 加载 JSON 配置（数组）</summary>
        public static T[] LoadJsonArrayFromResources<T>(string path) where T : class
        {
            string key = $"res_arr:{path}";

            if (_cache.TryGetValue(key, out var cached))
                return cached as T[];

            var asset = Resources.Load<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogError($"[ConfigLoader] 资源不存在: Resources/{path}");
                return Array.Empty<T>();
            }

            try
            {
                // Unity JsonUtility 不支持直接解析顶层数组，需要用 Wrapper
                var wrapper = JsonUtility.FromJson<JsonArrayWrapper<T>>(
                    $"{{\"Items\":{asset.text}}}");
                var result = wrapper.Items;
                _cache[key] = result;
                Resources.UnloadAsset(asset);
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfigLoader] JSON 数组解析失败: {path}, {ex.Message}");
                return Array.Empty<T>();
            }
        }

        /// <summary>从 StreamingAssets 加载 JSON 配置</summary>
        public static T LoadJsonFromStreamingAssets<T>(string relativePath) where T : class
        {
            string key = $"sa:{relativePath}";

            if (_cache.TryGetValue(key, out var cached))
                return cached as T;

            string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);

            try
            {
                string json = System.IO.File.ReadAllText(fullPath);
                var result = JsonUtility.FromJson<T>(json);
                _cache[key] = result;
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfigLoader] StreamingAssets 加载失败: {fullPath}, {ex.Message}");
                return null;
            }
        }

        // ==================== CSV 配置 ====================

        /// <summary>从 Resources 加载 CSV 配置（返回字典列表）</summary>
        public static List<Dictionary<string, string>> LoadCsvFromResources(string path)
        {
            string key = $"csv:{path}";

            if (_cache.TryGetValue(key, out var cached))
                return cached as List<Dictionary<string, string>>;

            var asset = Resources.Load<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogError($"[ConfigLoader] CSV 资源不存在: Resources/{path}");
                return new List<Dictionary<string, string>>();
            }

            var result = ParseCsv(asset.text);
            _cache[key] = result;
            Resources.UnloadAsset(asset);
            return result;
        }

        private static List<Dictionary<string, string>> ParseCsv(string csvText)
        {
            var result = new List<Dictionary<string, string>>();
            var lines = csvText.Split('\n');

            if (lines.Length < 2) return result;

            // 第一行为表头
            var headers = lines[0].Trim().Split(',');

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var values = line.Split(',');
                var row = new Dictionary<string, string>();

                for (int j = 0; j < headers.Length && j < values.Length; j++)
                {
                    row[headers[j].Trim()] = values[j].Trim();
                }

                result.Add(row);
            }

            return result;
        }

        // ==================== 缓存管理 ====================

        /// <summary>清空所有配置缓存</summary>
        public static void ClearCache()
        {
            _cache.Clear();
            Resources.UnloadUnusedAssets();
        }

        /// <summary>清除指定路径的缓存</summary>
        public static void RemoveCache(string path)
        {
            _cache.Remove(path);
        }

        /// <summary>当前缓存数量</summary>
        public static int CacheCount => _cache.Count;
    }

    /// <summary>JSON 数组解析包装</summary>
    [Serializable]
    internal class JsonArrayWrapper<T>
    {
        public T[] Items;
    }
}
