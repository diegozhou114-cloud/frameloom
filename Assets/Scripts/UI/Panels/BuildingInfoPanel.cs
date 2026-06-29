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
        private Text _hpText;
        private Text _statusText;
        private Text _upgradeCostText;

        public BuildingInfoPanel()
        {
            _layer = UILayer.Popup;
            _pauseLowerPanels = false;
        }

        protected override void OnOpen(object data)
        {
            var building = data as BuildingData;
            if (building == null) return;

            BuildUI(building);
        }

        private void BuildUI(BuildingData building)
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

            var config = ConfigTableManager.Instance.GetBuilding(building.TypeId);
            string bldName = config != null ? config.Name : $"建筑#{building.TypeId}";
            int hp = config != null ? config.GetHP(building.Level.Value) : 0;

            // 名称
            _nameText = MakeText("Name", bldName, 28, new Vector2(0.5f, 0.85f),
                card.transform, TextAnchor.MiddleCenter, Color.white, 300, 40);

            // 等级 + HP
            _levelText = MakeText("Level", $"Lv.{building.Level.Value}  ❤ {hp}",
                20, new Vector2(0.5f, 0.68f), card.transform,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f), 250, 30);

            // 状态
            string status = building.IsUpgrading ? "🔨 升级中..." : "✅ 闲置";
            _statusText = MakeText("Status", status, 18, new Vector2(0.5f, 0.52f),
                card.transform, TextAnchor.MiddleCenter,
                building.IsUpgrading ? Color.yellow : Color.green, 200, 26);

            // 升级消耗信息
            int nextLevel = building.Level.Value + 1;
            string costText = "";
            if (config != null && nextLevel <= config.MaxLevel)
            {
                int cost = config.GetBuildCost(nextLevel);
                int time = config.GetBuildTime(nextLevel);
                costText = $"升级: 💰{cost}  ⏱{time}s";
            }
            else if (config != null)
            {
                costText = "已满级";
            }
            _upgradeCostText = MakeText("Cost", costText, 16, new Vector2(0.5f, 0.38f),
                card.transform, TextAnchor.MiddleCenter, Color.white, 250, 24);

            // 升级按钮
            if (!building.IsUpgrading && config != null && nextLevel <= config.MaxLevel)
            {
                CreateButton("BtnUpgrade", "⬆ 升级", new Vector2(0.3f, 0.12f),
                    new Vector2(0.7f, 0.28f), card.transform,
                    new Color(0.3f, 0.55f, 0.3f), () =>
                {
                    var interaction = Object.FindObjectOfType<BuildingInteraction>();
                    if (interaction != null)
                    {
                        interaction.UpgradeSelected();
                        // 刷新面板：关闭再重新打开
                        var bld = SelectionManager.Instance?.SelectedBuilding;
                        Close();
                        if (bld != null)
                            UIManager.Instance.Open<BuildingInfoPanel>(bld);
                    }
                });
            }

            // 关闭按钮（右上角）
            CreateButton("BtnClose", "✕", new Vector2(0.88f, 0.88f),
                new Vector2(0.97f, 0.97f), card.transform,
                new Color(0.5f, 0.2f, 0.2f), () => Close());
        }

        private Text MakeText(string name, string text, int fontSize, Vector2 anchor,
            Transform parent, TextAnchor align, Color color, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = new Vector2(w, h);
            return txt;
        }

        private void CreateButton(string name, string label, Vector2 anchorMin, Vector2 anchorMax,
            Transform parent, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lt = labelGo.AddComponent<Text>();
            lt.text = label;
            lt.fontSize = 18;
            lt.color = Color.white;
            lt.alignment = TextAnchor.MiddleCenter;
            lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lt.rectTransform.anchorMin = Vector2.zero;
            lt.rectTransform.anchorMax = Vector2.one;
        }
    }
}
