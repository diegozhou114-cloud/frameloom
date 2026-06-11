using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Gameplay
{
    /// <summary>
    /// 建筑放置器 — 从 UI 菜单拖出建筑，跟随手指/鼠标，
    /// 自动吸附网格，实时合法性检测（红/绿），松手确认放置。
    /// </summary>
    public class BuildingPlacer : Singleton<BuildingPlacer>
    {
        /// <summary>EventBus：开始放置</summary>
        public const string EventPlacementStart = "PlacementStarted";
        /// <summary>EventBus：放置完成</summary>
        public const string EventPlacementComplete = "PlacementCompleted";
        /// <summary>EventBus：放置取消</summary>
        public const string EventPlacementCancelled = "PlacementCancelled";

        /// <summary>是否正在放置模式</summary>
        public bool IsPlacing { get; private set; }

        /// <summary>当前正在放置的建筑类型 ID</summary>
        public int PlacingTypeId { get; private set; }

        /// <summary>正在放置的建筑宽度（网格单位）</summary>
        public int PlacingWidth { get; private set; } = 3;

        /// <summary>正在放置的建筑高度（网格单位）</summary>
        public int PlacingHeight { get; private set; } = 3;

        [Header("预览")]
        [SerializeField] private GameObject _placementPreviewPrefab;
        [SerializeField] private Material _validMat;
        [SerializeField] private Material _invalidMat;

        private GameObject _previewInstance;
        private MeshRenderer _previewRenderer;
        private GridManager _grid;
        private GridRenderer _gridRenderer;
        private Camera _mainCamera;

        private void Start()
        {
            _grid = GridManager.Instance;
            _gridRenderer = FindObjectOfType<GridRenderer>();
            _mainCamera = Camera.main;

            // 监听手势
            EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        private void Update()
        {
            if (!IsPlacing) return;

            // 跟随鼠标/触摸位置
            UpdatePlacementPreview();
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        // ==================== 开始/结束放置 ====================

        /// <summary>开始放置模式（从 UI 调用）</summary>
        public void StartPlacement(int buildingTypeId, int width = 3, int height = 3)
        {
            // 取消当前选中
            SelectionManager.Instance?.ClearSelection();

            IsPlacing = true;
            PlacingTypeId = buildingTypeId;
            PlacingWidth = width;
            PlacingHeight = height;

            CreatePreview();
            _gridRenderer?.SetGridVisible(true);

            EventBus.Publish(EventPlacementStart);
        }

        /// <summary>取消放置</summary>
        public void CancelPlacement()
        {
            if (!IsPlacing) return;

            IsPlacing = false;
            DestroyPreview();
            _gridRenderer?.HideHighlight();
            _gridRenderer?.SetGridVisible(false);

            EventBus.Publish(EventPlacementCancelled);
        }

        // ==================== 预览 ====================

        private void CreatePreview()
        {
            if (_placementPreviewPrefab != null)
            {
                _previewInstance = Instantiate(_placementPreviewPrefab, transform);
                _previewRenderer = _previewInstance.GetComponentInChildren<MeshRenderer>();
            }
            else
            {
                // 动态创建简单预览
                _previewInstance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _previewInstance.name = "PlacementPreview";
                _previewInstance.transform.SetParent(transform);
                _previewRenderer = _previewInstance.GetComponent<MeshRenderer>();

                if (_validMat != null)
                    _previewRenderer.material = _validMat;
            }
        }

        private void DestroyPreview()
        {
            if (_previewInstance != null)
            {
                Destroy(_previewInstance);
                _previewInstance = null;
                _previewRenderer = null;
            }
        }

        private void UpdatePlacementPreview()
        {
            if (_previewInstance == null) return;

            var ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);

            if (!plane.Raycast(ray, out float enter)) return;

            Vector3 worldPos = ray.GetPoint(enter);
            var gridPos = _grid.SnapToGrid(worldPos, PlacingWidth, PlacingHeight);
            Vector3 snappedWorld = _grid.GridToWorld(gridPos.x, gridPos.y,
                PlacingWidth * 0.5f / _grid.CellSize,
                PlacingHeight * 0.5f / _grid.CellSize);

            // 更新预览位置
            _previewInstance.transform.position = snappedWorld + Vector3.up * 0.05f;
            _previewInstance.transform.localScale = new Vector3(
                PlacingWidth * _grid.CellSize * 0.9f,
                0.1f,
                PlacingHeight * _grid.CellSize * 0.9f);

            // 合法性检测
            bool valid = IsPlacementValid(gridPos);

            // 更新材质颜色
            if (_previewRenderer != null)
            {
                _previewRenderer.material.color = valid ?
                    new Color(0, 1, 0, 0.4f) : new Color(1, 0, 0, 0.4f);
            }

            // 更新网格高亮
            _gridRenderer?.ShowHighlight(snappedWorld, PlacingWidth, PlacingHeight, valid);
        }

        // ==================== 手势处理 ====================

        private void OnGesture(GestureData gesture)
        {
            if (!IsPlacing) return;

            if (gesture.Type == GestureType.Tap)
            {
                TryPlace();
            }
        }

        private void TryPlace()
        {
            var ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);

            if (!plane.Raycast(ray, out float enter)) return;

            Vector3 worldPos = ray.GetPoint(enter);
            var gridPos = _grid.SnapToGrid(worldPos, PlacingWidth, PlacingHeight);

            if (!IsPlacementValid(gridPos))
            {
                Debug.LogWarning("[BuildingPlacer] 放置位置不合法");
                return;
            }

            // 创建建筑数据
            var building = BuildingData.Create(PlacingTypeId, gridPos.x, gridPos.y);

            var village = DataManager.Instance.Village;
            if (!village.AddBuilding(building))
            {
                Debug.LogWarning("[BuildingPlacer] 不能添加更多建筑");
                return;
            }

            Debug.Log($"[BuildingPlacer] 放置建筑: type={PlacingTypeId}, pos={gridPos}");

            // 完成放置
            CancelPlacement();
            EventBus.Publish(EventPlacementComplete, building);
        }

        // ==================== 合法性检测 ====================

        private bool IsPlacementValid(Vector2Int gridPos)
        {
            if (!_grid.IsInBounds(gridPos, PlacingWidth, PlacingHeight))
                return false;

            if (_grid.IsAreaOccupied(gridPos, PlacingWidth, PlacingHeight))
                return false;

            return true;
        }
    }
}
