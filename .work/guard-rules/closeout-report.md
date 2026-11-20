# 守护规则收口验收记录

2026-10-01。用户选择先执行自动项目，人工环节待验收。结论：指定两项修复已验证；整体验收仍 BLOCKED，不能宣称全绿或完整完成。

## 修复及独立证据

| 项目 | 新鲜结果 |
|---|---|
| 配置替换 | 旧实现先删除正式文件；Save / 手动 mutation 的 RED 2 失败，最小 GREEN 3 通过；追加真实共享冲突后配置套件 411 通过、0 失败、0 跳过。失败时旧正式字节保留且释放冲突后可重试。见 [配置报告](closeout-config-report.md) |
| 协调器时间单位 | 原始 TimeProvider 计数不再直接当 TimeSpan ticks；1000 / 10MHz / 1GHz 的周期、末次轻信号、重评估及失败冷却 RED 8 失败 / 4 通过，GREEN 12 通过。Engine 独立套件 272 通过、0 失败、0 跳过。见 [RED](closeout-time-red/)、[GREEN](closeout-time-green/)、[Engine 回归](closeout-engine-green/) |
| 架构契约 | 两项约束各有文档门禁 RED，同步契约后 GuardRulesArchitectureTests 8 通过、0 失败、0 跳过。见 [GREEN](closeout-architecture-green/) |

入口、配置 Schema、人工期限格式和规则行为未扩展；没有新增调度器、缓存或功耗框架。配置的固定临时文件和并发读改写丢更新风险未在本轮解决。

## 最终完整门禁

精确执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1`，**退出 1**；[summary JSON](../test-results/run-tests-20261001T030849449Z-24877bc284ab43e59d6264270aa2becf.summary.json)。保留首次失败，没有重跑覆盖。

| 层 | 通过 | 失败 | 跳过 | 状态 / 原因 |
|---|---:|---:|---:|---|
| Pester | 126 | 0 | 0 | Passed |
| .NET | 1739 | 1 | 0 | Failed：Engine 状态发布替换拒绝访问 |
| Tray Integration | 0 | 0 | 0 | NOT RUN：上游失败 |
| Installer Integration | 0 | 0 | 0 | NOT RUN：上游失败 |

.NET 分项：Engine 271/1，Configuration 410/0，CLI 1/0，Architecture 322/0，Packaging 15/0，Tray 86/0，Settings.LogView 59/0，Settings 575/0；各项跳过为0。Configuration 独立套件411与全量410的差异来自全量过滤，不合并两种计数。

失败发生于 `StatusPublisherTests.Idle_growth_is_visible_at_refresh_cadence` 的实际 `File.Move(overwrite:true)`，不是刷新节奏断言失败。独立原用例1/1和相关类8/8通过，真实publisher连续发布却在第119次复现相同拒绝访问；共享读句柄对照也暴露替换限制。没有捕获原失败时的句柄/系统过滤器，不能归因杀毒、声称仅环境噪声或断言本轮引入回归。仅换Replace不能解决当前Reader的共享方式。保持阻塞并单独调查读写共同契约，禁止加sleep、吞异常或无限重试。详见 [有界调查](closeout-status-investigation.md)。

## 原始性能预算

运行既有 `EnginePerformanceTests`，不是另建预算。隔离并恢复实际安装任务和进程；[TRX](closeout-performance.trx)、[恢复回执](closeout-performance-result.json)。退出1，2通过 / 1失败 / 0跳过：

- 私有内存 <20MiB：PASS；测试未输出实测值，不能编造精确MB。
- 启动 <5秒：FAILED。10秒内主日志未读到启动标记；对应启动标记实际位于 `SmartGuard.startup.log` 的 LOG-FALLBACK，随后内存用例的另一启动在主日志出现标记。确认采样失败，不据此证明实际启动>5秒，也不改读fallback来放宽已登记预算。
- 日志尾部扫描辅助用例：PASS。

启动预算未通过，事件机制保留裁决仍仅为此前有限实测结论，不能升级为全部性能门禁通过。整机能耗 NOT MEASURED，长期稳定未验证。

## 真实桌面与人工待验收

使用本次发布的隔离 Engine/Tray/Settings，真实设置窗口、键盘输入和实际配置/状态文件：

- PASS：启用空名单后显示“添加程序后生效”。Tab从限制开关到添加按钮，Enter打开exe选择器；文件名输入和Enter确认后名单实际持久化，Engine在该Settings前台时显示守护中。
- PASS：Shift+Tab进入名单，Space选择条目，Tab经过添加到删除按钮，Enter删除；磁盘条目数0、限制true、Paused=false，Engine发布 `foreground-list-empty`，UI恢复空名单提示。添加按钮、名单及删除按钮有可访问名称；删除后焦点回到页面容器，未验证读屏实际播报。选择器文件名曾由鼠标聚焦，不能称全程纯键盘。
- 证据：[添加UI状态](closeout-ui-added.json)、[删除UI状态](closeout-ui-deleted.json)、[桌面恢复回执](closeout-desktop-result.json)。本轮未验证重新打开后的非空名单保留，历史证据不冒充本轮。
- BLOCKED：已尝试定位托盘通知区域，当前computer-use窗口列表未暴露Tray或任务栏操作目标，未能点击默认一小时按钮；不把Engine/Tray进程存在当作入口验收通过。
- NOT RUN（用户明确延后）：默认300/900秒静置降档、一小时自然到期、锁屏/解锁、睡眠/唤醒；100%/200% DPI、高对比、减少动画的真实系统适配仍待验收。

两次隔离均有恢复回执；原安装配置哈希保持，电源计划恢复高性能，原Engine/Tray恢复运行；本次Settings已关闭。安装器和用户既有修改未提交或覆盖。

## 下一步阻塞

1. 状态共享读写链：以实际共享句柄下发布行为建立RED，再明确原子替换与Reader共享删除共同契约；继续保留无自建reader的burst失败调查。
2. 启动预算：调查主日志fallback原因，使原指标具备可信采样；不能靠改预算绕过。
3. 以上收敛后重跑完整四层门禁，随后补人工验收。当前不以独立GREEN覆盖完整失败，也不将本轮标为可发布。
