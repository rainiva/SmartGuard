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
| T2 真实写入与非阻塞 | 待执行 | — |
| T3 日志健康 | 待执行 | — |
| T4 规则观测 | 待执行 | — |
| T5 停工恢复与心跳 | 待执行 | — |
| T6 附加文件与回退可见 | 待执行 | — |
| T7 诊断容错 | 待执行 | — |
| T8 导出异步所有权 | 待执行 | — |
| T9 集成与验收 | 待执行 | — |

真实桌面、性能比较、Soak、回滚与全量门禁尚未执行，不能据此认定修复完成。

## T0 / T1 证据

- T0 命令为原计划 T0 的两个 `dotnet test` 过滤器，添加 `--no-restore`、TRX logger 与上述独立结果目录；退出码均 0：`t0-configuration-baseline.trx`（45 passed / 0 failed / 0 skipped）、`t0-engine-baseline.trx`（27 / 0 / 0）。既有编译警告 CS1717/CS8604/xUnit1031 未由本轮引入，不把输出称为无警告。
- 审计工具沙箱构建失败无错误诊断，既有二进制运行被沙箱拒绝隔离 status 的 File.Replace；重新正常构建当前源码（0 warning / 0 error，exit 0）并在沙箱外运行同一隔离探针，exit 0。新证据为 `run-3d2e76927e0b4fb4a9170023cd4659dc/evidence.json`，13 个场景。该工具不操作真实电源计划或任务。
- T1 架构 RED：`dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~LoggingRepairArchitectureTests`，exit 1，`t1-architecture-red.trx`；失败原因是未登记 ConfigMutationFailed。
- T1 行为 RED：Configuration 过滤 `ConfigMutationServiceTests|SettingsSaveCoordinatorTests`，exit 1，`t1-behavior-red.trx`（3 passed / 4 failed / 0 skipped）；确认缓存被未提交 Paused 污染、成功记录缺已保存语义、规则替换无记录、保存失败留假成功。
- 后续系统任务隔离测试初次 RED 为缺少隔离注入点；不会触碰真实任务。坏日志路径测试在旧代码也能通过（writer 已隔离），`t1-path-red.trx` 实际为 PASS，只作为回归，不计作功能 RED。
- T1 GREEN：目标过滤增加 `GuardForegroundRestrictionConfigTests`，exit 0，`t1-green.trx`（38 / 0 / 0）；架构过滤 `LoggingRepairArchitectureTests|ArchitectureContractLogging|ConfigRepository|ForegroundGuardArchitecture`，exit 0，`t1-architecture-green.trx`（54 / 0 / 0）。Configuration 全项目回归 exit 0，`t1-configuration-regression.trx`（420 / 0 / 0）。
- T1 自审：原子配置写入 Owner 未变、未知键保留；缓存只在提交后或失败恢复时失效；日志使用已发布配置；AutoStart 失败反馈仅区分阶段，未改变系统任务行为；内置测试 seam 仅隔离真实任务。全套 Run-Tests 与真实桌面证据留在 T9，未宣称整个计划完成。
