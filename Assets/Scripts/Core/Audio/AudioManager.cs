using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 音频管理器 — 跨场景单例，管理 BGM / SFX 播放与音量控制。
    /// 后续接入对象池管理 AudioSource 实例。
    ///
    /// 用法：
    ///   AudioManager.Instance.Init();
    ///   AudioManager.Instance.PlayBGM(bgmClip);
    ///   AudioManager.Instance.SetMasterVolume(0.8f);
    /// </summary>
    public class AudioManager : SingletonDontDestroy<AudioManager>
    {
        /// <summary>是否已初始化</summary>
        public bool IsReady { get; private set; }
        private bool _initialized;

        [Header("音频源")]
        [SerializeField] private AudioSource _bgmSource;

        // SFX 对象池 — 4 通道轮询，避免音效互相截断
        private AudioSource[] _sfxPool;
        private int _sfxIndex;
        private const int SfxPoolSize = 4;

        [Header("音量（0~1）")]
        [SerializeField] [Range(0f, 1f)] private float _masterVolume = 1f;
        [SerializeField] [Range(0f, 1f)] private float _bgmVolume = 0.8f;
        [SerializeField] [Range(0f, 1f)] private float _sfxVolume = 1f;

        public float MasterVolume => _masterVolume;
        public float BGMVolume => _bgmVolume;
        public float SFXVolume => _sfxVolume;

        // ==================== 初始化 ====================

        /// <summary>
        /// 初始化音频管理器。
        /// </summary>
        public bool Init()
        {
            if (_initialized) return true;
            try
            {
                // 音量使用 Inspector 默认值，后续可通过 SetMasterVolume/SetBGMVolume/SetSFXVolume 运行时调整

                if (_bgmSource == null)
                {
                    var go = new GameObject("BGM_Source");
                    go.transform.SetParent(transform);
                    _bgmSource = go.AddComponent<AudioSource>();
                    _bgmSource.loop = true;
                    _bgmSource.playOnAwake = false;
                }

                // 创建 SFX 对象池
                _sfxPool = new AudioSource[SfxPoolSize];
                for (int i = 0; i < SfxPoolSize; i++)
                {
                    var go = new GameObject($"SFX_Source_{i}");
                    go.transform.SetParent(transform);
                    var src = go.AddComponent<AudioSource>();
                    src.loop = false;
                    src.playOnAwake = false;
                    _sfxPool[i] = src;
                }

                ApplyVolumes();
                _initialized = true;
                IsReady = true;
                Debug.Log("[AudioManager] ✅ 初始化完成");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AudioManager] ❌ 初始化失败: {ex.Message}");
                return false;
            }
        }

        // ==================== BGM ====================

        /// <summary>播放背景音乐（自动循环）</summary>
        public void PlayBGM(AudioClip clip)
        {
            if (clip == null || _bgmSource == null) return;
            if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;

            _bgmSource.clip = clip;
            _bgmSource.Play();
            Debug.Log($"[AudioManager] BGM: {clip.name}");
        }

        /// <summary>停止 BGM</summary>
        public void StopBGM()
        {
            _bgmSource?.Stop();
        }

        // ==================== SFX ====================

        /// <summary>播放音效（一次性，轮询池）</summary>
        public void PlaySFX(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || _sfxPool == null) return;
            if (_sfxIndex >= int.MaxValue - 1) _sfxIndex = 0;
            var src = _sfxPool[_sfxIndex % SfxPoolSize];
            _sfxIndex++;
            src.PlayOneShot(clip, volumeScale);
        }

        /// <summary>停止所有音效</summary>
        public void StopAllSFX()
        {
            if (_sfxPool == null) return;
            foreach (var s in _sfxPool) s.Stop();
        }

        // ==================== 音量控制 ====================

        /// <summary>设置主音量</summary>
        public void SetMasterVolume(float value)
        {
            _masterVolume = Mathf.Clamp01(value);
            ApplyVolumes();
        }

        /// <summary>设置 BGM 音量</summary>
        public void SetBGMVolume(float value)
        {
            _bgmVolume = Mathf.Clamp01(value);
            ApplyVolumes();
        }

        /// <summary>设置 SFX 音量</summary>
        public void SetSFXVolume(float value)
        {
            _sfxVolume = Mathf.Clamp01(value);
            ApplyVolumes();
        }

        /// <summary>静音切换</summary>
        public void ToggleMute()
        {
            AudioListener.pause = !AudioListener.pause;
        }

        // ==================== 内部 ====================

        protected override void OnDestroy()
        {
            _initialized = false;
            base.OnDestroy();
        }

        private void ApplyVolumes()
        {
            if (_bgmSource != null)
                _bgmSource.volume = _masterVolume * _bgmVolume;
            if (_sfxPool != null)
                foreach (var s in _sfxPool)
                    s.volume = _masterVolume * _sfxVolume;
        }
    }
}
