# 🏗️ 任务路由器 (Task Router)

## 任务状态机

```
用户输入需求
    ↓
 ┌──────────────────────────────────────────────────┐
 │                  Task State Machine               │
 │                                                  │
 │  todo ──→ assigned ──→ in_progress ──→ review    │
 │   ↑                      │              │        │
 │   │                      │              ├─ ✅ done│
 │   │                      │              └─ ❌     │
 │   └──────── blocked ─────┘              rejected  │
 │         (回用户确认)                     → todo   │
 └──────────────────────────────────────────────────┘
```

## 状态定义

| 状态 | 含义 | 谁负责 | 下一状态 |
|------|------|--------|----------|
| `todo` | 待处理 | PM Agent | `assigned` |
| `assigned` | 已分配 | Lead Dev | `in_progress` |
| `in_progress` | 执行中 | Dev Agent | `review` |
| `review` | 审查中 | QA / Design Agent | `done` / `rejected` |
| `done` | 已完成 | — | 终结（反馈给用户） |
| `blocked` | 阻塞 | PM Agent | `todo`（用户确认后） |

## Agent 协作规则

### PM Agent → Lead Dev
- PM 收到用户需求 → 创建 WBS 任务列表
- 所有子任务初始状态 = `todo`
- 吐出一条状态消息

### Lead Dev → Dev Agent
- 从任务池取 `todo` 任务
- 分解为原子子任务（每个独立可测）
- 按优先级派发给 Dev Agent
- 状态切换到 `assigned` → `in_progress`

### Dev Agent → QA Agent
- 编码 + 自测通过
- 提交交付物
- 编译器须无报错（CompileHook 已验证）
- 状态切换到 `review`

### QA Agent → 反馈
- 独立运行测试
- 交叉验证
- ✅ 通过 → `done`，反馈给用户
- ❌ 不通过 → `rejected` → 退回到 `todo`，附问题清单

### Design Agent → PM
- 体验评审
- 不直接改代码
- 反馈给 PM，PM 转化为新需求

## 消息格式

每个 Agent 产出消息时使用统一格式：

```
## [状态变更] task-name: oldState → newState
- **原因**: 简述
- **交付物**: 文件列表或检查结果
- **下一步**: 谁做什么
- **阻塞项**: 如果 blocked，说明需要用户确认什么
```
