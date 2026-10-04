# 日志必要缺口修复实施记录

计划：[原执行计划](2026-10-04-log-observability-repair.md)。本文件只记录新鲜实施证据，不替代架构契约。

## 起点与约束

- 开始日期：2026-10-04，Asia/Shanghai。
- 起点提交：`2c8d83136c70e631c0ac13b07dba2948133b96ba`。
- 实施分支：`fix/logging-observability`；当前工作区，顺序实施，不派发子 Agent。
- 起点受版本控制文件干净；已有未跟踪资料全部保留。
- 证据目录：`.work/logging-repair/20261004-implementation/`；不覆盖审计证据。
- 环境：Windows、.NET SDK `10.0.204`；项目目标仍为 .NET 8 Windows，不升级依赖。
- 影响矩阵、模块 Owner 与成本模型沿用原计划 §3–§4；按阶段更新架构契约和门禁。

## 进度

| 阶段 | 状态 | 证据/结论 |
|---|---|---|
| T0 基线 | 已通过 | Configuration 45/45；Engine 27/27；当前源码隔离探针 13 场景复现原缺口 |
| T1 保存日志真实性 | 实现与目标回归通过 | 目标 38/38；架构 54/54；Configuration 全项目 420/420；真实 UI 留待 T9 |
| T2 真实写入与非阻塞 | 实现与回归通过，展示接入 T3 | Configuration 431/431；Engine 293/293；日志架构 54/54；三轮隔离锁竞争比较 |
| T3 日志健康 | 实现与目标验证通过 | Engine 40/40；Configuration 42/42；Tray 全项目 87/87；Settings 56/56；日志架构 55/55 |
| T4 规则观测 | 待执行 | — |
| T5 停工恢复与心跳 | 待执行 | — |
| T6 附加文件与回退可见 | 待执行 | — |
| T7 诊断容错 | 待执行 | — |
| T8 导出异步所有权 | 待执行 | — |
| T9 集成与验收 | 待执行 | — |

真实桌面、规则轻路径/导出性能、Soak、回滚与全量门禁尚未执行，不能据此认定修复完成。

## T0 / T1 证据

- T0 命令为原计划 T0 的两个 `dotnet test` 过滤器，添加 `--no-restore`、TRX logger 与上述独立结果目录；退出码均 0：`t0-configuration-baseline.trx`（45 passed / 0 failed / 0 skipped）、`t0-engine-baseline.trx`（27 / 0 / 0）。既有编译警告 CS1717/CS8604/xUnit1031 未由本轮引入，不把输出称为无警告。
- 审计工具沙箱构建失败无错误诊断，既有二进制运行被沙箱拒绝隔离 status 的 File.Replace；重新正常构建当前源码（0 warning / 0 error，exit 0）并在沙箱外运行同一隔离探针，exit 0。新证据为 `run-3d2e76927e0b4fb4a9170023cd4659dc/evidence.json`，13 个场景。该工具不操作真实电源计划或任务。
- T1 架构 RED：`dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~LoggingRepairArchitectureTests`，exit 1，`t1-architecture-red.trx`；失败原因是未登记 ConfigMutationFailed。
- T1 行为 RED：Configuration 过滤 `ConfigMutationServiceTests|SettingsSaveCoordinatorTests`，exit 1，`t1-behavior-red.trx`（3 passed / 4 failed / 0 skipped）；确认缓存被未提交 Paused 污染、成功记录缺已保存语义、规则替换无记录、保存失败留假成功。
- 后续系统任务隔离测试初次 RED 为缺少隔离注入点；不会触碰真实任务。坏日志路径测试在旧代码也能通过（writer 已隔离），`t1-path-red.trx` 实际为 PASS，只作为回归，不计作功能 RED。
- T1 GREEN：目标过滤增加 `GuardForegroundRestrictionConfigTests`，exit 0，`t1-green.trx`（38 / 0 / 0）；架构过滤 `LoggingRepairArchitectureTests|ArchitectureContractLogging|ConfigRepository|ForegroundGuardArchitecture`，exit 0，`t1-architecture-green.trx`（54 / 0 / 0）。Configuration 全项目回归 exit 0，`t1-configuration-regression.trx`（420 / 0 / 0）。
- T1 自审：原子配置写入 Owner 未变、未知键保留；缓存只在提交后或失败恢复时失效；日志使用已发布配置；AutoStart 失败反馈仅区分阶段，未改变系统任务行为；内置测试 seam 仅隔离真实任务。全套 Run-Tests 与真实桌面证据留在 T9，未宣称整个计划完成。

## T2 证据与自审

- RED：`t2-engine-red.trx`（4/4/0）复现三层失败假成功、拒绝后写盘、背压漏计、回退丢结构；`t2-flush-red.trx`（0/4/0）复现关闭故障假成功、Flush 无确认和竞争读取。各 TRX 数字顺序为 passed/failed/skipped，失败运行 exit 1。
- 补充 RED：`t2-health-red.trx` 延迟假 0；`t2-stop-event-red.trx` 无停止事件；`t2-cold-flush-red.trx` 冷异步调用被 writer 阻塞；`t2-ownership-red.trx` 冷 reader 未退出即返回；`t2-publication-race-red.trx` 可控交错下把待处理 Lost 当历史，Flush 错误返回 true。后二者按同一短锁原子交接及提交水位/结果修复。
- `t2-clock-red.trx` / `t2-transitions-red.trx` / `t2-consumer-health-red.trx` 是缺失契约或观测入口 RED，不冒充已经观察到的 OS 时间故障。冻结范围、取消局部性、非法分区继续消费等首次已通过的新增测试只算回归。
- GREEN：`t2-final-config-target.trx` 48/0/0；`t2-transitions-green.trx` 23/0/0；`t2-architecture-final.trx` 54/0/0（各 exit 0）。旧架构门禁要求业务线程同步回退，与新批准契约冲突；更新为消费侧独占批回退，保留三层与结构化 sink 门禁。
- 全项目回归：`t2-configuration-regression.trx` 431/0/0、`t2-engine-regression.trx` 293/0/0，exit 0。回退不恢复主写 Healthy；冷却为可注入单调时间；累计写入分区与 ConsumerFailed/StopFailed 可观测。状态和 UI 的健康投影尚待 T3。
- 性能工具为新建 `benchmark/`，基线来自起点提交的只读 Git archive 展开副本；不运行 Engine，不观察/切换真实计划。每版三轮、每轮 10 个高队列满业务调用，主 sink 命名锁由专用线程持有。初次未预热：原版 P95 2013.80–2052.77ms；修改版首轮冷启动 P95/Max 30.0008ms（超过拟定 P95 20ms，低于 Max 100ms），后两轮 0.2639/0.0729ms；原始结果完整保留，不删除失败预算样本。
- 等量一次解锁预热后重测，原版 P95 2013.71–2016.27ms；修改版三轮 P95/Max 分别 0.8874、0.0565、0.0820ms，P50 为 0.0036–0.0045ms。原始结果见 `performance-after-warm/results-f973a58b9c5b4f04a458a8c9d5e4391e.json`；独立记录线程/私有内存、队列深度、拒绝计数与回退行数。预热会额外产生 1 次拒绝，累计 DroppedHigh=11，正式样本为 10。原版三轮各写 10 条回退，修改版均 0；源码边界与拒绝测试确认修改版业务侧 writer 入口 0 次，探针未注入 OS 写入计数器，不能把行数称为系统调用数。此实验只证明满队列业务侧无写盘等待，不证明整个 Engine 的性能或长期内存趋势。
- 自审边界：消费 IO 在 gate 外，信号容量 1，内部 flush 代数 ≤2，双优先级各自 FIFO；异常批次继续消费但健康失败保留。Stop 超时不提前销毁仍被消费任务持有的信号；AppStopping 只表示停止意图。主线程不会尝试同步回退。附加日志保留预算仍待 T6。

## T3 证据与自审

- 契约 RED：`t3-architecture-red.trx`、`t3-status-red.trx`、`t3-pipeline-red.trx` 证明缺失类型/序列化/投影入口。先增加 DTO 后再运行 `t3-status-behavior-red.trx`（0/2/0，exit 1），实际确认健康变为 failed 仍被旧发布去重忽略。新字段逐属性 JsonPropertyName 登记，测试读取真实 StatusPublisher 输出 JSON，不依赖额外 naming policy。
- UI RED：`t3-tray-red.trx`（0/1/0）健康代码变更被 Clone/equality 忽略；`t3-settings-red.trx`（0/3/0）缺少健康输入契约。`t3-background-red.trx`（0/1/0）实际观察到 UI Dispatcher 上状态读取 1 次；改为既有刷新 Owner 后 `t3-background-green.trx`（1/0/0）确认非 UI 读取且无新日志时 failed→healthy 更新。
- GREEN（均 exit 0）：`t3-engine-target.trx` 40/0/0；`t3-configuration-target.trx` 42/0/0；`t3-tray-green.trx` 31/0/0，Tray 全项目 `t3-tray-regression.trx` 87/0/0；`t3-settings-target.trx` 56/0/0（含 WPF STA 生命周期、分页和导航）；`t3-architecture-green.trx` 55/0/0。TRX 计数将与完成前全量 summary 独立核对。
- 旧 Tray 精确文案测试因缺失健康字段新增未知提示而失败（`t3-tray-green-attempt.trx` 13/18/0），更新旧格式预期；专门守护字段测试只增加健康夹具，仍验证原守护语义，未放宽来源/原因断言。新健康测试独立覆盖缺失/未知、过期/超前时间、计数与时间变化不重复应用。
- 全层日志失败而状态可写的隔离测试可读取 failed；状态 IO 再失败局部收敛，不结束 Engine。健康单独更新不推进守护 timestamp；稳定代码的样本时间/持续计数不每 5 秒写入，60 秒 freshness 或既有守护变化才带最新计数。
- 自审：无新文件、Timer、IPC、展示面板或日志推断状态路径；健康值不可变、累计分区原子发布，当前拥塞可解除而历史损失保留。Settings 状态独占原后台 flight/取消所有权，状态-only 请求忙时不排队；UI apply 不读取状态文件。WPF 线程边界核对[微软线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)。真实托盘、DPI、UIA 与 Soak 仍为 NOT RUN，T9 统一验收。
