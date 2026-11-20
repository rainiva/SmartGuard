# 连续运行采集器状态读取修正（2026-10-02）

修正范围为验收采集器；Engine 发布行为、桌面生产 reader、策略阈值不变。[原八小时重验](OVERNIGHT-AC-ACCEPTANCE-2026-10-02.md)因读取冲突中断，仍为 BLOCKED，不改写旧证据。

## 根因与入口

CodeGraph 定位 StatusPublisher/StatusJsonReader 后核对源代码：发布端 Move/Replace 原子提交完整快照；已有 reader 使用 ReadWrite|Delete 共享，短句柄读取后释放。临时采集器直接 ReadAllText，默认共享语义不符合该边界，单次冲突直接中断整轮。兼容读写句柄与临时独占故障已复现；原现场具体占用者未被追踪，不能反向宣称已确认其身份。

正式连续运行采集入口为 `scripts/Invoke-SmartGuardSoakAcceptance.ps1`，PowerShell 7.2 以上、管理员运行；状态通过 `Read-SmartGuardAcceptanceStatus.ps1` 调用候选中已有 StatusJsonReader，无第二套 JSON 解析。配置使用 GuardConfigRepository.TryLoad，不用默认配置代替读取失败。真实待机观察仍为 Invoke-PowerLifecycleAcceptance，未改变其 Owner。

## 分层兜底

1. 复用共享快照 reader，允许现有写句柄与原子替换，不阻塞发布端删除/替换。
2. 不可用读取最多六次、间隔 50ms；持续占用/损坏仍失败，不无限等、不返回缓存状态。配置读取也为六次有界尝试。
3. 每次有效状态核对 Engine PID、AC、paused、90 秒新鲜度和未来偏移 5 秒；文件大小预检上限 64KiB。语义失效立即拒绝，不靠重试放宽条件。
4. 样本和回执记录状态读取次数、耗时及累计重试；持续失败保存明确错误，进入原快照恢复 finally。
5. 原任务、配置 Hash、六项 AC 超时、计划与亮度恢复后独立复核；NeedsReview 只是收集完成，八小时仍需实际单调 28800 秒与资源/健康证据。

每至少15秒采样、最多2040项、日志证据1MiB，单次最多六次读取，无新后台线程或状态缓存。短测通过 SoakMinutes 明确指定时长，不能计入八小时验收。

## 已有验证与待补证据

RED 保存于 `.work/guard-rules/status-reader-red-real.xml`：原 ReadAllText 行为 2 通过、8 失败；共享写句柄和短暂独占读取失败，缺少无效状态约束。GREEN 十项故障测试通过，随后加入真正 StatusPublisher 并发发布500次、共享读取500次，11项全部通过（`status-reader-stress-green-v2.xml`）。持续占用/损坏、过期/未来、错误PID、断电、暂停、过大文件均拒绝。

压力测试初版把500次磁盘发布总时间混入单次读取预算，造成一次测试失败；保留旧证据，修正为分别记录最大单次读取耗时和整批耗时，单次2秒测试限制未放宽。首轮完整门禁 Pester155通过/1失败，后续三层未运行；失败为这项旧压力测量，后续重跑已通过。

真实一分钟隔离短测 `status-reader-live-session.result.json`：SoakMinutes=1、单调60.492秒、4样本、Error为空、Restored=true，Stage=collected/Result=NeedsReview。它证明修正入口可实际采集并恢复，不证明八小时或五分钟周期健康。本轮自动审批曾拒绝默认480分钟的“短测”启动；改为显式1分钟后获准执行，没有绕过拒绝或自动长测。

最终完整门禁于10:25:23通过，退出码0；summary 为 `.work/test-results/run-tests-20261002T022005059Z-b38e7e64d3e043b5b41313fd6f8ff7b5.summary.json`，四层结果见下。提权门禁包装 Before/After 原任务运行态一致、Error为空。下一轮八小时结果仍待真实时间证据，不将修正验证当成长测通过。用户 installer/version.txt 与原未跟踪证据保持原状；只提交本任务实现、回归测试和文档。

| 层 | 通过 | 失败 | 跳过 | 结果 |
|---|---:|---:|---:|---|
| Pester | 156 | 0 | 0 | Passed |
| .NET | 1757 | 0 | 0 | Passed |
| Tray Integration | 2 | 0 | 0 | Passed |
| Installer Integration | 9 | 0 | 0 | Passed |
