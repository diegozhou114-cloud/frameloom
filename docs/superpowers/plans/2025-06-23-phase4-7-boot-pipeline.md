# Phase 4-7 + 跨阶段任务 实现计划

> **For agentic workers:** Execute phase-by-phase, verify compilation after each phase.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成启动管线 Phase 4-7（场景过渡→登录→主城→战斗）+ 4 个跨阶段任务，共 23 个任务。

**Architecture:** 所有变更沿现有模块边界：FSM 状态类 → 场景/网络/战斗模块。UI 面板继承 `UIBase`。编译验证每 Phase 后执行。

**Tech Stack:** Unity 2022.3, C# 9.0, UniTask, ECS, FSM, EventBus

**依赖关系：**
```
Phase 4 (场景过渡) → Phase 5 (登录) → Phase 6 (主城) → Phase 7 (战斗)
                                     ↘ 跨阶段任务 (可并行插入)
```

**注意：** 4.3/4.4 需要 Unity Editor 创建 .unity 场景文件，本计划仅提供场景搭建说明，不做自动创建。

---

## Phase 4: 场景加载过渡

### Task 4.1: Launch → LoadingState → MainVillage 过渡

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/LaunchState.cs:123-125`

**当前代码** (Line 123-125):
```csharp
            UIManager.Instance.Close<LoadingPanel>();
            Debug.Log("[LaunchState] ✅ 初始化完成 → Login");
            ChangeState(GameState.Login);
```

**改为：**
```csharp
            UIManager.Instance.Close<LoadingPanel>();
            Debug.Log("[LaunchState] ✅ 初始化完成 → MainVillage (via Loading)");
            ChangeStateWithLoading(GameState.MainVillage);
```

> `ChangeStateWithLoading` 已在 `GameFSM.cs:126` 定义，自动插入 `LoadingState` 过渡。

---

### Task 4.2: LoadingState 对接 SceneLoader

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/LoadingState.cs` (替换 `LoadSequence`)

**LoadingPayload 需要新增 `TargetScene` 字段：**
检查 `Assets/Scripts/FSM/Game/GameFSM.cs` 中的 `LoadingPayload` 类，添加场景名字段。

**当前 LoadingState.LoadSequence** (模拟等待):
```csharp
private IEnumerator LoadSequence(LoadingPayload payload)
{
    yield return new WaitForSeconds(0.15f);
    Progress = 0.5f;
    yield return new WaitForSeconds(0.15f);
    Progress = 1.0f;
    EventBus.Publish("LoadingComplete");
    ChangeState(payload.TargetState, payload.TargetData);
}
```

**改为 (对接 SceneLoader):**
```csharp
private IEnumerator LoadSequence(LoadingPayload payload)
{
    // 如果有指定场景，异步加载
    if (!string.IsNullOrEmpty(payload.TargetScene))
    {
        var loadTask = SceneLoader.Instance.LoadSceneAsync(payload.TargetScene);
        while (!loadTask.Status.IsCompleted())
        {
            Progress = SceneLoader.Instance.Progress;
            yield return null;
        }
        Progress = 1.0f;
    }
    else
    {
        // 无场景加载需求，快速过渡
        yield return new WaitForSeconds(0.1f);
        Progress = 1.0f;
    }

    EventBus.Publish("LoadingComplete");
    ChangeState(payload.TargetState, payload.TargetData);
}
```

**LoadingPayload 修改** — 在 `GameFSM.cs` 中的 `LoadingPayload` 类添加：
```csharp
/// <summary>需要加载的场景名（null/empty = 仅过渡）</summary>
public string TargetScene;
```

---

### Task 4.3: MainVillage 场景搭建 ⚠️ Unity Editor

> **需要 Unity Editor 手动操作，不在此计划中自动创建。**

创建 `Assets/Scence/MainVillage.unity`，包含：
- Terrain/Ground (Plane)
- Main Camera (挂载 CameraController)
- Directional Light
- EventSystem
- Canvas (UIManager)

---

### Task 4.4: Battle 场景搭建 ⚠️ Unity Editor

> **需要 Unity Editor 手动操作，不在此计划中自动创建。**

创建 `Assets/Scence/Battle.unity`，包含：
- Ground
- Battle Camera
- Directional Light
- GridRenderer 可视化

---

## Phase 5: 登录流程

### Task 5.1: LoginState 真实流程

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/LoginState.cs`

**当前代码** (硬编码跳转):
```csharp
public override void OnEnter(object data = null)
{
    base.OnEnter(data);
    Debug.Log("[GameFSM] 进入登录状态");
    EventBus.Publish("LoginStarted");
    // 模拟登录成功，直接跳转主城
    ChangeState(GameState.MainVillage);
}
```

**改为** (SDK登录→网络连接→认证→角色数据):
```csharp
public override void OnEnter(object data = null)
{
    base.OnEnter(data);
    Debug.Log("[GameFSM] 进入登录状态");
    EventBus.Publish("LoginStarted");

    // 打开登录 UI
    UIManager.Instance.Open<LoginPanel>();
}

// 由 LoginPanel 触发的回调方法
public void OnGuestLogin()
{
    // 游客登录 → 跳过 SDK，直接连接
    UIManager.Instance.Close<LoginPanel>();
    ConnectToServer();
}

public void OnWeChatLogin()
{
    // TODO: 微信 SDK 登录流程
    Debug.Log("[LoginState] 微信登录 — SDK 待集成");
    UIManager.Instance.Close<LoginPanel>();
    ConnectToServer();
}

private void ConnectToServer()
{
    // 从 AppConfig 读取服务器地址
    var cfg = GameLauncher.Config;
    string host = cfg?.DefaultServerHost ?? "127.0.0.1";
    int port = cfg?.DefaultServerPort ?? 7777;

    NetworkManager.Instance.OnConnected += OnServerConnected;
    NetworkManager.Instance.OnDisconnected += OnServerDisconnected;
    NetworkManager.Instance.Connect(host, port);
}

private void OnServerConnected()
{
    NetworkManager.Instance.OnConnected -= OnServerConnected;
    Debug.Log("[LoginState] ✅ 服务器连接成功 → MainVillage");
    ChangeState(GameState.MainVillage);
}

private void OnServerDisconnected(string reason)
{
    NetworkManager.Instance.OnDisconnected -= OnServerDisconnected;
    Debug.LogError($"[LoginState] ❌ 连接失败: {reason}");
    // 重新打开登录面板，显示错误
    UIManager.Instance.Open<LoginPanel>(reason);
}
```

> 需要添加 `using ZWebGLFrameWork.Network;` 和 `using ZWebGLFrameWork.UI;`

---

### Task 5.2: NetworkManager 启动连接

**Files:**
- Modify: `Assets/Scripts/Network/NetworkManager.cs`

需要确保 `NetworkManager` 有公开的 `OnConnected`/`OnDisconnected` 事件和 `Connect(host, port)` 方法。检查现有 API。

**如果缺少 Connect(string, int) 重载，添加：**
```csharp
/// <summary>连接服务器（使用默认协议）</summary>
public void Connect(string host, int port)
{
    Connect(host, port, ProtocolType.Tcp);
}

/// <summary>连接服务器（指定协议）</summary>
public void Connect(string host, int port, ProtocolType protocol)
{
    // 根据协议类型创建对应 client
    switch (protocol)
    {
        case ProtocolType.Tcp:
            _currentClient = new TcpClient();
            break;
        case ProtocolType.Kcp:
            _currentClient = new KcpClient();
            break;
        default:
            _currentClient = new TcpClient();
            break;
    }

    _currentClient.OnConnected += () => OnConnected?.Invoke();
    _currentClient.OnDisconnected += (reason) => OnDisconnected?.Invoke(reason);
    _currentClient.OnConnectFailed += (err) => {
        Debug.LogError($"[NetworkManager] 连接失败: {err}");
        OnDisconnected?.Invoke(err);
    };

    _currentClient.Connect(host, port);
}
```

---

### Task 5.3: 登录 UI 面板

**Files:**
- Create: `Assets/Scripts/UI/Panels/LoginPanel.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.UI
{
    /// <summary>登录面板 — 微信/游客登录 + 服务器选择 + 连接状态</summary>
    public class LoginPanel : UIBase
    {
        protected override void OnOpen(object data)
        {
            BuildUI();
            // 如果有错误信息，显示
            if (data is string error)
                ShowError(error);
        }

        private void BuildUI()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // 背景
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.15f, 0.25f, 1f);

            // 标题
            CreateText("Title", "ZWebGL 部落冲突", 36, new Vector2(0.5f, 0.75f));

            // 游客登录按钮
            CreateButton("BtnGuest", "游客登录", new Vector2(0.5f, 0.55f),
                () => OnGuestClick());

            // 微信登录按钮
            CreateButton("BtnWeChat", "微信登录", new Vector2(0.5f, 0.45f),
                () => OnWeChatClick());

            // 连接状态文字
            var statusGo = CreateText("StatusText", "", 18, new Vector2(0.5f, 0.35f));
            _statusText = statusGo.GetComponent<Text>();
        }

        private Text _statusText;

        private void OnGuestClick()
        {
            _statusText.text = "连接中...";
            var loginState = GameFSM.Instance.GetStateInstance(GameState.Login)
                as LoginState;
            loginState?.OnGuestLogin();
        }

        private void OnWeChatClick()
        {
            _statusText.text = "启动微信登录...";
            var loginState = GameFSM.Instance.GetStateInstance(GameState.Login)
                as LoginState;
            loginState?.OnWeChatLogin();
        }

        private void ShowError(string msg)
        {
            if (_statusText != null) _statusText.text = msg;
        }

        private void ShowError(string msg, System.Action onRetry)
        {
            if (_statusText != null) _statusText.text = msg;
            // 添加重试按钮
            CreateButton("BtnRetry", "重试", new Vector2(0.5f, 0.28f), () => onRetry?.Invoke());
        }

        // ===== 快捷 UI 创建 =====

        private GameObject CreateText(string name, string text, int fontSize, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rt = txt.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(300, 50);
            return go;
        }

        private void CreateButton(string name, string label, Vector2 anchor, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.3f, 0.5f, 0.3f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(200, 50);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(onClick);

            var labelGo = CreateText(name + "_Label", label, 20, new Vector2(0.5f, 0.5f));
            labelGo.transform.SetParent(go.transform, false);
            labelGo.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            labelGo.GetComponent<RectTransform>().anchorMax = Vector2.one;
        }
    }
}
```

---

### Task 5.4: 角色创建/选择面板

**Files:**
- Create: `Assets/Scripts/UI/Panels/CharacterSelectPanel.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.UI
{
    /// <summary>角色选择面板 — 存档列表，选择→加载→进入主城</summary>
    public class CharacterSelectPanel : UIBase
    {
        private List<CharacterSlot> _slots = new();

        protected override void OnOpen(object data)
        {
            BuildUI();
            LoadSaveSlots();
        }

        private void BuildUI()
        {
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.12f, 0.2f, 1f);

            CreateText("Title", "选择角色", 32, new Vector2(0.5f, 0.85f));
        }

        private void LoadSaveSlots()
        {
            float yPos = 0.70f;
            for (int i = 0; i < 5; i++) // 5 个 SaveManager 槽位
            {
                if (SaveManager.Exists(i))
                {
                    var saveData = SaveManager.LoadGame<SaveGameData>(i);
                    if (saveData != null)
                    {
                        AddSlot(i, saveData.PlayerName,
                            $"Lv.{saveData.PlayerLevel}  TH{saveData.TownHallLevel}",
                            yPos);
                    }
                }
                else
                {
                    AddSlot(i, $"空槽位 {i + 1}", "新建角色", yPos);
                }
                yPos -= 0.10f;
            }
        }

        private void AddSlot(int slotIndex, string name, string info, float yPos)
        {
            var go = new GameObject($"Slot_{slotIndex}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.25f, 0.35f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.1f, yPos);
            rt.anchorMax = new Vector2(0.9f, yPos + 0.08f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Button>().onClick.AddListener(() => OnSlotSelected(slotIndex));

            var nameGo = CreateText("Name", name, 20, new Vector2(0.05f, 0.5f));
            nameGo.transform.SetParent(go.transform, false);
            nameGo.GetComponent<RectTransform>().anchorMin = new Vector2(0.05f, 0.1f);
            nameGo.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.9f);

            var infoGo = CreateText("Info", info, 16, new Vector2(0.5f, 0.5f));
            infoGo.transform.SetParent(go.transform, false);
            infoGo.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.1f);
            infoGo.GetComponent<RectTransform>().anchorMax = new Vector2(0.95f, 0.9f);
            infoGo.GetComponent<Text>().color = Color.gray;
        }

        private void OnSlotSelected(int slot)
        {
            SaveManager.CurrentSlot = slot;
            if (SaveManager.Exists(slot))
            {
                var saveData = SaveManager.LoadGame<SaveGameData>(slot);
                // 将存档数据注入 DataManager
                DataManager.Instance.Player.Name.Value = saveData.PlayerName;
                DataManager.Instance.Player.Level.Value = saveData.PlayerLevel;
                DataManager.Instance.Player.TownHallLevel.Value = saveData.TownHallLevel;
                DataManager.Instance.Player.Gold.Value = saveData.Gold;
                DataManager.Instance.Player.Elixir.Value = saveData.Elixir;
            }
            GameFSM.Instance.ChangeState(GameState.MainVillage);
        }

        private GameObject CreateText(string name, string text, int fontSize, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            return go;
        }
    }

    /// <summary>存档数据 DTO</summary>
    [System.Serializable]
    public class SaveGameData
    {
        public string PlayerName;
        public int PlayerLevel;
        public int TownHallLevel;
        public int Gold;
        public int Elixir;
    }
}
```

---

### Task 5.5: 登录失败处理

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/LoginState.cs`

在 `LoginState` 添加：
```csharp
// ==================== 错误处理 ====================

private int _retryCount;
private const int MaxRetries = 3;

private void HandleLoginError(string error, LoginErrorType errorType)
{
    _retryCount++;
    Debug.LogError($"[LoginState] 登录错误 ({errorType}): {error}");

    if (_retryCount > MaxRetries)
    {
        // 超过最大重试次数，回到登录面板
        UIManager.Instance.Close<ErrorPanel>();
        UIManager.Instance.Open<LoginPanel>($"登录失败，已重试{MaxRetries}次: {error}");
        _retryCount = 0;
        return;
    }

    switch (errorType)
    {
        case LoginErrorType.Timeout:
            ShowRetryDialog("网络超时，请检查网络连接", () => ConnectToServer());
            break;
        case LoginErrorType.AuthFailed:
            ShowRetryDialog("认证失败，请重新登录", () => {
                UIManager.Instance.Close<ErrorPanel>();
                UIManager.Instance.Open<LoginPanel>();
            });
            break;
        case LoginErrorType.ServerMaintenance:
            ShowNoticeDialog("服务器维护中，请稍后再试");
            break;
        case LoginErrorType.VersionMismatch:
            ShowNoticeDialog("客户端版本过低，请更新游戏");
            break;
    }
}

private void ShowRetryDialog(string message, System.Action onRetry)
{
    EventBus.Publish(BootErrorEvent.EventKey,
        new BootErrorEvent("Login", message, true));
    // 订阅重试回调
    // TODO: 通过 ErrorPanel 的回调机制实现
}

private void ShowNoticeDialog(string message)
{
    EventBus.Publish(BootErrorEvent.EventKey,
        new BootErrorEvent("Login", message, false));
}

private enum LoginErrorType
{
    Timeout,
    AuthFailed,
    ServerMaintenance,
    VersionMismatch,
}
```

在 `OnServerDisconnected` 中根据 reason 字符串映射到 `LoginErrorType`。

---

## Phase 6: 主城就绪

### Task 6.1: MainVillageState 初始化

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/MainVillageState.cs`

**当前 OnEnter（空壳）→ 改为完整初始化：**
```csharp
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.FSM
{
    public class MainVillageState : FSMState<GameState>
    {
        private ResourceProducer _resourceProducer;

        public MainVillageState() : base(GameState.MainVillage) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入主城状态");

            // ① 初始化 DataManager
            if (!DataManager.Instance.IsReady)
            {
                Debug.LogWarning("[MainVillageState] DataManager 未就绪，使用默认数据");
            }

            // ② 初始化 GridManager
            GridManager.Instance.Init();

            // ③ 加载已有建筑到场景
            LoadBuildingsToScene();

            // ④ 初始化 CameraController，定位到大本营
            var camera = Camera.main?.GetComponent<CameraController>();
            if (camera != null)
            {
                var townHallPos = FindTownHallPosition();
                camera.FocusOn(townHallPos);
            }

            // ⑤ 启动资源产出计时器
            _resourceProducer = new ResourceProducer(DataManager.Instance.Village);
            _resourceProducer.Start();

            EventBus.Publish("MainVillageEntered");
        }

        public override void OnUpdate()
        {
            _resourceProducer?.Tick(Time.deltaTime);
        }

        public override void OnExit()
        {
            _resourceProducer?.Stop();
            base.OnExit();
        }

        private void LoadBuildingsToScene()
        {
            var village = DataManager.Instance.Village;
            if (village == null || village.Buildings.Count == 0)
            {
                Debug.Log("[MainVillageState] 无建筑数据，跳过场景加载");
                return;
            }

            foreach (var building in village.Buildings)
            {
                // TODO: 从配置表获取建筑 prefab，实例化到网格位置
                var config = ConfigTableManager.Instance.GetBuilding(building.TypeId);
                if (config == null) continue;

                var worldPos = GridManager.Instance.GridToWorld(
                    building.GridX.Value, building.GridY.Value);
                Debug.Log($"[MainVillageState] 加载建筑: {config.Name} at ({building.GridX.Value},{building.GridY.Value})");
            }
        }

        private Vector3 FindTownHallPosition()
        {
            // 查找 Town Hall (Id=1) 的位置
            var village = DataManager.Instance.Village;
            var townHall = village?.Buildings.Find(b => b.TypeId == 1);
            if (townHall != null)
            {
                return GridManager.Instance.GridToWorld(
                    townHall.GridX.Value, townHall.GridY.Value);
            }
            return Vector3.zero; // 默认原点
        }
    }
}
```

> 需要添加 `using ZWebGLFrameWork.Gameplay;`

---

### Task 6.2: 主城 UI 面板

**Files:**
- Create: `Assets/Scripts/UI/Panels/MainVillageHUD.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.UI
{
    /// <summary>主城 HUD — 顶部资源栏 + 底部建筑菜单 + 功能按钮</summary>
    public class MainVillageHUD : UIBase
    {
        private Text _goldText;
        private Text _elixirText;
        private Text _gemsText;
        private Text _levelText;

        protected override void OnOpen(object data)
        {
            BuildUI();
            BindObservables();
        }

        protected override void OnClose()
        {
            UnbindObservables();
        }

        private void BuildUI()
        {
            // 顶部资源栏（半透明黑色背景）
            var topBar = new GameObject("TopBar", typeof(RectTransform), typeof(Image));
            topBar.transform.SetParent(transform, false);
            topBar.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            var tbRt = topBar.GetComponent<RectTransform>();
            tbRt.anchorMin = new Vector2(0, 0.92f);
            tbRt.anchorMax = new Vector2(1, 1);
            tbRt.offsetMin = Vector2.zero;
            tbRt.offsetMax = Vector2.zero;

            // 金币
            _goldText = CreateResourceText("GoldText", "金币", new Vector2(0.05f, 0.5f), topBar.transform);
            // 圣水
            _elixirText = CreateResourceText("ElixirText", "圣水", new Vector2(0.25f, 0.5f), topBar.transform);
            // 宝石
            _gemsText = CreateResourceText("GemsText", "宝石", new Vector2(0.45f, 0.5f), topBar.transform);
            // 等级
            _levelText = CreateResourceText("LevelText", "Lv.1", new Vector2(0.65f, 0.5f), topBar.transform);

            // 攻击按钮
            CreateButton("BtnAttack", "攻击!", new Vector2(0.85f, 0.5f), topBar.transform,
                () => OnAttackClick());
        }

        private Text CreateResourceText(string name, string label, Vector2 anchor, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = $"{label}: 0";
            txt.fontSize = 18;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rt = txt.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(120, 30);
            return txt;
        }

        private void CreateButton(string name, string label, Vector2 anchor, Transform parent,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f);
            go.GetComponent<RectTransform>().anchorMin = go.GetComponent<RectTransform>().anchorMax = anchor;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 35);
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lt = labelGo.AddComponent<Text>();
            lt.text = label;
            lt.fontSize = 16;
            lt.color = Color.white;
            lt.alignment = TextAnchor.MiddleCenter;
            lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lt.rectTransform.anchorMin = Vector2.zero;
            lt.rectTransform.anchorMax = Vector2.one;
        }

        private void BindObservables()
        {
            var player = DataManager.Instance.Player;
            player.Gold.OnChanged += OnGoldChanged;
            player.Elixir.OnChanged += OnElixirChanged;
            player.Gems.OnChanged += OnGemsChanged;
            player.Level.OnChanged += OnLevelChanged;

            // 初始刷新
            OnGoldChanged(player.Gold.Value);
            OnElixirChanged(player.Elixir.Value);
            OnGemsChanged(player.Gems.Value);
            OnLevelChanged(player.Level.Value);
        }

        private void UnbindObservables()
        {
            var player = DataManager.Instance.Player;
            if (player != null)
            {
                player.Gold.OnChanged -= OnGoldChanged;
                player.Elixir.OnChanged -= OnElixirChanged;
                player.Gems.OnChanged -= OnGemsChanged;
                player.Level.OnChanged -= OnLevelChanged;
            }
        }

        private void OnGoldChanged(int v) { if (_goldText) _goldText.text = $"💰 {v}"; }
        private void OnElixirChanged(int v) { if (_elixirText) _elixirText.text = $"💧 {v}"; }
        private void OnGemsChanged(int v) { if (_gemsText) _gemsText.text = $"💎 {v}"; }
        private void OnLevelChanged(int v) { if (_levelText) _levelText.text = $"Lv.{v}"; }

        private void OnAttackClick()
        {
            GameFSM.Instance.ChangeStateWithLoading(GameState.Battle);
        }
    }
}
```

---

### Task 6.3: 资源产出系统

**Files:**
- Create: `Assets/Scripts/Gameplay/Economy/ResourceProducer.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Gameplay
{
    /// <summary>
    /// 资源产出系统 — 基于建筑等级持续产出金币/圣水。
    /// 每帧累加，通过 Observable 通知 UI 更新。
    /// </summary>
    public class ResourceProducer
    {
        private readonly VillageData _village;
        private bool _isRunning;

        /// <summary>每秒金币产出速率</summary>
        public float GoldPerSecond { get; private set; }

        /// <summary>每秒圣水产出速率</summary>
        public float ElixirPerSecond { get; private set; }

        public ResourceProducer(VillageData village)
        {
            _village = village;
        }

        public void Start()
        {
            RecalculateRates();
            _isRunning = true;
            Debug.Log($"[ResourceProducer] 启动: 金币+{GoldPerSecond:F1}/s, 圣水+{ElixirPerSecond:F1}/s");
        }

        public void Tick(float deltaTime)
        {
            if (!_isRunning) return;

            var player = DataManager.Instance.Player;
            if (player == null) return;

            float goldIncome = GoldPerSecond * deltaTime;
            float elixirIncome = ElixirPerSecond * deltaTime;

            if (goldIncome > 0)
                player.Gold.Value += Mathf.FloorToInt(goldIncome);
            if (elixirIncome > 0)
                player.Elixir.Value += Mathf.FloorToInt(elixirIncome);
        }

        public void Stop()
        {
            _isRunning = false;
        }

        /// <summary>重新计算产出速率（建筑变更后调用）</summary>
        public void RecalculateRates()
        {
            float goldRate = 0f;
            float elixirRate = 0f;

            foreach (var building in _village.Buildings)
            {
                var config = ConfigTableManager.Instance.GetBuilding(building.TypeId);
                if (config == null) continue;

                // 按 Category 判断产出类型
                switch (config.Category)
                {
                    case "Resource" when config.Name.Contains("Gold"):
                        goldRate += config.GetHP(building.Level.Value) * 0.01f;
                        break;
                    case "Resource" when config.Name.Contains("Elixir"):
                        elixirRate += config.GetHP(building.Level.Value) * 0.01f;
                        break;
                }
            }

            GoldPerSecond = goldRate;
            ElixirPerSecond = elixirRate;
        }
    }
}
```

> Gameplay asmdef 需要引用 Data。检查 `ZWebGLFrameWork.Gameplay.asmdef`。

---

### Task 6.4: 建筑升级计时器

**Files:**
- Modify: `Assets/Scripts/Gameplay/Building/BuildingInteraction.cs`

当前 `BuildingInteraction` 已有完整的升级逻辑（`UpgradeSelected`/`CompleteUpgrade`/`CheckUpgradeTimers`）。需要做的是：

1. **集成 ConfigTableManager** — 从配置表读取升级消耗和时间，而非硬编码参数：
```csharp
// 在 UpgradeSelected 中替换硬编码参数为配置表查询
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

    UpgradeSelected(costGold, 0, buildTime);
}
```

2. **添加 `using ZWebGLFrameWork.Data;`** 

---

### Task 6.5: 主城 → 战斗跳转

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/MainVillageState.cs`

添加攻击触发方法：
```csharp
/// <summary>开始攻击（由 UI 按钮调用）</summary>
public void StartAttack()
{
    Debug.Log("[MainVillageState] 开始攻击 → 加载战斗场景");
    var payload = new LoadingPayload
    {
        TargetState = GameState.Battle,
        TargetScene = "Battle",  // Phase 4.4 创建的战斗场景
        TargetData = null,       // TODO: 传入敌方村庄数据
    };
    ChangeState(GameState.Loading, payload);
}
```

`MainVillageHUD` 的 `OnAttackClick` 改为调用此方法。

---

## Phase 7: 战斗系统接入

### Task 7.1: BattleState 实现

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/BattleState.cs`

**当前代码** (空壳):
```csharp
public class BattleState : FSMState<GameState>
{
    public BattleState() : base(GameState.Battle) { }

    public override void OnEnter(object data = null)
    {
        base.OnEnter(data);
        Debug.Log("[GameFSM] 进入战斗状态");
        EventBus.Publish("BattleEntered");
    }

    public override void OnUpdate() { }
    public override void OnExit() { base.OnExit(); }
}
```

**改为** (对接 CombatManager):
```csharp
using UnityEngine;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.FSM
{
    public class BattleState : FSMState<GameState>
    {
        private bool _battleEnded;

        public BattleState() : base(GameState.Battle) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            _battleEnded = false;
            Debug.Log("[GameFSM] 进入战斗状态");

            // 打开战斗 HUD
            UIManager.Instance.Open<BattleHUD>();

            // 订阅战斗结束事件
            EventBus.Subscribe<CombatResult>(CombatManager.EventCombatEnded, OnCombatEnded);

            // 获取当前模式和敌方数据
            var mode = GetBattleMode(data);
            var defenderData = GetDefenderData(data);

            // 启动战斗
            CombatManager.Instance.StartBattleAsync(mode, defenderData).Forget();

            EventBus.Publish("BattleEntered");
        }

        public override void OnUpdate()
        {
            // 战斗逻辑由 CombatOrchestrator.Tick 驱动
            // BattleState 只负责监听结束事件
        }

        public override void OnExit()
        {
            EventBus.Unsubscribe<CombatResult>(CombatManager.EventCombatEnded, OnCombatEnded);
            UIManager.Instance.Close<BattleHUD>();
            base.OnExit();
        }

        private void OnCombatEnded(CombatResult result)
        {
            if (_battleEnded) return;
            _battleEnded = true;

            Debug.Log($"[BattleState] 战斗结束: Winner={result.WinnerTeam}, " +
                      $"Stars={result.Stars}, Destruction={result.DestructionPercent}%");

            // 应用战利品
            ApplyLoot(result);

            // 显示结算面板
            UIManager.Instance.Open<BattleResultPanel>(result);
        }

        private CombatMode GetBattleMode(object data)
        {
            // 默认使用 VillageAttackMode
            return new VillageAttackMode();
        }

        private VillageData GetDefenderData(object data)
        {
            // TODO: 从服务器或本地配置加载敌方村庄
            return new VillageData();
        }

        private void ApplyLoot(CombatResult result)
        {
            var player = DataManager.Instance.Player;
            player.Gold.Value += result.GoldLooted;
            player.Elixir.Value += result.ElixirLooted;
            player.Trophies.Value += result.TrophyChange;
        }
    }
}
```

> 需要 FSM asmdef 引用 Combat（检查 `ZWebGLFrameWork.FSM.asmdef`）。

---

### Task 7.2: CombatManager 与 FSM 对接

**Files:**
- Modify: `Assets/Scripts/Combat/CombatManager.cs`

添加 `StartBattleAsync` 的公开重载（当前 `StartBattleAsync(CombatMode)` 存在，需要确认可从 FSM 调用）：

```csharp
/// <summary>启动战斗（FSM 入口）</summary>
public async UniTask StartBattleAsync(CombatMode mode, VillageData defenderData)
{
    if (CurrentPhase != CombatPhase.Idle)
    {
        Debug.LogWarning("[CombatManager] 已有战斗进行中");
        return;
    }

    CurrentMode = mode;
    _defenderData = defenderData;

    Debug.Log($"[CombatManager] ===== 战斗开始 [{mode.DisplayName}] =====");

    // 重建 ECS World
    FreshBattle();

    _pathfinding = new AStarPathfinding(GridManager.Instance);
    _systems = await CombatBootstrap.BuildAsync(ECSWorld);
    DeathSys = _systems.Find<DeathSystem>();
    _spawner = new UnitSpawner(ECSWorld, GridManager.Instance);
    _ai = new UnitAI(ECSWorld, _pathfinding, new TargetSelector());

    // 生成防守方单位
    if (defenderData != null)
    {
        mode.SpawnDefenders(_spawner, defenderData);
    }

    // 启动编排器
    _orchestrator.Run(mode, _systems, _spawner, _ai, DeathSys).Forget();
}

private VillageData _defenderData;
```

---

### Task 7.3: 战斗 UI

**Files:**
- Create: `Assets/Scripts/UI/Panels/BattleHUD.cs`
- Create: `Assets/Scripts/UI/Panels/BattleResultPanel.cs`

**BattleHUD:**
```csharp
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    /// <summary>战斗 HUD — 时间/星星/摧毁率 + 底部兵种栏</summary>
    public class BattleHUD : UIBase
    {
        private Text _timerText;
        private Text _destructionText;
        private Text _starsText;
        private float _elapsedTime;

        protected override void OnOpen(object data)
        {
            BuildUI();
            EventBus.Subscribe<string>(CombatManager.EventPhaseChanged, OnPhaseChanged);
        }

        protected override void OnClose()
        {
            EventBus.Unsubscribe<string>(CombatManager.EventPhaseChanged, OnPhaseChanged);
        }

        private void BuildUI()
        {
            // 顶部信息栏
            var topBar = new GameObject("TopBar", typeof(RectTransform), typeof(Image));
            topBar.transform.SetParent(transform, false);
            topBar.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);
            topBar.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.93f);
            topBar.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);

            _timerText = CreateText("Timer", "3:00", new Vector2(0.5f, 0.5f), topBar.transform);
            _destructionText = CreateText("Destruction", "0%", new Vector2(0.25f, 0.5f), topBar.transform);
            _starsText = CreateText("Stars", "⭐0", new Vector2(0.75f, 0.5f), topBar.transform);
        }

        private Text CreateText(string name, string text, Vector2 anchor, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = 18;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = new Vector2(100, 30);
            return txt;
        }

        private void OnPhaseChanged(string phase)
        {
            if (_timerText) _timerText.text = phase;
        }

        private void Update()
        {
            _elapsedTime += Time.deltaTime;
            int mins = Mathf.FloorToInt(_elapsedTime / 60);
            int secs = Mathf.FloorToInt(_elapsedTime % 60);
            if (_timerText) _timerText.text = $"{mins}:{secs:00}";
        }
    }
}
```

**BattleResultPanel:**
```csharp
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.UI
{
    /// <summary>战斗结算面板 — 星星/战利品/奖杯 + 返回主城按钮</summary>
    public class BattleResultPanel : UIBase
    {
        protected override void OnOpen(object data)
        {
            var result = data as CombatResult;
            if (result == null) return;

            BuildUI(result);
        }

        private void BuildUI(CombatResult result)
        {
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.85f);

            // 星星
            CreateText("Stars", new string('⭐', result.Stars), 48, new Vector2(0.5f, 0.7f));
            // 摧毁率
            CreateText("Destruction", $"摧毁率: {result.DestructionPercent}%", 24, new Vector2(0.5f, 0.55f));
            // 战利品
            CreateText("Loot", $"金币 +{result.GoldLooted}  圣水 +{result.ElixirLooted}", 20, new Vector2(0.5f, 0.45f));
            // 奖杯
            string trophyStr = result.TrophyChange >= 0
                ? $"奖杯 +{result.TrophyChange}"
                : $"奖杯 {result.TrophyChange}";
            CreateText("Trophy", trophyStr, 20, new Vector2(0.5f, 0.38f));

            // 返回主城按钮
            CreateButton("BtnReturn", "返回主城", new Vector2(0.5f, 0.2f),
                () => GameFSM.Instance.ChangeStateWithLoading(GameState.MainVillage));
        }

        private GameObject CreateText(string name, string text, int fontSize, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = new Vector2(400, 60);
            return go;
        }

        private void CreateButton(string name, string label, Vector2 anchor, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            go.GetComponent<Image>().color = new Color(0.3f, 0.6f, 0.3f);
            go.GetComponent<RectTransform>().anchorMin = go.GetComponent<RectTransform>().anchorMax = anchor;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(180, 50);
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var labelGo = new GameObject(name + "_Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lt = labelGo.AddComponent<Text>();
            lt.text = label;
            lt.fontSize = 22;
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

### Task 7.4: 部署阶段交互

**Files:**
- Modify: `Assets/Scripts/Combat/Core/CombatOrchestrator.cs` → `TickDeploy`

当前 `CombatOrchestrator` 已有 `Deploy` 阶段（`RunDeployPhaseAsync`/`TickDeploy`），需确保：
- `TickDeploy` 中监听玩家点击 → 网格坐标 → `UnitSpawner.Spawn()`
- 通过 `InputManager` 事件获取手势
- 调用 `GridManager.WorldToGrid` 转换坐标
- 调用 `GridManager.IsInBounds` + `IsOccupied` 校验
- 绿色（可部署）/红色（不可部署）反馈

> 此任务涉及 CombatOrchestrator 内部逻辑，保持最小变更 — 仅确认 Deploy 阶段钩子存在。

---

### Task 7.5: 战斗结算 + 返回主城

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/BattleState.cs`

已在 Task 7.1 中实现 `OnCombatEnded` → `ApplyLoot` → `BattleResultPanel`。`BattleResultPanel` 的「返回主城」按钮调用 `GameFSM.Instance.ChangeStateWithLoading(GameState.MainVillage)`。

---

## 跨阶段任务

### Task C.1: BuildValidator 增加启动流程检查

**Files:**
- Modify: `Assets/Editor/BuildValidator.cs`

添加编译后自动检查：
```csharp
private static void ValidateBootPipeline()
{
    // 检查 Config 目录是否包含必需文件
    string[] requiredConfigs = {
        "Assets/Resources/Config/AppConfig.asset",
        "Assets/Resources/Config/Buildings.json",
        "Assets/Resources/Config/Units.json",
        "Assets/Resources/Config/Levels.json",
    };

    foreach (var path in requiredConfigs)
    {
        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning($"[BuildValidator] 缺少配置文件: {path}");
        }
    }

    // 检查 Launcher 场景是否在 Build Settings
    var scenes = UnityEditor.EditorBuildSettings.scenes;
    bool hasLauncher = false;
    foreach (var scene in scenes)
    {
        if (scene.path.Contains("Launcher"))
        {
            hasLauncher = true;
            break;
        }
    }
    if (!hasLauncher)
    {
        Debug.LogWarning("[BuildValidator] Build Settings 中未找到 Launcher 场景");
    }
}
```

---

### Task C.2: 启动耗时性能埋点

**Files:**
- Create: `Assets/Scripts/Core/Diagnostics/BootProfiler.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 启动耗时性能埋点 — 记录每个 Phase 的开始/结束时间，
    /// 输出 Delta 到日志和 LocalStore。
    /// </summary>
    public static class BootProfiler
    {
        private static readonly List<PhaseTiming> _phases = new();
        private static float _phaseStartTime;

        /// <summary>开始新阶段</summary>
        public static void BeginPhase(string phaseName)
        {
            _phaseStartTime = Time.realtimeSinceStartup;
            Debug.Log($"[BootProfiler] ▶ {phaseName}");
        }

        /// <summary>结束当前阶段并记录耗时</summary>
        public static void EndPhase(string phaseName)
        {
            float elapsed = Time.realtimeSinceStartup - _phaseStartTime;
            _phases.Add(new PhaseTiming { Name = phaseName, ElapsedMs = elapsed * 1000f });
            Debug.Log($"[BootProfiler] ✓ {phaseName}: {elapsed * 1000f:F0}ms");

            // 写入 LocalStore 持久化
            SaveTimings();
        }

        /// <summary>获取所有阶段耗时报告</summary>
        public static string GetReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 启动耗时报告 ===");
            float total = 0;
            foreach (var p in _phases)
            {
                sb.AppendLine($"  {p.Name}: {p.ElapsedMs:F0}ms");
                total += p.ElapsedMs;
            }
            sb.AppendLine($"  总计: {total:F0}ms");
            return sb.ToString();
        }

        private static void SaveTimings()
        {
            string json = JsonUtility.ToJson(new PhaseTimingList { Phases = _phases });
            LocalStore.SetString("BootTimings", json);
        }

        [System.Serializable]
        private struct PhaseTiming
        {
            public string Name;
            public float ElapsedMs;
        }

        [System.Serializable]
        private class PhaseTimingList
        {
            public List<PhaseTiming> Phases;
        }
    }
}
```

---

### Task C.3: AppConfig 热更后自动同步

**Files:**
- Modify: `Assets/Scripts/FSM/Game/HotUpdateManager.cs`
- Modify: `Assets/Scripts/FSM/Game/GameLauncher.cs`

在 `HotUpdateManager.DownloadUpdatesAsync` 完成后，检查下载的 AssetBundle 中是否有新的 `AppConfig.asset`：

```csharp
// HotUpdateManager.cs → DownloadUpdatesAsync 末尾添加：
if (CheckAssetExists("Config/AppConfig.asset"))
{
    var newConfig = LoadAssetFromBundle<AppConfig>("Config/AppConfig.asset");
    if (newConfig != null)
    {
        GameLauncher.Config = newConfig;
        GameLauncher.Config.Apply();
        Debug.Log("[HotUpdateManager] ✅ AppConfig 已从热更包更新");
    }
}
```

---

### Task C.4: 离线模式支持

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/LaunchState.cs` → Phase 3 区块
- Modify: `Assets/Scripts/FSM/Game/States/LoginState.cs`

在 `LaunchState.RunInitPipeline` 末尾（Phase 3 之后），根据 `AppConfig.EnableOfflineMode` 决定跳过登录：

```csharp
// LaunchState.cs → 替换 ChangeState(GameState.Login) 为：
if (cfg.EnableOfflineMode && !HasNetwork())
{
    Debug.Log("[LaunchState] ⏭ 离线模式 — 跳过登录");
    ChangeState(GameState.MainVillage);
}
else
{
    ChangeState(GameState.Login);
}
```

`LoginState` 添加离线检测：
```csharp
// LoginState.OnEnter 开头：
if (GameLauncher.Config.EnableOfflineMode && !HasNetwork())
{
    Debug.Log("[LoginState] 离线模式，使用本地存档");
    ChangeState(GameState.MainVillage);
    return;
}
```

添加网络检测辅助方法到 `LaunchState`：
```csharp
private static bool HasNetwork()
{
    return Application.internetReachability != NetworkReachability.NotReachable;
}
```

---

## 执行顺序 & 编译验证

| 批次 | 任务 | 编译后验证 |
|------|------|:---:|
| B1 | 4.1, 4.2 | ✅ |
| B2 | 5.1, 5.2, 5.3, 5.4, 5.5 | ✅ |
| B3 | 6.1, 6.2, 6.3, 6.4, 6.5 | ✅ |
| B4 | 7.1, 7.2, 7.3, 7.5 | ✅ |
| B5 | C.1, C.2, C.3, C.4 | ✅ |
| — | 4.3, 4.4 | ⚠️ Unity Editor |
| — | 7.4 (Deploy交互) | 依赖CombatOrchestrator内部 |

> 注意：4.3/4.4 (场景搭建) 和 7.4 (部署交互) 需要 Unity Editor 或已存在的内部逻辑，不在纯代码变更范围。
