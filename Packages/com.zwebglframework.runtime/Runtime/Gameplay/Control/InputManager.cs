using System;
using UnityEngine;
using UnityEngine.EventSystems;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Gameplay
{
    /// <summary>手势类型</summary>
    public enum GestureType
    {
        None,
        Tap,        // 短按（< 0.2s）
        LongPress,  // 长按（> 0.5s，不移动）
        Drag,       // 拖拽（单指移动）
        Pinch,      // 双指缩放
    }

    /// <summary>手势事件数据</summary>
    public struct GestureData
    {
        public GestureType Type;
        public Vector2 ScreenPosition;
        public Vector2 Delta;
        public float PinchDelta;  // 缩放增量
    }

    /// <summary>
    /// 触摸输入管理器 — 手势识别，防止 UI 穿透。
    /// 识别结果通过 EventBus 广播。
    /// </summary>
    public class InputManager : SingletonDontDestroy<InputManager>
    {
        /// <summary>EventBus 事件键：手势</summary>
        public const string EventGesture = "Gesture";

        [Header("手势参数")]
        [SerializeField] private float _tapMaxDuration = 0.2f;
        [SerializeField] private float _tapMaxMovement = 10f;
        [SerializeField] private float _longPressDuration = 0.5f;
        [SerializeField] private float _pinchDeadZone = 5f;

        /// <summary>是否在 UI 上（防止穿透）</summary>
        public bool IsOverUI { get; private set; }

        // 手势状态
        private int _touchCount;
        private Vector2 _touchStartPos;
        private Vector2 _lastDragPosition;
        private float _touchStartTime;
        private float _lastPinchDist;
        private bool _isDragging;
        private bool _longPressTriggered;
        private bool _tapBlocked;

        [SerializeField] private CameraController _cameraController;
        private Camera _mainCamera;
        private SceneServiceRegistry _sceneServices;

        private void Start()
        {
            _sceneServices = SceneServiceRegistry.Instance;
            _sceneServices.Changed += BindSceneServices;
            BindSceneServices(_sceneServices.Current);
        }

        protected override void OnDestroy()
        {
            if (_sceneServices != null)
                _sceneServices.Changed -= BindSceneServices;
            base.OnDestroy();
        }

        private void BindSceneServices(SceneServices services)
        {
            _mainCamera = services.MainCamera;
            _cameraController = services.CameraController;
        }

        private void Update()
        {
            // 多指触控时 -1 只代表第一指；各触摸处理中再按 fingerId 二次验证
            IsOverUI = EventSystem.current != null &&
                       EventSystem.current.IsPointerOverGameObject(-1);

            if (Input.touchSupported)
                HandleTouch();
            else
                HandleMouse(); // Editor 调试
        }

        // ==================== 触摸 ====================

        private void HandleTouch()
        {
            int count = Input.touchCount;

            if (count == 0)
            {
                ResetGesture();
                return;
            }

            if (count == 1)
                HandleSingleTouch(Input.GetTouch(0));
            else if (count == 2)
                HandlePinch(Input.GetTouch(0), Input.GetTouch(1));
        }

        private void HandleSingleTouch(Touch touch)
        {
            switch (touch.phase)
            {
                case TouchPhase.Began:      HandleTouchBegan(touch);    break;
                case TouchPhase.Moved:      HandleTouchMoved(touch);    break;
                case TouchPhase.Ended:      HandleTouchEnded(touch);    break;
                case TouchPhase.Stationary: HandleTouchStationary(touch); break;
            }
        }

        private void HandleTouchBegan(Touch touch)
        {
            if (IsOverUI) return;
            if (EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return;
            _touchStartPos = touch.position;
            _touchStartTime = Time.unscaledTime;
            _isDragging = false;
            _longPressTriggered = false;
            _tapBlocked = false;
        }

        private void HandleTouchMoved(Touch touch)
        {
            float dist = Vector2.Distance(touch.position, _touchStartPos);
            if (dist > _tapMaxMovement && !_isDragging)
            {
                _isDragging = true;
                _tapBlocked = true;
            }

            if (_isDragging)
            {
                // 相机平移
                Vector3 worldDelta = ScreenToWorldDelta(touch.deltaPosition);
                _cameraController?.Pan(worldDelta);

                BroadcastGesture(new GestureData
                {
                    Type = GestureType.Drag,
                    ScreenPosition = touch.position,
                    Delta = touch.deltaPosition,
                });
            }
        }

        private void HandleTouchEnded(Touch touch)
        {
            float duration = Time.unscaledTime - _touchStartTime;
            if (!_tapBlocked && !_isDragging && duration <= _tapMaxDuration)
            {
                BroadcastGesture(new GestureData
                {
                    Type = GestureType.Tap,
                    ScreenPosition = touch.position,
                });
            }
        }

        private void HandleTouchStationary(Touch touch)
        {
            if (!_longPressTriggered && !_isDragging &&
                Time.unscaledTime - _touchStartTime >= _longPressDuration)
            {
                _longPressTriggered = true;
                _tapBlocked = true;
                BroadcastGesture(new GestureData
                {
                    Type = GestureType.LongPress,
                    ScreenPosition = touch.position,
                });
            }
        }

        private void HandlePinch(Touch t0, Touch t1)
        {
            float currentDist = Vector2.Distance(t0.position, t1.position);

            if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
            {
                _lastPinchDist = currentDist;
                _tapBlocked = true;
                return;
            }

            float delta = currentDist - _lastPinchDist;
            _lastPinchDist = currentDist;

            if (Mathf.Abs(delta) > _pinchDeadZone)
            {
                _cameraController?.Zoom(delta);

                BroadcastGesture(new GestureData
                {
                    Type = GestureType.Pinch,
                    PinchDelta = delta,
                });
            }
        }

        // ==================== 鼠标（Editor 调试） ====================

        private void HandleMouse()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                // 滚轮缩放
                _cameraController?.Zoom(scroll * 500f);
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (IsOverUI) return;
                _touchStartPos = Input.mousePosition;
                _touchStartTime = Time.unscaledTime;
                _isDragging = false;
            }

            if (Input.GetMouseButton(0))
            {
                float dist = Vector2.Distance(Input.mousePosition, (Vector3)_touchStartPos);
                if (dist > _tapMaxMovement) _isDragging = true;

                if (_isDragging)
                {
                    if (_lastDragPosition.sqrMagnitude < 0.1f)
                        _lastDragPosition = _touchStartPos; // 初始化
                    Vector3 worldDelta = ScreenToWorldDelta(
                        (Vector2)Input.mousePosition - _lastDragPosition);
                    _lastDragPosition = Input.mousePosition;
                    _cameraController?.Pan(worldDelta);
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                float duration = Time.unscaledTime - _touchStartTime;
                if (!_isDragging && duration <= _tapMaxDuration)
                {
                    BroadcastGesture(new GestureData
                    {
                        Type = GestureType.Tap,
                        ScreenPosition = Input.mousePosition,
                    });
                }
            }
        }

        // ==================== 辅助 ====================

        private Vector3 ScreenToWorldDelta(Vector2 screenDelta)
        {
            if (_mainCamera == null) return Vector3.zero;

            if (_mainCamera.orthographic)
            {
                float worldPerPixel = _mainCamera.orthographicSize * 2f / Screen.height;
                return new Vector3(-screenDelta.x * worldPerPixel, 0f, -screenDelta.y * worldPerPixel);
            }
            else
            {
                // 透视相机：在相机前方平面上计算世界空间偏移
                Vector3 screenPointStart = new Vector3(0, 0, _mainCamera.nearClipPlane);
                Vector3 screenPointEnd = new Vector3(screenDelta.x, screenDelta.y, _mainCamera.nearClipPlane);
                Vector3 worldStart = _mainCamera.ScreenToWorldPoint(screenPointStart);
                Vector3 worldEnd = _mainCamera.ScreenToWorldPoint(screenPointEnd);
                Vector3 delta = worldEnd - worldStart;
                delta.y = 0;
                return new Vector3(-delta.x, 0f, -delta.z);
            }
        }

        private void BroadcastGesture(GestureData data)
        {
            EventBus.Publish(EventGesture, data);
        }

        private void ResetGesture()
        {
            _isDragging = false;
            _longPressTriggered = false;
            _tapBlocked = false;
        }
    }
}
