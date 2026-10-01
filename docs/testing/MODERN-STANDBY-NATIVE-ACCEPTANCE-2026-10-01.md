# S0 原生通知接入与现场复验（2026-10-01）

承接 [首次现场失败与原生探针](MODERN-STANDBY-ACCEPTANCE-2026-10-01.md)。本次范围为原生暂停/恢复通知接入、健康事件进程标识，以及同机插电合盖复验。电池、S3、休眠、长期能耗和罕见文件替换故障不在本轮通过范围。

## 根因、边界与资源

`PowerEventWakeListener` 仍为通知入口，`PowerLifecycleStateMachine` 仍为生命周期唯一 Owner。原生 PowerRegisterSuspendResumeNotification 使用 DEVICE_NOTIFY_CALLBACK；SystemEvents 只分发 StatusChanged，避免重复暂停/恢复来源。每次暂停清除 Resume 分发标记，18/7 只分发首个恢复；状态机既有恢复稳定期、单调时间和有界转换队列不变。回调仅 O(1) 内存状态与唤醒，使用 listener 注入的 UTC，无 IO、计划操作、轮询、线程或新队列。

每 listener 一个注册句柄、一个强根委托；退出锁等待在途分发，标记 disposed 后回调无副作用，再注销。注册失败撤销 SystemEvents 并提供失败原因；回调异常被包含并标记失效，GuardianLoop 记录一次原因。注销失败抛出错误并保留委托强根，避免 Windows 调用悬空委托；此异常由既有进程失败路径报告，不无限重试。稳定契约登记 PWR-LIFE-NATIVE、LOG-HEALTH-PID。

健康 PID 使用现有 LogEvent.ProcessId 字段，经 `LogStructuredSink.CreatePipelineHealthEvent` 设置当前进程 PID；采样周期保持五分钟，旧日志缺 PID 仍 Blocked。唯一验收脚本还将此前已失败的宽泛 System 日志枚举改为源端 Provider/ID/UTC XPath 查询，避免无关事件解析阻断。

影响矩阵与 Critical Module Check 保存在 `.work/guard-rules/s0-integration-plan.md`：涉及 Function、Architecture、Data/Compatibility、Resource、Concurrency & Time、Reliability/Recovery、Observability 和隔离恢复 Operations；未引入网络、Secret、配置迁移或新权限入口。

## TDD 与自动门禁证据

- `s0-listener-red.trx`：3 通过 / 2 失败，原生 callback 未注册、注册失败未禁用；test seam 本身在 RED 前仅声明、未接入行为。
- `s0-callback-red.trx`：回调注册缺失导致目标失败；`s0-health-red.trx`：实际 PID 为 null；`s0-contract-red.trx`：新稳定契约未登记。
- GREEN：listener 与生命周期链 24/0/0、健康链 6/0/0、架构链 5/0/0；最终完整门禁包含注入 UTC 时间戳断言。
- XPath 行为 RED 为 6 通过 / 1 失败，GREEN 7/0/0；初次过滤器未选择测试的文件保留但不算 RED。
- 第一轮完整门禁 `run-tests-20261001T103719618Z-fe25c83c2c0d483394340f80f32c9831.summary.json` 退出 1：Pester 139/0/0、.NET 1753/0/0、Tray 2/0/0；Installer 0/9/0（BeforeAll 编译器不存在，九个用例未实际执行）。原因是 stale setup 触发 helper 默认 D 盘 ISCC 重建。任务快照已完全恢复。
- 环境修复使用已验证的 `.work/tools/inno/ISCC.exe`、独立 `s0-installer-staging` 和缓存运行时 8.0.18；保留旧 setup 为 `s0-prebuild-setup.exe`，显式 skip-version-bump、skip-publish，重建退出 0。没有改 helper 默认路径或用户版本文件，也没有部署候选到生产安装根。

第二轮完整门禁 `run-tests-20261001T104409510Z-32b1ae0aa0694df7aea3ba6bd0bae4c6.summary.json` 于 18:48:33 结束，退出 0：Pester 139/0/0、.NET 1753/0/0、Tray 2/0/0、Installer 9/0/0，任务快照完全恢复。GREEN 后又覆盖注册返回前的同步回调，初始化先设置 enabled，避免返回后覆盖回调已报告的失效状态；`s0-listener-reentrant-green.trx` 6/0/0。最终候选已重新发布、打包，第三轮完整门禁与现场会话串行进行。

没有实际诱发 Windows 原生注销失败，不将保留强根的源码检查折算为现场故障注入通过。

第三轮最终源码完整门禁 `run-tests-20261001T105009319Z-46ebe2ac1f0d4277904873fcb232a1ab.summary.json` 于 18:54:33 结束，退出 0：Pester 139/0/0、.NET 1753/0/0、Tray 2/0/0、Installer 9/0/0，无跳过层；所有 .NET 项目退出 0，TRX Completed。`s0-native-gates-v3.result.json` 前后任务快照完全相等。安装配置 SHA256 保持 9310CFD90225B1932AAFEA863A0DE150D7F02D7501DBFEE0887F187DD517DA93。第一轮失败保留，没有通过重试覆盖。

## 最终现场结果：本次插电 S0 路径 Passed

隔离候选于 18:54:35 启动，Engine PID 36980、Tray PID 40780；31 个文件的最终 SHA256 清单为 `.work/guard-rules/s0-desktop-root/candidate-manifest-final.json`，现场结束后与仓库最终发布 bin 一致。用户确认插电合盖约一分钟，再开盖移动鼠标唤醒。观察只通过规范入口 `scripts/Invoke-PowerLifecycleAcceptance.ps1`，包装会话仅采样、传递人工完成信号和恢复；没有降低原五分钟健康采样周期。

| 现场事实 | 北京时间 / 结果 |
|---|---|
| 前健康样本 | 18:59:35.9420643，processId=36980，三项丢日志/失败计数均 0 |
| Windows Kernel-Power 506 | 19:00:34.5861620，LidOpenState=false |
| Engine 暂停观察 | 19:00:34.7004532 |
| Engine 恢复观察 | 19:01:49.6441684，仅一条 PowerResumeObserved |
| Windows Kernel-Power 507 | 19:01:50.1057604，SleepEntered=true、PowerStateAc=true，SleepDurationInUs=70385532 |
| 暂停窗口计划写入 | Engine 暂停→恢复窗口内 0 条 PowerPlanSwitched |
| 恢复后策略写入 | 19:01:55.8967534，位于五秒稳定期后 |
| 后健康样本 | 19:05:45.8521406，PID 仍 36980，三项计数仍 0 |
| 规范脚本 | 19:06:38，Passed / 退出 0；生命周期、Windows 事件、暂停无计划写入、状态新鲜度、同进程健康计数五项全部 Passed |
| 恢复结束 | 19:06:40，Restored=true，Error 为空，前后任务定义和原实例运行态完全相等 |

原始证据：`s0-power-lifecycle.result.json`、`s0-power-lifecycle.transcript.txt`、`s0-desktop-session.result.json`、`s0-desktop-system-events.json`（Provider/ID/UTC 时间窗与原始 XML）、`s0-manual-operation.json`、`s0-desktop-root/SmartGuard.log`。所有时间按原始 UTC offset 核对后以北京时间展示。时间窗中另有 19:00:08.8827445 的 507，SleepEntered=false、SleepDurationInUs=0；没有把它当成本次实际待机，Engine 未为该屏幕状态退出生成生命周期日志。

恢复后实际 powercfg 高性能 GUID 为 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c，亮度 30，安装配置 SHA256 不变。候选没有部署到生产安装根。有限采样 42 份，Engine Private Bytes 峰值 23,105,536、Tray 峰值 26,865,664；原性能预算未修改，完整门禁内既有性能用例通过。这些有限样本不作为长期稳定、持续内存预算或整机节能改善证明；前次 Tray 峰值 16,748,544 与本次不同，未做受控 Tray 性能归因。

本轮完成的是单次 AC S0 合盖→开盖移动鼠标路径。电池、S3/休眠、长期运行、真实原生注销失败及罕见 Win32 替换错误仍 NOT RUN；首次缺生命周期事件的 Blocked 记录保留。
