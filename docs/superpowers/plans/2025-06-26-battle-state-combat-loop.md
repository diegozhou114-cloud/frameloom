# BattleState 战斗完整闭环 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 打通 BattleState 完整战斗流程：主城点攻击 → 选模式 → 部署单位 → 战斗推进 → 结算返回。

**Architecture:** BattleState.OnEnter 创建 CombatManager 并启动异步战斗流程（部署→战斗→结算），通过 EventBus 监听阶段变更驱动 UI 面板（DeployPanel/BattleHUD/ResultPanel）。CombatManager.Update() 自驱动 Tick，BattleState.OnUpdate 仅做事件轮询。

**Tech Stack:** Unity 2022.3, C#, UniTask, 现有 ECS/FSM/EventBus/UI 框架

---

### 架构决策

1. **BattleState 不驱动 CombatManager** — CombatManager 是 SingletonDontDestroy，自带 `Update()` 驱动 Orchestrator Tick。BattleState 只负责：进入时启动战斗、监听结束事件、管理战斗 UI 面板。
2. **部署阶段**：复用 Orchestrator 的计时器。BattleState 在 Deploying 阶段打开 DeployPanel，玩家点击网格放置单位。定时器到期自动进入 Battling 阶段。
3. **UI 构造**：延续现有模式（LoginPanel/MainVillageHUD），纯代码构建 UI，不使用 Prefab。
4. **模式参数**：通过 `ChangeState(GameState.Battle, BattleParams)` 传递战斗配置。

---

### 文件变更清单

| 操作 | 文件 | 职责 |
|------|------|------|
| Create | `Assets/Scripts/Combat/BattleParams.cs` | 战斗参数 DTO |
| Modify | `Assets/Scripts/FSM/Game/States/BattleState.cs` | 战斗入口+生命周期 |
| Create | `Assets/Scripts/UI/Panels/DeployPanel.cs` | 部署阶段：单位选择+网格放兵 |
| Create | `Assets/Scripts/UI/Panels/BattleHUD.cs` | 战斗中 HUD：计时/计数/波次 |
| Create | `Assets/Scripts/UI/Panels/ResultPanel.cs` | 结算面板：星级/战利品/返回 |
| Modify | `Assets/Scripts/FSM/Game/States/MainVillageState.cs` | 攻击按钮传递 BattleParams |
| Modify | `Assets/Scripts/Combat/Spawner/UnitSpawner.cs` | 新增 SpawnSingleUnit 方法 |
| Modify | `Assets/Scripts/Combat/CombatManager.cs` | 暴露公开属性 + SpawnDeployUnit + EndDeployPhase |

---

### Task 1: BattleParams — 战斗参数 DTO

**Files:**
- Create: `Assets/Scripts/Combat/BattleParams.cs`

- [ ] **Step 1: 创建 BattleParams 数据类**

```csharp
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 战斗参数 — 从 MainVillageState 传入 BattleState。
    /// 战斗模式类型 + 对手村庄数据。
    /// </summary>
    public class BattleParams
    {
        /// <summary>战斗模式 ID：village_attack / clan_war / builder_base / campaign</summary>
        public string ModeId;

        /// <summary>攻击方村庄数据</summary>
        public VillageData AttackerVillage;

        /// <summary>防守方村庄数据</summary>
        public VillageData DefenderVillage;

        public static BattleParams VillageAttack(VillageData attacker, VillageData defender)
        {
            return new BattleParams
            {
                ModeId = "village_attack",
                AttackerVillage = attacker,
                DefenderVillage = defender,
            };
        }
    }
}
```

---

### Task 2: UnitSpawner — 新增单兵部署方法

**Files:**
- Modify: `Assets/Scripts/Combat/Spawner/UnitSpawner.cs`

- [ ] **Step 1: 添加 SpawnUnit 方法**

在 `UnitSpawner` 类末尾（`}` 之前）添加：

```csharp
/// <summary>在指定网格位置部署一个攻击方单位</summary>
public Entity SpawnUnit(Vector2Int gridPos, UnitConfig config, Team team)
{
    var worldPos = _grid.GridToWorld(gridPos);
    var entity = _world.CreateEntity();
    _world.AddComponent(entity, TransformComponent.Create(worldPos, gridPos));
    _world.AddComponent(entity, HealthComponent.Create(config.HP));
    _world.AddComponent(entity, AttackComponent.Create(
        config.Damage, config.Range, config.AttackSpeed));
    _world.AddComponent(entity, MovementComponent.Create(config.MoveSpeed, 1f));
    _world.AddComponent(entity, TeamComponent.Create(team));
    _world.AddComponent(entity, TargetComponent.Create());
    Debug.Log($"[UnitSpawner] 部署 {config.Name} 在 {gridPos}");
    return entity;
}
```

---

### Task 3: BattleState — 核心战斗生命周期

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/BattleState.cs`

- [ ] **Step 1: 重写 BattleState**

完整替换 `Assets/Scripts/FSM/Game/States/BattleState.cs`：

```csharp
using UnityEngine;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 战斗状态 — 完整战斗闭环。
    /// OnEnter: 解析 BattleParams → 创建 CombatMode → 启动战斗 → 打开部署面板。
    /// OnUpdate: 轮询战斗阶段变更（CombatManager 自驱动 Tick）。
    /// OnExit: 清理面板 + 反注册事件。
    /// </summary>
    public class BattleState : FSMState<GameState>
    {
        private CombatMode _mode;
        private BattleParams _params;
        private bool _battleStarted;

        public BattleState() : base(GameState.Battle) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入战斗状态");

            _params = data as BattleParams;
            if (_params == null)
            {
                Debug.LogError("[BattleState] 未传入 BattleParams，返回主城");
                ChangeState(GameState.MainVillage);
                return;
            }

            // ① 解析战斗模式
            _mode = CreateMode(_params.ModeId);
            if (_mode == null)
            {
                Debug.LogError($"[BattleState] 未知模式: {_params.ModeId}");
                ChangeState(GameState.MainVillage);
                return;
            }

            // ② 监听战斗阶段 + 结束事件
            EventBus.Subscribe<CombatPhase>(CombatManager.EventPhaseChanged, OnPhaseChanged);
            EventBus.Subscribe<CombatEndEvent>(CombatManager.EventCombatEnded, OnCombatEnded);

            // ③ 启动战斗（异步，由 CombatManager.Update 驱动 Tick）
            _battleStarted = false;
            CombatManager.Instance.StartBattleAsync(_mode).Forget();
        }

        public override void OnUpdate()
        {
            // CombatManager.Update() 自驱动，此处仅做首次启动后的 UI 响应
            if (!_battleStarted && CombatManager.Instance.CurrentPhase == CombatPhase.Deploying)
            {
                _battleStarted = true;
                UIManager.Instance.Open<DeployPanel>();
            }
        }

        public override void OnExit()
        {
            EventBus.Unsubscribe<CombatPhase>(CombatManager.EventPhaseChanged, OnPhaseChanged);
            EventBus.Unsubscribe<CombatEndEvent>(CombatManager.EventCombatEnded, OnCombatEnded);

            UIManager.Instance.Close<DeployPanel>();
            UIManager.Instance.Close<BattleHUD>();
            UIManager.Instance.Close<ResultPanel>();

            base.OnExit();
            Debug.Log("[GameFSM] 退出战斗状态");
        }

        // ==================== 事件回调 ====================

        private void OnPhaseChanged(CombatPhase phase)
        {
            Debug.Log($"[BattleState] 阶段变更: {phase}");

            switch (phase)
            {
                case CombatPhase.Battling:
                    UIManager.Instance.Close<DeployPanel>();
                    UIManager.Instance.Open<BattleHUD>();
                    break;
                case CombatPhase.Ended:
                    UIManager.Instance.Close<BattleHUD>();
                    break;
            }
        }

        private void OnCombatEnded(CombatEndEvent result)
        {
            Debug.Log($"[BattleState] 战斗结束: winner={result.Winner}, stars={result.Stars}");
            UIManager.Instance.Open<ResultPanel>(result);
        }

        // ==================== 内部 ====================

        private CombatMode CreateMode(string modeId)
        {
            switch (modeId)
            {
                case "village_attack":
                    return new VillageAttackMode();
                case "builder_base":
                    return new BuilderBaseMode();
                case "clan_war":
                    return new ClanWarMode();
                case "campaign":
                    return new CampaignMode();
                default:
                    return null;
            }
        }
    }
}
```

---

### Task 4: DeployPanel — 部署单位选择 + 网格放置

**Files:**
- Create: `Assets/Scripts/UI/Panels/DeployPanel.cs`

- [ ] **Step 1: 创建 DeployPanel**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 部署面板 — 底部兵种选择栏 + 点击网格放兵。
    /// 在 CombatPhase.Deploying 期间显示。
    /// </summary>
    public class DeployPanel : UIBase
    {
        private List<UnitConfig> _availableUnits;
        private UnitConfig _selectedUnit;
        private Text _timerText;
        private Text _countText;
        private int _deployedCount;

        public DeployPanel()
        {
            _layer = UILayer.HUD;
            _pauseLowerPanels = false;
        }

        protected override void OnOpen(object data)
        {
            _deployedCount = 0;
            var allUnits = ConfigTableManager.Instance?.GetAllUnits();
            _availableUnits = allUnits != null
                ? new List<UnitConfig>(allUnits)
                : new List<UnitConfig>();
            if (_availableUnits.Count > 0)
                _selectedUnit = _availableUnits[0];

            BuildUI();
            EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        protected override void OnClose()
        {
            EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        // ==================== UI 构建 ====================

        private void BuildUI()
        {
            // 底部半透明栏 — 兵种选择
            var bottomBar = MakeBar("BottomBar", new Vector2(0, 0), new Vector2(1, 0.12f),
                new Color(0, 0, 0, 0.6f));

            float btnW = 1f / Mathf.Max(_availableUnits.Count, 1);
            for (int i = 0; i < _availableUnits.Count; i++)
            {
                int idx = i;
                var unit = _availableUnits[i];
                CreateButton($"UnitBtn_{unit.Id}", $"{unit.Name}\n💰{unit.TrainingCost}",
                    new Vector2(i * btnW, 0), new Vector2((i + 1) * btnW, 1),
                    bottomBar.transform, new Color(0.3f, 0.5f, 0.3f), () =>
                {
                    _selectedUnit = unit;
                    Debug.Log($"[DeployPanel] 选择兵种: {unit.Name}");
                });
            }

            // 顶部计时 + 计数
            var topBar = MakeBar("TopBar", new Vector2(0, 0.88f), new Vector2(1, 1),
                new Color(0, 0, 0, 0.45f));

            _timerText = CreateText("Timer", "30s", 20, new Vector2(0.05f, 0.5f),
                topBar.transform, TextAnchor.MiddleLeft, Color.white);
            _countText = CreateText("Count", "已部署: 0", 18, new Vector2(0.5f, 0.5f),
                topBar.transform, TextAnchor.MiddleCenter, Color.white);

            // 开始战斗按钮
            CreateButton("BtnStart", "⚔ 开始战斗", new Vector2(0.85f, 0.5f),
                new Vector2(0.96f, 1f), topBar.transform,
                new Color(0.85f, 0.25f, 0.2f), () =>
            {
                // 提前结束部署阶段
                Debug.Log("[DeployPanel] 手动开始战斗");
                CombatManager.Instance.EndDeployPhase();
            });
        }

        // ==================== 每帧更新 ====================

        private void Update()
        {
            if (!IsOpen) return;

            var cm = CombatManager.Instance;
            if (cm == null) return;

            if (cm.CurrentPhase == CombatPhase.Deploying)
            {
                float remain = Mathf.Max(0, cm.CurrentMode.DeployTimeLimit - Time.time);
                _timerText.text = $"{remain:F0}s";
            }
        }

        // ==================== 手势 → 部署 ====================

        private void OnGesture(GestureData gesture)
        {
            if (gesture.Type != GestureType.Tap) return;

            var cm = CombatManager.Instance;
            if (cm == null || cm.CurrentPhase != CombatPhase.Deploying) return;

            var mainCam = Camera.main;
            if (mainCam == null) return;

            // 屏幕坐标 → 世界坐标 → 网格坐标
            var worldPos = mainCam.ScreenToWorldPoint(
                new Vector3(gesture.ScreenPosition.x, gesture.ScreenPosition.y,
                    mainCam.transform.position.y));
            var gridPos = GridManager.Instance.WorldToGrid(worldPos);

            DeployAt(gridPos);
        }

        private void DeployAt(Vector2Int gridPos)
        {
            if (_selectedUnit == null) return;

            var cm = CombatManager.Instance;
            if (cm == null || cm.CurrentPhase != CombatPhase.Deploying) return;

            // 检查部署上限
            int max = cm.CurrentMode.MaxDeployCount;
            if (max > 0 && _deployedCount >= max)
            {
                Debug.Log("[DeployPanel] 已达到最大部署数量");
                return;
            }

            // 检查网格是否可部署（边界内 + 未被建筑占据）
            var grid = GridManager.Instance;
            if (grid == null || !grid.IsInBounds(gridPos))
            {
                Debug.Log($"[DeployPanel] 位置超出边界: {gridPos}");
                return;
            }
            if (grid.IsOccupied(gridPos))
            {
                Debug.Log($"[DeployPanel] 位置被占据: {gridPos}");
                return;
            }

            // 通过 CombatManager 公开 API 生成单位
            cm.SpawnDeployUnit(gridPos, _selectedUnit);
            _deployedCount++;
            _countText.text = $"已部署: {_deployedCount}";
        }

        // ==================== 快捷 UI ====================

        private GameObject MakeBar(string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.GetComponent<Image>().color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        private Text CreateText(string name, string text, int fontSize, Vector2 anchor,
            Transform parent, TextAnchor align, Color color)
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
            txt.rectTransform.sizeDelta = new Vector2(200, 28);
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
            lt.fontSize = 14;
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

### Task 5: BattleHUD — 战斗中 HUD

**Files:**
- Create: `Assets/Scripts/UI/Panels/BattleHUD.cs`

- [ ] **Step 1: 创建 BattleHUD**

```csharp
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 战斗中 HUD — 顶部显示剩余时间、波次、双方存活数。
    /// 在 CombatPhase.Battling 期间显示。
    /// </summary>
    public class BattleHUD : UIBase
    {
        private Text _timerText;
        private Text _waveText;
        private Text _aliveText;
        private float _battleStartTime;

        public BattleHUD()
        {
            _layer = UILayer.HUD;
            _pauseLowerPanels = false;
        }

        protected override void OnOpen(object data)
        {
            _battleStartTime = Time.time;
            BuildUI();
        }

        private void BuildUI()
        {
            var bar = new GameObject("HUDTopBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(transform, false);
            bar.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.88f);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _timerText = MakeText("Timer", "3:00", 22, new Vector2(0.05f, 0.5f),
                bar.transform, TextAnchor.MiddleLeft, Color.white);
            _waveText = MakeText("Wave", "波次: 0", 18, new Vector2(0.4f, 0.5f),
                bar.transform, TextAnchor.MiddleCenter, Color.yellow);
            _aliveText = MakeText("Alive", "⚔ 0 vs 0 🛡", 18, new Vector2(0.7f, 0.5f),
                bar.transform, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f));
        }

        private void Update()
        {
            if (!IsOpen) return;

            var cm = CombatManager.Instance;
            if (cm == null || cm.CurrentPhase != CombatPhase.Battling) return;

            var mode = cm.CurrentMode;
            float elapsed = Time.time - _battleStartTime;
            float remain = Mathf.Max(0, mode.BattleTimeout - elapsed);
            int min = (int)(remain / 60);
            int sec = (int)(remain % 60);
            _timerText.text = $"{min}:{sec:D2}";

            // 存活数从 DeathSystem 读取
            // 注意：DeathSystem 在 CombatManager 内部，需要暴露公开属性
            int attackerAlive = cm.AttackerAliveCount;
            int defenderAlive = cm.DefenderAliveCount;
            _aliveText.text = $"⚔ {attackerAlive} vs {defenderAlive} 🛡";

            _waveText.text = $"波次: {cm.CurrentWave}";
        }

        private Text MakeText(string name, string text, int fontSize, Vector2 anchor,
            Transform parent, TextAnchor align, Color color)
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
            txt.rectTransform.sizeDelta = new Vector2(200, 28);
            return txt;
        }
    }
}
```

---

### Task 6: ResultPanel — 结算面板

**Files:**
- Create: `Assets/Scripts/UI/Panels/ResultPanel.cs`

- [ ] **Step 1: 创建 ResultPanel**

```csharp
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 结算面板 — 显示星级、战利品、返回按钮。
    /// 在收到 CombatEnded 事件后打开。
    /// </summary>
    public class ResultPanel : UIBase
    {
        public ResultPanel()
        {
            _layer = UILayer.Popup;
            _pauseLowerPanels = true;
        }

        protected override void OnOpen(object data)
        {
            var result = data as CombatEndEvent?;
            if (result == null) return;

            BuildUI(result.Value);
        }

        private void BuildUI(CombatEndEvent result)
        {
            // 半透明背景
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.75f);

            // 结果卡片
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(transform, false);
            card.GetComponent<Image>().color = new Color(0.15f, 0.12f, 0.1f, 1f);
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.2f, 0.25f);
            crt.anchorMax = new Vector2(0.8f, 0.75f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            // 标题
            bool win = result.Winner == Team.Attacker;
            var titleText = MakeText("Title", win ? "🏆 胜利!" : "💀 失败",
                40, new Vector2(0.5f, 0.85f), card.transform,
                TextAnchor.MiddleCenter, win ? Color.yellow : Color.gray, 300, 60);

            // 星级
            string starsStr = new string('⭐', result.Stars) + new string('☆', 3 - result.Stars);
            MakeText("Stars", starsStr, 36, new Vector2(0.5f, 0.65f), card.transform,
                TextAnchor.MiddleCenter, Color.yellow, 300, 50);

            // 战利品
            string loot = $"💰 金币: {result.LootGold}   💧 圣水: {result.LootElixir}";
            MakeText("Loot", loot, 22, new Vector2(0.5f, 0.45f), card.transform,
                TextAnchor.MiddleCenter, Color.white, 350, 40);

            // 返回按钮
            CreateButton("BtnReturn", "↩ 返回主城", new Vector2(0.25f, 0.15f),
                new Vector2(0.75f, 0.30f), card.transform,
                new Color(0.35f, 0.6f, 0.3f), () =>
            {
                GameFSM.Instance.ChangeState(GameState.MainVillage);
            });
        }

        // ==================== 快捷 UI ====================

        private Text MakeText(string name, string text, int fontSize, Vector2 anchor,
            Transform parent, TextAnchor align, Color color, float w = 300, float h = 40)
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
            lt.fontSize = 26;
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

### Task 7: CombatManager — 暴露公开属性和部署 API

**Files:**
- Modify: `Assets/Scripts/Combat/CombatManager.cs`

- [ ] **Step 1: 添加公开属性和方法**

在 `CombatManager` 类的公开属性区域（`CurrentMode` 下方）添加：

```csharp
/// <summary>当前波次（供 BattleHUD 读取）</summary>
public int CurrentWave => _orchestrator?.CurrentWave ?? 0;

/// <summary>攻击方存活数（供 BattleHUD 读取）</summary>
public int AttackerAliveCount => DeathSys?.AttackerAlive ?? 0;

/// <summary>防守方存活数（供 BattleHUD 读取）</summary>
public int DefenderAliveCount => DeathSys?.DefenderAlive ?? 0;

/// <summary>在指定网格部署一个攻击方单位（供 DeployPanel 调用）</summary>
public void SpawnDeployUnit(Vector2Int gridPos, UnitConfig config)
{
    _spawner?.SpawnUnit(gridPos, config, Team.Attacker);
}

/// <summary>手动结束部署阶段（供 DeployPanel "开始战斗" 按钮调用）</summary>
public void EndDeployPhase()
{
    _orchestrator?.EndDeployPhase();
}
```

并在文件顶部添加 `using UnityEngine;`（如果未引用）。

---

### Task 8: CombatOrchestrator — 暴露 CurrentWave + 手动结束部署

**Files:**
- Modify: `Assets/Scripts/Combat/Core/CombatOrchestrator.cs`

- [ ] **Step 1: 添加公开属性和方法**

在 `CombatOrchestrator` 类中添加：

```csharp
/// <summary>当前波次</summary>
public int CurrentWave => _battleWave;

/// <summary>手动结束部署阶段</summary>
public void EndDeployPhase()
{
    if (CurrentPhase == CombatPhase.Deploying)
    {
        _deployTcs?.TrySetResult(true);
        Debug.Log("[CombatOrchestrator] 手动结束部署阶段");
    }
}
```

---

### Task 9: MainVillageState — 攻击按钮传递 BattleParams

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/MainVillageState.cs`

- [ ] **Step 1: 修改 OnAttackClicked**

替换 `OnAttackClicked` 方法：

```csharp
/// <summary>HUD 攻击按钮 → Battle</summary>
private void OnAttackClicked()
{
    Debug.Log("[MainVillageState] ⚔ 攻击按钮 → Battle");

    var village = DataManager.Instance.Village;
    if (village == null || village.Buildings.Count == 0)
    {
        Debug.LogWarning("[MainVillageState] 村庄无建筑，无法发起战斗");
        return;
    }

    // 创建攻击参数：攻击自己的村庄（PvE 模式）
    var bp = BattleParams.VillageAttack(village, village);
    ChangeState(GameState.Battle, bp);
}
```

---

### Task 10: DeployPanel — 监听手势事件实现网格点击部署

**Files:**
- Modify: `Assets/Scripts/UI/Panels/DeployPanel.cs` (追加内容)

- [ ] **Step 1: 在 DeployPanel 中监听 Gesture 事件**

不修改 InputManager，而是在 DeployPanel 的 `OnOpen` 中订阅 `"Gesture"` 事件，Tap 时转换屏幕坐标到网格坐标并部署。

在 `DeployPanel.OnOpen` 方法末尾、`OnClose` 方法反注册处添加：

```csharp
// OnOpen 中追加：
EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);

// OnClose 中追加：
EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
```

添加手势处理方法：

```csharp
private void OnGesture(GestureData gesture)
{
    if (gesture.Type != GestureType.Tap) return;

    var cm = CombatManager.Instance;
    if (cm == null || cm.CurrentPhase != CombatPhase.Deploying) return;

    // 屏幕坐标 → 世界坐标 → 网格坐标
    var mainCam = Camera.main;
    if (mainCam == null) return;

    var worldPos = mainCam.ScreenToWorldPoint(
        new Vector3(gesture.ScreenPosition.x, gesture.ScreenPosition.y,
            mainCam.transform.position.y));
    var gridPos = GridManager.Instance.WorldToGrid(worldPos);

    OnDeployAt(gridPos);
}
```

并在文件顶部添加 `using ZWebGLFrameWork.Gameplay;`。

---

### Task 11: 端到端验证

- [ ] **Step 1: 编译检查**

```bash
./ci_build.sh Validate
```

- [ ] **Step 2: 手动验证流程**

```
1. 启动游戏 → Launch → Login → MainVillage
2. 点击顶部"⚔ 攻击!"按钮
3. 进入 BattleState → DeployPanel 出现（底部兵种栏 + 顶部计时）
4. 点击兵种选择 → 点击网格放兵
5. 等待计时结束（或点"开始战斗"）→ BattleHUD 出现
6. 观察单位移动/攻击/死亡
7. 战斗结束 → ResultPanel 出现（星级+战利品）
8. 点击"返回主城" → MainVillageState
```

---

## 验证清单

- [ ] `BattleParams.VillageAttack()` 正确创建参数
- [ ] `BattleState.OnEnter` 解析参数，创建正确 CombatMode
- [ ] `UnitSpawner.SpawnUnit` 正确创建攻击方实体
- [ ] `DeployPanel` 显示兵种列表，点击选中，点击网格部署
- [ ] 部署计时归零或手动开始 → 进入 Battling 阶段
- [ ] `BattleHUD` 实时显示计时/波次/存活数
- [ ] 防守方全灭或攻击方全灭 → 触发 CombatEnded
- [ ] `ResultPanel` 显示正确星级和战利品
- [ ] 返回主城后状态正常（HUD 恢复，资源产出继续）
