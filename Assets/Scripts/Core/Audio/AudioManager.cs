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

        [Header("音频源")]
        [SerializeField] private AudioSource _bgmSource;
        [SerializeField] private AudioSource _sfxSource;

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
        /// TODO: 从 AppConfig 读取默认音量。
        /// TODO: 创建 AudioSource 对象池（默认 4 个 SFX 通道）。
        /// </summary>
        public void Init()
        {
            if (_bgmSource == null)
            {
                var go = new GameObject("BGM_Source");
                go.transform.SetParent(transform);
                _bgmSource = go.AddComponent<AudioSource>();
                _bgmSource.loop = true;
                _bgmSource.playOnAwake = false;
            }

            if (_sfxSource == null)
            {
                var go = new GameObject("SFX_Source");
                go.transform.SetParent(transform);
                _sfxSource = go.AddComponent<AudioSource>();
                _sfxSource.loop = false;
                _sfxSource.playOnAwake = false;
            }

            ApplyVolumes();
            IsReady = true;
            Debug.Log("[AudioManager] ✅ 初始化完成");
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

        /// <summary>播放音效（一次性）</summary>
        public void PlaySFX(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || _sfxSource == null) return;
            _sfxSource.PlayOneShot(clip, volumeScale);
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

        private void ApplyVolumes()
        {
            if (_bgmSource != null)
                _bgmSource.volume = _masterVolume * _bgmVolume;
            if (_sfxSource != null)
                _sfxSource.volume = _masterVolume * _sfxVolume;
        }
    }
}
