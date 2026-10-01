# 守护规则修复验收记录（2026-10-01）

范围：动作前复核、状态刷新、空名单查询、消息掩码及对应行为测试；既有用户修改未纳入本任务。

## 行为证据

| 缺口 | RED | GREEN |
|---|---|---|
| 前台离开/暂停/名单移除/配置失效；事件空名单；投递QUIT唤醒 | review-engine-red.trx：6失败 | review-engine-green.trx：6通过 |
| 动作前最新idle/供电 | review-engine-facts-red-corrected.trx：2失败 | review-engine-final-green.trx：8通过（含上行） |
| 强制供电慢探测期间配置撤销 | review-engine-slow-probe-red.trx：2失败 | review-engine-final-suite-v2.trx：260通过、0失败、0跳过 |
| 完整周期空名单 | review-engine-empty-periodic-red.trx：1失败 | review-engine-final-suite.trx：258通过、0失败、0跳过 |
| Tray自然过期 | Tray TestResults/guard-status-expiry-red-20261001.trx：1失败 | review-tray-green.trx：20通过 |
| Settings运行中变化与关闭 | Settings TestResults/guard-status-window-red-20261001.trx：1失败、1通过 | review-settings-green.trx：9通过 |
| 慢读取期间仍自然过期 | Settings TestResults/guard-status-stale-inflight-red-20261001.trx：1失败 | review-settings-lifecycle-green.trx：11通过 |

上表文件位于本目录或对应 Tests 项目的 TestResults；编译失败、DLL锁定及沙箱注册表失败均不是行为RED。最早低电测试错误期待节能，已按原策略修正为平衡并重新保存有效RED，不用错误断言作为验收证据。

## 真实前台试验

`review-live-trial-unsandbox/` 与 `review-live-trial-supplement/` 保留每轮JSON、轨迹、计数、TRX；补测目录保存临时spike源码及SHA256核验，正常测试项目内已移除。

仅测试实际前台采集、真实WinEvent监听、协调器和Runner至拦截动作。没有真实电源/亮度写入，没有整机能耗测量。初次沙箱读取不到交互桌面，丢弃；沙箱外6轮中2/5轮无有效动作，剔除；随后补2轮，有效A/B各3轮。不是等相位受控矩阵，不能用均值推断整体收益。

| 原始轮次 | 模式 | 动作延迟秒 | Reader/完整轮次 |
|---|---|---|---|
| 原1 | A | 1.345–1.502 | 3/2 |
| 原3 | B | 0–0.072 | 23/4 |
| 原4 | A | 2.104–2.244 | 3/2 |
| 原6 | B | 3.496–3.622 | 24/4 |
| 补1 | A | 11.067–11.183 | 4/3 |
| 补2 | B | 1.312–1.440 | 24/5 |

事件轮真实前台有额外变化，完整评估受5秒下限影响；数据证明事件链可用，尚不足以裁决复杂度保留、静止/同准入开销、长期资源或能耗。补测包含最新idle/供电复核；随后补了空名单周期跳过及慢探测的授权读取顺序，故资源数字不是最终二进制性能预算验收。

## 最终门禁与桌面

第一轮沙箱外完整门禁退出0：Pester126、.NET1723、Tray Integration2、Installer Integration9，均0失败/0跳过；summary：run-tests-20261001T005734281Z-edd462d818ba49d5b4f97a8c8e6e3f4b.summary.json。随后增加2项慢供电撤销回归并修正顺序，最终完整门禁退出1：Pester126、.NET1725、Tray Integration2均通过且0失败/0跳过；Installer Integration等待提升Pester结果JSON超过10分钟，未返回用例结果（计数0/0/0，非9项通过）。summary：run-tests-20261001T012404727Z-0cda72548cff435989e0da590c1786d5.summary.json。

最终版本安装器层独立重试于01:42:26UTC完成：退出0、9通过、0失败、0跳过；结果为installer-final-retry.result.json，日志为installer-final-retry.transcript.log。最新版各层都有通过证据，但完整门禁曾超时退出1，再由安装器单层重试转绿；不得覆盖失败summary或宣称同一轮全绿。停滞根因尚未确定，不归因于产品代码，也不把重试成功当作根因修复。

首次沙箱命令因Pester TestRegistry权限失败，summary：run-tests-20261001T005713104Z-a4608252a4724fa39535bfbb5c3b98f7.summary.json；不得当产品失败或通过。完整门禁默认排除Performance/Live类别，0跳过也不表示受控性能矩阵执行过。最终超时后停止确认为本轮的提升测试进程，恢复原安装两个任务；final-environment-restored-*.json记录两者Running、原路径D:\Apps\SmartGuard及配置哈希不变。

安装器重试后最终核对（01:44:33UTC）：原Engine PID15884和Tray PID36092均运行于D:\Apps\SmartGuard，任务动作仍指向该根；Tray任务为Ready而其启动的托盘进程仍在，不把任务状态等同进程状态。配置SHA256仍为97C46A635FA50E07D513318AC1AA55846FE6B38A33189F9C82131E0C093066F2，当前计划为原高性能；证据final-environment-probe-d9ea0637c8e74a71a6424945fb254999.json。

真实设置窗口使用独立根 `desktop-root` 的最终Release发布。通过实际文件选择器添加charmap.exe、开启限制、显示空名单提示、保存及关闭重开后保留；隔离配置UI设置平衡/节能阈值为60/120秒，原安装配置不变。`list-added.jpg`为名单添加界面凭据。

最终生产Engine（无Reader/电源/亮度替身）的桌面链：

| 路径 | 实测证据 |
|---|---|
| 名单目标前台持续无输入降档 | 01:13:35UTC idle62秒实际平衡，01:14:34UTC idle121秒实际节能、foreground-matched、亮度41%；后者见idle-observation.json，前者可核对该根日志 |
| 离开名单保留计划 | 启动计算器后01:16:29UTC waiting-for-listed-program，实际计划仍节能 |
| 名单外外部改计划不纠偏 | 实际powercfg切平衡，等待原周期后仍none/waiting、无新纠偏通知；outside-external-change.json |
| 返回重新决策并升档 | 计算器实际键入后重开目标，01:18:52UTC idle8秒、automatic/foreground-matched、实际高性能、亮度41%；return-observation.json |
| 名单外人工例外及自然到期 | 使用原HighPerformanceBoost.Apply及真实PowerPlanActivator，显式35秒期限；同一Engine PID43040先manual-active，再在期限后01:22:55UTC转none/waiting，高性能保留，符合退出不干预；bounded-manual-active.json与bounded-manual-expired.json |
| 原环境恢复 | 两次隔离均在finally恢复原计划及原任务enabled/running状态；原安装配置SHA256前后相同，error为空；isolation-first.json及isolation.json |

隔离脚本仅为本次有界试验（8分钟上限），不进入生产或常规测试。窗口自动化遇到旧窗口最小化/用户输入状态冲突，改用新启动的计算器取得新窗口后继续；降档结论来自实际Engine状态及日志，不将错误窗口截图当证据。首次35秒试验在截止轮次前触发隔离上限，未观察到到期，保留但不计通过；第二次在同一Engine内观察到自然到期后主动结束。

**边界：** 60/120秒是通过真实设置界面配置的测试阈值，不是默认300/900秒实测。35秒接管使用生产API已有duration参数，不是托盘按钮默认一小时UI路径。后者、键盘完整添加/删除、100%/200%DPI、高对比、锁屏/UAC和休眠恢复均NOT RUN。本次未测整机能耗，也未完成0/1/128项静止/同准入/快速切换受控性能矩阵，不能声明功能全部验收或省电通过。
