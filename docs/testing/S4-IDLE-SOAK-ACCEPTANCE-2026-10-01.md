# S4、默认空闲阈值与有限稳态验收（2026-10-01）

范围：用户选择 S4 休眠恢复，以及默认 300/900 秒空闲阈值后继续观察 60 分钟。承接 [S0 原生接入验收](MODERN-STANDBY-NATIVE-ACCEPTANCE-2026-10-01.md)。首次 S4 暴露在途轮次仍消费旧生命周期许可，本轮增加副作用前准入重验，默认配置不变。

## 准备与自动门禁

规范 `Invoke-PowerLifecycleAcceptance.ps1` 增加 HibernateResume，保留原五项判定并额外核对 Kernel-Power 42 原始 XML 的 TargetState/EffectiveState 均为 5。Windows [SYSTEM_POWER_STATE](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-system_power_state) 的 Hibernate=5 表示 S4，不把 S0/S3 代入；无状态字段 Blocked、明确非 S4 Failed。脚本不自动触发休眠。行为 RED 7/3/0，GREEN 10/0/0；TRX/XML 真证据保存在 `.work/guard-rules/s4-acceptance-red.xml`、`s4-acceptance-green.xml`。RED 为缺 HibernateResume 场景导致参数拒绝及回执缺失，非生产实现失败后制造期望迁就。

完整门禁 `run-tests-20261001T112343186Z-9784062a62d343ac8d67b2f41bec2bc0.summary.json` 于 19:28:32 结束，退出 0：Pester 142/0/0、.NET 1753/0/0、Tray 2/0/0、Installer 9/0/0（通过/失败/跳过），无跳过或未运行层。`s4-gates.result.json` 保留前后快照；最终候选新独立 staging 和已验证 ISCC 打包，无版本改写、无部署到生产安装根。

## 受控空闲与稳态观察条件

平衡计划当前 AC 自动睡眠为 300 秒，直接无人输入会打断 900 秒观察。仅在后续空闲会话中，保存三种计划的 AC 自动睡眠与息屏六项原值，临时设为 0（不自动息屏/睡眠），finally 按原值逐项恢复并由 PowerReadACValueIndex 复核；不改 DC 设置、不缩短 Engine 阈值。此条件用于真实 GetLastInputInfo 空闲观察，不称为原系统超时设置下的无人运行结果。S4 会话独立执行，保持正常系统设置。

`.work/guard-rules/idle-soak-session.ps1` 为有界调查 spike，只记录原始状态和实际 powercfg 计划：每 15 秒一个 JSONL 样本，最多 480 条，不对历史样本反复全量刷新。明确人工开始后观察 300/900 秒，再按实际经过的 60 分钟结束；进程、状态 PID、新鲜度、供电和意外暂停持续检查，结束后健康计数和内存/线程/句柄趋势另行核对。没有记录完整时长、发生中断或恢复失败时不得声明 Passed。所有样本为有限稳态证据，不外推为无限长期、整机节能或未测故障保证。

## 首次 S4：Failed，已恢复环境

Engine PID 35020，19:35:08.2461043 观察暂停，19:36:27.2685331 观察恢复；Kernel-Power 42 原始 TargetState/EffectiveState 均为 5，用户确认实际休眠恢复。19:35:08.3245009 外部变更日志与 19:35:08.9138096 计划切换事件处于首个暂停区间。后续 19:41:38 至 19:41:44 又发生一组暂停/恢复，不能用末次暂停配首次恢复而跳过首区间。

原规范回执 `s4-power-lifecycle.result.json` 为 Failed：lifecycle-events Failed，其余五项 Passed；其中 no-suspend-plan-write Passed 是错误配对造成的漏报，不作为安全证明。原始回执与日志保留。`s4-desktop-session.result.json` ObserverExit=1、Restored=true，原任务及运行态、配置 Hash、电源与亮度恢复已核对。恢复后健康样本同 PID、丢弃/失败计数为 0。

逐区间验收回归 RED 10/3/0、GREEN 13/0/0，证据 `s4-pairing-red.xml`、`s4-pairing-green.xml`。对完整暂停/恢复区间逐一检查，最后未闭合区间 Blocked，任何已证明的区间写入 Failed。

运行时根因是 Runner 在慢探测后仍用轮首 AllowPolicyMutation。计划探测和强制供电探测内注入暂停的行为测试 RED 0/2/0 (`s4-inflight-red.trx`)；改由现有 PowerLifecycleStateMachine 提供 O(1) 当前准入、在外部判断和副作用前重验后，生命周期链 GREEN 12/0/0 (`s4-inflight-green.trx`)，包括亮度同步期间暂停后禁止启动 setactive、状态显示 blocked-by-lifecycle。Owner 锁不跨 IO/await，已启动的系统动作无法回溯撤销，完成日志不能单独证明启动时刻。

修正后的完整门禁 `run-tests-20261001T120046316Z-068d46c169cf4ebbb8835345a8d54b7a.summary.json` 于 20:05:13 结束，退出 0、Passed：Pester 145/0/0、.NET 1757/0/0、Tray 2/0/0、Installer 9/0/0，无跳过或未运行层。`s4-v2-gates.result.json` 保留前后任务快照。新候选独立根 `s4-v2-desktop-root`，基线 Engine PID 4692，不覆盖首次失败材料。

S4 重验结果见下节，60 分钟空闲/稳态尚待记录，不将首次 Failed 或自动测试替代真实复验。

第二次基线期间自动待机：20:10:13 暂停，规范观察器 20:10:18 启动，20:14:44 恢复，窗口只有恢复而缺起始暂停；未提示用户执行 S4。`s4-v2-power-lifecycle.result.json` 正确为 Blocked/退出 2，`s4-v2-desktop-session.result.json` Restored=true。这是调查环境打断，不计为 S4 复验通过或失败。

第三次基线只在采集五分钟健康样本期间请求 ES_CONTINUOUS|ES_SYSTEM_REQUIRED，基线就绪后清除并记录 BaselineWakeReleased=true，再开始规范观察器、提示人工 S4。请求归属调查脚本线程，finally 清除，不改生产电源超时或默认阈值；[SetThreadExecutionState 官方说明](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate) 定义其清除方式，且不能阻止用户手动休眠。新证据使用 `s4-v3-*`，不覆盖前两次记录。

## S4 重验：Passed

用户确认已休眠并恢复；Engine PID 27568，20:28:38.6898362 暂停，20:30:02.3410800 恢复。`s4-v3-system-events.json` 保存 Kernel-Power 42 原始 XML，TargetState/EffectiveState 均为 5。暂停区间无 PowerPlanSwitched；恢复后建立 idle=0 的观测、稳定期后 20:30:09 恢复高性能计划，没有 ExternalPlanChange 纠偏日志。20:30:05.7513176 健康样本与基线同 PID，丢日志、失败批次和队列深度均为 0。

`s4-v3-power-lifecycle.result.json` 于 20:31:06 返回 Passed/退出 0，lifecycle-events、windows-suspend-resume-events、no-suspend-plan-write、hibernate-state、status-freshness、pipeline-health-delta 六项全部 Passed。`s4-v3-desktop-session.result.json` Stage=scenario-passed、Restored=true、Error 为空，20:31:09 完成任务运行态、配置 Hash、电源与亮度恢复。临时唤醒请求在手工操作前已解除（BaselineWakeReleased=true）。本结果只证明本次真实 S4 路径，电池路径及罕见系统故障注入仍未运行。

下一阶段 `.work/guard-rules/idle-soak-v2-session.ps1` 已启动隔离候选 PID 15724，保存六项 AC 超时原值后临时禁用自动息屏/睡眠。用户选择开始静置，20:35:26.7447185 开始会话；初始样本 AC=true、状态与实际高性能计划一致，随后验证真实 300/900 秒空闲阈值，再额外观察 60 分钟。以真实 idleSeconds 判断阈值，不把会话经过时长替代系统空闲时长。聊天跟进 `smartguard-60` 每五分钟检查一次，仅阈值完成、最终完成、失败或用户操作需求时通知，最终汇报后停用。尚未声明阈值或 60 分钟通过。
