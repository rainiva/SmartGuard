# 电池合盖与鼠标唤醒验收（2026-10-01）

结论：本次电池供电下的合盖、开盖与鼠标唤醒路径 Passed。仅证明本次生命周期、状态和日志恢复，不证明待机耗电或深度低功耗驻留。后续整夜运行与候选部署尚未执行。

## 范围与基线

复用 [S4 与空闲长运行验收](S4-IDLE-SOAK-ACCEPTANCE-2026-10-01.md) 的候选；隔离 bin 的 31 个文件逐项匹配原候选 SHA256 清单，未修改生产代码或重跑门禁。此前完整门禁为 Pester 145、.NET 1757、Tray 2、Installer 9，通过数如列，四层失败和跳过均为 0，退出码 0；本次新增证据为真实电池桌面路径。

`.work/guard-rules/battery-v1-session.ps1` 为本次有界调查 spike：复用唯一观察入口 `scripts/Invoke-PowerLifecycleAcceptance.ps1 -Scenario LidMouseBattery`，最长 30 分钟、15 秒采样且最多 120 项。基线唤醒请求在人工操作前已解除；不改 300/900 秒阈值或系统 AC/DC 设置。三种原计划的 DC 合盖动作查询均为睡眠。

Engine PID 25648、Tray PID 27860。22:23:10.738 的同 PID 健康基线丢弃、失败批次和队列深度均为 0；基线状态 DC（AC=false）、高性能、亮度 30%，实际计划查询一致。基线期 22:22:11 曾记录外部改为平衡并随后纠偏，保留原日志；该事件发生在规范观察开始和合盖之前。

## 真实用户路径与结果

用户确认“已合盖，开盖并移动鼠标唤醒”。22:24:44.3351498 原生挂起、22:25:50.0838323 原生恢复，均为 PID 25648；该区间没有 PowerPlanSwitched。恢复后电池供电，空闲计时归零，22:25:56 稳定期后切回高性能。

Windows Kernel-Power 506/507 原始 XML 保存在 `battery-v1-system-events.json`，时间分别为 22:24:44.2148343、22:25:50.4250840。506 的 LidOpenState=false；507 的 DurationInUs=66209701、DripsResidencyInUs=0。此处证明 S0 会话进入/退出的观测覆盖，不据此宣称深度低功耗驻留或节能效果。

22:29:11.434 的恢复后健康样本与基线同 PID，droppedNormal、droppedHigh、failedBatches、normalQueueDepth、highQueueDepth 均为 0。提交观察器前再次保存 DC 状态、实际计划、健康样本与原生恢复事件至 `battery-v1-post-resume-evidence.json`。

22:30:08，`battery-v1-power-lifecycle.result.json` 返回 Passed、退出码 0，lifecycle-events、windows-suspend-resume-events、no-suspend-plan-write、status-freshness、pipeline-health-delta 五项全部 Passed。S4 专属 hibernate-state 不适用。

## 恢复与证据

22:30:10.989 包装回执 Stage=scenario-passed、ObserverExit=0、Restored=true、Error 为空。提权包装内原任务命令、工作目录、根与两个进程运行态 Before/After 精确一致，生产配置 Hash 保持 `9310CFD90225B1932AAFEA863A0DE150D7F02D7501DBFEE0887F187DD517DA93`；原高性能计划恢复。包装执行原亮度 30% 的恢复；用户可重新插电并正常使用。

22:32 的额外只读复核确认亮度 30%、配置 Hash 一致、安装根 Engine PID 36636 持续发布状态，且已经恢复插电（AC=true）。

原始证据位于 `.work/guard-rules/`：`battery-v1-session.result.json`、`battery-v1-power-lifecycle.result.json`、`battery-v1-power-lifecycle.transcript.txt`、`battery-v1-system-events.json`、`battery-v1-manual-operation.json`、`battery-v1-post-resume-evidence.json` 与 `battery-v1-root/SmartGuard.log`。原始回执不改写，用户的 `installer/version.txt` 修改未纳入本任务提交。
