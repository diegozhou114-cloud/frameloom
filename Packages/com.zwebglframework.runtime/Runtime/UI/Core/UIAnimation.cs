using System;
using System.Collections;
using UnityEngine;

namespace ZWebGLFrameWork.UI
{
    /// <summary>动画类型</summary>
    public enum UIAnimationType
    {
        None,   // 无动画
        Scale,  // 缩放弹性
        Fade,   // 淡入淡出
    }

    /// <summary>
    /// UI 动画组件 — 提供打开/关闭的过渡动画。
    /// 挂载到 UI 面板上，由 UIBase 在生命周期中调用。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UIAnimation : MonoBehaviour
    {
        [Header("打开动画")]
        [SerializeField] private UIAnimationType _openType = UIAnimationType.Scale;
        [SerializeField] private float _openDuration = 0.25f;
        [SerializeField] private AnimationCurve _openCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("关闭动画")]
        [SerializeField] private UIAnimationType _closeType = UIAnimationType.Scale;
        [SerializeField] private float _closeDuration = 0.15f;
        [SerializeField] private AnimationCurve _closeCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        [Header("弹性效果")]
        [SerializeField] private bool _useElastic = true;
        [SerializeField] private float _elasticAmplitude = 0.15f;

        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;
        private Coroutine _currentAnimation;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _rectTransform = GetComponent<RectTransform>();
        }

        private void OnDestroy()
        {
            _currentAnimation = null;
        }

        /// <summary>播放打开动画</summary>
        public void PlayOpen(Action onComplete = null)
        {
            if (_currentAnimation != null)
                StopCoroutine(_currentAnimation);

            if (_openType == UIAnimationType.None)
            {
                _canvasGroup.alpha = 1f;
                _rectTransform.localScale = Vector3.one;
                onComplete?.Invoke();
                return;
            }

            _canvasGroup.alpha = 0f;
            _rectTransform.localScale = _openType == UIAnimationType.Scale ? Vector3.zero : Vector3.one;

            _currentAnimation = StartCoroutine(AnimateOpen(onComplete));
        }

        /// <summary>播放关闭动画</summary>
        public void PlayClose(Action onComplete = null)
        {
            if (_currentAnimation != null)
                StopCoroutine(_currentAnimation);

            if (_closeType == UIAnimationType.None)
            {
                onComplete?.Invoke();
                return;
            }

            _currentAnimation = StartCoroutine(AnimateClose(onComplete));
        }

        private IEnumerator AnimateOpen(Action onComplete)
        {
            float elapsed = 0f;
            float startScale = 0f;
            float startAlpha = 0f;

            while (elapsed < _openDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _openDuration);
                float curveValue = _openCurve.Evaluate(t);

                // 弹性效果
                if (_useElastic && t > 0.8f)
                {
                    float elasticT = (t - 0.8f) / 0.2f;
                    float overshoot = 1f + _elasticAmplitude * Mathf.Sin(elasticT * Mathf.PI * 2f) * (1f - elasticT);
                    _rectTransform.localScale = Vector3.one * overshoot;
                }
                else if (_openType == UIAnimationType.Scale)
                {
                    _rectTransform.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, curveValue);
                }

                if (_openType == UIAnimationType.Fade || _openType == UIAnimationType.Scale)
                {
                    _canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, curveValue);
                }

                yield return null;
            }

            _rectTransform.localScale = Vector3.one;
            _canvasGroup.alpha = 1f;
            _currentAnimation = null;
            onComplete?.Invoke();
        }

        private IEnumerator AnimateClose(Action onComplete)
        {
            float elapsed = 0f;
            float startScale = _rectTransform.localScale.x;
            float startAlpha = _canvasGroup.alpha;

            while (elapsed < _closeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _closeDuration);
                float curveValue = _closeCurve.Evaluate(t);

                if (_closeType == UIAnimationType.Scale)
                {
                    _rectTransform.localScale = Vector3.one * Mathf.Lerp(startScale, 0f, curveValue);
                }

                if (_closeType == UIAnimationType.Fade || _closeType == UIAnimationType.Scale)
                {
                    _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, curveValue);
                }

                yield return null;
            }

            _rectTransform.localScale = Vector3.zero;
            _canvasGroup.alpha = 0f;
            _currentAnimation = null;
            onComplete?.Invoke();
        }

        /// <summary>停止当前动画</summary>
        public void Stop()
        {
            if (_currentAnimation != null)
            {
                StopCoroutine(_currentAnimation);
                _currentAnimation = null;
            }
        }
    }
}
