using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 建筑信息面板 — 显示选中建筑的名称/等级/HP/状态 + 升级按钮。
    /// 弹出在屏幕中下方，Popup 层。
    /// </summary>
    public class BuildingInfoPanel : UIBase
    {
        private Text _nameText;
        private Text _levelText;
        private Text _statusText;
        private Text _upgradeCostText;
        private BuildingData _building;
        private Button _upgradeButton;

        private BuildingInteraction _interaction;

        public BuildingInfoPanel()
        {
            _layer = UILayer.Popup;
            _pauseLowerPanels = false;
        }

        protected override void Awake()
        {
            base.Awake();
            _interaction = Object.FindObjectOfType<BuildingInteraction>();
        }

        protected override void OnOpen(object data)
        {
            BuildOnce(BuildUI);
            BindBuilding(data as BuildingData);
        }

        protected override void OnRebind(object data) => BindBuilding(data as BuildingData);

        private void BuildUI()
        {
            // 半透明背景卡片
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(transform, false);
            card.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.07f, 0.92f);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.15f, 0.35f);
            rt.anchorMax = new Vector2(0.85f, 0.65f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 名称
            _nameText = CreateText("Name", string.Empty, 28, new Vector2(0.5f, 0.85f),
                card.transform, TextAnchor.MiddleCenter, Color.white, 300, 40);

            // 等级 + HP
            _levelText = CreateText("Level", string.Empty,
                20, new Vector2(0.5f, 0.68f), card.transform,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f), 250, 30);

            // 状态
            _statusText = CreateText("Status", string.Empty, 18, new Vector2(0.5f, 0.52f),
                card.transform, TextAnchor.MiddleCenter,
                Color.white, 200, 26);

            // 升级消耗信息
            _upgradeCostText = CreateText("Cost", string.Empty, 16, new Vector2(0.5f, 0.38f),
                card.transform, TextAnchor.MiddleCenter, Color.white, 250, 24);

            // 升级按钮
            _upgradeButton = CreateButton("BtnUpgrade", "⬆ 升级", new Vector2(0.3f, 0.12f),
                new Vector2(0.7f, 0.28f), card.transform,
                new Color(0.3f, 0.55f, 0.3f), () =>
                {
                    if (_interaction == null) return;
                    _interaction.UpgradeSelected();
                    var selected = SelectionManager.Instance?.SelectedBuilding;
                    if (selected != null)
                        BindBuilding(selected);
                });

            // 关闭按钮（右上角）
            CreateButton("BtnClose", "✕", new Vector2(0.88f, 0.88f),
                new Vector2(0.97f, 0.97f), card.transform,
                new Color(0.5f, 0.2f, 0.2f), Close);
        }

        private void BindBuilding(BuildingData building)
        {
            _building = building;
            if (_building == null)
            {
                _nameText.text = string.Empty;
                _levelText.text = string.Empty;
                _statusText.text = string.Empty;
                _upgradeCostText.text = string.Empty;
                _upgradeButton.gameObject.SetActive(false);
                return;
            }

            var config = ConfigTableManager.Instance?.GetBuilding(_building.TypeId);
            int level = _building.Level.Value;
            int nextLevel = level + 1;
            _nameText.text = config != null ? config.Name : $"建筑#{_building.TypeId}";
            _levelText.text = $"Lv.{level}  ❤ {config?.GetHP(level) ?? 0}";
            _statusText.text = _building.IsUpgrading ? "🔨 升级中..." : "✅ 闲置";
            _statusText.color = _building.IsUpgrading ? Color.yellow : Color.green;

            bool canUpgrade = !_building.IsUpgrading && config != null &&
                nextLevel <= config.MaxLevel;
            _upgradeCostText.text = canUpgrade
                ? $"升级: 💰{config.GetBuildCost(nextLevel)}  ⏱{config.GetBuildTime(nextLevel)}s"
                : config == null ? string.Empty : "已满级";
            _upgradeButton.gameObject.SetActive(canUpgrade);
        }

        /// <summary>刷新面板数据（升级后调用，无闪烁）</summary>
        public void Refresh(BuildingData building) => BindBuilding(building);

    }
}
