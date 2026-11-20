# 加固版安装与验收（2026-11-05）

对应代码：`eada2e2b`。本次只安装既有候选并验收，不改生产代码或用户规则。使用 computer-use 技能操作已安装设置与 WPS；UAC 由用户确认。

## 安装

- 安装器退出0，完成时间2026-11-05 07:52:46（Asia/Shanghai）。安装包SHA256：`B9F9DE39C55110CBD8014B71964543C552170C61D1A348740697012913D82AB5`。
- 安装后的Engine DLL与候选完全一致：`99D1B5EBBE6285DD8176296BC68A06CFDAE3B95AE968C52D3404C804297EB3FB`。Engine、Tray、Settings均在交互会话15运行。
- 配置安装前后SHA256均为`AB173E29F11E80FF4569CD8BDB22DB9EEE2618B0C0B8768A84B993E1F5374FAF`。保留平衡300秒、节能900秒、电池30%、轮询15秒、前台限制开启、两条程序名单、退出观察180秒。
- 原bin与配置备份：`.work/guardian-isolation-acceptance-20261105/original-install`。安装结果：同目录`install.result.json`。

## 真实桌面与日志

- 常规页显示5/15分钟与30%；高级页显示15秒轮询、180秒观察、Word/WPS名单及三档映射正常。
- WPS真实窗口验证显示“已确认前台匹配（本次设置）”。Engine发布`foreground-matched`与`automatic`，日志匹配第2条规则；转后台后发布`foreground-observation`，保留原高性能计划。状态快照为`wps-matched.status.json`与`background-observing.status.json`。
- 首次5秒验证因自动化刷新遇到窗口最小化、未及时切换而捕获了其他前台程序，不计通过；取消添加提示，名单未变。第二次在倒计时内切到WPS后验证通过。
- 真实`NativeProbeFailed`已落盘且日志页可读取：`brightness-read / wmi-failed`，首次耗时137ms、预算2000ms、退出3、清理确认true；后续约一分钟汇总并记录合并次数，没有逐轮刷屏。异常后仍持续更新前台准入、心跳与状态。没有伪造亮度值或成功恢复事件。
- 180秒观察期自然到期通过：07:55:07离开WPS，07:58:11发布“观察时间已结束”和`waiting-for-listed-program`，保持原计划；07:58:33返回WPS后再次发布`foreground-matched / automatic`。保存`observation-expired.status.json`与`returned-after-expiry.status.json`。
- `runtime.result.json`记录6条真实异常、合并17次重复请求，最大探测耗时650ms，全部清理确认。90秒/19次连续采样中计划保持、日志健康、丢失0，Engine句柄309–329；这只是短时采样，不作为长期资源稳定性结论。原始本轮日志为`installed-run.log.jsonl`（截止观察期到期）。

## 自动化证据与边界

- 本次安装后新跑194项，失败0、跳过0：`installed-acceptance-policy-probes.trx`114、`installed-acceptance-reload-observation.trx`21、`installed-acceptance-config-reload.trx`34、`installed-acceptance-settings-rules.trx`25。均保存于上述本地证据目录。配置改动/热重载、300/900秒边界、低电量、撤销资格、异常和恢复用隔离夹具验证，未写实际用户阈值。
- 对应候选上一轮全量门禁为2026-11-04的`run-tests-20261104T143509492Z-e612e5123e65459ab616dfd6811fb435.summary.json`：Pester156、.NET2015、Tray Integration2、Installer Integration9，全部通过、退出0。本次仅安装和文档记录，不将旧门禁称为本次重新全量运行。
- NOT RUN：真实空闲满5/15分钟的物理计划切换、实际低电量供电变化、实际Word窗口、实际亮度恢复、WMI提供程序自行恢复、长期/休眠/内核挂死组合。本机亮度WMI仍不可用；本次证据证明降级、记录和守护继续推进，不能证明硬件提供程序已修好。

## 后续系统组件修复

上述“亮度WMI仍不可用”是本次安装验收时的历史结果。2026-11-05 08:22完成WMIProv组件运行状态修复后，底层查询恢复，原Engine自行记录恢复事件并读回亮度。详见[底层亮度查询修复记录](2026-11-05-brightness-wmiprov-repair.md)，不将后续恢复倒填为早先已通过。
