using UnityEngine;
using UnityEngine.UI;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 屏幕适配组件 — 处理安全区域（刘海屏）、宽高比自适应。
    /// 挂载到 Canvas 上使用。
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public class UIScreenAdapter : MonoBehaviour
    {
        [Header("参考分辨率")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920, 1080);

        [Header("适配模式")]
        [SerializeField] private bool _enableSafeArea = true;
        [SerializeField] private bool _enableAspectRatioFit = true;

        [Header("安全区域适配")]
        [SerializeField] private RectTransform _safeAreaTarget; // 需要适配的根 RectTransform

        private CanvasScaler _scaler;
        private Rect _lastSafeArea;
        private Vector2 _lastScreenSize;

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            if (_safeAreaTarget == null)
                _safeAreaTarget = GetComponent<RectTransform>();

            ConfigureScaler();
        }

        private void Start()
        {
            ApplyAdaptation();
        }

        private void Update()
        {
            // 检测屏幕变化（旋转、分屏等）
            if (Screen.width != _lastScreenSize.x ||
                Screen.height != _lastScreenSize.y ||
                Screen.safeArea != _lastSafeArea)
            {
                ApplyAdaptation();
            }
        }

        private void ConfigureScaler()
        {
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = _referenceResolution;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = ComputeMatchRatio();
        }

        private float ComputeMatchRatio()
        {
            float screenRatio = (float)Screen.width / Screen.height;
            float refRatio = _referenceResolution.x / _referenceResolution.y;

            // 越宽（横屏）越 MatchHeight，越窄（竖屏）越 MatchWidth
            if (screenRatio >= refRatio)
                return 0f; // 横屏 → 匹配高度
            else
                return 1f; // 竖屏 → 匹配宽度
        }

        public void ApplyAdaptation()
        {
            _lastScreenSize = new Vector2(Screen.width, Screen.height);
            _lastSafeArea = Screen.safeArea;

            if (_enableAspectRatioFit)
            {
                _scaler.matchWidthOrHeight = ComputeMatchRatio();
            }

            if (_enableSafeArea)
            {
                ApplySafeArea();
            }
        }

        private void ApplySafeArea()
        {
            if (_safeAreaTarget == null) return;

            var safeArea = Screen.safeArea;
            var anchorMin = safeArea.position;
            var anchorMax = safeArea.position + safeArea.size;

            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            _safeAreaTarget.anchorMin = anchorMin;
            _safeAreaTarget.anchorMax = anchorMax;
            _safeAreaTarget.offsetMin = Vector2.zero;
            _safeAreaTarget.offsetMax = Vector2.zero;
        }
    }
}
