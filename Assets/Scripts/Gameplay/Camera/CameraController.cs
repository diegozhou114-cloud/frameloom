using System.Collections;
using UnityEngine;

namespace ZWebGLFrameWork.Gameplay
{
    /// <summary>
    /// 等距视角相机控制器 — 部落冲突风格 45° 斜俯视。
    /// 单指拖拽平移 / 双指缩放 / 边界限制 / 平滑缓动。
    /// 挂载到场景主 Camera 上。
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("视角")]
        [SerializeField] private float _pitchAngle = 45f;        // 俯仰角
        [SerializeField] private float _yawAngle = 45f;          // 偏航角（等距风格）

        [Header("缩放")]
        [SerializeField] private float _minZoom = 5f;
        [SerializeField] private float _maxZoom = 20f;
        [SerializeField] private float _defaultZoom = 12f;
        [SerializeField] private float _zoomSpeed = 0.05f;
        [SerializeField] private float _zoomSmoothTime = 0.15f;

        [Header("平移")]
        [SerializeField] private float _dragSpeed = 0.5f;
        [SerializeField] private float _edgeScrollSize = 20f;   // 边缘滚动像素
        [SerializeField] private float _edgeScrollSpeed = 10f;

        [Header("边界")]
        [SerializeField] private Vector2 _mapMin = new Vector2(-20, -20);
        [SerializeField] private Vector2 _mapMax = new Vector2(20, 20);

        [Header("平滑")]
        [SerializeField] private float _moveSmoothTime = 0.1f;

        private float _targetZoom;
        private float _zoomVelocity;
        private Vector3 _targetPosition;
        private Vector3 _moveVelocity;
        private Transform _camTransform;
        private float _originalSmoothTime;

        private void Awake()
        {
            _camTransform = transform;
            _targetZoom = _defaultZoom;
            _targetPosition = _camTransform.position;
            _originalSmoothTime = _moveSmoothTime;

            // 设置等距视角（如果未预设）
            SetIsometricView();
        }

        private void SetIsometricView()
        {
            _camTransform.rotation = Quaternion.Euler(_pitchAngle, _yawAngle, 0f);
        }

        private void LateUpdate()
        {
            // 鼠标边缘滚动（Editor 调试用）
            HandleEdgeScroll();

            // 平滑缩放
            float currentZoom = _camTransform.position.y;
            float smoothZoom = Mathf.SmoothDamp(currentZoom, _targetZoom,
                ref _zoomVelocity, _zoomSmoothTime);

            // 平滑移动
            Vector3 smoothPos = Vector3.SmoothDamp(_camTransform.position,
                _targetPosition, ref _moveVelocity, _moveSmoothTime);

            // 应用（保持等距旋转）
            _camTransform.position = new Vector3(
                Mathf.Clamp(smoothPos.x, _mapMin.x, _mapMax.x),
                Mathf.Clamp(smoothZoom, _minZoom, _maxZoom),
                Mathf.Clamp(smoothPos.z, _mapMin.y, _mapMax.y));
        }

        // ==================== 公共 API ====================

        /// <summary>缩放（正值=放大，负值=缩小）</summary>
        public void Zoom(float delta)
        {
            _targetZoom = Mathf.Clamp(_targetZoom - delta * _zoomSpeed,
                _minZoom, _maxZoom);
        }

        /// <summary>世界坐标平移</summary>
        public void Pan(Vector3 worldDelta)
        {
            _targetPosition += worldDelta * _dragSpeed;
            _targetPosition.x = Mathf.Clamp(_targetPosition.x, _mapMin.x, _mapMax.x);
            _targetPosition.z = Mathf.Clamp(_targetPosition.z, _mapMin.y, _mapMax.y);
        }

        /// <summary>立刻设置位置</summary>
        public void SetPosition(Vector3 position)
        {
            _targetPosition = position;
            _camTransform.position = position;
        }

        /// <summary>聚焦到世界坐标</summary>
        public void FocusOn(Vector3 worldPos, float duration = 0.5f)
        {
            _targetPosition = new Vector3(worldPos.x, _targetZoom, worldPos.z);
            _moveSmoothTime = duration;
            StopAllCoroutines();
            StartCoroutine(RestoreSmoothTime(duration));
        }

        private IEnumerator RestoreSmoothTime(float delay)
        {
            yield return new WaitForSeconds(delay);
            _moveSmoothTime = _originalSmoothTime;
        }

        /// <summary>重置视角</summary>
        public void ResetView()
        {
            _targetPosition = Vector3.zero;
            _targetZoom = _defaultZoom;
        }

        // ==================== 边缘滚动 ====================

        private void HandleEdgeScroll()
        {
            Vector3 mouse = Input.mousePosition;
            Vector3 move = Vector3.zero;

            if (mouse.x <= _edgeScrollSize) move.x = -1;
            if (mouse.x >= Screen.width - _edgeScrollSize) move.x = 1;
            if (mouse.y <= _edgeScrollSize) move.z = -1;
            if (mouse.y >= Screen.height - _edgeScrollSize) move.z = 1;

            if (move != Vector3.zero)
            {
                // 考虑旋转方向
                Vector3 worldMove = _camTransform.TransformDirection(move);
                worldMove.y = 0;
                _targetPosition += worldMove.normalized * _edgeScrollSpeed * Time.deltaTime;
                _targetPosition.x = Mathf.Clamp(_targetPosition.x, _mapMin.x, _mapMax.x);
                _targetPosition.z = Mathf.Clamp(_targetPosition.z, _mapMin.y, _mapMax.y);
            }
        }

        /// <summary>相机当前缩放值</summary>
        public float CurrentZoom => _camTransform.position.y;
    }
}
