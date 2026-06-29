# P1-2 + P1-3: 存档系统统一 + 自动存档 实施计划

> **Goal:** 统一存档格式为 PlayerSaveData，删除无用的 KVP 路径，加入自动存档。

**Architecture:** SaveGame() 直接读取 DataManager 的 Player/Village 写入 PlayerSaveData；MainVillageState 定时调用 + BattleState 进入前存档。

---

### 文件变更

| 操作 | 文件 | 职责 |
|------|------|------|
| Modify | `Assets/Scripts/Data/SaveManager.cs` | 重写 SaveGame，删除 KVP 代码 |
| Modify | `Assets/Scripts/Data/PlayerSaveData.cs` | 注释更新 |
| Modify | `Assets/Scripts/FSM/Game/States/MainVillageState.cs` | 30s 自动存档 + 切后台存档 |
| Modify | `Assets/Scripts/FSM/Game/States/BattleState.cs` | 进入战斗前存档 |

---

### Task 1: SaveManager — 统一格式 + 清理

**File:** `Assets/Scripts/Data/SaveManager.cs`

需要做的：
1. 重写 `SaveGame()` 直接用 DataManager 构建 `PlayerSaveData`
2. 删除 `GameSaveData`、`SerializableKVP`、`ToSerializable`、`FromSerializable`、`ParseTyped`
3. 删除 `LoadGame()`（改用 `LoadToDataManager`）

**完整替换 SaveGame 方法（第 72-128 行）：**

```csharp
/// <summary>保存游戏完整状态（Player + Village）</summary>
public static bool SaveGame(int? slot = null)
{
    int targetSlot = slot ?? CurrentSlot;

    var player = DataManager.Instance.Player;
    var village = DataManager.Instance.Village;

    var saveData = new PlayerSaveData
    {
        PlayerName = player.Name.Value,
        PlayerLevel = player.Level.Value,
        TownHallLevel = player.TownHallLevel.Value,
        Gold = player.Gold.Value,
        Elixir = player.Elixir.Value,
        DarkElixir = player.DarkElixir.Value,
        Gems = player.Gems.Value,
        Trophies = player.Trophies.Value,
        Exp = player.Exp.Value,
        Buildings = new System.Collections.Generic.List<BuildingSaveData>(),
        SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    };

    foreach (var b in village.Buildings)
    {
        saveData.Buildings.Add(new BuildingSaveData
        {
            TypeId = b.TypeId,
            GridX = b.GridX.Value,
            GridY = b.GridY.Value,
            Level = b.Level.Value,
        });
    }

    return Save(saveData, targetSlot);
}
```

**然后删除**（从第 87 行到第 167 行，即 KVP 辅助方法 + LoadGame）：

删除以下全部内容：
- `ToSerializable()`
- `FromSerializable()`
- `ParseTyped()`
- `LoadGame()`

同时删除文件末尾的 `GameSaveData` 和 `SerializableKVP` 类定义（第 239-254 行）。

---

### Task 2: PlayerSaveData — 注释更新

**File:** `Assets/Scripts/Data/PlayerSaveData.cs`

修改第 17-18 行注释：

```csharp
    /// <summary>
    /// 玩家完整存档 DTO — 离线模式自包含。
    /// 通过 SaveManager.SaveGame / LoadToDataManager 读写。
    /// </summary>
```

（已经是正确的，确认无需修改）

---

### Task 3: MainVillageState — 自动存档

**File:** `Assets/Scripts/FSM/Game/States/MainVillageState.cs`

1. 添加自动存档字段和逻辑：

在字段区添加：
```csharp
private float _autoSaveTimer;
private int _lastGold;
private int _lastElixir;
```

在 `OnEnter` 末尾添加：
```csharp
// ⑤ 自动存档状态初始化
_autoSaveTimer = 0f;
_lastGold = DataManager.Instance.Player.Gold.Value;
_lastElixir = DataManager.Instance.Player.Elixir.Value;
```

在 `OnUpdate` 中添加：
```csharp
// 自动存档：每 30 秒检查资源变化
_autoSaveTimer += Time.deltaTime;
if (_autoSaveTimer >= 30f)
{
    _autoSaveTimer = 0f;
    var p = DataManager.Instance.Player;
    if (p.Gold.Value != _lastGold || p.Elixir.Value != _lastElixir)
    {
        SaveManager.SaveGame();
        _lastGold = p.Gold.Value;
        _lastElixir = p.Elixir.Value;
        Debug.Log("[MainVillageState] 自动存档");
    }
}
```

---

### Task 4: BattleState — 进入战斗前存档

**File:** `Assets/Scripts/FSM/Game/States/BattleState.cs`

在 `OnEnter` 的 `// ③ 启动战斗` 之前添加：

```csharp
// ②.⑤ 进入战斗前存档
SaveManager.SaveGame();
```

---

### Task 5: 编译验证
