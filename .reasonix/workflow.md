# 🔄 ZWebGLFrameWork 开发工作流

## 全流程视图

```
用户提出需求
    │
    ▼
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│  ① PM Agent   │───→│ ② Lead Dev   │───→│ ③ Dev Agent   │
│  需求→WBS     │    │  拆解→派发    │    │  编码→测试    │
└──────────────┘    └──────────────┘    └──────┬───────┘
                                               │ 交付
                                               ▼
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│  ⑥ 用户确认   │←───│  ⑤ Design    │←───│  ④ QA Agent   │
│  反馈新需求   │    │  体验评审     │    │  独立验证     │
└──────────────┘    └──────────────┘    └──────────────┘
                                               │
                                          ┌────▼─────┐
                                          │ CompileHook│  ← 自动编译验证
                                          │ (Editor)   │
                                          └────────────┘
```

## 阶段详解

### ① PM Agent — 需求管理
- **触发**: 用户输入需求
- **产出**: WBS 任务分解列表
- **规则**: 
  - 每个任务 = 一个独立可验证的交付物
  - 任务含验收标准
  - 初始状态 `todo`
- **示例产出**:
  ```
  ## WBS
  ### T1: 实现 Observable<T> 响应式值
  - 验收: 值变化触发 OnChanged，零变化跳过
  - 状态: todo
  
  ### T2: 实现 ObservableModel 基类
  - 验收: 反射收集字段，OnAnyChanged 回调
  - 状态: todo
  ```

### ② Lead Dev — 任务分解与派发
- **触发**: PM 产出的 WBS
- **动作**: 
  1. 取 `todo` 任务
  2. 阅读相关代码上下文
  3. 拆解为原子子任务
  4. 按优先级派发给 Dev Agent
- **规则**:
  - 子任务必须可独立编译
  - 每个子任务 ≤ 200 行
  - 明确输入/输出/验收标准
- **状态变更**: `todo` → `assigned`

### ③ Dev Agent — 编码与自测
- **触发**: Lead Dev 分配的子任务
- **动作**:
  1. 理解需求和上下文
  2. 编写代码（遵循底层行为准则）
  3. 编写测试
  4. 运行测试直到全部通过
- **规则**:
  - 不确定时主动向 Lead Dev 提问，不猜测
  - 每个函数有确定性理由
  - 文件 ≤ 200 行
- **状态变更**: `assigned` → `in_progress` → `review`

### ④ QA Agent — 独立验证
- **触发**: Dev Agent 交付
- **动作**:
  1. 独立运行测试套件
  2. 边界条件测试
  3. 回归检查
  4. 输出验证报告
- **规则**:
  - 不信任 Dev Agent 的自测结果
  - 所有结论必须有测试证据
  - 两个 QA 交叉验证同一交付物
- **状态变更**: `review` → `done` ✅ 或 `rejected` ❌

### ⑤ Design Agent — 体验评审
- **触发**: 功能完成后（可并行于 QA）
- **动作**: 以用户视角评审易用性、可视化、交互
- **规则**: 反馈给 PM，不直接改代码
- **产出**: 体验问题清单 → PM 转化为新需求

### ⑥ 用户确认与迭代
- **触发**: PM 收集所有完成报告
- **动作**: 汇总报告给用户
- **循环**: 用户新需求 → 回到 ①

## CI/CD 集成

```
Push 代码
    │
    ▼
GitHub Actions ──→ ① Compile & Validate (EditMode)
    │                    │
    │                    ├─ ❌ → 通知 Dev Agent 修复
    │                    └─ ✅
    │                         ▼
    │                   ② Unit Tests (EditMode + PlayMode)
    │                         │
    │                         ├─ ❌ → 通知 QA Agent
    │                         └─ ✅
    │                              ▼
    │                   ③ Build (WebGL + Windows)
    │                              │
    │                              └─ ✅ → Upload Artifact
    │                                       │
    │                                  ④ Deploy WebGL → GitHub Pages
    │
    ▼
用户可访问在线版本验证
```

## 文件位置

| 组件 | 路径 |
|------|------|
| Agent Skill 定义 | `.reasonix/skills/*.md` |
| 任务状态机 | `.reasonix/task-router.md` |
| 工作流编排 | `.reasonix/workflow.md`（本文件） |
| CI/CD 流水线 | `.github/workflows/ci.yml` |
| 编译验证器 | `Assets/Editor/CompileHook.cs` |
| 构建脚本 | `ci_build.sh` |
