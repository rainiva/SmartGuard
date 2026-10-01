# 八小时插电开盖连续观察重验（2026-10-02）

状态：RUNNING，尚未通过。[上一轮](OVERNIGHT-AC-ACCEPTANCE-2026-10-01.md)因供电变化而中断，证据全部保留，本轮使用新 v2 根重新计满八小时，不拼接时长。

## 开始与范围

2026-10-02 07:43:36.924（Asia/Shanghai）开始，预计同日 15:43 完成。恢复包装 PID 44644，Engine PID 10016、Tray PID 48080。首样本 AC=true、paused=false、亮度 30%，状态与实际均为高性能。隔离 bin 31 个文件逐项匹配原通过候选 SHA256；没有生产代码变更或部署。

此前完整门禁 Pester 145、.NET 1757、Tray Integration 2、Installer Integration 9，四层失败和跳过均为 0、退出码 0，本轮未重跑。真实 S4、300/900 秒阈值与 60 分钟 AC 观察见[原验收](S4-IDLE-SOAK-ACCEPTANCE-2026-10-01.md)，电池路径见[电池验收](BATTERY-LID-MOUSE-ACCEPTANCE-2026-10-01.md)。这些结果不能替代本轮八小时证据。

仅验证插电、开盖连续运行资源与健康，可正常使用桌面；保持 Codex 和候选运行，不手动睡眠、改计划或暂停。合盖耗电、深度低功耗与罕见故障注入不属于本轮。

## 边界与恢复

沿用已有有界调查包装 `.work/guard-rules/overnight-v2-session.ps1`，新根为 `overnight-v2-root`。最长 510 分钟，每至少 15 秒采样、最多 2040 项 JSONL；回执保存最新样本，日志尾部 100 行读取，完整日志证据上限 1 MiB。启动样本不是稳定资源平台；健康首样本等待真实五分钟周期。

阈值保持 300/900。原任务配置与运行态、生产配置 Hash、计划、亮度及三个计划的六项 AC 睡眠/息屏超时均保存快照；临时防止自动睡眠与息屏，DC 不改。成功、失败、取消都进入唯一包装 finally 恢复，并验证六项超时及任务/配置；取消只写该根 stop.request，不杀包装进程。

## 待判定

最终记录 ObservationElapsedSec，需不低于 28800 秒；同时报告样本跨度与末样本到完成的间隙。核对 PID/AC、新鲜度预算 90 秒、全部同 PID 健康样本丢弃/失败/队列，以及暂停和失败事件。比较预热后首段、分小时与末段的两进程内存、线程、句柄、CPU，限定为实际时间窗口结论。

采集器 NeedsReview 不是通过。必须核对 Restored=true、Error 为空、原任务 Before/After 配置与运行态一致、生产配置 Hash 一致、六项 AC 超时精确恢复、计划和亮度恢复及候选退出。恢复后原安装根正常策略可能再改变计划，不能混淆恢复命令与稍后状态。证据不足判 BLOCKED，不自动重启。

聊天跟进每 15 分钟检查现有 v2 回执；正常保持安静，仅最终完成、失败或需要操作时通知，最终汇报后停用。原始证据为 `.work/guard-rules/overnight-v2-session.result.json`、`overnight-v2-samples.jsonl`、`overnight-v2-root/SmartGuard.log`、候选清单及 `overnight-v2-plan.md`。只提交本轮验收文档，保护用户 `installer/version.txt` 和其他修改。
