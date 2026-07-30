using System;
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
        public List<string> SupportedLanguages { get; private set; } = new();

        /// <summary>语言切换事件（key: 新语言代码）</summary>
        public event System.Action<string> OnLanguageChanged;

        // 当前语言的翻译表
        private Dictionary<string, string> _translations = new();

        private bool _scanned;

        // ==================== 初始化 ====================

        /// <summary>
        /// 初始化本地化系统，加载指定语言的翻译表。
        /// 若未指定语言，从 SystemLanguage 自动检测。
        /// </summary>
        public bool Init(string language = null)
        {
            try
            {
                // 自动检测语言
                if (string.IsNullOrEmpty(language))
                    language = DetectSystemLanguage();

                // 扫描可用语言文件
                ScanAvailableLanguages();

                // 先完整解析候选语言表，成功后再提交运行时状态。
                if (!TryLoadLanguageFile(language, out string loadedLanguage,
                        out Dictionary<string, string> translations))
                    throw new InvalidOperationException("未找到有效的本地化语言表。");

                CurrentLanguage = loadedLanguage;
                _translations = translations;

                IsReady = true;
                Debug.Log($"[LocalizationManager] ✅ 初始化完成 — 语言: {CurrentLanguage}, 条目: {_translations.Count}");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LocalizationManager] ❌ 初始化失败: {ex.Message}");
                return false;
            }
        }

        private static bool TryParseTranslations(TextAsset asset,
            out Dictionary<string, string> translations)
        {
            translations = null;
            if (asset == null) return false;

            LocalizationData data = JsonUtility.FromJson<LocalizationData>(asset.text);
            if (data?.entries == null || data.entries.Count == 0) return false;

            var parsed = new Dictionary<string, string>();
            foreach (LocalizationEntry entry in data.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key))
                    return false;
                parsed[entry.key] = entry.value;
            }

            translations = parsed;
            return true;
        }

        private static bool TryLoadLanguageFile(string language, out string loadedLanguage,
            out Dictionary<string, string> translations)
        {
            var asset = Resources.Load<TextAsset>($"Config/Languages/{language}");
            if (TryParseTranslations(asset, out translations))
            {
                loadedLanguage = language;
                return true;
            }

            Debug.LogWarning($"[LocalizationManager] 语言文件无效或不存在: {language}，尝试 en-US");
            asset = Resources.Load<TextAsset>("Config/Languages/en-US");
            if (TryParseTranslations(asset, out translations))
            {
                loadedLanguage = "en-US";
                return true;
            }

            loadedLanguage = "en-US";
            translations = new Dictionary<string, string>
            {
                ["test_key"] = "测试文本",
            };
            return true;
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
            catch (System.FormatException ex)
            {
                Debug.LogWarning($"[LocalizationManager] 格式化失败 key={key}, template={template}: {ex.Message}");
                return template;
            }
        }

        /// <summary>判断 key 是否存在</summary>
        public bool HasKey(string key) => _translations.ContainsKey(key);

        /// <summary>切换语言</summary>
        public void SwitchLanguage(string language)
        {
            if (CurrentLanguage == language) return;
            _scanned = false;
            if (Init(language))
                OnLanguageChanged?.Invoke(CurrentLanguage);
        }

        /// <summary>从系统语言自动检测最佳匹配</summary>
        private static string DetectSystemLanguage()
        {
            return Application.systemLanguage switch
            {
                SystemLanguage.ChineseSimplified => "zh-CN",
                SystemLanguage.ChineseTraditional => "zh-TW",
                SystemLanguage.Japanese => "ja-JP",
                SystemLanguage.Korean => "ko-KR",
                SystemLanguage.German => "de-DE",
                SystemLanguage.French => "fr-FR",
                SystemLanguage.Spanish => "es-ES",
                SystemLanguage.Portuguese => "pt-BR",
                _ => "en-US",
            };
        }

        /// <summary>扫描 Resources/Config/Languages/ 下可用的语言文件</summary>
        private void ScanAvailableLanguages()
        {
            if (_scanned) return;
            _scanned = true;

            SupportedLanguages.Clear();
            foreach (TextAsset languageAsset in Resources.LoadAll<TextAsset>("Config/Languages"))
            {
                string language = languageAsset.name;
                if (!string.IsNullOrWhiteSpace(language) && !SupportedLanguages.Contains(language))
                    SupportedLanguages.Add(language);
            }
            SupportedLanguages.Sort(StringComparer.Ordinal);
            Debug.Log($"[LocalizationManager] 可用语言: {string.Join(", ", SupportedLanguages)}");
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
