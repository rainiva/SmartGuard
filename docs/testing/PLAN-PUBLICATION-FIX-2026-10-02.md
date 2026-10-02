# 计划切换最终发布复核（2026-10-02）

## 根因与范围

[八小时v3记录](OVERNIGHT-AC-ACCEPTANCE-2026-10-02.md)保持BLOCKED。现场七次实际计划/状态不一致，其中六次连续。本次修复可确定复现的发布窗口；后续[系统事件调查](ACCEPTANCE-FOLLOWUP-2026-10-02.md)已将两段外部覆盖关联到荣耀电脑管家HnPerformanceCenter.exe，但内部触发条件仍未知。

`PowerCfgExecutor.ApplyAndVerifyAsync`已有setactive退出码和最多三次即时观测验证。`GuardianIterationRunner`随后等待BrightnessRestoreMs并恢复亮度，却仍按较早ObservedGuid生成成功日志、通知和状态；外部在此期间改回计划时，较早观测不再代表动作结束后的计划。另一同类缺口是apply失败携带ObservedGuid时，Runner未将该实测值更新到发布字段。

[PowerSetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme)与[PowerGetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme)分别设置、获取当前计划，各次成功不保证调用之间没有外部修改。本次不建立原子切换承诺，不尝试无限对抗未知外部进程。

最初调查线索：现场其他GUID`961cc777-2547-4f9d-8174-7d86181b8a7a`与微软[电源滑块文档](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/customize-power-slider)登记的Better Battery overlay GUID一致。GUID本身不能证明调用者；后续Windows事件12才直接记录了HnPerformanceCenter.exe的高性能→该GUID改动。仍不能据此断言具体用户操作、内部触发条件或基础计划/overlay的采集关系。

## 变更与边界

在亮度等待/恢复结束后，Runner经既有PowerCfgExecutor入口追加一次最终读取；最终匹配才生成成功事件/通知，最终不一致或不可读生成PowerPlanApplyFailed并发布最新实测GUID，未知为null。初始apply验证失败的ObservedGuid同样进入发布。保留决策目标、300/900阈值、单写者StatusPublisher和生命周期门禁；不新增字段、Owner、后台线程或缓存。

同根因横向检查发现失败轮次仍可能输出“计划正常”心跳，追加确定性RED（3失败1通过，`plan-publication-heartbeat-red.trx`）后，最小修复禁止该失败轮次追加正常心跳；失败事件保持可观察，不提前消耗心跳时隙，后续成功轮次恢复正常心跳。

影响矩阵：功能和可观察性改变为最终验证成功后才报成功；状态格式和reader兼容不变；切换轮次增加一次O(1)探测（现有5秒进程超时），非切换轮次不增加；取消沿用token，不追加setactive或盲目重试。外部仍能在最终探测后再次改计划，后续正常轮询继续观察，不能将最终读取描述成持续一致性保证。配置/安装/升级/回滚格式不变，权限不变，没有部署。时间、后台执行和状态三个关键模块保持有界探测、失败公开和唯一发布入口；稳定不变量登记为PLAN-FINAL-OBSERVATION。

## 验证

确定性RED：两项测试分别模拟即时验证成功后实际变为其他计划、最终探测不可用，旧代码仍发布高性能GUID，按预期失败。证据`.work/guard-rules/plan-publication-red.trx`；最小修复GREEN两项通过，`plan-publication-green.trx`。扩展覆盖最终匹配、外部改回、最终探测失败、初始apply失败携带实际GUID。正常桌面访问下Engine回归286通过、0失败0跳过，`plan-publication-engine-green.trx`。沙箱内首次回归282通过、2项前台监听测试失败；退出沙箱复验通过，不将环境失败算作根因RED。

架构登记测试先保存RED（契约缺少不变量），`.work/guard-rules/plan-publication-architecture-red.trx`；随后同步架构契约。最终版本完整门禁和一分钟隔离纠偏路径已通过，下文列出证据与限制。原v3八小时不能因本次修复或短测变成Passed。

门禁中间回执全部保留：第一轮Pester156/.NET1762/Tray2通过，Installer0通过9失败（准备阶段找不到默认`D:\Apps\Inno Setup 6\ISCC.exe`），退出码1。通过现有SmartGuard.Packaging入口、仓库已有`.work/tools/inno/ISCC.exe`和`--skip-version-bump`预建测试安装器；沙箱嵌套dotnet发布不可用，退出沙箱构建退出码0，未改用户版本号。第二轮Pester156通过、.NET1761通过1失败（既有前台监听限制开关测试），Tray/Installer为NOT RUN（上游失败）。独立原断言复核1通过0失败0跳过，`plan-publication-listener-recheck.trx`；不将间歇失败默默删除，也不据独立复核宣称完整门禁通过。

第三轮门禁退出码0，但其编译早于最终失败心跳修复，因此不作为最终版本完成证据。最后一次Engine单独回归再次遇到两项前台监听夹具失败（284通过2失败），调查定位到测试WriteConfig在覆盖文件后才读取previous时间戳：之前人为增加的时间戳被覆盖，快速连续写入可能复用时间戳，导致真实缓存按契约看不到新配置。新增确定性夹具RED（预设未来时间戳后再写，旧实现回退）证明缺口，`plan-publication-fixture-clock-red.trx`。修复只在写前保存previous，写后设max(previous,written)+1秒；不改变生产配置缓存、不降低断言、不加等待碰运气。最终Engine287通过0失败0跳过，`plan-publication-final-engine-green-v2.trx`，包含四个发布场景与后续正常恢复。

## 实际探测成本

在本机通过候选PowerCfgExecutor.GetCurrentPlanGuid真实入口只读测量，预热三次，每组20轮：一次查询中位28.021ms、最大30.415ms；两次查询中位60.734ms、最大68.817ms，证据`.work/guard-rules/plan-publication-probe-cost.json`。这是原验证一次与额外最终复核两次的探测部分比较，非整个切换轮次或八小时性能对照；本次增加可解释的有界成本，不声称性能改善，未放宽已有预算。新增探测只在即时验证成功的切换轮次发生，单次既有超时5秒，不增加普通轮次、线程、队列或缓存。

## 最终门禁与真实短测

最终执行`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1`，退出码0；2026-10-02 19:15:57（Asia/Shanghai）完成。权威summary为`.work/test-results/run-tests-20261002T111111159Z-e7106a0ff86f46f7baed9017cd8e50fa.summary.json`，提权包装回执`.work/guard-rules/plan-publication-gates-v4.result.json`的GateExit=0、Error为空，Before/After任务配置及运行态一致。

| 层级 | 结果 | 通过 | 失败 | 跳过 |
| --- | --- | ---: | ---: | ---: |
| Pester | Passed | 156 | 0 | 0 |
| .NET | Passed | 1763 | 0 | 0 |
| Tray Integration | Passed | 2 | 0 | 0 |
| Installer Integration | Passed | 9 | 0 | 0 |

一分钟隔离候选Engine PID19040、Tray PID5584，单调观测60.503秒。19:17:45.004主动将实际计划改为平衡，powercfg退出码0；同PID在19:17:54.824记录ExternalPlanChange，19:17:55.475记录PowerPlanSwitched，距注入10.472秒，随后状态与实际计划均为高性能。此路径证明真实轮询纠偏和最终成功发布；亮度恢复期间被改回、探测失败及后续恢复由前述确定性测试覆盖，短测没有人为制造这两个竞态。

四个真实样本全部同PID、AC、未暂停，实际/状态GUID一致，最大状态年龄32.772秒；样本跨度48.345秒，结束回执距最后样本12.772秒。候选Engine DLL SHA256为`7BD9E96E85C4B3C78E61F5125ED15208B464B1E1AEF0C5FB57FF8A0ACDA82E23`。采集器原始Result=NeedsReview保留，通过结论仅限独立复核的一分钟纠偏路径。未达到300秒健康采样周期，健康样本0，健康长期趋势为NOT RUN；修复后连续八小时为NOT RUN并按用户要求延后。外部覆盖调用进程已由后续系统事件定位，内部触发条件仍未知，原v3保持BLOCKED。

恢复复核：Restored=true、Error为空；原任务配置与运行态Before/After一致，生产配置Hash一致，两个候选进程均已退出。六项AC超时逐值精确恢复：高性能睡眠/息屏600/600秒，平衡300/300秒，节能300/300秒；亮度按实例恢复30%。finally恢复原高性能计划的命令退出码已检查，19:22:57独立读取仍为原高性能计划；原安装根恢复运行后可按正常策略再次改计划，后续读取不等同于恢复命令时刻。用户`installer/version.txt`及其他修改未纳入本次提交，没有部署候选。

原始证据均保留在`.work/guard-rules/`：`plan-publication-live-session.result.json`、`plan-publication-live-samples.jsonl`、`plan-publication-live-injection.json`、`plan-publication-live-root/SmartGuard.log`及`plan-publication-live-final-review.json`。本次兜底链为已有退出码/即时有界验证、亮度工作后的最终复核、失败发布实测或未知且不报正常心跳、下一正常轮次恢复，以及隔离验收finally恢复环境；不构成对未知外部进程持续修改计划的无限对抗保证。
