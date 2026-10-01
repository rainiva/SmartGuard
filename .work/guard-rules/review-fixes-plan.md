# 守护规则验收缺口修复

范围：动作前配置/前台复核、Tray/Settings 持续状态刷新、空名单跳过查询、消息泵唤醒掩码。配置原子替换和 timestamp 单位问题仍单独记录，不混入本轮。

## 影响检查

| 域 | 本轮约束与验证 |
|---|---|
| Function / Reliability | 所有切前副作用前经原 PolicyEngine 复核；撤销授权无新纠偏通知；完成后的亮度恢复不改变。 |
| Architecture / Data / Compatibility | 原 Reader、Repository、StatusStore、Freshness 为真源；不新增配置字段、状态格式或框架。 |
| Performance / Resource | 复核仅发生于候选切换；名单最多128项 O(N)。空/非法名单在事件及完整周期均跳过进程查询。Settings 最多一个5秒刷新和一个后台读取，仅更新变化文案，关闭收敛。 |
| Concurrency & Time / Recovery | 状态过期须能触发展示变化；窗口释放后迟到结果不更新。消息线程仍同线程注册卸载、阻塞等待。 |
| Security / Observability | 不提高权限、不采集标题；故障原因仍经已有状态代码展示。 |
| Operations | 桌面链、事件延迟比较、full门禁按现场条件执行并区分自动化/真实桌面。 |

Critical Module Check：运行状态仍 Engine 单写；后台刷新由 SettingsForegroundCoordinator 拥有；Tray 新鲜度复用 GuardStatusFreshness；取消/关闭不产生新UI副作用。日志与通知只消费复核后结果。安装/更新、网络与持久化格式 N/A。

验收：先保存四类行为 RED，再最小 GREEN、业务链回归、架构及全量门禁；桌面真实路径和事件延迟必须有独立证据，合成输入不得替代。
