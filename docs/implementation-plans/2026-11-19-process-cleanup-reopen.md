# 子进程清理闩锁可证实重开 — 设计

日期：2026-11-19　状态：设计已确认，待实现计划

## 1. 背景与根因

2026-11-19 09:38:57（本地），Engine（PID 29256，09:34 启动）在 WPS 前台匹配后执行切换。切换前的可选亮度同步 `/setdcvalueindex` 被 AC+DC 共享的 2 秒预算取消，`PowerCfgProcessRunner` 返回 `TerminationConfirmed=false`。`GuardianIterationRunner.ReportProcessResult` 将 `_processExecutionClosed` 永久置位，此后：

- 同轮 `/setactive` 0ms 被拒，切换失败；
- 09:39–14:22 共 232 次 `plan-read` 被拒，未启动任何 powercfg；
- 拒绝结果伪造 `Started=false`，经 `ParseProbeResult`/`MapFailureKind` 显示为 `ProcessStartFailed`，误导排查；
- 亮度同步把运行器给出的 `cleanup-unconfirmed` 改写为 `timed-out`，丢失真实分类；
- 事后系统中已无存活 powercfg，但闩锁从不复核。14:26 通过计划任务重启 Engine 后立即恢复。

被破坏的不变量：设计表“周期继续…规则仍可工作；恢复单独记录”无恢复路径；AGENTS“局部故障默认局部影响”“失败路径必须收敛到确定状态”（收敛到了永久停工）。

疑似触发子条件（代码推断，未复现）：`RunProcessCoreAsync` 在 kill 与 250ms 等待之前快照 `orphanedPipes`，此后不再重算。父进程恰在截止时正常退出、读取任务尚未读到 EOF 时，即使管道 1ms 后关闭也会被判为孤儿。日志不记录哪个确认条件失败，无法事后区分。

横向扫描：`WmiProbeProcess._closed` 是同一模式的永久闩锁，本次一并修复。

## 2. 目标与非目标

目标：

1. 清理未确认后仍禁止再启动子进程（保留“不确定子进程不累积兄弟进程”）。
2. 持有该不确定子进程；仅当它被证实已退出且两路管道均结束时重开，并记录恢复。
3. 闩锁拒绝使用独立失败类型，不再冒充 `ProcessStartFailed`。
4. 清理确认在 250ms 收尾等待之后判定。
5. 诊断记录未确认的具体条件；亮度同步不覆盖真实失败分类。

非目标：不按“可选/核心”拆分 powercfg 闩锁（powercfg 真挂死时核心路径同样受影响）；不引入基于时间的重开；不改变预算数值、冷却、合并告警节奏或状态码 `probe-failed`。

## 3. 设计

### 3.1 `UnconfirmedChild`（SmartGuard.Configuration）

唯一职责：拥有一个终止尚未被证实的子进程。

- `bool TryConfirm()`：非阻塞；每次调用至多发起一次 `Kill(entireProcessTree: true)`（仅在未退出时）；`HasExited` 且两路读取任务均已完成时返回 true 并释放 `Process` 句柄；任何异常视为未证实返回 false。
- `string Evidence`：最近一次判定的未确认原因，固定取值 `kill-failed`、`not-exited`、`pipes-open`、`executor-pending`。
- 来源一（进程）：运行器清理未确认时不再 `Dispose` 该 `Process`，将其与两个读取任务移交。读取任务仍独占 StreamReader 释放责任（不变）。
- 来源二（执行任务）：`WmiProbeProcess` 外层 2.5 秒兜底超时，或取消后 500ms 宽限内执行器未返回时，持有该执行任务；它完成后若结果 `TerminationConfirmed=true` 即证实，若带 `PendingCleanup` 则委托之，若失败/异常则保持未证实。

### 3.2 `PowerCfgCommandResult`

新增只读 init 属性 `UnconfirmedChild? PendingCleanup`，仅在运行器给出 `TerminationConfirmed=false` 时非空；默认 null，现有构造与调用方不受影响。

### 3.3 运行器 `RunProcessCoreAsync`

`confirmed` 在 250ms 收尾等待后判定：`killSucceeded && proc.HasExited && settled`。`settled` 已蕴含两路读取完成；预先快照的 `orphanedPipes` 仅用于 `Evidence`，不再参与确认。未确认时结果携带 `PendingCleanup` 与 `Evidence`。

### 3.4 闩锁持有方

`GuardianIterationRunner`：`bool _processExecutionClosed` 改为 `UnconfirmedChild? _pendingCleanup`，仅由主轮次访问，无锁。每次将要启动 powercfg（计划读取、`/setactive`、亮度同步、目录加载）前：

1. 若 `_pendingCleanup` 非空，调用一次 `TryConfirm()`；true 则清空并发出 `process-cleanup` 恢复观测，继续启动；
2. 仍非空则拒绝：返回 `Started=false`、`FailureKind="cleanup-pending"`、`TerminationConfirmed=true`（未启动新子进程）、无 `PendingCleanup`，不覆盖已持有对象；
3. 新结果带 `PendingCleanup` 时由其接管（闩锁关闭期间不启动新子进程，故任一时刻至多一个）。

亮度同步经现有 `canMutate` 组合闩锁门控，使所有 powercfg 启动走同一判定。

`WmiProbeProcess`：`_closed` 改为持有 `UnconfirmedChild`。`Run` 在锁内取得引用并以现有 `_busy` 声明单飞，在锁外调用 `TryConfirm()`，再于锁内重开或拒绝，遵守“Owner 锁不跨进程操作”。

### 3.5 失败分类映射

`ParseProbeResult`、`MapFailureKind` 在 `!Started` 判断之前识别 `cleanup-pending`，映射为新增 `PowerPlanFailureKind.ProcessCleanupPending`。

## 4. 可观测性

沿用 `NativeProbeFailed`/`NativeProbeRecovered`，不新增事件名。

- 新增属性 `cleanupEvidence`（取值同 3.1，确认时为 `confirmed`）；`WmiProbeObservation` 增加对应字段。
- 未确认文案：`清理未确认（{evidence}），暂停启动新的探测子进程，确认其退出后自动恢复`。
- `cleanup-pending` 文案：`等待上次子进程清理确认，本次未启动`。
- 重开恢复事件：固定新操作名 `process-cleanup`，标签“子进程清理确认”，`recoveryScope=process-cleanup`，`elapsedMs` 为闩锁关闭时长。
- 亮度同步：清理未确认时保留 `cleanup-unconfirmed`；仅清理已确认时将预算取消记为 `timed-out`。
- 持续拒绝沿用每分钟合并告警与 `guardReason=probe-failed`。

## 5. 失败路径

| 情形 | 结果 |
|------|------|
| 子进程始终未证实退出 | 闩锁保持关闭，持续合并告警并带 evidence |
| `TryConfirm` 异常或 kill 拒绝访问 | 视为未证实，不重开 |
| 执行器抛异常、无结果 | 无证明，保持关闭（与现状一致） |
| Engine 退出 | 句柄由 OS 回收，无持久状态 |
| WMI 并发调用 | `_busy` 单飞，拒绝方不确认、不启动 |

## 6. Change Impact Matrix

| 影响域 | 结论 |
|--------|------|
| Function | 清理被证实后自动恢复计划读取/切换与 WMI 探测；失败类型与文案修正 |
| Architecture | 新增 `UnconfirmedChild`（进程所有权转移的唯一载体）；入口与 Owner 不变 |
| Data | 无持久化变化；日志新增属性与取值，无迁移 |
| Performance | 每次启动前一次 O(1) 检查，未退出时至多一次 kill；无新增轮询 |
| Resource | 每持有方至多 1 个进程句柄，全局至多 2 个；无新线程/Timer/队列 |
| Concurrency & Time | Runner 单线程；WMI 锁外确认、`_busy` 单飞；不依赖时间推断 |
| Reliability | 消除“一次误判永久停工”；真实挂死仍安全关闭 |
| Recovery | 证实即重开，恢复单独记录；重启 Engine 仍可清空 |
| Compatibility | 新属性默认 null；旧日志读者忽略新增属性 |
| Security | 无权限/提升变化；不记录路径或原始异常文本 |
| Observability | `cleanupEvidence`、`cleanup-pending`、`process-cleanup` 恢复事件 |
| Operations | N/A（安装、升级、回滚流程不变） |

## 7. Critical Module Check

命中模块：后台任务、进程与计划任务（L0）。Owner：`PowerCfgProcessRunner`（执行与清理证明）、`GuardianIterationRunner` 与 `WmiProbeProcess`（启动资格）。

保持的不变量：

1. 清理未证实的子进程存续期间，同一持有方不启动新子进程。
2. 重开只依据同一子进程“已退出且两路管道结束”的证据，不以时间或其他操作成功推断（登记为 `PWR-INV-CLEANUP-REOPEN`）。
3. 每持有方至多持有一个未证实子进程，资源有界。
4. 外部进程失败分类不被覆盖或伪装。

## 8. 契约同步

- `ARCHITECTURE-CONTRACT.md` PWR-NATIVE-PROBE-ISOLATION：“清理未确认后关闭后续启动”补为“……直至持有的同一子进程被证实退出且两路管道结束后重开；不以时间或其他操作成功推断”，登记 `PWR-INV-CLEANUP-REOPEN`。
- 架构门禁：契约含该不变量；闩锁持有方不得以裸布尔闩锁替代 `UnconfirmedChild`。

## 9. 测试（先 RED）

Configuration.Tests（真实子进程）：

1. 父进程派生占管道后代后退出；测试取消的同时写信号文件使后代约 20ms 内退出，期望 `TerminationConfirmed=true`（当前判为孤儿，RED）。
2. 扩展后代占管道用例：未确认结果带 `PendingCleanup`；后代存活时 `TryConfirm()` 为 false；终止后代后 2 秒有界轮询内为 true，句柄已释放。

Engine.Tests：

3. 闩锁拒绝映射为 `ProcessCleanupPending`，无启动；保留现有 `calls == 1`。
4. 以可手动完成的执行任务作为未证实子进程：第 2 轮拒绝；完成后第 3 轮启动并发出 `process-cleanup` 恢复事件。
5. 亮度同步清理未确认时保留 `cleanup-unconfirmed` 与 evidence。
6. `WmiProbeProcess`：永不证实保持关闭；证实后重开；并发确认单飞。
7. 日志格式：`cleanupEvidence`、`cleanup-pending` 与 `process-cleanup` 文案。
8. 架构门禁（第 8 节）。

## 10. 完成验证

- 目标测试后运行全量 `Run-Tests.ps1`，核对退出码与 `.work/test-results/` summary JSON。
- 真实路径：Release 构建并安装（UAC 由用户确认），验证 WPS 前台切换与后台观察期正常。自然触发后的自动恢复难以现场复现，标记 `NOT RUN`，以第 9 节第 1、2 项真实进程故障注入为证据。
- 性能：仅 O(1) 检查，记录说明，不做基线对比。
