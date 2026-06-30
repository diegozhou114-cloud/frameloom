using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Gameplay
{
    /// <summary>选中的对象类型</summary>
    public enum SelectionType
    {
        None,
        Building,
        EmptyCell,
    }

    /// <summary>选中事件数据</summary>
    public struct SelectionEvent
    {
        public SelectionType Type;
        public string BuildingId;
        public Vector2Int GridPosition;
    }

    /// <summary>
    /// 选中管理器 — 监听 Tap 手势，射线检测命中建筑或空地。
    /// 选中变更通过 EventBus 广播。
    /// </summary>
    /// <remarks>当前仅支持单选；多选（框选/Shift+点击）尚未实现。</remarks>
    public class SelectionManager : Singleton<SelectionManager>
    {
        /// <summary>EventBus：选中变更</summary>
        public const string EventSelectionChanged = "SelectionChanged";

        /// <summary>EventBus：取消选中</summary>
        public const string EventSelectionCleared = "SelectionCleared";

        /// <summary>当前选中的建筑 ID（null = 未选中建筑）</summary>
        public string SelectedBuildingId { get; private set; }

        /// <summary>当前选中的网格位置</summary>
        public Vector2Int SelectedGridPos { get; private set; }

        /// <summary>选中类型</summary>
        public SelectionType CurrentType { get; private set; }

        /// <summary>被选中的建筑数据</summary>
        public BuildingData SelectedBuilding
        {
            get
            {
                if (CurrentType != SelectionType.Building) return null;
                return DataManager.Instance?.Village?.GetBuilding(SelectedBuildingId);
            }
        }

        private Camera _mainCamera;
        private GridManager _grid;

        private void Start()
        {
            _mainCamera = Camera.main;
            _grid = GridManager.Instance;

            // 监听 Tap 手势
            EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        private void OnGesture(GestureData gesture)
        {
            if (gesture.Type != GestureType.Tap) return;

            var ray = _mainCamera.ScreenPointToRay(gesture.ScreenPosition);

            // 射线检测建筑 Collider
            if (Physics.Raycast(ray, out var hit, 100f))
            {
                // 检查是否是建筑
                var buildingView = hit.collider.GetComponent<BuildingView>();
                if (buildingView != null)
                {
                    SelectBuilding(buildingView.BuildingId);
                    return;
                }
            }

            // 平面射线检测（检测网格地块）
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 worldPos = ray.GetPoint(enter);
                var gridPos = _grid.WorldToGrid(worldPos);

                if (_grid.IsInBounds(gridPos))
                {
                    SelectCell(gridPos);
                    return;
                }
            }

            // 什么都没选中
            ClearSelection();
        }

        private void SelectBuilding(string buildingId)
        {
            SelectedBuildingId = buildingId;
            CurrentType = SelectionType.Building;

            var building = SelectedBuilding;
            if (building != null)
            {
                SelectedGridPos = new Vector2Int(building.GridX.Value, building.GridY.Value);
            }

            BroadcastEvent(new SelectionEvent
            {
                Type = SelectionType.Building,
                BuildingId = buildingId,
            });
        }

        private void SelectCell(Vector2Int gridPos)
        {
            // 检查该位置是否有建筑
            var building = DataManager.Instance?.Village?.GetBuildingAt(gridPos.x, gridPos.y);
            if (building != null)
            {
                SelectBuilding(building.Id);
                return;
            }

            SelectedBuildingId = null;
            SelectedGridPos = gridPos;
            CurrentType = SelectionType.EmptyCell;

            BroadcastEvent(new SelectionEvent
            {
                Type = SelectionType.EmptyCell,
                GridPosition = gridPos,
            });
        }

        /// <summary>取消选中</summary>
        public void ClearSelection()
        {
            if (CurrentType == SelectionType.None) return;

            SelectedBuildingId = null;
            CurrentType = SelectionType.None;
            EventBus.Publish(EventSelectionCleared);
        }

        private void BroadcastEvent(SelectionEvent evt)
        {
            EventBus.Publish(EventSelectionChanged, evt);
        }
    }

    /// <summary>
    /// 建筑视图组件 — 挂载到建筑 GameObject 上，用于射线检测识别。
    /// </summary>
    public class BuildingView : MonoBehaviour
    {
        public string BuildingId;
    }
}
