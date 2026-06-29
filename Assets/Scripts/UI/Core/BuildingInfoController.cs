using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 建筑信息控制器 — 监听选择事件，管理 BuildingInfoPanel 生命周期。
    /// 在 UI 层避免 Gameplay↔UI 循环依赖。
    /// </summary>
    public class BuildingInfoController : MonoBehaviour
    {
        private void Start()
        {
            EventBus.Subscribe<SelectionEvent>(SelectionManager.EventSelectionChanged, OnSelectionChanged);
            EventBus.Subscribe(SelectionManager.EventSelectionCleared, OnSelectionCleared);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<SelectionEvent>(SelectionManager.EventSelectionChanged, OnSelectionChanged);
            EventBus.Unsubscribe(SelectionManager.EventSelectionCleared, OnSelectionCleared);
        }

        private void OnSelectionChanged(SelectionEvent evt)
        {
            if (evt.Type != SelectionType.Building) return;

            var building = DataManager.Instance.Village.GetBuilding(evt.BuildingId);
            if (building == null) return;

            UIManager.Instance.Open<BuildingInfoPanel>(building);
        }

        private void OnSelectionCleared()
        {
            UIManager.Instance.Close<BuildingInfoPanel>();
        }
    }
}
