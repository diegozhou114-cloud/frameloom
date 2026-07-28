using System;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Gameplay
{
    /// <summary>
    /// 建筑交互 — 点击建筑弹出信息面板（升级/移动/取消），
    /// 升级扣资源+启动计时器，编辑模式支持移动建筑。
    /// </summary>
    public class BuildingInteraction : MonoBehaviour
    {
        /// <summary>EventBus：建筑升级开始</summary>
        public const string EventUpgradeStarted = "UpgradeStarted";
        /// <summary>EventBus：建筑升级完成</summary>
        public const string EventUpgradeCompleted = "UpgradeCompleted";

        /// <summary>是否在编辑模式</summary>
        public bool IsEditMode { get; private set; }

        [SerializeField] private SelectionManager _selection;
        [SerializeField] private BuildingPlacer _placer;
        [SerializeField] private GridRenderer _gridRenderer;
        private SceneServiceRegistry _sceneServices;

        private void Start()
        {
            _sceneServices = SceneServiceRegistry.Instance;
            _sceneServices.Changed += BindSceneServices;
            BindSceneServices(_sceneServices.Current);

            EventBus.Subscribe<SelectionEvent>(SelectionManager.EventSelectionChanged, OnSelectionChanged);
            EventBus.Subscribe(SelectionManager.EventSelectionCleared, OnDeselected);
            EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        private void Update()
        {
            // 遍历所有建筑，检查升级是否完成
            CheckUpgradeTimers();
        }

        private void OnDestroy()
        {
            if (_sceneServices != null)
                _sceneServices.Changed -= BindSceneServices;
            EventBus.Unsubscribe<SelectionEvent>(SelectionManager.EventSelectionChanged, OnSelectionChanged);
            EventBus.Unsubscribe(SelectionManager.EventSelectionCleared, OnDeselected);
            EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        private void BindSceneServices(SceneServices services)
        {
            if (services.Interaction != null && services.Interaction != this) return;
            _selection = SelectionManager.HasInstance ? SelectionManager.Instance : null;
            _placer = services.Placer;
            _gridRenderer = services.GridRenderer;
            if (_placer == null)
                IsEditMode = false;
        }

        // ==================== 选中/取消选中 ====================

        private void OnSelectionChanged(SelectionEvent evt)
        {
            if (evt.Type != SelectionType.Building) return;
            var building = DataManager.Instance.Village.GetBuilding(evt.BuildingId);
            if (building == null) return;
        }

        private void OnDeselected()
        {
        }

        // ==================== 手势处理 ====================

        private void OnGesture(GestureData gesture)
        {
            if (_selection == null) return;
            if (gesture.Type == GestureType.LongPress)
            {
                // 长按进入编辑模式
                if (_selection.CurrentType == SelectionType.Building)
                {
                    EnterEditMode();
                }
            }
        }

        // ==================== 升级 ====================

        /// <summary>升级当前选中建筑（从 ConfigTableManager 读取消耗和时间）</summary>
        public void UpgradeSelected()
        {
            if (_selection == null) return;
            var building = _selection.SelectedBuilding;
            if (building == null) return;

            var config = ConfigTableManager.Instance.GetBuilding(building.TypeId);
            if (config == null)
            {
                Debug.LogError($"[BuildingInteraction] 配置表中未找到建筑类型 {building.TypeId}");
                return;
            }

            int nextLevel = building.Level.Value + 1;
            if (nextLevel > config.MaxLevel)
            {
                Debug.LogWarning("[BuildingInteraction] 已是最高等级");
                return;
            }

            int costGold = config.GetBuildCost(nextLevel);
            int buildTime = config.GetBuildTime(nextLevel);

            if (costGold < 0 || buildTime < 0)
            {
                Debug.LogError($"[BuildingInteraction] 配置数据异常: level={nextLevel}");
                return;
            }

            UpgradeSelected(costGold, 0, buildTime);
        }

        /// <summary>开始升级当前选中建筑（手动指定消耗）</summary>
        public void UpgradeSelected(int costGold, int costElixir, int durationSeconds)
        {
            if (_selection == null) return;
            var building = _selection.SelectedBuilding;
            if (building == null) return;

            if (costGold < 0 || costElixir < 0 || durationSeconds < 0)
            {
                Debug.LogError("[BuildingInteraction] 升级消耗或时长不能为负数");
                return;
            }

            if (building.IsUpgrading)
            {
                Debug.LogWarning("[BuildingInteraction] 建筑正在升级中");
                return;
            }

            BuildingConfig config = ConfigTableManager.Instance.GetBuilding(building.TypeId);
            if (config != null && building.Level.Value >= config.MaxLevel)
            {
                Debug.LogWarning("[BuildingInteraction] 已是最高等级");
                return;
            }

            var player = DataManager.Instance.Player;

            // 检查资源
            if (player.Gold.Value < costGold || player.Elixir.Value < costElixir)
            {
                Debug.LogWarning("[BuildingInteraction] 资源不足");
                return;
            }

            // 扣资源
            player.Gold.Value -= costGold;
            player.Elixir.Value -= costElixir;

            // 设置升级状态
            building.State.Value = BuildingState.Upgrading;
            building.UpgradeEndTime.Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + durationSeconds;

            EventBus.Publish(EventUpgradeStarted, building);
            Debug.Log($"[BuildingInteraction] 建筑升级开始: {building.Id}, " +
                      $"cost=({costGold}g, {costElixir}e), duration={durationSeconds}s");
        }

        /// <summary>立即完成升级（宝石加速）</summary>
        public void InstantUpgrade(int gemCost)
        {
            if (_selection == null) return;
            var building = _selection.SelectedBuilding;
            if (building == null || !building.IsUpgrading) return;

            if (gemCost < 0)
            {
                Debug.LogError("[BuildingInteraction] 宝石消耗不能为负数");
                return;
            }

            var player = DataManager.Instance.Player;
            if (player.Gems.Value < gemCost)
            {
                Debug.LogWarning("[BuildingInteraction] 宝石不足");
                return;
            }

            player.Gems.Value -= gemCost;
            CompleteUpgrade(building);
        }

        private void CompleteUpgrade(BuildingData building)
        {
            building.Level.Value++;
            building.State.Value = BuildingState.Normal;
            building.UpgradeEndTime.Value = 0;

            EventBus.Publish(EventUpgradeCompleted, building);
            Debug.Log($"[BuildingInteraction] 建筑升级完成: {building.Id}, level={building.Level.Value}");
        }

        private void CheckUpgradeTimers()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var village = DataManager.Instance.Village;

            foreach (var building in village.Buildings)
            {
                if (building.IsUpgrading && building.UpgradeEndTime.Value <= now)
                {
                    CompleteUpgrade(building);
                }
            }
        }

        // ==================== 编辑模式 ====================

        private void EnterEditMode()
        {
            IsEditMode = true;
            Debug.Log("[BuildingInteraction] 进入编辑模式");

            // 显示网格
            if (_gridRenderer != null)
                _gridRenderer.gameObject.SetActive(true);

            EventBus.Publish("EditModeEntered");
        }

        /// <summary>退出编辑模式</summary>
        public void ExitEditMode()
        {
            IsEditMode = false;
            Debug.Log("[BuildingInteraction] 退出编辑模式");

            // 隐藏网格
            if (_gridRenderer != null)
                _gridRenderer.gameObject.SetActive(false);

            EventBus.Publish("EditModeExited");
        }

        /// <summary>移动建筑到新位置（编辑模式下）</summary>
        /// <remarks>
        /// 注意：传统 CoC 类游戏中升级中的建筑通常不可移动。
        /// 当前实现对此做了保护——升级中的建筑会被拒绝移动。
        /// </remarks>
        public void MoveBuilding(string buildingId, Vector2Int newGridPos)
        {
            if (_selection == null) return;
            if (!IsEditMode) return;

            var building = DataManager.Instance.Village.GetBuilding(buildingId);
            if (building == null) return;

            // 升级中的建筑不允许移动（符合 CoC 风格预期）
            if (building.IsUpgrading)
            {
                Debug.LogWarning($"[BuildingInteraction] 建筑正在升级中，无法移动: {buildingId}");
                return;
            }

            // 从配置表获取建筑尺寸
            var config = ConfigTableManager.Instance.GetBuilding(building.TypeId);
            int width = config != null ? config.SizeX : 1;
            int height = config != null ? config.SizeY : 1;

            // 检查新位置是否在边界内
            var grid = GridManager.Instance;
            if (!grid.IsInBounds(newGridPos, width, height))
            {
                Debug.LogWarning($"[BuildingInteraction] 建筑超出边界: {newGridPos}, size=({width},{height})");
                return;
            }

            // 检查新位置是否被其他建筑占用（排除自身）
            if (grid.IsAreaOccupied(newGridPos, width, height, building.Id))
            {
                Debug.LogWarning($"[BuildingInteraction] 位置被占用: {newGridPos}");
                return;
            }

            building.GridX.Value = newGridPos.x;
            building.GridY.Value = newGridPos.y;
            Debug.Log($"[BuildingInteraction] 建筑移动: {buildingId} → {newGridPos}");
        }
    }
}
