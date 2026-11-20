# 子进程清理闩锁可证实重开 — 设计

日期：2026-11-19　状态：**设计第 3 版（范围收缩为仅 WMI 子进程，即电源 API 迁移第三阶段），待外部审查与用户批准后另出实施计划。** 第 2 版正文保留在“第 2 版设计（历史）”之后，凡与第 3 版冲突处以第 3 版为准。

## 第 3 版：范围收缩到 WMI 子进程（2026-11-19）

### 3v.1 迁移后的事实

| 第 2 版中的对象 | 迁移后现状 | 第 3 版处理 |
|-----------------|------------|-------------|
| `GuardianIterationRunner._processExecutionClosed`（powercfg 闩锁，事故主因） | 第一阶段 `64d42228` 已删除；计划读取、切换、亮度同步、目录均经 `PowerSchemeService`，无子进程 | 移出范围 |
| Guardian 目录加载的 powercfg 路径与预算改写 | 第一阶段 `e52d1e8f` 已改走电源 API | 移出范围 |
| `PowerPlanFailureKind.ProcessCleanupPending` 及 `ParseProbeResult`/`MapFailureKind` 映射 | 解析器已删除（`e37ee958`） | 移出范围 |
| `GuardianFirstRunInitializer` 同步 powercfg 调用 | 已改走电源 API（`b58c6147`、`8311b1e5`） | 移出范围 |
| Settings 目录 powercfg 调用（第 2 版“范围外缺口”） | 第二阶段计划删除 | 移出范围，缺口随之消失 |
| `PowerCfgProcessRunner.RunProcessCoreAsync` 的 `orphanedPipes` 预先快照（疑似误判子条件） | **仍存在**（`PowerCfgProcessRunner.cs:138`），WMI 探测经同一运行器 | **保留**：第 2 版 3.1–3.3 |
| `WmiProbeProcess._closed` 永久闩锁 | **仍存在**；一次清理未确认后，本 Engine 生命周期内亮度读取、亮度写入、电池补充探测全部被拒，亮度读数恒为 -1，切换后的亮度恢复因此停止 | **保留**：第 2 版 3.4–3.5 的 WMI 部分 |

### 3v.2 第 3 版目标

1. WMI 子进程清理未确认后，`WmiProbeProcess` 仍不启动新的 WMI 子进程（保留“不确定子进程不累积兄弟进程”）。
2. 持有该未证实子进程；仅当它被证实已退出、且两路管道读到 EOF 时重开，并以 `process-cleanup` 单独记录恢复。
3. 运行器的清理判定改为第 2 版 3.1–3.3：以 EOF 为证据，在 250ms 收尾等待之后判定，删除 `orphanedPipes` 预先快照，收尾期间不取消读取。
4. 日志区分本次执行（`cleanupEvidence`）与持有对象（`pendingCleanupEvidence`）。

### 3v.3 保留、删减与改动

- **保留：** 第 2 版 3.1（证明定义）、3.2（读取任务独立令牌、超限后丢弃读到 EOF）、3.3（判定与移交、`retainUnconfirmed`、`AfterTerminationRequestForTests` 钩子）、3.4（`UnconfirmedChild`，去掉只为 Guardian 服务的部分）、3.5 中 `WmiProbeProcess` 一段、第 4 节观测（拒绝文案改为“上次 WMI 子进程清理尚未确认……”）、第 5 节失败路径中与 WMI 相关的行、第 6 节成本中 WMI 一行。
- **删减：**
  - 3.5 中 `GuardianIterationRunner` 一段整体删除。
  - 3.6 表只剩两行：`WmiProbeProcess`（保留，持有方 `_pending`），以及运行器的其他调用方。后者在第二阶段后应为空，以全仓 grep 核实，非空则登记为“立即释放”。
  - 3.7 的映射删除。
  - 全局保留对象上限由“≤2 个句柄、≤4 个读取任务”改为“≤1 个句柄、≤2 个读取任务”。
- **改动：**
  - `UnconfirmedChild.FromExecution`（WMI 执行器未在 2.5 秒兜底内返回）保留。
  - `WmiProbeProcess` 的业务取消路径现有 500ms 证书宽限保留：宽限内拿到 `PendingCleanup` 则移交，否则 `FromExecution`。
- **不变：** 预算（2 秒 + 500ms 兜底）、30 秒失败冷却、60 秒合并摘要、三项固定操作名、`_busy` 单飞、Owner 锁不跨进程操作。

### 3v.4 不变量与契约

- 登记 `PWR-INV-CLEANUP-REOPEN`，限定为 WMI 子进程：重开只依据同一子进程“已退出且两路管道读到 EOF”的证据，不以时间、读取取消或其他操作的成功推断；只有 `WmiProbeProcess` 可以保留未证实子进程，且至多一个。
- 架构门禁：`WmiProbeProcess` 中不得出现裸布尔闩锁 `_closed`；`retainUnconfirmed: true` 只允许出现在 `WmiProbeProcess.cs`。

### 3v.5 测试（RED 必须因行为缺口失败）

第 2 版第 10 节中，第 1–5 项（Configuration，真实子进程）、第 9 项（`WmiProbeProcess`）、第 10 项（日志与门禁）保留；第 6–8 项（Guardian powercfg 映射、重开、亮度同步与目录改写）删除。

另增一项 Engine 用例：WMI 闩锁关闭后，亮度读取被拒并返回 -1；证明完成后的下一次读取重新启动子进程，并恢复真实读数。这一项锁定本版要修的用户可见后果。

### 3v.6 成本与验收

- **成本：** 闩锁关闭期间，每次 WMI 探测前调用一次 `TryConfirm`（常数成本，不枚举进程）；保留对象 ≤1 句柄、≤2 读取任务。确认路径的 P50/P95/最大值以第 2 版第 10 节第 5 项实测为准。
- **真实路径：** 真实触发清理未确认需要一个故意持有管道的后代进程，只能在自动化夹具中复现，真机验收标 `NOT RUN`。安装后只验收正常路径不回退：亮度读数正常，切换后亮度恢复正常，无 WMI 告警。

### 3v.7 实施计划约束（2026-11-19 外部审查）

新实施计划必须满足以下约束。

1. **基线：**
   - 旧计划 [2026-11-19-process-cleanup-reopen-plan.md](2026-11-19-process-cleanup-reopen-plan.md) 已标为被替代，不得执行或仅改标题复用。
   - 新计划在第二阶段完成后编写，以当时代码为基线。第二阶段会删除 `PowerCfgProcessRunner` 的 powercfg 包装，使 `RunProcess*` 只剩 WMI 调用方。
2. **日志增量扩展，不回退第一阶段：**
   - `WmiProbeObservation` 现有 9 个参数保持原位，第 9 个为 `int? Win32Error`。新增的 `CleanupEvidence`、`PendingCleanupEvidence` 只能追加在其后。
   - `GuardianLoop.CreateNativeProbeEvent` 保留现有电源 API 分支（`schemeApi` 文案）与 `win32Error` 属性，只在非电源 API 分支中增量加入清理证据文案与两个属性。
   - `NativeProbeLoggingTests` 中 `Scheme_api_wait_failures_do_not_mention_child_processes`、`Scheme_api_error_reports_its_win32_code` 必须保持通过，作为回归门。
3. **拒绝事实独立记录：**
   - 现状：`WmiProbeProcess.Run` 在 `_closed` 时只对**本操作已有失败记录**输出合并摘要；没有失败记录的操作（如亮度失败关闭闩锁后的首次电池请求）被静默拒绝；摘要报告的也是旧执行的事实。
   - 新计划为“本次未启动、上次子进程清理未确认”单独产生 `cleanup-pending` 观测：
     - `cleanupEvidence=not-started`，`pendingCleanupEvidence` 为持有对象的当前证据；
     - 每个操作名首次被拒立即记录，之后按现有 60 秒节奏合并；
     - 该观测**不得写入或延长**该操作的 30 秒失败冷却状态（`FailureState.FailedAt`），重开后按原冷却规则继续。
   - 对应测试：亮度失败关闭闩锁后，首次电池请求产生一条 `cleanup-pending`；重复请求在 60 秒内只合并计数；重开后电池请求不受该观测影响。
4. **确认成本覆盖两条路径：**
   - 第 2 版第 10 节第 5 项只测已退出父进程。新计划分别测量“已退出、仅检查读取状态”与“未退出、执行一次 `Process.Kill()`”两条路径，各连续 200 次，记录 P50/P95/最大值。
   - 两条路径单次最大值都须低于 50ms，否则不得声称确认路径为常数低成本。

### 3v.8 实施与验证记录

（Task 7 填写。）

---

## 第 2 版设计（历史）

## 1. 背景与根因

2026-11-19 09:38:57（本地），Engine（PID 29256，09:34 启动）在 WPS 前台匹配后执行切换。切换前的可选亮度同步 `/setdcvalueindex` 被 AC+DC 共享的 2 秒预算取消，`PowerCfgProcessRunner` 返回 `TerminationConfirmed=false`。`GuardianIterationRunner.ReportProcessResult` 将 `_processExecutionClosed` 永久置位，此后：

- 同轮 `/setactive` 0ms 被拒，切换失败；
- 09:39–14:22 共 232 次 `plan-read` 被拒，未启动任何 powercfg；
- 拒绝结果伪造 `Started=false`，经 `ParseProbeResult`/`MapFailureKind` 显示为 `ProcessStartFailed`，误导排查；
- 亮度同步把运行器给出的 `cleanup-unconfirmed` 改写为 `timed-out`，丢失真实分类；
- 事后系统中已无存活 powercfg，但闩锁从不复核。14:26 通过计划任务重启 Engine 后立即恢复。

被破坏的不变量：设计表“周期继续…规则仍可工作；恢复单独记录”无恢复路径；AGENTS“局部故障默认局部影响”“失败路径必须收敛到确定状态”（收敛到了永久停工）。

疑似触发子条件（代码推断，未复现）：`RunProcessCoreAsync` 在 kill 与 250ms 等待之前快照 `orphanedPipes`，此后不再重算。父进程恰在截止时正常退出、读取任务尚未读到 EOF 时，即使管道随后关闭也会被判为孤儿。日志不记录哪个确认条件失败，无法事后区分。

横向扫描：`WmiProbeProcess._closed` 是同一模式的永久闩锁，本次一并修复。目录加载路径（`LoadPlanCatalogAsync`）同样把预算取消无条件改写为 `timed-out`，一并修正。

## 2. 目标与非目标

目标：

1. 清理未确认后仍禁止持有方再启动子进程（保留“不确定子进程不累积兄弟进程”）。
2. 持有该不确定子进程；仅当它被证实已退出、且两路重定向管道都**读到 EOF** 时重开，并单独记录恢复。
3. 闩锁拒绝使用独立失败类型，不再冒充 `ProcessStartFailed`；拒绝事件区分“本次执行”与“上次未证实子进程”两个对象的事实。
4. 清理判定以 EOF 为证据，并在 250ms 收尾等待之后进行；收尾期间不取消读取。
5. 诊断记录未确认的具体条件；亮度同步与目录加载都不覆盖真实失败分类。

非目标：

- 不按“可选/核心”拆分 powercfg 闩锁（powercfg 真挂死时核心路径同样受影响）。
- 不引入基于时间的重开。
- 不改变预算数值、冷却、合并告警节奏或状态码 `probe-failed`。
- 不为无闩锁的调用方（见 3.6）新增闩锁；它们保持现有行为，既有缺口登记为范围外。
- 不追踪未继承重定向管道的孙进程（无 Job Object，无法可靠枚举）；证明范围见 3.1。

## 3. 设计

### 3.1 清理证明的定义

“清理已证实”= 同一子进程 `HasExited` **且** stdout、stderr 两个读取任务都以 EOF（读到 0 字节）正常结束。

以下状态**不是**证明：读取任务被取消、读取异常、因输出超限提前返回、尚未结束。读取任务处于取消或异常状态时，该对象永久不可证实（`reader-failed`），闩锁保持关闭。

证明范围：父进程本身，以及所有继承了这两路管道写端的后代。未继承管道的后代不在可证明范围内；运行器在失败时已做一次 `Kill(entireProcessTree: true)` 尽力终止。

### 3.2 读取任务（`PowerCfgProcessRunner.ReadBoundedAsync`）

- 返回 `PipeReadOutcome(string Text, bool ReachedEof)`。
- 超过 65536 字符时：发出 `output-limit` 失败信号，之后**继续读取并丢弃**直到 EOF（固定 2048 字符缓冲，内存有界），使 EOF 仍可观测。
- 读取任务使用独立的 `readerCts`，不再挂在截止/业务取消令牌上。截止与业务取消只结束“等待业务结果”，不中断读取。
- 判定结束后：若清理已证实，读取任务已自然结束；若未证实且调用方不保留，取消 `readerCts`（与现状一致）；若调用方保留，读取任务继续运行到 EOF，由 `UnconfirmedChild` 拥有。

### 3.3 运行器判定与移交（`RunProcessCoreAsync`）

1. 业务等待结束（成功、失败信号、截止或取消）后，失败路径执行一次 `TryKill`（`Kill(entireProcessTree: true)`，与现状相同）。
2. 等待 `exit + 两路读取` 至多 250ms，**期间不取消读取**。
3. 判定 evidence：`!killSucceeded → kill-failed`；`!HasExited → not-exited`；任一读取取消/异常 → `reader-failed`；两路均 `ReachedEof` → `confirmed`；否则 `pipes-open`。删除预先快照的 `orphanedPipes`。
4. `confirmed` 时返回原失败分类、`TerminationConfirmed=true`。
5. 未确认时返回 `FailureKind="cleanup-unconfirmed"`、`TerminationConfirmed=false`：
   - 调用方传入 `retainUnconfirmed: true` → 不释放 `Process`、不取消读取，结果携带 `PendingCleanup = UnconfirmedChild.FromProcess(...)`。
   - 否则 → 取消读取、释放 `Process`，`PendingCleanup=null`（与现状一致）。
6. 新增内部测试钩子 `AfterTerminationRequestForTests`（kill 之后、收尾等待之前调用），用于固定竞态测试时序。

### 3.4 `UnconfirmedChild`（SmartGuard.Configuration）

唯一职责：持有一个终止尚未被证实的子进程，并提供非阻塞的证明。

- `bool TryConfirm()`：
  - 子进程未退出：调用一次 `Process.Kill()`（**非进程树**，对已持有句柄执行一次 `TerminateProcess`，常数成本，不枚举进程），返回 false，evidence 为 `not-exited` 或 `kill-failed`。
  - 已退出：仅检查两个读取任务的状态与 `ReachedEof` 标志（`HasExited` 为一次 `GetExitCodeProcess`）；满足 3.1 则释放 `Process` 与 `readerCts`，返回 true。
  - 不等待、不枚举进程；任何异常视为未证实。
- `string Evidence`：`kill-failed`、`not-exited`、`pipes-open`、`reader-failed`、`executor-pending`、`confirmed`。
- 来源：`FromProcess`（运行器移交）、`FromExecution`（WMI 执行器未在兜底期限内返回时，持有该执行任务；完成后按其结果的 `TerminationConfirmed` / `PendingCleanup` 判断，故障则不可证实）、`Unprovable`（无证明材料）、`ForTests`（内部测试替身，注入可阻塞的证明函数）。
- 非线程安全：由持有方串行调用。

### 3.5 闩锁持有方

`GuardianIterationRunner`：`bool _processExecutionClosed` 改为 `UnconfirmedChild? _pendingCleanup`，仅由主轮次访问，无锁。目录加载、计划读取、亮度同步、`/setactive` 启动 powercfg 前统一调用 `TryReopenProcessExecution()`：

1. `_pendingCleanup` 非空时调用一次 `TryConfirm()`；true 则清空并发出 `process-cleanup` 恢复观测；
2. 仍非空则拒绝：结果 `Started=false`、`FailureKind="cleanup-pending"`、`TerminationConfirmed=true`（本次未启动，未遗留子进程），不覆盖已持有对象；
3. 新结果 `TerminationConfirmed=false` 时由 `PendingCleanup` 接管（无则 `Unprovable`）。闩锁关闭期间不启动新子进程，任一时刻至多持有一个。

Guardian 的 powercfg 调用（`PowerCfgExecutor.RunPowerCfgAsync`、`SyncPlanBrightnessAsync`、经 `TryLoadOnceAsync` 的目录加载）传入 `retainUnconfirmed: true`。

`WmiProbeProcess`：`_closed` 改为持有 `UnconfirmedChild`；默认执行器传入 `retainUnconfirmed: true`。`Run` 在锁内取得引用并以现有 `_busy` 声明单飞，在锁外调用 `TryConfirm()`，再于锁内重开，遵守“Owner 锁不跨进程操作”。确认进行中的其他调用立即被拒绝，不排队、不重复确认。

### 3.6 运行器调用方与清理责任

| 调用方 | 进程 | 保留未证实子进程 | 清理责任 | 说明 |
|--------|------|------------------|----------|------|
| `GuardianIterationRunner`（计划读取、`/setactive`、亮度同步、目录） | Engine | 是 | `_pendingCleanup` | 本次修复 |
| `WmiProbeProcess` | Engine | 是 | `_pending` | 本次修复 |
| `PowerPlanCatalogProvider.TryLoad` / `LoadWithRetry` / `LoadWithRetryAsync`（Settings） | Settings | 否 | 运行器立即释放 | 行为不变；范围外缺口见下 |
| `GuardianFirstRunInitializer`（`IsBrightnessSupported`、`DisableAdaptiveBrightness`、`SyncPlanBrightness`） | Engine | 否 | 运行器立即释放 | 每次安装仅首次运行，至多 13 次同步调用 |
| `PowerCfgExecutor` 同步包装（`ProbeCurrentPlan`、`SetActivePlanResult`） | — | 否 | 运行器立即释放 | 当前无生产调用方 |

保留对象只存在于上述两个持有方，全局至多 2 个进程句柄、4 个读取任务。

**范围外缺口（只登记，不在本次修改）：** Settings 目录加载在单次调用内遇到清理未确认后仍会继续补全隐藏方案并最多重试 3 次（每次调用至多约 7 条命令 × 4 次尝试），跨调用由用户操作触发。本次改动不改变其行为、不增加其资源占用；如需修复，另开任务为 Settings 设计持有方。

### 3.7 失败分类映射

`ParseProbeResult`、`MapFailureKind` 在 `!Started` 判断之前识别 `cleanup-pending`，映射为新增 `PowerPlanFailureKind.ProcessCleanupPending`。亮度同步与目录加载仅在 `TerminationConfirmed=true` 时才把预算取消记为 `timed-out`，否则保留 `cleanup-unconfirmed`。

## 4. 可观测性

沿用 `NativeProbeFailed`/`NativeProbeRecovered`，不新增事件名。两个属性分别描述两个对象：

- `cleanupEvidence`：**本次执行**的清理状态；拒绝时为 `not-started`。
- `pendingCleanupEvidence`：**闩锁持有的上次未证实子进程**的当前状态；闩锁打开时省略。

文案：

- 清理未确认：`清理未确认（{cleanupEvidence}），暂停启动新的探测子进程，确认其退出后自动恢复`。
- 闩锁拒绝：`上次子进程清理尚未确认（{pendingCleanupEvidence}），本次未启动`。
- 重开恢复：操作名 `process-cleanup`，标签“子进程清理确认”，`recoveryScope=process-cleanup`，`elapsedMs` 为闩锁关闭时长。
- 持续拒绝沿用每分钟合并告警与 `guardReason=probe-failed`。

## 5. 失败路径

| 情形 | 结果 |
|------|------|
| 子进程或继承管道的后代始终存活 | 闩锁保持关闭，持续合并告警并带 evidence |
| 读取任务被取消或异常 | `reader-failed`，永久不可证实，闩锁保持关闭 |
| 输出超限 | 继续丢弃读取直到 EOF；EOF 前为 `pipes-open` |
| `TryConfirm` 异常或终止被拒 | 视为未证实，不重开 |
| 执行器抛异常、无结果 | 无证明，保持关闭（与现状一致） |
| Engine 退出 | 句柄与读取线程随进程回收，无持久状态 |
| WMI 并发调用 | `_busy` 单飞，确认中的其他调用立即拒绝 |

## 6. 成本模型

| 路径 | 频率 | 单次成本 | 上限 |
|------|------|----------|------|
| `TryConfirm`（已退出） | 闩锁关闭时每次启动尝试一次（Guardian 每轮≤4 处，WMI 每次探测一次） | `GetExitCodeProcess` + 两个任务状态读取 | 常数，不枚举进程 |
| `TryConfirm`（未退出） | 同上 | 一次 `TerminateProcess` | 常数，不枚举进程 |
| 运行器失败路径 `Kill(entireProcessTree)` | 每次失败执行一次（与现状相同） | 进程枚举 O(系统进程数) | 不新增调用 |
| 保留读取任务的丢弃读取 | 闩锁关闭期间 | 受写端速率限制；2048 字符固定缓冲 | 全局至多 4 个读取任务 |

WMI 确认发生在执行预算之前，单次调用总耗时 = 确认成本 + 原 2.5 秒兜底。确认路径成本由实测验证（第 10 节），不以复杂度声明豁免。

## 7. Change Impact Matrix

| 影响域 | 结论 |
|--------|------|
| Function | 清理被证实后自动恢复计划读取/切换与 WMI 探测；失败类型与文案修正 |
| Architecture | 新增 `UnconfirmedChild`（未证实子进程的唯一所有权载体）；运行器新增 `retainUnconfirmed` 选择权；入口与 Owner 不变 |
| Data | 无持久化变化；日志新增两个属性与取值，无迁移 |
| Performance | 见第 6 节；确认路径无进程枚举，需实测 |
| Resource | 保留对象全局≤2 个句柄、≤4 个读取任务；无新 Timer/队列；非保留调用方不变 |
| Concurrency & Time | Runner 单线程；WMI 锁外确认、`_busy` 单飞；读取改用独立令牌；不依赖时间推断 |
| Reliability | 消除“一次误判永久停工”；真实挂死仍安全关闭；EOF 才算证明 |
| Recovery | 证实即重开，恢复单独记录；重启 Engine 仍可清空 |
| Compatibility | 新属性默认 null；`retainUnconfirmed` 默认 false，未改调用方行为不变 |
| Security | 无权限/提升变化；不记录路径或原始异常文本 |
| Observability | `cleanupEvidence`、`pendingCleanupEvidence`、`cleanup-pending`、`process-cleanup` |
| Operations | N/A（安装、升级、回滚流程不变） |

## 8. Critical Module Check

命中模块：后台任务、进程与计划任务（L0）。Owner：`PowerCfgProcessRunner`（执行与清理证明）、`GuardianIterationRunner` 与 `WmiProbeProcess`（启动资格）。

保持的不变量：

1. 清理未证实的子进程存续期间，同一持有方不启动新子进程。
2. 重开只依据同一子进程“已退出且两路管道读到 EOF”的证据，不以时间、读取取消或其他操作成功推断（登记为 `PWR-INV-CLEANUP-REOPEN`）。
3. 只有登记的持有方可以保留未证实子进程；每个持有方至多一个。
4. 外部进程失败分类不被覆盖或伪装；拒绝事件不把上次子进程的状态记成已确认。

## 9. 契约同步

- `ARCHITECTURE-CONTRACT.md` PWR-NATIVE-PROBE-ISOLATION：登记 `PWR-INV-CLEANUP-REOPEN`（证明定义、持有方、拒绝分类、两个日志属性）。
- 架构门禁：契约含该不变量；两处闩锁不得为裸布尔；`retainUnconfirmed: true` 只允许出现在登记的持有方源码中。

## 10. 测试与验证（先 RED，且 RED 必须因行为缺口而失败）

先以不改变行为的脚手架引入新类型、参数与测试钩子，并跑现有测试证明无行为变化；之后每个 RED 都能编译运行，因目标行为缺失而失败。

Configuration.Tests（真实子进程）：

1. 竞态：父进程派生后代后正常退出，后代就绪握手后等待信号；测试取消运行器，在 `AfterTerminationRequestForTests` 钩子中释放后代。期望 `TerminationConfirmed=true`。钩子保证取消发生时管道仍被占用。
2. 保留移交：后代持有管道时 `PendingCleanup.TryConfirm()` 为 false、evidence `pipes-open`；结束后代后有界轮询内为 true。
3. 非保留调用方：同一夹具下 `PendingCleanup` 为 null（行为不变）。
4. `UnconfirmedChild` 证明规则：读取取消/异常 → `reader-failed`；读取正常结束但未到 EOF → `pipes-open`；执行任务证明与委托；故障执行不可证实。
5. 确认成本：对真实已持有子进程连续调用 `TryConfirm` 200 次，记录 P50/P95/最大值，单次最大值低于 50ms。

Engine.Tests：

6. 拒绝映射为 `ProcessCleanupPending`，无启动；保留现有 `calls == 1`。
7. 重开流程：第 2 轮拒绝（`cleanupEvidence=not-started`，`pendingCleanupEvidence` 为持有对象的真实值）；证明完成后第 3 轮启动并发出 `process-cleanup` 恢复。
8. 亮度同步与目录加载：预算已取消且运行器返回清理未确认时，保留 `cleanup-unconfirmed` 与 evidence。
9. `WmiProbeProcess`：不可证实保持关闭；证明后重开；用事件屏障让确认阻塞，验证并发调用立即被拒且证明只执行一次。
10. 日志格式与架构门禁。

完成验证：

- 目标测试后运行全量 `Run-Tests.ps1`，核对退出码与 `.work/test-results/` summary JSON。
- 真实路径：Release 构建并安装（UAC 由用户确认），验证 WPS 前台切换与后台观察期正常。自然触发后的自动恢复难以现场复现，标记 `NOT RUN`，以第 1、2 项真实进程故障注入为证据。
- 性能：记录第 5 项实测分布；确认路径无基线可比（新增路径），以实测上限作为预算。

## 11. 外部审查处理记录（2026-11-19）

| 审查项 | 核实结果 | 处理 |
|--------|----------|------|
| P1 读取结束≠EOF | 成立：`ReadBoundedAsync` 可因取消、异常、超限结束；现有代码在收尾等待前已取消读取 | 3.1–3.3：以 EOF 为唯一证据，收尾期间不取消读取，超限后丢弃读取至 EOF |
| P1 确认成本非 O(1) | 成立：`Kill(entireProcessTree)` 枚举进程 | 3.4、第 6 节：确认路径只用非树 `Kill()` 与状态查询；实测验证 |
| P1 共享运行器所有权 | 成立：Settings 与首次初始化也走该运行器 | 3.3、3.6：改为调用方显式选择保留；未登记调用方行为不变；Settings 重试缺口登记范围外 |
| P1 验证不足 | 成立 | 第 10 节：测试钩子与握手固定时序；补并发单飞屏障测试；脚手架先行，RED 不依赖编译错误 |
| P2 拒绝证据矛盾 | 成立 | 第 4 节：拆分 `cleanupEvidence` 与 `pendingCleanupEvidence` |
| P2 目录分类覆盖 | 成立：`NativeProbes.cs:128-129` 无条件改写 | 3.7：目录与亮度同步同一规则，并补测试 |
