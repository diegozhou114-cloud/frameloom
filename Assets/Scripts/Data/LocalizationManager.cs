using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 多语言本地化管理器 — 跨场景单例，按 key 获取本地化字符串。
    /// 支持运行时切换语言，从 JSON 配置表加载翻译。
    ///
    /// 用法：
    ///   LocalizationManager.Instance.Init("zh-CN");
    ///   string text = LocalizationManager.Instance.Get("login_title");
    ///   string msg  = LocalizationManager.Instance.GetFormat("welcome", playerName);
    /// </summary>
    public class LocalizationManager : SingletonDontDestroy<LocalizationManager>
    {
        /// <summary>是否已初始化</summary>
        public bool IsReady { get; private set; }

        /// <summary>当前语言代码（如 zh-CN / en-US）</summary>
        public string CurrentLanguage { get; private set; }

        /// <summary>支持的语言列表</summary>
        public List<string> SupportedLanguages { get; private set; } = new()
        {
            "zh-CN",
            "en-US",
            // TODO: 从配置表动态加载支持的语言列表
        };

        /// <summary>语言切换事件（key: 新语言代码）</summary>
        public System.Action<string> OnLanguageChanged;

        // 当前语言的翻译表
        private Dictionary<string, string> _translations = new();

        // ==================== 初始化 ====================

        /// <summary>
        /// 初始化本地化系统，加载指定语言的翻译表。
        /// TODO: 从 Resources 或 AssetBundle 加载语言 JSON 文件。
        /// TODO: 接入 AppConfig 获取默认语言。
        /// </summary>
        /// <param name="language">语言代码，如 "zh-CN"</param>
        public bool Init(string language = "zh-CN")
        {
            try
            {
                CurrentLanguage = language;
                _translations.Clear();

                // 从 Resources 加载语言文件
                var asset = Resources.Load<TextAsset>($"Config/Languages/{language}");
                if (asset != null)
                {
                    var data = JsonUtility.FromJson<LocalizationData>(asset.text);
                    if (data?.entries != null)
                    {
                        foreach (var entry in data.entries)
                            _translations[entry.key] = entry.value;
                    }
                }
                else
                {
                    Debug.LogWarning($"[LocalizationManager] 语言文件不存在: {language}，使用硬编码默认值");
                    _translations["test_key"] = "测试文本";
                }

                IsReady = true;
                Debug.Log($"[LocalizationManager] ✅ 初始化完成 — 语言: {language}, 条目: {_translations.Count}");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LocalizationManager] ❌ 初始化失败: {ex.Message}");
                return false;
            }
        }

        // ==================== 公开 API ====================

        /// <summary>获取本地化字符串（key 不存在返回 key 本身）</summary>
        public string Get(string key)
        {
            if (_translations.TryGetValue(key, out var value))
                return value;
            Debug.LogWarning($"[LocalizationManager] 未找到翻译 key: {key}");
            return key;
        }

        /// <summary>获取格式化后的本地化字符串</summary>
        /// <example>GetFormat("welcome", "Alice") → "欢迎你，Alice！"</example>
        public string GetFormat(string key, params object[] args)
        {
            var template = Get(key);
            if (args == null || args.Length == 0) return template;
            try { return string.Format(template, args); }
            catch { return template; }
        }

        /// <summary>判断 key 是否存在</summary>
        public bool HasKey(string key) => _translations.ContainsKey(key);

        /// <summary>切换语言</summary>
        public void SwitchLanguage(string language)
        {
            if (CurrentLanguage == language) return;
            Init(language);
            OnLanguageChanged?.Invoke(language);
        }

        // ==================== 内部数据结构 ====================

        [System.Serializable]
        private class LocalizationData
        {
            public List<LocalizationEntry> entries;
        }

        [System.Serializable]
        private class LocalizationEntry
        {
            public string key;
            public string value;
        }
    }
}
