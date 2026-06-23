# Phase 5: 登录流程 — 离线优先实现计划

> **核心原则:** 游戏始终可离线运行。本地数据完整自洽。联网为可选增强。

**离线模式数据流:**
```
AppConfig.EnableOfflineMode
    ↓
LaunchState (C.4) → 跳过 LoginState → 直接 MainVillage
    OR
LoginState.OnEnter → 检测离线标志 → 显示 CharacterSelectPanel
    ↓
CharacterSelectPanel → 选择已有存档 OR 创建新存档
    ↓
GameInitializer → 创建默认 PlayerData + VillageData → SaveManager.Save()
    ↓
MainVillageState → DataManager 加载存档数据
```

**联网模式数据流:**
```
LoginState.OnEnter → 显示 LoginPanel (游客/微信)
    ↓
LoginPanel → 点击登录 → NetworkManager.Connect()
    ↓
EventBus: NetworkStateChanged → Connected → CharacterSelectPanel (服务器角色列表)
    ↓
MainVillageState
```

---

## 任务列表

### 5.0-AppConfig: 添加离线模式开关 (新增)

**文件:** `Assets/Scripts/Data/AppConfig.cs`

在 `[Header("功能开关")]` 区域添加:

```csharp
[Header("离线模式")]
[Tooltip("开启后跳过登录和网络连接，直接使用本地存档")]
[SerializeField] private bool _enableOfflineMode = true;
public bool EnableOfflineMode => _enableOfflineMode;
```

---

### 5.0-GameInitializer: 首次启动默认数据创建 (新建)

**文件:** `Assets/Scripts/Data/GameInitializer.cs`

```csharp
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 游戏初始化器 — 首次启动时创建默认玩家数据和村庄。
    /// 离线模式下完全自给自足，不依赖服务器。
    /// </summary>
    public static class GameInitializer
    {
        private const string FirstLaunchKey = "Game_FirstLaunchDone";

        /// <summary>是否已完成首次初始化</summary>
        public static bool IsFirstLaunchDone =>
            PlayerPrefs.GetInt(FirstLaunchKey, 0) == 1;

        /// <summary>
        /// 确保存档存在。如果当前槽位无存档，创建默认数据并保存。
        /// </summary>
        public static void EnsureSaveExists()
        {
            int slot = SaveManager.CurrentSlot;

            if (SaveManager.Exists(slot))
            {
                Debug.Log($"[GameInitializer] 存档已存在 slot={slot}，跳过初始化");
                return;
            }

            Debug.Log($"[GameInitializer] 首次启动 slot={slot}，创建默认数据...");
            CreateDefaultSave(slot);
        }

        /// <summary>创建默认新手存档（Town Hall Lv1 + 基础建筑）</summary>
        public static void CreateDefaultSave(int slot)
        {
            var saveData = new GameSaveData
            {
                PlayerName = "Villager",
                PlayerLevel = 1,
                TownHallLevel = 1,
                Gold = 500,
                Elixir = 300,
                DarkElixir = 0,
                Gems = 50,
                Trophies = 0,
                // 初始村庄：Town Hall + 1 Gold Mine + 1 Elixir Collector + 1 Cannon
                Buildings = new System.Collections.Generic.List<BuildingSaveData>
                {
                    new BuildingSaveData { TypeId = 1, GridX = 22, GridY = 20, Level = 1 },
                    new BuildingSaveData { TypeId = 2, GridX = 18, GridY = 16, Level = 1 },
                    new BuildingSaveData { TypeId = 3, GridX = 26, GridY = 16, Level = 1 },
                    new BuildingSaveData { TypeId = 5, GridX = 15, GridY = 24, Level = 1 },
                },
            };

            SaveManager.SaveGame(saveData, slot);
            PlayerPrefs.SetInt(FirstLaunchKey, 1);
            PlayerPrefs.Save();

            Debug.Log($"[GameInitializer] ✅ 默认存档已创建 slot={slot}: " +
                      $"{saveData.PlayerName} Lv.{saveData.PlayerLevel}");
        }

        /// <summary>重置首次启动标记（调试用）</summary>
        public static void ResetFirstLaunchFlag()
        {
            PlayerPrefs.DeleteKey(FirstLaunchKey);
            PlayerPrefs.Save();
        }
    }
}
```

> `GameSaveData` 和 `BuildingSaveData` 已在 `SaveManager.cs` 中定义，需确认结构。

---

### 5.4: CharacterSelectPanel — 存档选择面板 (新建)

**文件:** `Assets/Scripts/UI/Panels/CharacterSelectPanel.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.UI
{
    public class CharacterSelectPanel : UIBase
    {
        [Header("UI 组件")]
        private Text _titleText;
        private Button[] _slotButtons = new Button[5];
        private Text[] _slotTexts = new Text[5];

        protected override void OnOpen(object data)
        {
            BuildUI();
            RefreshSlots();
        }

        private void BuildUI()
        {
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.12f, 0.2f, 1f);

            _titleText = CreateText("Title", "选择存档", 32, new Vector2(0.5f, 0.85f));
            _titleText.alignment = TextAnchor.MiddleCenter;

            for (int i = 0; i < 5; i++)
            {
                float y = 0.70f - i * 0.12f;
                var go = new GameObject($"Slot_{i}", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(transform, false);
                go.GetComponent<Image>().color = new Color(0.2f, 0.25f, 0.35f);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.1f, y);
                rt.anchorMax = new Vector2(0.9f, y + 0.10f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                int slotIndex = i;
                go.GetComponent<Button>().onClick.AddListener(() => OnSlotClick(slotIndex));
                _slotButtons[i] = go.GetComponent<Button>();

                var txtGo = CreateText($"SlotText_{i}", "", 18, new Vector2(0.5f, 0.5f));
                txtGo.transform.SetParent(go.transform, false);
                txtGo.rectTransform.anchorMin = Vector2.zero;
                txtGo.rectTransform.anchorMax = Vector2.one;
                _slotTexts[i] = txtGo;
            }
        }

        private void RefreshSlots()
        {
            for (int i = 0; i < 5; i++)
            {
                if (SaveManager.Exists(i))
                {
                    var data = SaveManager.LoadGame<GameSaveData>(i);
                    _slotTexts[i].text = $"[存档 {i + 1}] {data.PlayerName} Lv.{data.PlayerLevel} TH{data.TownHallLevel}";
                    _slotTexts[i].color = Color.white;
                }
                else
                {
                    _slotTexts[i].text = $"[空槽位 {i + 1}] 点击创建新角色";
                    _slotTexts[i].color = Color.gray;
                }
            }
        }

        private void OnSlotClick(int slot)
        {
            SaveManager.CurrentSlot = slot;

            if (!SaveManager.Exists(slot))
            {
                // 空槽位 → 创建默认存档
                GameInitializer.CreateDefaultSave(slot);
            }

            // 加载存档数据到 DataManager
            LoadSaveToDataManager(slot);

            // 进入主城
            GameFSM.Instance.ChangeState(GameState.MainVillage);
        }

        private void LoadSaveToDataManager(int slot)
        {
            var data = SaveManager.LoadGame<GameSaveData>(slot);
            if (data == null) return;

            var player = DataManager.Instance.Player;
            player.Name.Value = data.PlayerName;
            player.Level.Value = data.PlayerLevel;
            player.TownHallLevel.Value = data.TownHallLevel;
            player.Gold.Value = data.Gold;
            player.Elixir.Value = data.Elixir;
            player.DarkElixir.Value = data.DarkElixir;
            player.Gems.Value = data.Gems;
            player.Trophies.Value = data.Trophies;

            // 加载建筑
            var village = DataManager.Instance.Village;
            village.Buildings.Clear();
            if (data.Buildings != null)
            {
                foreach (var b in data.Buildings)
                {
                    village.AddBuilding(BuildingData.Create(b.TypeId, b.GridX, b.GridY));
                }
            }
        }

        private Text CreateText(string name, string text, int fontSize, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            return txt;
        }
    }
}
```

---

### 5.1: LoginState — 离线优先登录流程 (修改)

**文件:** `Assets/Scripts/FSM/Game/States/LoginState.cs`

**当前代码** (硬编码跳转):
```csharp
public override void OnEnter(object data = null)
{
    base.OnEnter(data);
    Debug.Log("[GameFSM] 进入登录状态");
    EventBus.Publish("LoginStarted");
    ChangeState(GameState.MainVillage);
}
```

**改为** (离线优先 + 联网可选):
```csharp
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.FSM
{
    public class LoginState : FSMState<GameState>
    {
        /// <summary>登录模式</summary>
        public enum LoginMode { Offline, Online }

        public LoginState() : base(GameState.Login) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入登录状态");
            EventBus.Publish("LoginStarted");

            var cfg = GameLauncher.Config;
            if (cfg != null && cfg.EnableOfflineMode)
            {
                // 离线模式：确保本地存档存在，直接进角色选择
                Debug.Log("[LoginState] 🔌 离线模式");
                GameInitializer.EnsureSaveExists();

                // 如果当前槽位已有存档，直接进主城
                if (SaveManager.Exists(SaveManager.CurrentSlot))
                {
                    LoadSaveToDataManager(SaveManager.CurrentSlot);
                    ChangeState(GameState.MainVillage);
                }
                else
                {
                    // 打开角色选择/创建面板
                    UIManager.Instance.Open<CharacterSelectPanel>();
                }
            }
            else
            {
                // 联网模式：显示登录面板
                Debug.Log("[LoginState] 🌐 联网模式");
                UIManager.Instance.Open<LoginPanel>();
            }
        }

        public override void OnExit()
        {
            UIManager.Instance.Close<LoginPanel>();
            UIManager.Instance.Close<CharacterSelectPanel>();
            base.OnExit();
        }

        // ==================== 联网模式回调 ====================

        public void OnGuestLogin()
        {
            UIManager.Instance.Close<LoginPanel>();
            ConnectToServer();
        }

        private void ConnectToServer()
        {
            var cfg = GameLauncher.Config;
            string host = cfg?.DefaultServerHost ?? "127.0.0.1";
            int port = cfg?.DefaultServerPort ?? 7777;

            EventBus.Subscribe<NetworkStateChangedEvent>(
                NetworkManager.EventNetworkStateChanged, OnNetworkStateChanged);

            NetworkManager.Instance.Connect(NetworkProtocol.Tcp, host, port);
        }

        private void OnNetworkStateChanged(NetworkStateChangedEvent evt)
        {
            if (evt.NewState == ConnectionState.Connected)
            {
                EventBus.Unsubscribe<NetworkStateChangedEvent>(
                    NetworkManager.EventNetworkStateChanged, OnNetworkStateChanged);
                Debug.Log("[LoginState] ✅ 服务器连接成功 → MainVillage");
                ChangeState(GameState.MainVillage);
            }
            else if (evt.NewState == ConnectionState.Disconnected && !string.IsNullOrEmpty(evt.Message))
            {
                EventBus.Unsubscribe<NetworkStateChangedEvent>(
                    NetworkManager.EventNetworkStateChanged, OnNetworkStateChanged);
                Debug.LogError($"[LoginState] ❌ 连接失败: {evt.Message}");
                UIManager.Instance.Open<LoginPanel>($"连接失败: {evt.Message}");
            }
        }

        // ==================== 辅助 ====================

        private void LoadSaveToDataManager(int slot)
        {
            var data = SaveManager.LoadGame<GameSaveData>(slot);
            if (data == null) return;

            var player = DataManager.Instance.Player;
            player.Name.Value = data.PlayerName;
            player.Level.Value = data.PlayerLevel;
            player.TownHallLevel.Value = data.TownHallLevel;
            player.Gold.Value = data.Gold;
            player.Elixir.Value = data.Elixir;
            player.DarkElixir.Value = data.DarkElixir;
            player.Gems.Value = data.Gems;
            player.Trophies.Value = data.Trophies;

            var village = DataManager.Instance.Village;
            village.Buildings.Clear();
            if (data.Buildings != null)
            {
                foreach (var b in data.Buildings)
                    village.AddBuilding(BuildingData.Create(b.TypeId, b.GridX, b.GridY));
            }
        }
    }
}
```

> 需要添加 `using ZWebGLFrameWork.Network;` 和 `using ZWebGLFrameWork.UI;`

---

### 5.3: LoginPanel — 登录 UI (新建)

**文件:** `Assets/Scripts/UI/Panels/LoginPanel.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.UI
{
    public class LoginPanel : UIBase
    {
        private Text _statusText;

        protected override void OnOpen(object data)
        {
            BuildUI();
            if (data is string error)
                ShowError(error);
        }

        private void BuildUI()
        {
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.15f, 0.25f, 1f);

            CreateText("Title", "部落冲突", 36, new Vector2(0.5f, 0.75f), TextAnchor.MiddleCenter, Color.white);

            CreateButton("BtnGuest", "游客登录", new Vector2(0.5f, 0.55f), new Color(0.3f, 0.5f, 0.3f), () => {
                var ls = GameFSM.Instance.GetStateInstance(GameState.Login) as LoginState;
                ls?.OnGuestLogin();
            });

            CreateButton("BtnOffline", "离线模式", new Vector2(0.5f, 0.43f), new Color(0.4f, 0.35f, 0.25f), () => {
                UIManager.Instance.Close<LoginPanel>();
                GameInitializer.EnsureSaveExists();
                var ls = GameFSM.Instance.GetStateInstance(GameState.Login) as LoginState;
                if (SaveManager.Exists(SaveManager.CurrentSlot))
                {
                    GameFSM.Instance.ChangeState(GameState.MainVillage);
                }
                else
                {
                    UIManager.Instance.Open<CharacterSelectPanel>();
                }
            });

            var statusGo = CreateText("Status", "", 16, new Vector2(0.5f, 0.25f), TextAnchor.MiddleCenter, Color.gray);
            _statusText = statusGo;
        }

        private void ShowError(string msg)
        {
            if (_statusText) _statusText.text = msg;
            _statusText.color = Color.red;
        }

        // ==================== 快捷 UI ====================

        private Text CreateText(string name, string text, int fontSize, Vector2 anchor, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = new Vector2(300, 50);
            return txt;
        }

        private void CreateButton(string name, string label, Vector2 anchor, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            go.GetComponent<Image>().color = color;
            go.GetComponent<RectTransform>().anchorMin = go.GetComponent<RectTransform>().anchorMax = anchor;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 50);
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lt = labelGo.AddComponent<Text>();
            lt.text = label;
            lt.fontSize = 20;
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

### 5.2 & 5.5: NetworkManager 事件 + 登录错误处理

NetworkManager 已有 `Connect(NetworkProtocol, string, int)` 并通过 `EventBus.Publish(EventNetworkStateChanged)` 广播状态。LoginState 通过 `EventBus.Subscribe<NetworkStateChangedEvent>` 监听。

5.5 登录失败处理已在 LoginState 中实现（连接失败→回LoginPanel显示错误）。

---

### C.4: LaunchState 离线模式集成

**文件:** `Assets/Scripts/FSM/Game/States/LaunchState.cs`

在 Phase 3 之后（第117行之前），添加离线检测：
```csharp
// ─── 离线模式检测 (C.4) ───
if (cfg.EnableOfflineMode && Application.internetReachability == NetworkReachability.NotReachable)
{
    Debug.Log("[LaunchState] 🔌 无网络 + 离线模式 → 跳过登录");
    UIManager.Instance.Close<LoadingPanel>();
    ChangeState(GameState.MainVillage);
    return;
}
```

> 插入在 Phase 3 区块之后、现有 `ChangeStateWithLoading` 之前。

---

## 执行顺序

| 步骤 | 任务 | 类型 |
|:---:|------|:---:|
| 1 | 5.0-AppConfig: 添加 EnableOfflineMode | 修改 |
| 2 | 5.0-GameInitializer: 默认数据创建 | 新建 |
| 3 | 5.3: LoginPanel UI | 新建 |
| 4 | 5.4: CharacterSelectPanel | 新建 |
| 5 | 5.1: LoginState 重写 | 修改 |
| 6 | C.4: LaunchState 离线检测 | 修改 |
| 7 | 编译验证 | — |
