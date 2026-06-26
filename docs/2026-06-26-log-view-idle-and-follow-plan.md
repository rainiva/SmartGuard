# 日志页「跟随最新」与「空闲时间」修改规划

> **适用对象：** 后续实现此功能的开发者或 Agent。  
> **流程要求：** 遵循 `AGENTS.md` RED → GREEN → REFACTOR；每阶段独立可测、可提交。  
> **用户未明确要求前，不要 push。**

**目标：** 让设置窗口日志页的空闲时间显示准确、持续刷新；「跟随最新」能可靠滚到最新日志；并修复引擎状态文件在用户恢复活动后空闲时间虚高的问题。

**架构原则：** 引擎 `SmartGuard.status.json` 仍是策略决策的权威来源；日志页显示层通过「读 status + 时间外推 + 本地活动校正」呈现；磁盘写入策略在准确性与 I/O 之间取折中。

**技术栈：** C# / WPF（`SmartGuard.Settings`）、xUnit + FluentAssertions、Pester 集成测试（可选）、`GetLastInputInfo`（`IdleDetector`）、`SmartGuard.Engine` 状态发布。

---

## 一、现状与数据流

```
┌─────────────────┐     每 CheckIntervalSec      ┌──────────────────────────┐
│  GuardianLoop   │ ───────────────────────────► │ SmartGuard.status.json   │
│  + IdleTracker  │     StatusPublisher.Publish   │ idleSeconds + timestamp │
└─────────────────┘                               └────────────┬─────────────┘
                                                                 │
                    ┌────────────────────────────────────────────┘
                    ▼
         ┌──────────────────────┐     每 2s（日志页激活）    ┌─────────────────────┐
         │ LogViewIdleReader    │ ◄─────────────────────── │ RefreshLogView       │
         │ + LogViewIdleResolver│                          │ SettingsWindowController │
         └──────────┬───────────┘                          └─────────────────────┘
                    ▼
         ┌──────────────────────┐
         │ lblLogStatus         │
         │ 「当前空闲 N 秒」     │
         └──────────────────────┘
```

**关键文件：**

| 职责 | 文件 |
|------|------|
| 日志页刷新、定时器、状态栏 | `src/SmartGuard.Settings/SettingsWindowController.cs` |
| 读 status / 回退 API | `src/SmartGuard.Settings/LogViewIdleReader.cs` |
| status 时间外推 | `src/SmartGuard.Settings/LogViewIdleResolver.cs` |
| 状态栏文案 | `src/SmartGuard.Settings/LogViewStatusTextBuilder.cs` |
| 跟随滚动 | `SettingsWindowController.ScrollLogViewToTail` + `LogViewScrollState` |
| 引擎发布 | `src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs` |
| 引擎空闲采样 | `src/SmartGuard.Engine/Infrastructure/IdleTracker.cs` |
| WinForms 旧日志查看器（行为对齐参考） | `src/SmartGuard.LogViewer/LogViewerSession.cs` |

---

## 二、问题清单

### 2.1 已完成（本会话内，需纳入回归）

| ID | 问题 | 根因 | 修复 | 测试 |
|----|------|------|------|------|
| **F1** | 勾选「跟随最新」不滚到底 | `scrollToTail` 要求 `wasAtTail`；虚拟列表 `ScrollToEnd` 不可靠 | `ScrollLogViewToTail`（`ScrollIntoView` + 延迟二次滚动）；`SetFollowTail(true)` 强制滚底 | `User_enabling_follow_tail_scrolls_to_latest_log` 等 |
| **D1** | 空闲时间不随 2s 定时器更新；打开日志页不显示 | `_logIdleSeconds` 仅在手动刷新时读取 | 每次 `RefreshLogView` 调用 `LogViewIdleReader.TryReadSeconds` | `User_on_logs_page_sees_idle_without_clicking_refresh`、`User_automatic_log_refresh_updates_idle_seconds_without_manual_refresh` |

### 2.2 待完成（本规划主体）

| ID | 问题 | 根因 | 影响 |
|----|------|------|------|
| **D2** | 用户恢复操作后，日志页「当前空闲」仍持续攀升 | `StatusPublisher` 在**仅** `idleSeconds` 变化时跳过写盘；`LogViewIdleResolver` 只能外推递增 | 用户已活跃，状态栏仍显示数百秒空闲 |
| **D3** | 引擎未运行时 status 文件陈旧 | 无进程更新 status，外推仍按旧 `idleSeconds + elapsed` | 长时间未跑引擎时显示不可信 |
| **D4** | WinForms 日志查看器未读 status 空闲 | `LogViewerSession` 只管日志文本，状态栏无「当前空闲」 | 两套 UI 行为不一致（低优先级） |

### 2.3 明确不做（除非产品另提需求）

| 项目 | 原因 |
|------|------|
| **默认倒序显示（最新在上）** | 与日志文件顺序、导出、WinForms 查看器不一致；改动面大（`LogViewDisplaySlice`、滚动语义、过滤） |
| **修改 `StatusPublisher` 为每次迭代都写盘** | I/O 过高；应使用「有意义变化才写」+ 活动校正 |

---

## 三、推荐方案总览

分 **三个阶段**，每阶段可独立合并，互不阻断：

| 阶段 | 名称 | 范围 | 风险 | 预估 |
|------|------|------|------|------|
| **Phase A** | 显示层活动校正 | `LogViewIdleReader` + 测试 | 低 | 0.5 天 |
| **Phase B** | 引擎状态发布修正 | `StatusPublisher` + `GuardianLoop` + 测试 | 中 | 0.5–1 天 |
| **Phase C** | 陈旧 status 降级与文档 | 文件年龄检测 + 文档 | 低 | 0.25 天 |

**核心设计决策（Phase A + B 组合）：**

1. **显示层（Phase A）：** 当本地 `IdleDetector` 表明用户刚有活动时，不信任 status 外推的高空闲值。复用 `IdleTracker.IsUserActivity` 同款阈值：`apiIdle <= 2` 或 `apiIdle + 5 < extrapolated` → 采用 `apiIdle`。
2. **引擎层（Phase B）：** `StatusPayloadEquals` 增加空闲**显著下降**判定：若 `payload.idleSeconds + 5 < _lastPayload.idleSeconds`，视为有变化，强制写盘。保持「仅 idle 缓慢上升」时仍跳过写盘。
3. **陈旧文件（Phase C）：** 若 `timestamp` 距今超过 `2 × CheckIntervalSec`（或固定 90s），且本地 API 空闲明显更低，优先 API；若文件过旧（>5min），状态栏可加「引擎可能未运行」提示（可选）。

**为何不只用 `Math.Min(api, extrapolated)`：** 睡眠期间 API 可能卡住而 extrapolated 正确增大；简单取 min 会在睡眠场景低估空闲。必须用「活动检测」而非无脑取小。

---

## 四、分阶段任务（TDD）

### Phase A：显示层活动校正

**修改文件：**

- `src/SmartGuard.Settings/LogViewIdleReader.cs` — 新增 `ResolveIdleSeconds(status, now)` 私有逻辑
- `Tests/SmartGuard.Settings.Tests/LogViewIdleReaderTests.cs` — 单元测试
- `Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs` — 可选集成测试

#### Task A1：活动下降时使用本地 API

- [ ] **RED** — 在 `LogViewIdleReaderTests` 新增：
  - `TryReadSeconds_uses_local_api_when_user_becomes_active`  
    status: `idleSeconds=500`, `timestamp=30s前` → extrapolated≈530；`ApiReadOverride=8` → 期望 `8`
  - `TryReadSeconds_keeps_extrapolated_when_user_still_idle`  
    status: `idleSeconds=100`, `timestamp=25s前`；`ApiReadOverride=120` → 期望 `125`（外推）
- [ ] 运行测试，确认失败
- [ ] **GREEN** — 在 `TryReadSeconds` 中：
  ```csharp
  var extrapolated = LogViewIdleResolver.ResolveFromStatus(status, nowLocal);
  var apiIdle = (int)(ApiReadOverrideForTests?.Invoke() ?? IdleDetector.GetIdleSeconds());
  if (IdleTracker.IsUserActivity((uint)apiIdle, (uint)Math.Max(0, extrapolated - elapsedSincePublish)))
      return apiIdle;
  // 或更直接：若 apiIdle + 5 < extrapolated 则 return apiIdle
  return extrapolated;
  ```
  建议提取 `LogViewIdleDisplayPolicy.Resolve(extrapolated, apiIdle)` 纯函数，便于单测。
- [ ] **GREEN** — 全量 `SmartGuard.Settings.Tests` 通过
- [ ] **REFACTOR** — 去掉重复阈值魔法数，引用 `IdleTracker` 或共享常量

#### Task A2：集成验证

- [ ] **RED** — `SettingsWindowControllerTests`：
  - `User_sees_lower_idle_after_local_activity_while_status_stale`
- [ ] **GREEN** — 无需改 Controller（已通过 `RefreshLogView` 每 tick 读 idle）
- [ ] 贴出 `dotnet test` 通过输出

**Phase A 验收标准：**

- 有 status 文件、用户刚操作鼠标后 2s 内，状态栏空闲秒数降至个位数级
- 用户持续空闲时，空闲秒数仍随时间递增（外推正常）
- 无 status 文件时行为与现在一致（纯 API）

---

### Phase B：引擎状态发布修正

**修改文件：**

- `src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs`
- `Tests/SmartGuard.Engine.Tests/StatusPublisherTests.cs`
- 可选：`src/SmartGuard.Engine/Worker/GuardianLoop.cs`（仅当需调整 payload 字段）

#### Task B1：空闲显著下降时强制写盘

- [ ] **RED** — `StatusPublisherTests` 新增：
  - `Publish_writes_when_idle_seconds_drops_significantly`  
    先 `idleSeconds=500`，再 `idleSeconds=10`（其余字段相同）→ 文件内容应更新
  - `Publish_skips_write_when_idle_seconds_increases_only`  
    先 `100`，再 `115`（同 timestamp 逻辑下仅递增）→ 仍跳过（保持现有优化）
- [ ] 运行测试，确认失败
- [ ] **GREEN** — 修改 `StatusPayloadEquals` 或在 `Publish` 入口增加：
  ```csharp
  if (_lastPayload is not null
      && payload.idleSeconds + 5 < _lastPayload.idleSeconds)
  {
      // 用户活动导致空闲下降 — 必须写盘
      WritePayload(payload);
      return;
  }
  ```
  或将 `idleSeconds` 的「显著变化」纳入 equals 的反向条件。
- [ ] 保持 `Publish_skips_write_when_only_idle_seconds_changed` 语义调整为「缓慢递增仍跳过」

#### Task B2：端到端（可选，Integration）

- [ ] 在 `Tests/Integration/` 或 Engine 测试中模拟：写入 status → 模拟 idle 下降 → 读 `LogViewIdleReader` 应反映新值
- [ ] 贴出测试通过输出

**Phase B 验收标准：**

- 引擎运行中，用户活动后下一次 `CheckIntervalSec` 迭代内，status 文件 `idleSeconds` 更新
- 纯空闲递增时，写盘频率不明显上升（对比修改前）

---

### Phase C：陈旧 status 与可观测性

**修改文件：**

- `src/SmartGuard.Settings/LogViewIdleReader.cs`
- `src/SmartGuard.Settings/LogViewStatusTextBuilder.cs`（可选文案）
- `Tests/SmartGuard.Settings.Tests/LogViewIdleReaderTests.cs`

#### Task C1：status 文件过旧时降级

- [ ] **RED** — `TryReadSeconds_uses_api_when_status_timestamp_too_old`
- [ ] **GREEN** — 若 `now - publishedAt > MaxStatusAge`（建议 300s），直接 `IdleDetector`，不走外推
- [ ] 可选：status 存在但超过 90s 未更新，在状态栏追加 `| 状态可能过期`

#### Task C2：文档

- [ ] 更新 `docs/SETTINGS-SUPPORT-PLAN.md` 或 `PHASE-4-TASK-CONTRACT.md` 中日志状态栏一节，说明：
  - 空闲时间来源优先级
  - 2s 刷新与引擎 `CheckIntervalSec` 的关系
  - 引擎未运行时的表现

**Phase C 验收标准：**

- 引擎停止 5 分钟后，日志页空闲时间跟随本地输入，不再无限外推

---

## 五、测试矩阵

| 场景 | 期望 | 阶段 | 测试类型 |
|------|------|------|----------|
| 打开日志页 | 显示「当前空闲 N 秒」 | D1 ✅ | WPF 集成 |
| 2s 定时刷新 | N 递增 | D1 ✅ | WPF 集成 |
| 勾选跟随最新（从上方） | 滚到最后一行 | F1 ✅ | WPF 集成 |
| 在底部 + 跟随 + 新日志 | 保持底部 | F1 ✅ | WPF 集成 |
| 向上滚动 + 新日志 | 位置不变 | 已有 | WPF 集成 |
| status 外推 + 用户活动 | 显示低空闲 | A | 单元 |
| 持续空闲 | 外推递增 | A | 单元 |
| 仅 idle 缓增 | 不写 status | B | 单元 |
| idle 骤降 | 写 status | B | 单元 |
| 引擎停机 >5min | 用 API / 提示过期 | C | 单元 |
| 手动点刷新 | 仍优先 status 逻辑 | D1 ✅ | 已有 |

**必跑命令：**

```powershell
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj
```

完成声明前必须在 PR/回复中贴出全绿输出。

---

## 六、风险与缓解

| 风险 | 缓解 |
|------|------|
| 睡眠后 API 卡住，活动校正误判 | 仅用 `api+5 < extrapolated` 判断活动，不用无脑 `min` |
| Phase B 增加写盘频率 | 仅在 idle **下降**时额外写；上升仍跳过 |
| `IdleDetector` 在 Settings 进程读与 Engine 不一致 | 显示层本就应该反映「当前控制台用户」输入；策略仍以引擎为准 |
| 双进程读 status 文件竞争 | 已有 atomic write（tmp + move）；Reader 已有 try/catch |
| 中文 / 编码 | 测试与源文件 UTF-8 无 BOM；终端不写多行中文 |

---

## 七、提交策略

按阶段提交，遵循 Conventional Commits：

1. `fix(settings): scroll log view to tail when follow-latest enabled` — F1（若尚未提交）
2. `fix(settings): refresh idle seconds on every log view update` — D1
3. `fix(settings): correct idle display when user becomes active` — Phase A
4. `fix(engine): publish status when idle drops after user activity` — Phase B
5. `fix(settings): fall back to local idle when status file is stale` — Phase C
6. `docs: document log page idle time behavior` — 文档

**注意：** 用户规则要求「仅在被明确要求时 commit」；实现阶段由用户决定是否提交。

---

## 八、验收清单（给用户的手动验证）

1. 启动 SmartGuard 引擎，打开设置 → 日志页，确认**无需点刷新**即可看到「当前空闲」。
2. 静置 10 秒，确认空闲秒数大约增加 10。
3. 晃动鼠标，在 2–4 秒内确认空闲降到很低。
4. 滚到顶部，勾选「跟随最新」，确认跳到底部且最后一行可见。
5. 保持跟随，向 `SmartGuard.log` 追加一行，确认自动滚到底。
6. 停止引擎 5 分钟，确认空闲不再按旧 status 无限增长（Phase C 后）。

---

## 九、后续可选（Out of Scope）

- 日志倒序显示开关
- WinForms `LogViewerSession` 状态栏同步显示空闲
- 托盘菜单显示空闲（需产品定义）
- 将 `IdleTracker` 抽到 `SmartGuard.Configuration` 供 Settings / Engine 共用显示策略

---

## 十、当前代码基线（2026-06-26）

| 项 | 状态 |
|----|------|
| F1 跟随最新滚动 | 已实现，203+ 测试通过 |
| D1 每次 RefreshLogView 读空闲 | 已实现，205 测试通过 |
| D2 活动后空闲虚高 | **已实现 Phase A + B** |
| D3 陈旧 status | **已实现 Phase C** |
| 倒序显示 | 不纳入本规划 |

---

*文档版本：2026-06-26 · 与 `AGENTS.md` TDD 流程对齐*
