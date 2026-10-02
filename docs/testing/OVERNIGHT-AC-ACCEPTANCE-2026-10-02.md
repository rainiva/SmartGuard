# 八小时插电开盖连续观察重验（2026-10-02）

当前 v3：RUNNING，未通过八小时验收。此前 v2 为 BLOCKED（采集器状态文件共享冲突），已停止并恢复原环境。[上一轮](OVERNIGHT-AC-ACCEPTANCE-2026-10-01.md)因供电变化而中断；旧证据全部保留，不拼接时长。

## 开始与范围

2026-10-02 07:43:36.924（Asia/Shanghai）开始，原预计同日 15:43 完成，实际提前中断。恢复包装 PID 44644，Engine PID 10016、Tray PID 48080。首样本 AC=true、paused=false、亮度 30%，状态与实际均为高性能。隔离 bin 31 个文件逐项匹配原通过候选 SHA256；没有生产代码变更或部署。

此前完整门禁 Pester 145、.NET 1757、Tray Integration 2、Installer Integration 9，四层失败和跳过均为 0、退出码 0，本轮未重跑。真实 S4、300/900 秒阈值与 60 分钟 AC 观察见[原验收](S4-IDLE-SOAK-ACCEPTANCE-2026-10-01.md)，电池路径见[电池验收](BATTERY-LID-MOUSE-ACCEPTANCE-2026-10-01.md)。这些结果不能替代本轮八小时证据。

仅验证插电、开盖连续运行资源与健康，可正常使用桌面；保持 Codex 和候选运行，不手动睡眠、改计划或暂停。合盖耗电、深度低功耗与罕见故障注入不属于本轮。

## 边界与恢复

沿用已有有界调查包装 `.work/guard-rules/overnight-v2-session.ps1`，新根为 `overnight-v2-root`。最长 510 分钟，每至少 15 秒采样、最多 2040 项 JSONL；回执保存最新样本，日志尾部 100 行读取，完整日志证据上限 1 MiB。启动样本不是稳定资源平台；健康首样本等待真实五分钟周期。

阈值保持 300/900。原任务配置与运行态、生产配置 Hash、计划、亮度及三个计划的六项 AC 睡眠/息屏超时均保存快照；临时防止自动睡眠与息屏，DC 不改。成功、失败、取消都进入唯一包装 finally 恢复，并验证六项超时及任务/配置；取消只写该根 stop.request，不杀包装进程。

## 中断判定与恢复

最后有效样本为 08:49:24.105，仍为 PID 10016、AC=true、paused=false。下一次状态文件 ReadAllText 抛出“文件正由另一进程使用”异常，包装将采集判为 Blocked 并进入 finally；不是供电或暂停判定失败，也不能据此宣称 Engine 故障。冲突读数未进入 JSONL，保留回执原始异常。

恢复完成于 08:49:41.276。ObservationElapsedSec=3963.304 秒，远少于要求的 28800 秒；246 个样本跨度 3947.146 秒，末样本至恢复完成间隔 17.171 秒。13 次同 PID 健康样本的丢弃、失败和队列深度全部为 0，日志未记录原生暂停。约 66 分钟的窗口不能替代八小时证据，不作八小时资源趋势通过声明。

回执 Restored=true，Error 保留采集冲突；提权包装内 Before/After 原任务配置与两个进程运行态一致，生产配置 Hash 一致，候选已退出。09:01 独立 PowerReadACValueIndex 复核六项 AC 超时：高性能 600/600、平衡 300/300、节能 300/300 秒，逐项匹配原值；亮度为 30%。包装恢复原高性能计划并核对命令退出码，原安装根 Engine PID 3020 随后持续刷新，AC=true。复查时 idle=4587，实际与期望均为节能，属于恢复后正常策略行为，不能将稍后计划变化视为恢复命令未执行。

只读恢复证据保存于 `overnight-v2-final-review.json`。跟进停用，不自动重启新会话；下一步应先复现并修正采集器在状态发布并发写入时的读取边界，保持 90 秒新鲜度和有界失败语义，再开始全新八小时观察。此轮未修改采集器或生产实现，不用吞异常、放宽预算或拼接窗口宣称通过。

聊天跟进每 15 分钟检查现有 v2 回执；正常保持安静，仅最终完成、失败或需要操作时通知，最终汇报后停用。原始证据为 `.work/guard-rules/overnight-v2-session.result.json`、`overnight-v2-samples.jsonl`、`overnight-v2-root/SmartGuard.log`、候选清单及 `overnight-v2-plan.md`。只提交本轮验收文档，保护用户 `installer/version.txt` 和其他修改。

## 修正后 v3 重验：RUNNING

[采集器修正](SOAK-STATUS-READER-FIX-2026-10-02.md)已提交91ad78e，完整门禁Pester156/.NET1757/Tray2/Installer9零失败零跳过，退出码0；一分钟真实短测恢复通过。正式入口 Invoke-SmartGuardSoakAcceptance 复用共享 reader、有限重试与语义校验，不再使用原临时脚本直读状态。

v3于10:27:57.657开始全新480分钟计时，预计18:27:57结束；Engine PID6548、Tray PID17552、恢复包装42928。新根 bin31文件匹配原通过清单，首样本AC=true、未暂停，读取尝试1次，Error为空。原始证据为 `.work/guard-rules/overnight-v3-session.result.json`、`overnight-v3-samples.jsonl`、`overnight-v3-root/SmartGuard.log`。状态重试次数与耗时进入样本/回执，原恢复和八小时判定要求不变。

此前授权的聊天跟进已切换为仅观察v3，每15分钟有界检查，正常保持安静；失败或完成汇报后停用。用户继续保持插电、开盖、Codex/候选运行，可正常使用但不手动睡眠、改计划或暂停。本轮真实八小时仍未完成，不把已通过的修正测试与短测当作长测通过。
