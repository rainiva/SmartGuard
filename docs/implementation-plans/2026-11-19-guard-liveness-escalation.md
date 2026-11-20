# 守护长时间停工告警 — 设计

日期：2026-11-19　状态：**已出实施计划并实施**，第 6 节决定项已由用户决定（2026-11-20，全部按建议）。实施计划 [2026-11-20-guard-liveness-escalation-plan.md](2026-11-20-guard-liveness-escalation-plan.md)（第 1 版，含相对本设计的 4 处取舍）；用户决定不等外部审查直接实施（2026-11-20）。前置：电源方案 API 第一阶段（已暴露 `PowerSchemeService.IsCallInFlight`/`InFlightSince`/`LastSucceededAt`）。

## 1. 背景

2026-11-19 09:39–14:22，Engine 进程一直存活，但 powercfg 闩锁使 232 次计划读取全部被拒，守护在近 5 小时内没有做任何事，直到手动重启才恢复。期间：

- `status.json` 持续新鲜（Engine 每轮都发布），托盘不会显示“守护未运行”；
- `guardReason=probe-failed` 只在托盘状态文字里显示为“状态探测失败”，与偶发的一次探测失败在外观上没有区别；
- 日志每分钟一条合并告警，用户不会主动查看。

现有健康显示只覆盖两种情况：Engine 不在运行（状态过期，`GuardStatusFreshness`）和日志写入异常（`logHealth`）。**“进程活着但核心能力持续停工”没有升级路径**，违反 AGENTS §1“核心能力静默停工必须可观测”。

迁移消除了这一次的根因，但同类停工仍可能出现：电源 API 调用在原生层挂起（门被持有，后续全部 `call-pending`）、电源服务被策略禁止（持续 `api-error`）、WMI 闩锁关闭（第三阶段修复前）。

## 2. 目标与非目标

**目标：**

1. 定义“守护停工”：Engine 存活、处于可守护状态，但计划读取在一段连续的清醒时间内没有成功过。
2. 停工时升级，用户能在不查日志的情况下察觉：状态文件给出确定的健康码，托盘与设置窗口显示区别于偶发失败的文案，日志记录一次进入停工、一次恢复。
3. 恢复以计划读取再次成功为准，不以时间推断。

**非目标：**

- 不自动重启 Engine（见第 6 节决定项 D3）。
- 不改变探测预算、冷却与合并告警节奏。
- 不为亮度、电池等可选能力定义停工：它们失败时守护仍能切换计划，已有降级文案。
- 不新增配置项；阈值为常量（YAGNI，见 D1）。

## 3. 设计

### 3.1 事实与 Owner

| 事实 | Owner | 说明 |
|------|-------|------|
| 最近一次计划读取成功的单调时间 | `GuardianIterationRunner`（主轮次单线程） | 每轮计划读取成功时更新；不用 `PowerSchemeService.LastSucceededAt`，因为名称读取等成功不能证明计划读取可用 |
| 停工计时起点 | 同上 | 取“最近一次成功”与“最近一次恢复（唤醒、Engine 启动）”中较晚者；睡眠时间不计入 |
| 电源服务在途时长 | `PowerSchemeService.InFlightSince` | 仅用于文案，说明“电源服务调用已挂起 X 分钟”，不单独判定 |
| 守护健康码 | `GuardianIterationRunner` 计算，`StatusPublisher` 发布 | 新字段 `guardHealth` |

### 3.2 判定

每轮计算，O(1)：

- `paused`、`config-unavailable`、`blocked-by-lifecycle` → `not-applicable`（这些情况下不守护属于预期）。
- 否则，若自停工计时起点起的清醒时间 ≥ **阈值 T**（默认 10 分钟，见 D1），且其间计划读取从未成功 → `stalled`。
- 否则 → `healthy`。

清醒时间以注入的 `TimeProvider` 单调时间计算，并在 `PowerResumeObserved` 时重置起点。这样不依赖单调时钟在睡眠期间是否继续计数（PWR-LIFE-TIME）。

### 3.3 发布与展示

- **状态文件：** `StatusPayload.guardHealth`，结构仿照 `LogHealthStatus`：`code`（`healthy`/`stalled`/`not-applicable`）、`stalledSinceUtc`（UTC，仅 `stalled`）、`lastFailureKind`（最近一次计划读取失败分类，如 `call-pending`）、`observedAtUtc`。旧状态缺字段视为未知，Tray/Settings 不据此报警。
- **托盘：** `TrayStatusFormatter` 增加子句；`stalled` 时显示 `| 守护已停工 N 分钟（原因）`，替代“状态探测失败”。是否同时弹出通知见 D2。
- **设置窗口：** `ForegroundGuardStatusText` 同样区分 `stalled`。
- **日志：** 进入 `stalled` 时一条 `Warn`，新事件名 `GuardStalled`，属性包括停工时长、最近失败分类、电源服务在途时长；恢复时一条 `Info` `GuardStallRecovered`，属性为停工总时长。同一次停工期间不重复。

### 3.4 失败路径

| 情形 | 结果 |
|------|------|
| 读取持续失败 | 达到 T 后 `stalled`，之后每轮保持；日志只在进入时记一次 |
| 读取间歇成功 | 每次成功重置计时，不会进入 `stalled` |
| 睡眠跨越阈值 | 唤醒时重置起点，不误报 |
| Engine 重启 | 计时从启动开始，无持久状态 |
| 状态发布失败 | 沿用现有发布失败处理；健康码随下一次成功发布出现 |

### 3.5 成本

每轮常数次比较与一次时间读取；状态文件多一个小对象（约 150 字节）；无新线程、定时器、队列或 IO。

## 4. Change Impact Matrix（摘要）

| 维度 | 影响 |
|------|------|
| Function | 新增停工判定与展示，现有守护行为不变 |
| Data | `StatusPayload` 新增可选字段，旧读者忽略；无迁移 |
| Observability | 新增 2 个事件名，须登记到 `EventNameRegistry` 与日志通道 |
| Compatibility | 新 Tray 读旧状态显示未知；旧 Tray 读新状态忽略新字段 |
| Concurrency & Time | 单线程计算；单调时间；唤醒重置 |

## 5. 契约

- 新增不变量 `GUARD-INV-LIVENESS`：Engine 存活且可守护时，计划读取连续 T 分钟清醒时间未成功，必须以 `guardHealth=stalled` 发布并记录一次 `GuardStalled`；恢复以计划读取成功为准。
- 真源表：“守护健康码 → `GuardianIterationRunner` 计算、`StatusPublisher` 发布、Tray/Settings 经共享 reader 读取”。

## 6. 用户决定（2026-11-20 已决定：全部按建议）

- **D1：** 阈值 T = 10 分钟。
- **D2：** 进入停工时托盘弹一次系统通知，同一次停工只弹一次。
- **D3：** 不自动重启。

| # | 问题 | 选项 | 建议 |
|---|------|------|------|
| D1 | 阈值 T | 5 / 10 / 15 分钟 | 10 分钟：守护轮询间隔为 15 秒，10 分钟约 40 轮全部失败，足以排除偶发；也不会让用户在停工后半小时才知道 |
| D2 | 托盘是否弹出系统通知 | 仅状态文字 / 进入停工时弹一次通知 | 弹一次：这次事故说明仅靠状态文字用户察觉不到。复用现有 Toast 通道，新增 `NotificationKinds.GuardStalled`，同一次停工只弹一次 |
| D3 | 是否自动恢复 | 不自动 / 停工 T 后由 Engine 自行退出、交给计划任务重启 | 本次不自动：自动重启会掩盖根因，且计划任务重启策略属另一 Owner（`ScheduledTaskRegistrar`）。如需要，另立设计 |

## 7. 测试要点（实施计划中展开为 RED/GREEN）

- Runner：
  - 读取持续失败 T−1 分钟仍为 `healthy`，到达 T 时为 `stalled`；
  - 中途成功一次即重置；
  - 暂停或生命周期阻断时为 `not-applicable`；
  - 唤醒重置起点。
  - 以上均用注入的 `TimeProvider` 驱动，不真实等待。
- 日志：同一次停工只记一次 `GuardStalled`，恢复记一次 `GuardStallRecovered`。
- Tray/Settings：`stalled` 文案；旧状态缺字段显示未知而非健康。
- 架构门禁：契约含 `GUARD-INV-LIVENESS`；事件名已登记。

## 8. 实施与验证记录

按实施计划第 1 版执行，用户决定不等外部审查直接实施。分支 `feat/guard-liveness`，证据目录 `.work/guard-liveness/`。

**基线（Task 0）：** Configuration 529 通过、1 未执行（真实写入用例，受写入保护跳过）；Engine 452；Tray 89；Settings 641；Architecture 355。

| Task | 提交 | RED | GREEN |
|------|------|-----|-------|
| 1 状态字段与两处相等判定 | `f5c6d61f` | 4 例中 3 例按行为缺口失败：Publisher 忽略 `guardHealth` 跳过写入、轻路径 Clone 丢字段、托盘相等判定忽略它；Clone 保留用例为保护性 | Engine 454、Tray 91、Settings 641、Architecture 355 |
| 2 判定规则 | `3a2f84d1` | 9 例中 6 例失败（骨架恒为 healthy/None、分类码恒为 other）；`One_success…`、`Resume_restarts…`、`NotAdmitted→other` 为保护性 | Engine 463、Architecture 355 |
| 3 轮次集成、事件名与通知 | `d6a6d7e8` | 6 例全部失败：4 个轮次用例在第 102 行等处 `guardHealth` 为 null（核对堆栈，非夹具问题）；2 个事件未登记 | Configuration 529/1、Engine 469、Architecture 355 |
| 4 托盘与设置文案 | `6f690458` | 与计划所列一致：托盘 5 例失败、设置 1 例失败；旧状态/过期、`(true,true)`、healthy 文案为保护性 | Tray 98、Settings 643、Architecture 355 |
| 5 契约与门禁 | `2334ff56` | 门禁 3 例中契约 1 例失败 | Architecture 358 |

**全量测试：** `Run-Tests.ps1` 退出码 0，summary `run-tests-20261120T102446897Z-d9a9e0254dd24e698c468448dfcb7df2.summary.json`。前后两次 `powercfg /getactivescheme` 均为“高性能”（18:24:45、18:31:53），测试未改动电源设置。

| 层 | 通过 | 失败 | 跳过 |
|----|------|------|------|
| Pester | 158 | 0 | 0 |
| .NET | 2167 | 0 | 0 |
| Tray Integration | 2 | 0 | 0 |
| Installer Integration | 9 | 0 | 0 |

**安装：**
- 安装包 `dist\SmartGuard-Setup-1.6.7-x64.exe`，SHA256 `DDDAD816A97001C06D659F509CBACD847547EBF990438E0652C102DE710D33D8`。版本提升单独提交为 `15c01533`。
- 安装前已备份程序目录与配置。
- 用户确认 UAC 后核对：
  - Engine、Configuration、Settings、Tray、Contracts 五个 DLL 与候选一致，版本 `1.6.7+2334ff56`。
  - 配置哈希 `0426BAA1…B32A` 未变。
  - Engine 自 18:39:09 在交互会话运行。

**真实路径验收：**
- **运行 22 分钟（18:39–19:01）：PASS。**
  - `status.json` 含 `"guardHealth":{"code":"healthy"}`。
  - 共 19 条事件，没有 `GuardStalled`、`NativeProbeFailed` 或 `PowerPlanProbeFailed`。
  - 唯一一条 Warn 是 18:49:15“暂时无法识别当前程序”，与本次改动无关；同类告警在旧版本下 16:37 也出现过。
- **暂停与恢复：PASS。**
  - 19:04:57 暂停后，19:05:05 状态为 `paused=true`、`guardReason=paused`、`guardHealth=not-applicable`。
  - 19:07:48 恢复后，19:07:56 状态为 `paused=false`、`guardHealth=healthy`。
  - 安装后共 25 条事件，没有停工或探测失败事件。
- **托盘与设置窗口文字：未取得用户反馈。** 状态原因为 `waiting-for-listed-program`、`guardHealth=healthy`，按代码应显示“等待指定程序”，不出现“停工”；该路径由 Task 4 用例覆盖。
- **NOT RUN（真机无法安全制造）：** 真实停工需要电源服务调用持续失败 10 分钟。进入/恢复、日志、通知与停工文案由 Task 2–4 用例覆盖。
