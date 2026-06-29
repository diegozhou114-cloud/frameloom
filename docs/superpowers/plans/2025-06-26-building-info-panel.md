# P2-1: 建筑信息面板 实施计划

> **Goal:** 点击建筑弹出信息面板，显示名称/等级/生命值/状态，支持升级按钮。
> **架构决策:** 不直接让 BuildingInteraction 调用 UIManager（会造成 Gameplay↔UI 循环依赖）。改为在 UI 层创建 BuildingInfoController 监听 SelectionEvent 来打开/关闭面板。

---

### 文件变更

| 操作 | 文件 | 职责 |
|------|------|------|
| Create | `Assets/Scripts/UI/Panels/BuildingInfoPanel.cs` | 建筑信息面板 UI |
| Create | `Assets/Scripts/UI/Core/BuildingInfoController.cs` | 监听 SelectionEvent，管理面板开闭 |
| Modify | `Assets/Scripts/Gameplay/Building/BuildingInteraction.cs` | 删除 TODO 注释（不再直接管理面板） |

---

### Task 1: BuildingInfoPanel

**Create:** `Assets/Scripts/UI/Panels/BuildingInfoPanel.cs`

```csharp
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
        private Button _upgradeBtn;

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

        protected override void OnClose()
        {
            EventBus.Unsubscribe("BuildingUpgrade_Click", OnUpgradeClick);
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
                    EventBus.Publish("BuildingUpgrade_Click");
                });
                EventBus.Subscribe("BuildingUpgrade_Click", OnUpgradeClick);
            }

            // 关闭按钮（右上角）
            CreateButton("BtnClose", "✕", new Vector2(0.88f, 0.88f),
                new Vector2(0.97f, 0.97f), card.transform,
                new Color(0.5f, 0.2f, 0.2f), () =>
            {
                Close();
            });
        }

        private void OnUpgradeClick()
        {
            var interaction = Object.FindObjectOfType<BuildingInteraction>();
            if (interaction != null)
            {
                interaction.UpgradeSelected();
                // 刷新面板
                var building = SelectionManager.Instance?.SelectedBuilding;
                if (building != null)
                {
                    Close();
                    UIManager.Instance.Open<BuildingInfoPanel>(building);
                }
            }
        }

        // ==================== 快捷 UI ====================

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
```

---

### Task 2: BuildingInfoController — 事件驱动的面板管理

**Create:** `Assets/Scripts/UI/Core/BuildingInfoController.cs`

这个组件订阅 SelectionEvent/BuildingUpgrade_Click 事件，管理 BuildingInfoPanel 的打开/关闭/升级刷新。放在 UI 层，避免 Gameplay→UI 循环依赖。

```csharp
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 建筑信息控制器 — 监听选择事件，管理 BuildingInfoPanel 生命周期。
    /// 挂载到 UIManager 上，避免 Gameplay↔UI 循环依赖。
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
```

---

### Task 3: BuildingInteraction — 清理 TODO

**Modify:** `Assets/Scripts/Gameplay/Building/BuildingInteraction.cs`

删除 `OnSelectionChanged` 中的 TODO 注释（第 57 行），删除 `OnDeselected` 中的 TODO 注释（第 65 行）。方法本身保留（BuildingInteraction 仍需要升级计时器和编辑模式逻辑）。

---

### Task 4: UIManager — 挂载 BuildingInfoController

**Modify:** `Assets/Scripts/UI/Core/UIManager.cs`

在 `Awake()` 的 `Initialize()` 调用之后，添加：

```csharp
// 挂载建筑信息控制器
if (GetComponent<BuildingInfoController>() == null)
    gameObject.AddComponent<BuildingInfoController>();
```

---

### Task 5: 编译验证
