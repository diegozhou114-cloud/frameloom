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

        private void Start()
        {
            if (_selection == null) _selection = FindObjectOfType<SelectionManager>();
            if (_placer == null) _placer = FindObjectOfType<BuildingPlacer>();

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
            EventBus.Unsubscribe<SelectionEvent>(SelectionManager.EventSelectionChanged, OnSelectionChanged);
            EventBus.Unsubscribe(SelectionManager.EventSelectionCleared, OnDeselected);
            EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        // ==================== 选中/取消选中 ====================

        private void OnSelectionChanged(SelectionEvent evt)
        {
            if (evt.Type != SelectionType.Building) return;

            var building = DataManager.Instance.Village.GetBuilding(evt.BuildingId);
            if (building == null) return;

            // TODO: 弹出建筑信息面板（用 UIManager）
            Debug.Log($"[BuildingInteraction] 选中建筑: {evt.BuildingId}, " +
                      $"type={building.TypeId}, level={building.Level.Value}, " +
                      $"upgrading={building.IsUpgrading}");
        }

        private void OnDeselected()
        {
            // TODO: 关闭建筑信息面板
        }

        // ==================== 手势处理 ====================

        private void OnGesture(GestureData gesture)
        {
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
            var building = _selection.SelectedBuilding;
            if (building == null) return;

            if (building.IsUpgrading)
            {
                Debug.LogWarning("[BuildingInteraction] 建筑正在升级中");
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
            var building = _selection.SelectedBuilding;
            if (building == null || !building.IsUpgrading) return;

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
            // TODO: 所有建筑进入可移动状态，显示网格
        }

        /// <summary>退出编辑模式</summary>
        public void ExitEditMode()
        {
            IsEditMode = false;
            Debug.Log("[BuildingInteraction] 退出编辑模式");
        }

        /// <summary>移动建筑到新位置（编辑模式下）</summary>
        public void MoveBuilding(string buildingId, Vector2Int newGridPos)
        {
            if (!IsEditMode) return;

            var building = DataManager.Instance.Village.GetBuilding(buildingId);
            if (building == null) return;

            // TODO: 检查新位置是否合法（需要知道建筑尺寸）
            building.GridX.Value = newGridPos.x;
            building.GridY.Value = newGridPos.y;
        }
    }
}
