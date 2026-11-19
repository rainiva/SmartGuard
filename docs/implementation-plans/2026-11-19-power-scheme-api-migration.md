# 电源方案操作迁移到 Windows 电源 API — 设计

日期：2026-11-19　状态：设计已确认，待用户审阅后出第一阶段实施计划
依据：[电源 API 评估](2026-11-19-power-api-evaluation.md)、[子进程清理闩锁设计](2026-11-19-process-cleanup-reopen.md)

## 1. 背景与目标

2026-11-19 的停工发生在“启动 powercfg 子进程、读管道、终止、清理确认、闩锁”这一层。评估确认 `PowrProf.dll` 电源 API 能完成全部现有 powercfg 用途，当前计划读取 p50 0.11ms（powercfg 24ms），且没有进程、管道与清理问题。

目标：

1. 生产代码中电源方案的读取、切换、名称、列表、亮度设置项读写全部经唯一 Owner `PowerSchemeService` 调用电源 API；生产代码不再启动 `powercfg.exe`。
2. 电源服务调用卡住时：至多阻塞一个线程，期间不发起新调用，在途调用返回即自动恢复。
3. 失败分类真实可读（错误码、超时、在途），不再出现 `ProcessStartFailed` 之类与实际不符的分类。
4. 去掉首次初始化“向平衡方案写入亮度 50”的探测副作用。

已确认的决策：范围为 Engine、Settings、Tray 全部，分阶段交付；卡住采用单飞 + 截止 + 自动恢复；API 失败不回退 powercfg；活性保障（停工 N 分钟升级）另开设计，本次只暴露所需的健康事实；结构采用唯一 Owner。

非目标：

- 活性保障与 Engine 受控重启。
- WMI 亮度探测（仍为子进程，由第三阶段的闩锁收缩处理）。
- 用通知替代轮询：官方 Power Setting GUIDs 未登记“活动方案 GUID 变化”通知。
- 修改 `LoadWithRetryAsync` 等对外签名或 Settings 的重试语义。

## 2. 组件

### 2.1 `PowerSchemeService`（`SmartGuard.Configuration`，静态入口，每进程一个执行器）

**原生层 `PowrProfNative`（internal）**：仅 P/Invoke 声明——`PowerGetActiveScheme`、`PowerSetActiveScheme`、`PowerReadFriendlyName`、`PowerEnumerate`（`ACCESS_SCHEME`=16）、`PowerReadACValueIndex`/`PowerReadDCValueIndex`、`PowerWriteACValueIndex`/`PowerWriteDCValueIndex`，以及 `LocalFree`。测试辅助另需 `PowerDuplicateScheme`/`PowerDeleteScheme`（仅测试程序集使用）。所有保留参数传 NULL；API 分配的 GUID 指针一律 `LocalFree`。

**执行器**：

- 每进程一个 `SemaphoreSlim(1,1)`。调用方以截止时间等待获取；获取成功后在线程池执行原生调用，并以同一截止时间等待其完成。
- 截止前完成：返回结果并释放。
- 超过截止：本次返回 `timed-out`；信号量由在途调用的完成延续释放，直到原生调用真正返回。
- 在途期间新调用获取失败：返回 `call-pending`，不调用原生层。
- 截止时间：`EnumerateSchemes` 5 秒（实测 0.6–0.8 秒），其余 2 秒。
- 资源上限：每进程至多一个被阻塞的线程池线程；无队列、无 Timer。

**对外操作**（返回 `PowerSchemeResult<T>`：`Succeeded`、`Value`、`FailureKind`∈{`none`,`api-error`,`timed-out`,`call-pending`}、`Win32Error`、`ElapsedMilliseconds`）：

| 操作 | 原生调用 |
|------|----------|
| `GetActive()` | `PowerGetActiveScheme` + `LocalFree` |
| `SetActive(Guid)` | `PowerSetActiveScheme` |
| `ReadName(Guid)` | `PowerReadFriendlyName`（先取大小，Unicode） |
| `EnumerateSchemes()` | `PowerEnumerate` 循环至 `ERROR_NO_MORE_ITEMS`(259) |
| `ReadValueIndex(Guid scheme, PowerSchemeSetting, PowerSource)` | `PowerRead{AC,DC}ValueIndex` |
| `WriteValueIndex(Guid scheme, PowerSchemeSetting, PowerSource, uint)` | `PowerWrite{AC,DC}ValueIndex` |

`PowerSchemeSetting` 只有两个固定值：`VideoNormalLevel`（SUB_VIDEO `7516b95f…` / `aded5e82-b909-4619-9949-f5d71dac0bcb`）与 `AdaptiveBrightness`（`fbd9aa66-9553-4097-ba44-ed6e9d65eab8`），不接受任意设置项 GUID。

**健康事实（只读）**：`IsCallInFlight`、`InFlightSince`（单调时间戳）、`LastSucceededAt`（单调时间戳）。供后续活性保障设计使用，本次不消费。

**测试替身**：internal 静态 `NativeOverrideForTests`（替身原生层接口），可控制返回码、阻塞与时序，取代现有按命令行字符串模拟 powercfg 的 `RunOverrideForTests` / `RunAsyncOverrideForTests`。

### 2.2 现有入口（职责不变，底层改调 Owner）

| 入口 | 迁移后 | 阶段 |
|------|--------|------|
| `PowerCfgExecutor.ProbeCurrentPlan` / Runner 计划读取 / 最终观测 | `GetActive`；名称由 `ReadName` 补充 | 一 |
| `PowerCfgExecutor.ApplyAndVerifyAsync` | `SetActive`，随后 `GetActive` 验证，保持 0/250/750ms 至多三次 | 一 |
| `SyncPlanBrightnessAsync`（切换前写目标方案） | `WriteValueIndex` AC、DC 各一次；文档规定对活动方案的修改需 `SetActive` 生效，切换紧随其后，语义一致 | 一 |
| `GuardianFirstRunInitializer` | 能力探测改为 `ReadValueIndex(Balanced, VideoNormalLevel, AC)` 返回 0 即支持（只读）；关闭自适应亮度与亮度对齐用 `WriteValueIndex` | 一 |
| Guardian 计划名称（`LoadPlanCatalogAsync`） | 对已配置方案与三个内置方案逐个 `ReadName`，不列举，去掉 2 秒目录预算 | 一 |
| `PowerPlanCatalogProvider`（Settings 目录） | `EnumerateSchemes` + `ReadName`，再补隐藏的内置方案名称；对外签名与重试语义不变 | 二 |
| Tray `PowerPlanActivator` | `SetActive`（带截止，修复当前无超时 `WaitForExit`） | 二 |

`PowerCfgProcessRunner` 第二阶段后仅剩 `RunProcessAsync` 供 WMI 探测使用；powercfg 包装与文本解析器（`ParseActiveSchemeGuid`、`ParsePowerSchemeList`、`PowerPlanCatalogParser` 的 powercfg 部分）删除。

## 3. 失败语义与可观测性

**`PowerPlanFailureKind`**：删除 `ProcessStartFailed`、`CommandFailed`、`ParseFailed`；新增 `ApiFailed`（携带 Win32 错误码）、`CallPending`；保留 `TimedOut`、`VerificationMismatch`。

**告警文案**改为中文标签，例如：`电源计划探测失败，停止本轮切换：电源服务调用失败（错误码 5）`、`……：上次电源服务调用仍未返回，本轮未发起`、`……：电源服务调用超时（2000ms）`。不再输出枚举名。

**日志**：沿用 `NativeProbeFailed`/`NativeProbeRecovered` 与操作名 `plan-read`、`plan-apply`、`plan-brightness-ac/dc`、`plan-catalog`。`failureKind` 取值为 `api-error`、`timed-out`、`call-pending`；新增属性 `win32Error`。电源 API 操作的文案不提“子进程”：超时与在途写“电源服务调用仍在进行，期间不发起新调用，返回后自动恢复”，错误码失败只写错误码。恢复沿用按操作的现有恢复事件，不新增事件类型。

**不变**：`guardReason=probe-failed`；生命周期门控 `PWR-LIFE-SUSPEND`（措辞由 `powercfg /setactive` 改为 `PowerSetActiveScheme`）；切换后验证、最终观测、每分钟合并告警。

**失败路径**：

| 情形 | 结果 |
|------|------|
| API 返回错误码 | `ApiFailed` + 错误码，本轮停止切换，下一轮重新探测 |
| 调用超时 | 本次 `TimedOut`；在途期间其他调用 `CallPending`；返回后自动恢复 |
| 电源服务长期卡住 | 每进程至多一个阻塞线程，持续合并告警；健康事实可读，升级处理由活性保障设计负责 |
| 切换后读回不一致 | `VerificationMismatch`（与现状一致） |
| 进程退出时有在途调用 | 随进程回收，无持久状态 |

## 4. 契约与门禁

- 真源表新增：**电源方案操作 → `PowerSchemeService`**（唯一入口）；禁止项：生产代码启动 `powercfg.exe`、在 `PowrProfNative` 之外 P/Invoke `powrprof.dll`、API 失败回退外部进程。
- 新增不变量 `PWR-INV-SCHEME-API`：电源方案操作经同一进程内的单飞执行器；超时调用返回前阻止新调用；恢复以在途调用返回为准，不以时间推断；不回退外部进程。
- `PowerPlanCatalogProvider` 仍为计划目录唯一真源（M-04、S-05 保持成立），底层改为 Owner。
- 门禁分阶段：第一阶段 Engine 源码不得出现 `powercfg`，Settings/Tray 为登记例外；第二阶段删除例外，`src` 全部不得出现 `powercfg`。
- `PWR-NATIVE-PROBE-ISOLATION` 中关于 powercfg 的部分在第一阶段后改写为仅适用于 WMI 子进程。

## 5. 分阶段交付

1. **第一阶段（Engine 核心链路）**：建立 Owner；替换计划读取、切换验证、亮度同步、最终观测、首次初始化、Guardian 名称；删除 Guardian 的 powercfg 闩锁路径。交付后本次事故路径不再启动子进程。
2. **第二阶段（Settings / Tray）**：替换计划目录与一键高性能；删除 powercfg 包装与解析器；门禁收紧到全部 `src`。
3. **第三阶段（WMI 闩锁收缩）**：将[闩锁方案第 2 版](2026-11-19-process-cleanup-reopen-plan.md)缩减为仅覆盖 WMI 子进程（EOF 证明与移交仍适用），重新出计划。

每阶段单独出实施计划、单独全量验证与验收。

## 6. 测试与验证

按 `PROJECT-RULES.md` §5.1：先以不改变行为的骨架引入新类型，RED 必须因行为缺口失败。

**执行器（替身原生层，事件屏障固定时序）**：截止前成功；超时返回 `timed-out`；在途期间新调用立即 `call-pending` 且原生层未被调用；在途返回后下一调用恢复；任意时刻至多一个在途调用。

**结果映射**：非 0 返回码映射 `ApiFailed` 并保留错误码；`GetActive` 分配的内存经 `LocalFree` 释放（替身记录释放次数）；名称缓冲按大小查询后读取。

**Runner 行为迁移**：现有基于 powercfg 命令行替身的测试（集中在 `GuardianProbeHardeningTests`、`PowerPlanOperationTests`、`GuardianPowerLifecycleIntegrationTests` 等）迁到替身原生层，断言改为新分类，行为覆盖不减；迁移前后各跑一次并对照用例清单。

**真实 API 集成测试（自动化，永不调用 `SetActive`）**：

- 只读：`GetActive` 与 `powercfg /getactivescheme` 读数一致（测试中允许调用 powercfg 作为对照）；能读到内置方案名称；能读到 `VideoNormalLevel`。
- 写入：`PowerDuplicateScheme` 复制临时方案 → 写 `VideoNormalLevel` AC/DC → 读回相等 → `PowerDeleteScheme` 删除（`finally` 中执行）。

**成本**：记录 `GetActive` p50/p95/max 与 `EnumerateSchemes` 耗时作为基线与预算。

**验收（需用户配合）**：

- 安装后由提升的 Engine 实际切换：WPS 前台切到高性能并确认生效、后台观察期、返回检测。
- 等价性对照：应用内切换、系统设置手动切换、电源模式滑块介入时，对比 API 与 `powercfg /getactivescheme`。
- 至少一次睡眠/唤醒后核对恢复与耗时；未执行则标 `NOT RUN`。
- 发版前全量 `Run-Tests.ps1`，核对 summary JSON。

## 7. Change Impact Matrix

| 影响域 | 结论 |
|--------|------|
| Function | 计划读取/切换/亮度设置项改走 API；失败分类与文案变化；去掉首次初始化写入探测副作用 |
| Architecture | 新增唯一 Owner `PowerSchemeService`；现有入口职责不变；删除 powercfg 包装与解析器 |
| Data | 无配置/状态格式变化；日志 `failureKind` 取值变化、新增 `win32Error` |
| Performance | 计划读取由约 24ms 降至约 0.1ms；Guardian 不再列举方案；Settings 列举耗时不变（服务端成本） |
| Resource | 每进程至多一个阻塞线程；无子进程、管道、句柄移交 |
| Concurrency & Time | 每进程单飞；截止使用单调时间；在途期间拒绝而非排队 |
| Reliability | 消除子进程故障类；电源服务卡住时受控降级并自动恢复 |
| Recovery | 在途调用返回即恢复；无需清理证明或重启 |
| Compatibility | 对外签名不变；旧日志读者忽略新增属性；`failureKind` 新取值需日志中心可显示 |
| Security | 不新增提升；API 权限与 powercfg 相同前提，需验收核实 |
| Observability | 错误码、超时、在途分别可见；健康事实可读 |
| Operations | 安装/升级/回滚流程不变 |

## 8. Critical Module Check

命中模块：后台任务、进程与计划任务（L0）——移除核心链路的外部进程；时间（L0）——截止与健康事实使用单调时钟。Owner：`PowerSchemeService`（电源方案操作）、`GuardianIterationRunner`（守护动作）、`PowerPlanCatalogProvider`（目录）。

保持的不变量：

1. 电源方案操作只有一个入口，生产代码不启动外部进程完成同一能力。
2. 每进程至多一个在途原生调用；超时调用返回前不发起新调用；恢复以返回为准。
3. 生命周期门控、切换后验证与最终观测语义不变。
4. 失败分类不伪装（不把拒绝或超时报告为启动失败）。

## 9. 未决与风险

- 写入接口在提升的 Engine 下的返回码、组策略限制：第一阶段验收核实。
- 电源模式滑块覆盖方案（此前现场出现过 `961cc777…`）下 API 与 powercfg 的读数是否一致：验收对照。
- `PowerRead*ValueIndex` 对不存在设置项的错误码文档未载明：以本机实测为准写入测试。
- 测试迁移量大：通过“迁移前后用例清单对照”防止覆盖丢失。
