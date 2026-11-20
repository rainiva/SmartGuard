# SmartGuard 架构契约 — 单真源与单入口

**PWR-NATIVE-PROBE-ISOLATION（2026-11-04）：** `BrightnessService`/`BatteryInfoProvider` 仍为业务入口，所有 WMI 物理调用迁入 Engine 自身固定 `--wmi-probe` 子命令，先于根目录/配置/日志/单实例初始化分派。父端 `WmiProbeProcess` 单飞、零排队、请求2秒/防御上限2.5秒、按操作冷却30秒；清理未确认后关闭后续启动；子命令在WMI前开启独立3秒退出守卫，避免父进程死亡后无限存活。底层复用 `PowerCfgProcessRunner.RunProcessAsync`，每路输出最多65536字符，超限失败，覆盖退出与管道的截止，预取消不启动，有界终止并报告确认状态；两路reader由读任务显式释放，晚到读任务仍持有释放责任。无整个轮次 Task.Run 后超时遗弃、无晚到结果恢复旧授权；已进入驱动的动作不能承诺撤销。电池保持原生优先，不被亮度不支持的冷却覆盖。稳定等待最多2秒，亮度恢复总预算3秒、最多5次、每次延迟0–1000ms；未读回亮度保持未知。AC/DC每次写前重验生命周期，目录总预算2秒且清理不明立即取消全部剩余补全。`NativeProbeFailed`/`NativeProbeRecovered` 经既有有界日志管线记录操作/分类/耗时预算/退出/清理/按操作按区间抑制/恢复；固定子协议错误码保留，不输出原始路径。仅完成语义解析才能宣布恢复，旧计划失败告警同步按分钟合并。该约束替代先前仅250ms枚举的临时防线；子命令内仍保留枚举时限和对象释放。本地文件 IO/低延迟 Win32 调用不属于硬实时保证。门禁：`GuardianProbeIsolationArchitectureTests`、进程/WMI/守护日志行为测试；[加固计划与证据](implementation-plans/2026-11-04-guardian-isolation.md)。

**探测取消与最终事实补充：** 上述3秒为恢复工作预算；取消后只给原执行至多500ms清理证明余量，迟到业务结果作废，确认清理后不能误关健康WMI能力。恢复未收敛仍保留实际读回，日志/心跳/status/通知使用同一最终亮度，未知在托盘显示“亮度未知/尚未确认”。计划读取/目录以语义解析后的整体结果决定恢复；`plan-apply`恢复仅表示 `PowerSetActiveScheme` 调用恢复（`recoveryScope=command-execution`），不宣称计划已切换，后者仍取最终GUID验证。

**窗口验证边界补充（2026-10-05）：** 当前共享采集确认的是前台外层窗口所属EXE；WPS整合窗口可由wps.exe承载wpspdf.exe等组件的子窗口，因此不能把宿主识别等同于内部文档组件验证。候选wps.exe显示为WPS Office，设置交互解释宿主限制；精确路径准入语义未变。未来内部组件支持必须同步扩展唯一采集入口和Engine/Settings共同语义，不能仅调整设置验证结果。名单/搜索列表的嵌套滚轮由ScrollViewerSmoothScroll按方向让原生内层优先处理，边界交给外层；不新增监听或列表扫描。证据与未完成项：[排查记录](implementation-plans/2026-10-05-nested-wheel-and-wps-host.md)。

**PROGRAM-WINDOW-IDENTIFICATION（2026-10-05）：** Settings 以 Compile Link 编译 Engine 的 `ForegroundProgramReader.cs` 和纯事实 `ForegroundObservation.cs`，唯一物理实现不变、不引用 Engine 可执行项目。`SettingsForegroundCoordinator` 同类型 Identification partial 拥有一次窗口识别/验证/路径修正交互；可取消5秒切换延迟、一个在途任务，关闭取消且丢弃晚到结果，不增加常驻前台监听或Timer。仅用户确认具体exe后经既有名单/防抖保存链变更；正常路径错配可添加组件，旧路径不存在或不可访问时可确认替换所选一项，确认前后复核原条目身份，不扩张软件目录/厂商/子进程授权。UI条目使用对象身份避免WPF复用重载前的旧选择；验证状态只在当前设置会话展示，不持久化、不宣称Engine已采用规则。名单载入、添加或显式刷新请求一次路径可用性检查，经原5秒单在途状态读取后台执行，最多128项/合作两秒预算，拒绝UNC；不逐周期扫描，仅更新同一对象仍在名单的条目。`SettingsProgramSearchCoordinator` 显式刷新仍单在途发现；候选标记窗口/快捷方式/同软件组件来源，不视作匹配证据。门禁：`ProgramWindowIdentificationArchitectureTests`、`SettingsProgramIdentificationTests`、`SettingsProgramSearchTests`、`SettingsForegroundUiTests`。详情：[执行记录](implementation-plans/2026-10-05-program-window-identification.md)。

**FOREGROUND-EXIT-OBSERVATION（2026-10-05）：** `ForegroundObservationSec` 默认180秒、范围1–3600秒，由既有 Repository 保存并纳入规则内容指纹；旧配置缺字段使用默认值。`GuardianLoopIterationState` 的同类型 Foreground partial 是观察运行状态唯一 Owner，轻路径和完整轮次共享，只保存单调起点、最近程序文件名、阶段与指纹；`PolicyEngine` 保持准入和动作决策唯一入口，观察只将已拒绝的未命中原因映射为登记代码 `foreground-observation`，从不授予资格。只有已确认名单内转名单外开始；外部窗口切换不续期，返回任意名单程序立即取消；超时仍未命中保持不干预。规则变更、坏配置、暂停/人工、探测失败、周期异常及恢复丢弃旧观察状态。截止参与既有 Coordinator 等待，不新增 Timer/队列或逐秒日志；查询仍遵守一秒轻路径/五秒完整节流，完整轮次至多增加一次有界128项准入遍历。最新观察阶段/时长复用准入事件和原两次排出上限，文件名仍限128字符；未知身份不得宣称确认后台，规则保存与 Engine 实际采用分别记录。两端按稳定代码显示“退出观察中”。WPS 候选优先实际窗口程序，只检查其目录的四个已存在且描述为WPS的文档exe，四组件独立选择，排除启动器，不修改已有名单或扩张授权；搜索预算不变。门禁：`GuardStatusCodesArchitectureTests`、`ForegroundObservationWindowTests`、`GuardRuleLoggingTests`、`GuardForegroundRestrictionConfigTests`、`SettingsForegroundUiTests`、`SettingsProgramSearchTests`。

**LOG-INV-COLD-FORMATTING（2026-10-05）：** LogJsonlFormatter 仍是 JSONL 格式唯一 Owner；固定字段使用 Utf8JsonWriter，不在首次业务日志创建反射序列化元数据，也不靠预热掩盖冷预算。保持 schemaVersion、字段顺序、UTC round-trip 时间、可选字段省略、中文/控制字符转义、异常链和 JsonElement 属性兼容；最大 JSON 深度 64，方法内缓冲随事件字节增长且不跨调用持有，不新增共享 writer/池/线程/IO。环形、单条和落盘预算仍归既有 Owner。门禁：LoggingRepairArchitectureTests、LogJsonlFormatterTests；性能另用无预热独立新进程、2000 项满高队列与持有主日志互斥，P95≤20ms、Max≤100ms。

**LOG-INV-EXTERNAL-OBSERVATION（2026-10-05）：** GuardianIterationRunner 的 ExternalPlanChange 与外部变更通知只描述已观测事实，不预告纠偏；后续动作以 GuardStateChanged/PowerPlanSwitched 的实际结果为准。既有 iteration state 只增加一个可空 GUID，确认接受或用户级别过滤后去重同一连续外部计划；拒绝/背压不确认，新的 GUID 或外部判定解除后可重新记录。该字段仅抑制日志，不改变 LastKnownGuid、决策、暂停、动作或通知投递/冷却，不新增历史集合、探测、IO 或 Owner。门禁：ExternalPlanObservationLoggingTests。

**LOG-INV-PHYSICAL-PAGING（2026-10-04）：** LogTailReader 的主日志游标及 HasNewer 按 catalog 中的物理来源顺序/行偏移推进，显示仍按事件时间；相同或倒退的时间戳不能跳过物理行或把文件末尾误报为尚有更新。只有实际附加历史存在时进入合并读取；新创建的 fallback 可加入当前 reader，但保持已有主源身份。合并 newer 每源仅取物理前缀，latest/连续 older 每源仅取物理后缀，再按时间展示；不以旧 wall clock 排除新追加记录或同秒历史行。另一来源上已返回的重复 eventId 不能确认本来源中间行已消费；只推进实际选中的物理范围。去重限定于当前页及既有有界显示窗口，不新增全历史 eventId 集合；只保存两份当前边界与源偏移。LogViewController 在空边界页仅收敛该方向标志，保留另一方向的游标，不能因此丢失返回物理另一端的能力。门禁：StartupFallbackHistoryTests、LogViewControllerTests。

**LOG-INV-DIAGNOSTIC-FLIGHT（2026-10-04）：** SettingsWindowController 拥有一项 SettingsDiagnosticExportSession，About 与 Logs coordinator 共享；独立 host 测试才自有 session，并在 Dispose 取消。session 从保存对话框前占有 busy，取消后直到工作结束才释放，Cancel(expectedOrigin) 只影响匹配来源，关闭取消所有来源但不在 Dispatcher 等待；其他窗口独立。GetDiagnosticPaths 只返回已解析值/规范默认路径，不初始化页面或读配置。两个 coordinator 返回 Task，仅点击边界 async void；UI 只捕获路径/版本/筛选/关联/有限窗口，后台新建同一 LogTailReader 的独立实例、catalog 并 await 既有 exporter，不共用刷新可变读者。About 默认附带四类日志。导航离开来源页取消自身；销毁/非活动页面忽略晚到结果，busy 状态控件只在活窗口更新，无新 Timer 或任务队列。取消不能承诺中断正在执行的 OS 同步调用，最终目标提交语义遵循 LOG-INV-DIAGNOSTIC-EXPORT。门禁：SettingsDiagnosticExportCoordinatorTests、SettingsDiagnosticExportLifetimeTests、LoggingRepairArchitectureTests。

**LOG-INV-DIAGNOSTIC-EXPORT（2026-10-04）：** DiagnosticBundleExporter 仍是唯一导出 Owner；Files partial 仅拆分同目录同命名空间的同一类型。配置、状态和附加日志分别容错，读取/脱敏失败仅输出固定 readStatus/omittedReason，不回退原文；DiagnosticRedactor 递归处理数组、属性、状态和完整异常链，深度上限 64。主 JSONL 与文本分别计脱敏后 UTF-8+换行，每份至多 512KiB，保留队列与 eventId 集合均随字节预算淘汰；截断标记及 omittedRecords 明确记录省略，主源中途失败保留已读资料。DiagnosticLogTailReader 只读附加活动与最多三归档，合用 260KiB 读取/256KiB 输出预算，跳过不完整行；后续已有 writer 的遗留维护另有 1MiB/32 候选上限，不混淆读量。配置读取 8MiB、状态 256KiB，hash/includedBytes 取实际 ZIP 条目。四类资料标志独立，IncludeCrashRingLog 默认 false。临时 ZIP 在目标同目录，取消在提交前检查，目标创建竞态失败关闭；覆盖使用 File.Replace 与恢复备份，不先删除旧目标。complete 只在提交成功后报告；提交后观察者失败不逆转结果。门禁：DiagnosticBundleExporterTests、DiagnosticLogTailReaderTests、LoggingRepairArchitectureTests、LoggingWorkbenchArchitectureTests。

**LOG-INV-SUPPLEMENTAL-BOUNDS（2026-10-04）：** SupplementalLogPolicy 固定附加日志活动 1MiB、3 归档、7 天、单批 128KiB，正常维护完成每类 4MiB。SmartGuardLogWriter.TryAppendSupplementalLines 共用命名互斥/UTF-8 无 BOM；日常在追加前按日期/字节轮转，只扫描至多 32 候选，合法自身归档才清理。独立 MaintainSupplementalLog 返回 Succeeded/Completed/ExaminedFiles；巨大旧文件只读 1MiB 内尾部完整行，不能转移巨大归档伪称有界。消费首批及既有 5 分钟健康批次触发，未完成发健康事件；诊断准备复用同入口。紧急模式零等待、无重试、无扫描，在容量不足时原子替换为标记+最新批次，失败保留旧文件。Crash 主日志只在初始化上下文已有预算且无须轮转时尽力追加；全局/桌面异常回调不读配置。截断摘要不算原事件，fallback 按已确认选择计数，剩余 Lost；环形默认 500 条，单条 4KiB、总计 2MiB。LogSourceCatalog.StartupHistoryOldestFirst 独立最多活动+3归档，StartupFallback 是活动投影，不混入 MainHistory。既有 LogTailReader latest/newer/older/snapshot 可见更新回退、按 eventId 去重；顺序分页只保存两份当前边界和各源偏移，合用请求字节预算，不保存页历史；源身份/轮转仍归已有 catalog/snapshot 生命周期。共享 UTF-8 尾扫不能将切开的首行标为完整或重复产出。门禁：LoggingRepairArchitectureTests、SupplementalLogRetentionTests、StartupFallbackHistoryTests、LogUtf8TailBoundaryTests。

**CFG-INV-LOG-AFTER-COMMIT（2026-10-04）：** 暂停与设置保存仍分别经 `ConfigMutationService` / `SettingsSaveCoordinator` 汇入 `GuardConfigRepository.Save`。暂停变更使用配置副本；提交成功后才经 `AppendInfoLog` 发 `ConfigMutationLogged`，该事件只表示已保存，不能表示 Engine 已采用。提交失败使读取缓存失效，经同一 Repository 的 `AppendMutationFailureLog` 尽力发已登记 `ConfigMutationFailed`（Policy、Warn、固定 `failureKind=save-failed`），原异常上抛。日志的准备/路径/落盘/回退全部尽力隔离，不能逆转提交结果。前台规则开关或规范化名单内容变化成功保存后只记数量/开关摘要，无普通日志完整程序路径；无变化不重复记录。AutoStart 同步保持既有 Owner 和语义，提交后失败明确反馈“配置已保存，但开机自启同步失败”，不得记录配置未提交或回滚。门禁：`LoggingRepairArchitectureTests` 与保存/缓存行为测试。

**状态：** 生效  
**CFG-INV-INITIALIZATION-FAILURE（2026-10-04）：** Engine 启动仅在 Repository 详细读取确认 missing 后尝试默认配置首次提交；访问拒绝、目录占位、解析/IO 失败不得被 File.Exists=false 当作缺失覆盖。初始化提交失败保留 unavailable，进入既有轮次/日志/状态的受控观测，不使日志和守护主循环在报告前退出。首次提交能力保持，默认配置不替代失败读取授权。门禁：`GuardInterruptionLoggingTests` 初始化边界两场景。
**LOG-INV-CONFIG-UNAVAILABLE（2026-10-04）：** Loop 的配置读取不再 LoadOrDefault；无效配置始终不可授权。Loop 私有不可变 LogSettingsSnapshot 值类型只保存 MinimumLevel/MainPath/MainRetention/StartupPath，短锁发布/读取，不保存旧 GuardConfig 或人工期限。首次无有效配置按 Warn、无 primary、规范 fallback 入既有管线；消费者 primaryAvailable=false 直接回退，不推进主失败/冷却。最后有效日志设置仅用于路由/过滤，配置损坏不能恢复动作资格。规则采用摘要在 Loop drain 独立尽力提交，仅有效读取、指纹内容变化或恢复产生 ConfigReloaded；拒绝保留摘要原时间，用户过滤视为已观察，无每轮刷屏。配置失败 GuardStateChanged 使用固定类别，稳定失败只一次；恢复摘要只一次。心跳消费最终 reason/target/actual，仅已知相等且允许守护的理由表达符合目标；状态变化普通记录只称当前计划，无通用“计划正常”。门禁：`LoggingRepairArchitectureTests`、`GuardInterruptionLoggingTests`、`GuardianLogMessagesTests` 与既有心跳/恢复回归。
**LOG-INV-RULE-OBSERVATION（2026-10-04）：** `PolicyEngine.EvaluateAdmission` 在既有匹配遍历中返回零起始 MatchedRuleIndex；`Evaluate` 将它带入最终 GuardDecision，Runner 不为日志再匹配。Engine 私有 `GuardObservationTracker` 只持有最新 pending / accepted 的准入、执行两部分与观测 UTC，不保存配置/窗口历史、不授权动作。Coordinator 轻路径只提交内存事实，A→B 的 admitted 相同也不丢变化；Loop 在既有周期返回、等待前排出准入→执行至多两次，唯一结构化 WriteLog/EnqueueLog 无 IO 旁路。各部分独立确认；拒绝保留最初时间，用户过滤终止重试，级别变化最多一份当前摘要；规则指纹/gate 或新准入事实淘汰过时执行结果。程序只记录 128 字符内文件名+名单索引，无完整路径；最终已知目标/实际 GUID 与固定 actionResult 仅来自现有最终复核事实，轻路径不借用旧执行值。Runner 的内部统一六参数回调包含 properties/observedAtUtc；Loop 保留观测 timestamp 到正式 JSONL。新 Policy 事件登记于 EventNameRegistry/LogEventChannels。门禁：`LoggingRepairArchitectureTests`、`GuardRuleLoggingTests` 与前台/策略/最终发布回归。
**CFG-INV-DETAILED-LOAD（2026-10-05 更新）：** `GuardConfigRepository.TryLoadDetailed` 是受控读取结果唯一入口，`TryLoad` 只投影其 Config；缺失、解析、访问、IO、结构无效与未知失败均不返回旧配置。规则内容指纹只在实际重读后于 Repository 串流计算，与有效读取结果一同缓存；仅包含限制开关、退出观察时长及按顺序规范化的名单，不包含主题，也不用于授权。元数据失败在同一读取边界收敛；日志/Engine 不另读配置或保留可授权旧配置。门禁：`LoggingRepairArchitectureTests` 与 `GuardConfigDetailedLoadTests`。
**LOG-INV-STATUS-HEALTH（2026-10-04）：** `LogPipeline.SampleLogHealth` 在内存中投影已确认分区、最近失败与当前拥塞，不清零历史；`LogHealthStatus` / `LogHealthCodes` 归 Contracts，所有 JSON 属性显式 camelCase。Engine 主循环经既有 `StatusPublisher.PublishLogHealth` 发布，不新增 Timer/文件/写者；健康单独更新保留最后真实守护 timestamp。代码变化及首次丢失/拒绝触发，稳定代码的计数/采样时间随既有状态变化或 60s freshness 发布。日志和状态故障局部收敛，不结束守护循环。Tray 克隆保留不可变健康值，健康代码或独立新鲜度变化才更新短提示；Settings 健康/空闲状态读取归 `SettingsLogRefreshPipeline` 既有单 flight 后台任务，无新日志时复用相同 flight 所有权做状态采样，忙碌时不额外排队，不新增定时器。缺失/未知/非法计数或不可信时间展示未知，健康提示不修改守护来源/原因。门禁：`LoggingRepairArchitectureTests`、`LoggingHealthPublishingTests`、`LogPipelineStatusTests` 与两端展示/后台刷新行为测试。

**关联：** [AGENTS.md](../AGENTS.md) §8、§11；Pester `Phase 8`；`SmartGuard.Architecture.Tests`

本文档是 SmartGuard **配置、路径、状态、生命周期** 的唯一架构权威。新增字段、入口或共享类型必须先更新本文档并补充门禁测试（TDD RED）。

---

## 1. 真源注册表

**PLAN-FINAL-OBSERVATION：** `GuardianIterationRunner` 在已验证切换及亮度等待/恢复结束后，经既有计划探测入口追加一次有界最终观测，再决定成功日志、通知和状态。最终不匹配或不可读不得沿用瞬时成功；失败结果携带的实际 GUID（未知为 null）也进入发布，失败轮次仅记录失败事件、不追加“计划正常”心跳，后续成功轮次恢复正常心跳。该观测不保证后续外部修改下持续一致，不追加 setactive 重试；每个切换轮次最多增加一次探测（既有进程超时5秒），普通未切换轮次不增加探测，取消沿用已有 token。

| 概念 | 权威来源 | 允许读者 | 允许写者 | 禁止模式 |
|------|----------|----------|----------|----------|
| 安装根 `{root}` | `InstallRootResolver` | 全部 exe | — | 各 exe 内私有 `RootResolver` |
| 配置文件 | `{root}/SmartGuard.config.json`；内部提交实现 `GuardConfigAtomicFileWriter`：既有文件 `File.Replace`，首次创建 `File.Move`，提交失败保留最后有效配置并上抛 | 全部经 `GuardConfigRepository.TryLoad` | `GuardConfigRepository.Save`、`ConfigMutationService` | Engine 直调 `GuardConfig.LoadFromFile`；配置提交不得先删正式文件；不把原子发布当作跨进程读改写事务或断电持久性承诺 |
| 运行时状态 | `{root}/SmartGuard.status.json`；守护字段 `guardSource`/`guardReason` 取值限 `GuardStatusCodes` 登记表，`foregroundEventsAvailable`（null=不适用/未知） | `StatusJsonReader` / `StatusStore`（Tray `TrayDisplayState` 相等判定 + Clone、Settings 展示均含新字段；字段缺失/代码未登记/状态过期 → 显示"未知"，不得显示确定的正在守护） | `StatusPublisher`（Engine，唯一写者；相等判定含守护字段） | Settings/Tray 各自解析 JSON；Engine 携带展示文案；UI 用未登记代码或缺失字段显示确定的守护声明 |
| 电池与供电状态 | `BatteryInfoProvider`；Windows `GetSystemPowerStatus` 为主来源，WMI 仅作未知字段的可选补充 | Engine 轮询、电源事件、状态发布 | — | WMI 异常覆盖原生状态；独立采集入口；异常直接伪造 `100% / 插电` |
| 电源生命周期 | `PowerLifecycleStateMachine`（Engine 进程内） | `PowerEventWakeListener` typed signal；`GuardianLoop` 消费有界 directive batch | `PowerLifecycleStateMachine` | Suspend/Resume 压缩为 AC/DC；UI/PolicyEngine 自建生命周期；转换批次无界增长 |
| Awake 空闲时间 | `IdleTracker` + 注入的 `TimeProvider` | `GuardianIterationRunner` | `IdleTracker` | 睡眠墙钟直接进入 300/900 秒阈值；Settings 重复计算策略 idle |
| 主日志路径 | `config.LogFile` 或 `SmartGuardPaths.DefaultLogFile(root)` | Engine、Settings 日志页 | Engine、`AppendInfoLog` | Settings 硬编码 `SmartGuard.log` |
| 启动日志路径 | `SmartGuardPaths.StartupLogFile(root)` | Engine CLI、Settings/Tray 暂停回退 | — | `InstallCommands` 硬编码 `SmartGuard.startup.log` |
| 日志行追加落盘 | `SmartGuardLogWriter.TryAppendLine` | 全部进程 | `SmartGuardLogWriter` | 各处 `File.AppendAllText` 直写日志 |
| 可执行文件路径 | `SmartGuardPaths.EngineExe` / `TrayExe` / `SettingsExe` / `CliExe` | 注册、shell、启动 | — | `Path.Combine(root, "bin", "*.exe")` 散落 |
| 进程镜像名 | `SmartGuardPaths.ProcessImageNames` | `EngineLifecycle` | — | `EngineLifecycle` 内独立 exe 名字面量列表 |
| Settings XAML | `src/SmartGuard.Settings/SmartGuard.Settings.xaml`（编译嵌入/BAML） | `SettingsXamlLoader.TryLoadEmbeddedWindow` | Settings 项目源文件 | runtime 读取 `installRoot\\lib\\SmartGuard.Settings.xaml` |
| 品牌图标 | `assets/branding/official/*.png` 官方源帧 -> `assets/branding/SmartGuard.ico`（经项目嵌入资源消费） | `BrandIconLoader`、`AppBrandIcon`、Tray 图标；允许的 Windows consumers 仅 `ApplicationIcon`、`window.Icon`、`Tray.exe,0`、`IconUri=SmartGuard.ico`（安装时经 `BrandIconLoader.SaveEmbeddedIconToFile` 部署到 `{root}/bin/SmartGuard.ico`，为 Toast 标题栏图标真源）；Toast XML 不得含 `appLogoOverride`/`<image>` | official 源帧 + canonical `.ico` + csproj 引用 | runtime 读取 `installRoot\\lib\\SmartGuard.ico`；`IconUri=Tray.exe`（.exe 不被 toast 识别）；`SystemIcons.Shield`、caller-supplied fallback，或 live `assets/icon/**` 草稿树 |
| 初始化标记 | `SmartGuardPaths.InitMarkerFile(root)` | Engine 首次运行（标记只管一次性亮度对齐写入；亮度设置项能力每次启动经 `GuardianLoop.RunStartupInitialization` 只读探测，Runner 经委托读取该字段，不保存副本） | Engine | `".SmartGuard.initialized"` 字面量散落 |
| 提升拒绝标记 | `SmartGuardPaths.ElevationDeclinedMarkerFile(root)` | Engine install/uninstall | `ElevationDeclinedMarker` | `".SmartGuard.elevation-declined"` 字面量散落 |
| 脚本/Inno 文件名 | `SmartGuardPaths.*FileName`（C# 权威）→ `InnoDistributionManifestWriter` 生成 `installer/generated/SmartGuard.Distribution.Constants.iss.inc` | ps1/Inno | — | 脚本与 Inno 硬编码文件名；`SmartGuard.iss` 手写 live exe/task literal |
| 计划任务名 | `ScheduledTaskRegistrar.GuardianTaskName` / `TrayTaskName` → generated constants/run include | 注册、恢复、安装 | `ScheduledTaskRegistrar` | 字面量 `"SmartGuard Guardian"` 散落；`SmartGuard.iss` 手写 `/TN` |
| Inno live entry/file-name truth | `SmartGuardPaths.*ExeFileName` + `ScheduledTaskRegistrar.GuardianTaskName` → `InnoDistributionManifestWriter` generated `installer/generated/SmartGuard.Distribution.Constants.iss.inc` / `installer/generated/SmartGuard.Distribution.Run.iss.inc` | Inno 安装流程 | `InnoDistributionManifestWriter` | `SmartGuard.iss` 仅保留安装流程与 UI，不得手写 live exe/task literal |
| legacy cleanup 任务名 | `LegacyScheduledTaskCatalog.TaskNames` | `LegacyTaskCleaner`（经 `InstallCommands`） | — | `LegacyTaskCleaner` 内联旧任务名；live `scripts/*.ps1` 维护第二份名单 |
| 开机自启 UI | `config.AutoStartEnabled` | Settings | `SettingsSaveCoordinator` + `AutoStartService.SetEnabled` | Settings 读 `schtasks`/`SyncFromTasks` 决定展示 |
| 暂停状态展示 | `status.json` `paused`（Tray 菜单与 Settings `tglPaused` 初始展示读源） | Tray、Settings | `ConfigMutationService.SetPaused` | Tray/Settings 读 `config.Paused` 决定展示或切换 |
| 前台限制规则配置 | `config.ForegroundRestrictionEnabled`（默认 `false`）+ `config.ForegroundProgramPaths`（默认空名单）；旧配置缺字段兼容为关闭+空名单；路径规则与规范化单源 = `GuardConfigValidator`（Win32 绝对 `.exe` 路径、`Path.GetFullPath` 规范化、`OrdinalIgnoreCase` 去重、≤128 项、单条 ≤32767 字符） | Engine 决策、Settings/Tray 展示 | `GuardConfigRepository.Save`、`ConfigMutationService` | 第二套路径规则/规范化算法；通配符、相对路径、设备路径条目；非法名单静默降级为无限制或静默丢弃条目 |
| 自动守护决策 | `PolicyEngine.Evaluate`（Domain 唯一自动决策入口：输入空闲/电源/前台事实 `ForegroundObservation`/配置/显式本地当前时间，输出 `GuardDecision`，空目标 = 不干预；决策原因稳定代码 `GuardDecisionReason`）；前台准入子规则唯一产出 = `PolicyEngine.EvaluateAdmission`（`Evaluate` 内部复用，轻路径与完整轮次共用同一名单规则，不复制逻辑；空/非法名单跳过进程查询）；`GuardianIterationRunner` 只消费该决策，守护动作资格 = `GuardConfigRepository.TryLoad` 有效性（`LoadOrDefault` 不得授权动作）；候选切换先完成慢亮度及强制供电探测，再采空闲、有效配置、当前前台及本地时间，经 Evaluate 决定切前同步、切换和纠偏通知；不承诺原子切换 | `GuardianIterationRunner`；`GuardianCycleCoordinator`（轻路径准入比较）；状态发布经 `GuardDecisionCodes` 映射为登记代码写入 status.json（配置不可用=config-unavailable、探测失败=probe-failed、生命周期阻止=blocked-by-lifecycle） | `PolicyEngine.Evaluate` | 第二套预期计划推导（`GetExpectedPlanGuid` 已退役）；Runner 内联阈值/人工期限推导；第二套名单匹配/准入算法；配置不可用时用默认配置猜测授权；使用复核前旧事实授权切前副作用；限制族不干预期间发“将纠偏”通知或不吸收基线 |
| 电源方案操作 | `PowerSchemeService`（Configuration：读/切换当前方案、方案名称、方案列表、`VIDEONORMALLEVEL`/`ADAPTBRIGHT` 读写；真实原生层每进程一个单飞执行器 `PowerSchemeExecutor`，截止 2 秒、列举 5 秒，在途返回前拒绝新调用为 `call-pending`，未进入原生层的工作项在调用方放弃或写准入撤销时跳过；健康事实 `IsCallInFlight`/`InFlightSince`/`LastSucceededAt`） | Engine 守护轮次、首次初始化、Guardian 计划名称、Settings 计划目录、Tray 一键高性能 | `PowerSchemeService` | 任何进程启动 `powercfg.exe`；在 `PowrProfNative` 之外导入电源方案函数；API 失败回退外部进程 |
| 守护状态稳定代码 | `GuardStatusCodes`（`SmartGuard.Contracts` 登记表：sources `manual`/`automatic`/`none` + reasons 全量 kebab-case 代码，含 `config-unavailable`/`blocked-by-lifecycle`/`probe-failed`）；枚举映射唯一入口 = `GuardDecisionCodes`（Engine.Domain） | Engine 发布、Tray/Settings 展示 | Engine（经登记映射） | 第二套代码表或枚举直写字符串；展示文案出现在 Engine；UI 信任未登记代码 |
| 前台事实采集 | `ForegroundProgramReader`（Engine物理源码，Settings链接编译同一实现）：单次 Win32 查询（`GetForegroundWindow` → `GetWindowThreadProcessId` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `QueryFullProcessImageNameW`）后复核前台 HWND/PID 仍一致；空 HWND、进程退出、查询失败或查询期间前台变化一律收敛为识别失败，不旁路回退 | `GuardianCycleCoordinator`（事件轻路径）、`GuardianIterationRunner`（周期完整轮次，经注入 `readForeground`）；Settings单次识别仅用于用户确认名单，不提供Engine事实 | — | 第二套采集实现；reader拥有hook、监听线程、Timer、事件队列或进程身份缓存；读取窗口标题；查询后不释放进程句柄；按事件携带的旧窗口直接执行动作 |
| 前台变化事件 | `ForegroundEventListener`（唯一前台事件监听 Owner：一个 `EVENT_SYSTEM_FOREGROUND` hook + 一个阻塞消息循环线程，注册与卸载都在监听线程；Stop 后回调不再做任何工作）；合并待处理标记由 `GuardianCycleCoordinator` 唯一持有（0→1 转换唤醒 Engine） | `GuardianLoop`（启动/停止/等待退出、降级一次性日志、限制开关同步） | `GuardianLoop.SyncForegroundSubscription`（仅此入口启停监听） | 每事件 Task 或历史事件队列；回调内查询进程/读配置/写日志/触发电源动作；高频路径自动重启监听（重建仅允许限制关闭→开启转换）；hook 未在注册线程卸载 |
| 版本号（UI） | `installer/version.txt` → 各 csproj `<Version>` | Settings About | Packaging bump | 仅 Settings 同步版本 |
| Engine 停止 | `EngineLifecycle.StopForUninstall` | CLI uninstall、Inno `TryStopSmartGuardProcesses`（委托 Engine `--uninstall`，以 `Exec` 成功且退出码 0 为准）、测试 helpers | `EngineLifecycle` | 独立 taskkill/schtasks 脚本副本；Inno 以 `tasklist` 探测结果跳过停止 |
| 计划任务安装跳过 | `ScheduledTaskInstallGate`；`InstallCommands` 读 `SMARTGUARD_SKIP_SCHEDULED_TASKS` / `--skip-scheduled-tasks` | Engine `--install`、Cli `install`、Inno 集成测试 | `ScheduledTaskInstallGate` | skip 时仍调用 `RegisterGuardian`/`RegisterTray`/`DesktopShellRegistration.Apply` |
| 日志页空闲秒数 | `LogViewIdleReader.TryRead` / `TryReadSeconds` | Settings 日志页 | — | Settings 内不得重复 `GetLastInputInfo` 计算 |
| UAC 提升 | `ElevationHelper` + `CliAdminRunner` | `InstallCommands`、`UpdateInstallerLauncher`、`SmartGuard.Cli` | — | 内联 `Verb = "runas"` / VBS 散落 |
| shell 集成 | `DesktopShellRegistration` | Installer / repair | `DesktopShellRegistration.Apply`、`DesktopShellRegistration.Remove` | Tray runtime 自注册、marker 自愈、Inno `[Icons]` 用户快捷方式 |
| 分发清单 | `DesktopDistributionManifest` | `DesktopSuitePublisher`、`StageCommand`、`PayloadCopier`、`StagingLayout`、Inno include 生成（Files/Processes/LegacyCleanup/Constants/Run） | — | 在 publisher/staging/installer 各自维护项目/文件/进程列表；`SmartGuard.iss` 手写 `[Run]` exe/task |
| 运维子命令 | `SmartGuard.Cli` 薄路由 | dev cmd 单行转发、README | `Configuration` 操作真源 | Cli 内嵌 `schtasks`/`taskkill`/spawn 逻辑 |
| 退役敏感字段 | `RetiredSecretFieldRegistry` 仅登记旧配置键 | `GuardConfigRepository.TryLoad`、`DiagnosticRedactor` | `GuardConfigRepository.Save` | 禁止恢复已退役认证功能；读取/保存尽力擦除旧字段，诊断导出继续兼容脱敏 |
| 历史文档/脚本边界 | `docs/archive/**`、`scripts/archive/**`、`docs/superpowers/plans/**` | 人类只读历史快照 | — | 仅允许保留 superseded snapshots；不得作为 live authority；禁止通过 `PHASE-`、`MIGRATION`、日期、单文件名白名单继续豁免旧真相 |

---

## 2. 入口注册表

| 用户操作 | 唯一推荐入口 | 说明 |
|----------|--------------|------|
| 注册计划任务 | `SmartGuard.Engine.exe --install` 或 `SmartGuard.Cli.exe install` | Inno / `Register-AllTasks.cmd` 委托 Cli；实现真源 `InstallCommands` |
| 测试隔离 install（无 live 任务/shell） | `InstallCommands.RunInstall(..., skipScheduledTasks)` + `SMARTGUARD_SKIP_SCHEDULED_TASKS=1` 或 `--skip-scheduled-tasks` | Inno/Pester 默认 skip；skip 时不得 Register 任务或 Apply shell |
| 启动 Engine（生产） | 计划任务 `SmartGuard Guardian` | `SmartGuard.Cli start guardian` → `GuardianTaskRunner`；dev 前台 `Cli start engine` |
| 启动 Tray（生产） | 计划任务 `SmartGuard Tray` | 登录时自动；dev `Cli start tray` / `restart tray`（仅 dev 例外，生产入口受 **E-10 阻断**） |
| 停止 Engine（卸载） | `EngineLifecycle.StopForUninstall` | Engine/Cli `uninstall`、Inno、集成测试共用 |
| 暂停/恢复守护 | `ConfigMutationService.SetPaused` | Tray 菜单、Settings `tglPaused` 均经此 API |
| 打开设置 | `SettingsMainPageLauncher.Open` | Tray、`Cli open settings` 均委托此 API；无独立开始菜单快捷方式 |
| 打开日志 | `SettingsLogsPageLauncher.Open` | Tray、`Cli open logs` 均委托此 API；运行时入口为 `SmartGuard.Settings.exe --page logs`；无独立开始菜单快捷方式 |
| 打开托盘 | `TrayLauncher.Start` | `Cli open tray`、Inno postinstall |
| 查看状态 | `StatusReporter.FormatStatus` | `SmartGuard.Cli status` |
| 发布桌面套件 | `DesktopSuitePublisher.Publish`（末尾 `SystemEventsRidAssetGuard.EnsureAligned`） | `SmartGuard.Cli publish`、`build.cmd` 转发；禁止根目录留下 NuGet `lib` stub 的 `Microsoft.Win32.SystemEvents.dll` |
| 开始菜单/桌面快捷方式 | `DesktopShellRegistration` → 开始菜单仅 `SmartGuard.lnk`（`SmartGuard.Tray.exe --root`）；桌面可选 `SmartGuard.lnk` | 仅 install/repair 写入；禁止 `SmartGuard 托盘/设置/日志.lnk` 等第二 shell surface；shell 损坏必须经 install/repair 恢复，Tray runtime 禁止补写 |
| 保存设置 | `SettingsSaveCoordinator.Save` | 含主题；禁止 `SaveThemePreferences` 旁路 |

---

## 3. 共享基础设施清单

| 类型 | 命名空间 | 职责 |
|------|----------|------|
| `InstallRootResolver` | `SmartGuard.Configuration` | `--root` / `SMARTGUARD_ROOT` / `bin` 提升 / config walk-up |
| `SmartGuardPaths` | `SmartGuard.Configuration` | config、status、log、exe 路径常量 |
| `GuardConfigRepository` | `SmartGuard.Configuration` | 读（含迁移）、全量 Save；经 `FileReadStamp`（写入时间 + 长度，读内容前取得；写入后 1 秒内不信任缓存、一律重读）按需检测外部变更，禁止每实例持有长生命周期文件监听器；Tray `StatusStore` 使用同一规则 |
| `ConfigMutationService` | `SmartGuard.Configuration` | 原子局部写：Paused、ManualHighPerformanceUntil |
| `GuardConfigValidator` | `SmartGuard.Configuration` | `GuardConfig` 保存前校验唯一入口；前台程序路径规则与 `NormalizeProgramPaths` 规范化/去重单源（Win32 绝对 `.exe`、≤128 项、单条 ≤32767 字符、无通配符/设备路径） |
| `PolicyEngine`（`Evaluate`）/ `GuardDecision` / `ForegroundObservation` | `SmartGuard.Engine.Domain` | 自动守护统一决策唯一入口（优先级矩阵：手动 → 暂停 → 前台准入 → 空闲/电量/插电链）与决策/前台事实值；`GetExpectedPlanGuid` 已退役；Domain 值不依赖 Win32/WPF、不携带窗口标题 |
| `GuardConfigReadResult` | `SmartGuard.Engine.Worker` | Runner 的配置读取有效性结论：仅 `TryLoad` 成功（`IsValid`）授权守护动作；缺失/破损/访问异常降级为仅观测发布，禁止新动作 |
| `GuardDecisionCodes` | `SmartGuard.Engine.Domain` | `GuardDecisionSource`/`GuardDecisionReason` → `GuardStatusCodes` 登记代码的唯一映射；Engine 不产生展示文案 |
| `GuardStatusCodes` | `SmartGuard.Contracts` | 跨进程守护状态稳定代码登记表（sources/reasons，kebab-case）；代码语义一经发布不变；读取端未登记代码按未知处理 |
| `GuardStatusFreshness` | `SmartGuard.Contracts` | 守护状态新鲜度判定唯一实现（3 分钟上界）：Tray/Settings 展示共用，状态缺失/过期不得显示确定的守护声明；TrayDisplayState 每次刷新比较当前新鲜度，字段相同也须处理自然过期 |
| `ForegroundProgramReader` | `SmartGuard.Engine.Infrastructure` | 前台事实采集唯一真源：单次 Win32 查询 + 复核 + finally 释放进程句柄；缓冲上限 32,768 字符；无 hook/监听线程/Timer/事件队列/进程身份缓存；不读取窗口标题；Win32 调用点经构造函数注入可测 |
| `ForegroundEventListener` | `SmartGuard.Engine.Infrastructure` | 唯一前台事件监听 Owner：一个 hook + 一个阻塞消息循环线程（`MsgWaitForMultipleObjectsEx` + 消息泵，等待时阻塞不忙等）；托管回调委托持有于字段（GC 安全）；注册失败/线程退出经 `IsHealthy`/`LastFailure` 可观测，故障路径不自动重启 |
| `GuardianCycleCoordinator` | `SmartGuard.Engine.Worker` | 周期唤醒分类与节流唯一 Owner：完整周期截止 = 最近完整轮次 + 配置间隔，前台事件不得推迟截止时间（持续事件不饿死周期轮次）；原始时间戳按 TimestampFrequency 换算，持续时间经同一 TimeProvider.GetElapsedTime 计算，不与 TimeSpan.Ticks 混算；首次未执行用可空时间戳表达；唯一待处理前台标记（级别触发，最后信号不丢失）；轻路径准入复核 ≤1 次/秒；前台命中转变触发的完整评估 ≥5 秒且节流结束后必补跑；暂停/人工/限制关/名单空/名单非法/配置不可用时不做业务查询，状态变化按原周期重新检查 |
| `StatusJsonReader` | `SmartGuard.Configuration` | 读取并反序列化 status.json |
| `BatteryInfoProvider` | `SmartGuard.Engine.Infrastructure` | 以 `GetSystemPowerStatus` 为电量/供电状态主来源；WMI 失败不得丢弃原生结果 |
| `PowerPlanCatalogProvider` | `SmartGuard.Configuration` | 三档 Windows 电源计划目录唯一真源；Settings 只经可取消的 `LoadWithRetryAsync`，有效目录缓存并限制并发；Engine 经 `TryLoadOnceAsync`、Settings 经 `LoadWithRetryAsync`，均走 `PowerSchemeService`；每次加载发布不可变的 `PowerPlanCatalogSnapshot`（Loading/Failed/Loaded 与单方案存在性），UI 只读快照 |
| `PowerSchemeService` | `SmartGuard.Configuration` | 电源方案操作唯一入口（PWR-INV-SCHEME-API）；`PowrProfNative` 为电源方案函数唯一导入处（睡眠/唤醒通知注册不在此列） |
| `ScheduledTaskRegistrar` | `SmartGuard.Configuration` | 任务名与 XML 唯一源 |
| `ScheduledTaskInstallGate` | `SmartGuard.Configuration` | 安装时跳过计划任务/shell 注册（CLI `--skip-scheduled-tasks` 或 env `SMARTGUARD_SKIP_SCHEDULED_TASKS`） |
| `EngineLifecycle` | `SmartGuard.Configuration` | 停止进程与任务顺序 |
| `ElevationHelper` | `SmartGuard.Configuration` | C# UAC `runas` 单源 |
| `InstallCommands` | `SmartGuard.Configuration.Cli` | 计划任务 install/uninstall |
| `GuardianTaskRunner` | `SmartGuard.Configuration` | 生产式 `schtasks /Run` Guardian |
| `TrayLauncher` / `TrayLifecycle` | `SmartGuard.Configuration` | dev Tray 启停 |
| `StatusReporter` | `SmartGuard.Configuration` | 状态与启动日志尾部 |
| `DesktopDistributionManifest` | `SmartGuard.Configuration.Distribution` | 发布项目、payload、installer 进程、legacy cleanup 唯一真源 |
| `InnoDistributionManifestWriter` | `SmartGuard.Packaging.Inno` | Inno generated include（Constants/Run/Files/Processes/LegacyCleanup） |
| `LegacyScheduledTaskCatalog` | `SmartGuard.Configuration` | 旧 SmartPowerPlan 计划任务名唯一 live 名单 |
| `DesktopSuitePublisher` | `SmartGuard.Configuration.Build` | 四件桌面组件 `dotnet publish` 编排（消费 `DesktopDistributionManifest`）；publish 结束后调用 `SystemEventsRidAssetGuard.EnsureAligned` |
| `SystemEventsRidAssetGuard` | `SmartGuard.Configuration.Build` | 确保 `bin/Microsoft.Win32.SystemEvents.dll` 与 `runtimes/win/lib/net8.0/` 同名文件 SHA256 一致；`StagingLayoutValidator` 复用 `Validate` |
| `DiagnosticBundleExporter` | `SmartGuard.Configuration` | 诊断包 zip 导出；config 经 `DiagnosticRedactor` 剔除 token/secret；日志事件经注入的 `ILogEventSource` 读取 |
| `DiagnosticBundleRequest` | `SmartGuard.Configuration` | 诊断包导出范围/过滤/覆盖策略请求 DTO |
| `DiagnosticRedactor` | `SmartGuard.Configuration` | 诊断包导出脱敏（路径用户名、token/password/secret、命令行敏感参数） |
| `BrandIconLoader.SaveEmbeddedIconToFile` | `SmartGuard.Configuration` | 安装时将嵌入 .ico 提取到 `{root}/bin/SmartGuard.ico`，供 AUMID `IconUri`（Toast 标题栏图标真源）消费 |
| `ExceptionFormatter` | `SmartGuard.Configuration` | 异常完整链格式化（类型/消息/StackTrace/InnerException/AggregateException 展开）唯一入口 |
| `SmartGuardLogWriter` | `SmartGuard.Configuration` | 统一日志追加：命名 Mutex + FileShare 重试 + 永不抛出 |
| `GlobalExceptionHandlers` | `SmartGuard.Configuration` | 注册 AppDomain/TaskScheduler 全局未处理异常处理器 |
| `CrashLogWriter` | `SmartGuard.Configuration` | 崩溃应急同步尽力写入：主日志仅在已有预算内非等待追加；独立 `%TEMP%\SmartGuard\crash.log` 受固定预算约束，失败不再抛出 |
| `LogFallbackChain` | `SmartGuard.Configuration` | 日志三层降级链（主日志 → startup 回退 → `%TEMP%\SmartGuard\emergency.log`），每层异常隔离 |
| `LogLevelFilter` | `SmartGuard.Engine.Infrastructure` | 日志级别统一过滤入口：`ShouldWrite`（Heart 恒放行）+ `ParseMinimum`（大小写不敏感，非法回退 Info）；配置项 `GuardConfig.LogMinimumLevel` |
| `LogPipeline` | `SmartGuard.Configuration` | 异步日志管线：有界双优先级队列 + 批量出队（High 先）+ 诊断计数；Slice 3 起含单写者消费线程与生命周期 |
| `LogPipelineOptions` | `SmartGuard.Configuration` | 管线容量/批量/定时/关闭超时参数（默认 20000/2000/200/100ms/3000ms） |
| `LogPipelineDiagnostics` | `SmartGuard.Configuration` | 管线诊断快照：Enqueued/DroppedNormal/DroppedHigh/WrittenLines/FailedBatches |
| `LogPriority` | `SmartGuard.Configuration` | 日志行优先级（Normal/High），满时先丢 Normal |
| `LogPipelineState` | `SmartGuard.Configuration` | 管线生命周期状态机：Starting/Running/Flushing/Stopped |
| `HeartbeatSchedule` | `SmartGuard.Engine.Infrastructure` | Heart 心跳单调调度唯一 Owner：仅在日志管线/降级链接受后推进；Resume 触发一次立即尝试；配置项 `GuardConfig.LogHeartMinIntervalSec` |
| `CrashRingBuffer` | `SmartGuard.Configuration` | 崩溃环形缓冲：固定容量内存环形队列，仅接收高优先级行，崩溃时同步 flush |
| `EmergencySyncWriter` | `SmartGuard.Configuration` | 应急同步写入器：崩溃时绕过管线直接同步落盘，全路径异常隔离 |
| `LogRecoveryStateMachine` | `SmartGuard.Engine.Infrastructure` | 管线消费侧故障恢复状态机：Healthy/Degraded/Failed 三态，连续失败阈值 + 定时探测恢复 |
| `LogConsumerRecovery` | `SmartGuard.Engine.Infrastructure` | 消费者自恢复包装器：Failed 态降级到 `LogFallbackChain` 同步写入，恢复后复位 |
| `LogDeduplicationFilter` | `SmartGuard.Configuration` | 重复日志聚合：对入队行按指纹聚合，连续相同指纹超过阈值后折叠为 `[DEDUP x{count}]` 前缀行，释放后重置 |
| `LogBackpressureController` | `SmartGuard.Configuration` | 背压控制器：监控 Normal 队列深度超过阈值时主动丢弃低价值行，High 不受影响 |
| `OperationIdContext` | `SmartGuard.Configuration` | OperationId 进程内 AsyncLocal 传播 + 跨进程经 status.json 中继 |
| `TraceIdContext` | `SmartGuard.Configuration` | TraceId 进程内 AsyncLocal 传播 + 跨进程经 status.json traceId 字段中继（D3 裁决） |
| `SpanIdContext` | `SmartGuard.Configuration` | SpanId 进程内 AsyncLocal 传播（子步骤关联） |
| `UserActionIdContext` | `SmartGuard.Configuration` | UserActionId 进程内 AsyncLocal 传播（用户交互事件链关联） |
| `EventNameRegistry` | `SmartGuard.Configuration` | 事件名称唯一注册表：内置常量 + 运行时注册 + 门禁强制 |
| `EventNameMigration` | `SmartGuard.Configuration` | 事件名称历史别名兼容名单：预留阶段四渐进迁移接口 |
| `LogEventOutcome` | `SmartGuard.Configuration` | 日志事件明确结果属性唯一注册表；生产端显式写入 `outcome`，消费端仅映射已登记值；禁止从 Severity 或 Message 推导状态 |
| `LogFailureInjector` | `SmartGuard.Configuration` | 故障注入测试框架：测试专用钩子，不修改生产代码路径 |
| `LogPipelineHealth` | `SmartGuard.Configuration` | 可观测健康指标：8 项内部指标快照，以 `[DIAG] yyyy-MM-dd HH:mm:ss {JSON}` 标准时间戳日志行定时追加到日志末尾 |
| `LogHealthSnapshot` | `SmartGuard.Configuration` | 健康指标快照 record：11 字段含速率与延迟 |
| `LogIndexEntry` | `SmartGuard.Configuration` | 日志行索引条目 record：行号/时间戳/级别/类别/事件名/OperationId/TraceId/PID/TID |
| `LogIndex` | `SmartGuard.Configuration` | 日志索引集合：内存索引 + 源文件路径 |
| `LogIndexBuilder` | `SmartGuard.Configuration` | 日志索引构建器：按行解析日志文件构建内存索引，CancellationToken 可取消 |
| `LogQueryRequest` | `SmartGuard.Configuration` | 日志查询请求 record：多条件筛选 + 分页参数 |
| `LogQueryResult` | `SmartGuard.Configuration` | 日志查询结果 record：匹配条目 + 总数 + 分页信息 |
| `LogQueryService` | `SmartGuard.Configuration` | 日志查询服务：消费 LogIndex 按多条件过滤 + 分页返回（D2 裁决） |
| `LogViewBackgroundQuery` | `SmartGuard.Settings.LogView` | 后台日志查询：Task 线程执行过滤 + CancellationToken 取消（可复用后台查询原语，页面主刷新走 `SettingsLogRefreshPipeline`） |
| `LogTailReader` | `SmartGuard.Settings.LogView` | 日志页历史读取唯一入口：`ILogEventSource` 实现，消费 configured 主日志 + 归档 + startup 兼容源 |
| `LogEvent` | `SmartGuard.Configuration` | 结构化日志 canonical record：schemaVersion/eventId/timestamp/severity/component/channel/message 与关联字段 |
| `LogEventWindow` | `SmartGuard.Settings.LogView` | 有界显示窗口：2,000 条或 4MB 上限，产出 append/prepend delta，禁止 Reset |
| `LogViewportController` | `SmartGuard.Settings` | 日志 ScrollViewer 唯一滚动命令所有者 |
| `SettingsForegroundCoordinator` | `SmartGuard.Settings` | 前台程序限制设置唯一 Owner（开关 + 名单 + 添加/删除 + 退出观察时长 + 一次窗口识别/验证/失效修正）：条目经 `GuardConfigValidator` 规范化/去重，时长1–3600秒，保存走既有防抖保存链；状态刷新每5秒一次且最多一个后台读取，复用 StatusJsonReader，只更新变化文案；名单载入/添加或显式刷新请求一次后台路径检查，不逐周期扫描；Policy Dispose 停止 Timer、取消单次识别、移除订阅并丢弃晚到结果；不触碰配置仓库、不维护第二套路径规则 |
| `SettingsProgramCatalog` / `SettingsProgramSearchCoordinator` | `SmartGuard.Settings` | 高级页名称搜索及显式刷新：只读本机开始菜单快捷方式及有窗口的进程，不启动程序、不解析启动参数或读取窗口标题；每窗口最多一个在途STA发现线程，合作取消和两秒预算，最多检查2048对象/512候选，目录深度8、查询128字符、显示20结果；图标后台提取并冻结，COM/进程/图标资源归发现线程释放。候选来源不等于验证；仅用户选择具体候选后经 `SettingsForegroundCoordinator.AddProgramPath` 添加完整exe路径，保存/匹配/配置Schema不变。关闭取消并丢弃晚到结果，单次平台调用不可强制中断；未识别/截断目录不代表本机完整应用清单，允许浏览文件补充；候选不是Engine前台事实来源 |
| `ForegroundGuardStatusText` | `SmartGuard.Settings` | Settings 侧守护状态文案映射（`GuardStatusCodes` → 展示文案）：状态缺失/过期/未登记代码显示"状态未知"；只读 `StatusJsonReader` 共享链 |
| `LogJsonlFormatter` | `SmartGuard.Configuration` | versioned JSONL 编解码：只处理纯 JSONL；legacy 文本由 `LogEventDecoder` 负责 |
| `LogQueryFilterSpec` | `SmartGuard.Configuration` | 结构化筛选规格 record：时间/等级/模块/事件/OperationId/TraceId/PID/TID/关键词 + 分页 |
| `LogFilterBuilder` | `SmartGuard.Configuration` | 流式筛选构建器：链式 With 方法构建 LogQueryFilterSpec |

---

## 3.1 电源生命周期稳定不变量

| ID | 状态 | 契约 |
|---|---|---|
  | PWR-LIFE-OWNER | 阻断 | `PowerLifecycleStateMachine` 是 `Running`/`Suspending`/`ResumeStabilizing` 唯一 Owner；`PolicyEngine` 不拥有生命周期。 |
  | PWR-LIFE-NATIVE | 阻断 | `PowerEventWakeListener` 显式 `PowerRegisterSuspendResumeNotification` 注册暂停/恢复；SystemEvents 只分发 StatusChanged。同一暂停后的 18/7 Resume 仅分发首次；回调 O(1)、使用注入 UTC，不做 IO/计划写入。Dispose 阻止新分发并等待在途分发退出，随后注销单个原生句柄；注销失败保留委托强根并报告错误。注册失败回收 SystemEvents，回调异常不跨 ABI，GuardianLoop 记录一次禁用/失效原因。 |
  | LOG-HEALTH-PID | 阻断 | `LogStructuredSink.CreatePipelineHealthEvent` 在现有 `LogEvent.ProcessId` 字段记录实际进程 PID；五分钟健康计数仅在同一有效 PID 内比较，旧日志缺 PID 为 Blocked，不按 0==0 通过。 |
| PWR-LIFE-SUSPEND | 阻断 | Suspend 期间禁止计划仲裁、`PowerSetActiveScheme` 和外部计划变更判定。Runner 慢探测后与副作用前重新读取生命周期准入（唯一 Owner 的 O(1) 内存状态）；轮首许可不能授权暂停后的新动作。亮度同步后、setactive 前以及恢复亮度前重验；状态拒绝原因为 blocked-by-lifecycle。Owner 锁不跨 WMI、进程等待或 await；已经启动的系统动作只能完成观测，不能回溯撤销，完成事件不等同命令启动时间。 |
| PWR-LIFE-RESUME | 阻断 | Resume 建立新的 awake-idle epoch；先吸收 OS 当前计划并完成恢复稳定期，再允许普通策略仲裁。 |
| PWR-LIFE-TIME | 阻断 | awake 持续时间和心跳间隔只使用注入的 `TimeProvider` 单调时间；持久化时间为 UTC offset。 |
| PWR-INV-SCHEME-API | 阻断 | 电源方案操作经 `PowerSchemeService`，真实原生层每进程一个单飞执行器。门属于工作项：原生函数返回后、结果对调用方可见前释放，调用方从不释放。超过截止或被取消的调用若已进入原生层，在原生函数返回前持有门，期间新调用为 `call-pending` 且不触达原生层；若尚未进入原生层，工作项跳过原生调用。写操作在进入原生层前复核生命周期准入，撤销时为 `not-admitted`，不算失败也不算恢复。恢复以在途调用返回为准，不以时间推断；不回退外部进程。Guardian 目录加载（列举与读名）共享 5 秒总预算。失败分类 `ApiFailed`（含 Win32 码）/`CallPending`/`TimedOut`/`VerificationMismatch`/`NotAdmitted`，告警使用中文标签。测试进程默认禁止真实写入。门禁覆盖全部 src。门禁：`PowerSchemeApiArchitectureTests`。 |
| PWR-LIFE-VERIFY | 阻断 | 单元/组合测试通过不等于真实待机验收；Lid/Mouse/AC/DC 未执行时标记 `NOT RUN` 或 `BLOCKED`。 |
| STATUS-FRESH | 阻断 | `StatusPublisher` 对有意义变化立即写；无变化按单调时钟最多 60 秒刷新一次，且不以 delete+move 暴露缺失窗口。 |
| STATUS-SNAPSHOT | 阻断（候选待完整验收） | `StatusPublisher` 首次创建用 Move，已有文件用 Replace，保留 `SmartGuardPaths.StatusBackupForFile` 提供的单份 `.bak` 旧版本；替换失败保留旧正式状态，正式文件缺失时从备份恢复，恢复失败保留备份并报告双重错误，发布前恢复中断遗留备份；成功缓存/时间只在写入成功后推进。`StatusJsonReader` 在短期 `FileShare.ReadWrite \| FileShare.Delete` 句柄内读取完整快照，允许发布端替换；旧句柄保持旧快照，新读取看到新状态。沙箱内 1175 失败证据保留，沙箱外无 reader burst 1000 次通过；尚未实际注入 Win32 1176/1177，不得宣称全部底层故障已逐项验证。证据见 `.work/guard-rules/resume-auto-report.md`。门禁：`GuardStatusCodesArchitectureTests`、`StatusPublisherTests`。 |
| STATUS-GUARD-CODES | 阻断 | `guardSource`/`guardReason`/`foregroundEventsAvailable` 进入 Publisher 相等判定与 Tray 相等判定 + Clone；探测失败与配置不可用路径同样发布状态；Engine 不携带展示文案；UI 对字段缺失/未登记代码/过期状态显示"未知"，暂停展示只读 `status.paused`。 |
| PWR-LIFE-ACCEPT | 阻断 | `scripts/Invoke-PowerLifecycleAcceptance.ps1` 是唯一真实待机观察入口；HibernateResume 额外核对 Kernel-Power 42 的 TargetState/EffectiveState 均为 5（S4），缺字段 Blocked、非 S4 Failed；结果缺证据为 `Blocked`/退出码 2，不自动触发睡眠、改计划或改系统时间。 |
| SOAK-STATUS-READ | 阻断 | `scripts/Invoke-SmartGuardSoakAcceptance.ps1` 只采集连续运行证据，不替代真实待机入口；状态经 `Read-SmartGuardAcceptanceStatus` 适配现有 `StatusJsonReader`，禁止另行 JSON 解析或缓存旧状态。最多六次、间隔50ms，只重试不可用读取；PID/AC/暂停、90秒新鲜度、未来5秒与64KiB预检失败立即阻断。重试次数与耗时进入回执/样本；配置复用 `GuardConfigRepository.TryLoad`，不使用默认值充当有效配置。原任务与六项AC超时快照、finally恢复及独立复核为必需。 |

---

## 4. 禁止清单（机器可测 — Phase 8 / Architecture.Tests）

1. `SmartGuard.Engine` 中不得调用 `GuardConfig.LoadFromFile`（经 Repository）。
2. 不得在 Tray/Settings/LogViewer/Engine 中定义 `class RootResolver` 或 `internal static class RootResolver`（使用 `InstallRootResolver`）。
3. 不得在 `GuardianRecovery` 等处重复字面量 Guardian 任务名（使用 `ScheduledTaskRegistrar.GuardianTaskName`）。
4. 不得在 Settings 硬编码 `Path.Combine(root, "SmartGuard.log")`（使用 `SmartGuardPaths` + `config.LogFile`）。
5. 不得新增绕过 `SettingsSaveCoordinator` 的主题/config 全量写（含 `SaveThemePreferences` 直写 `_originalConfig`）。
6. 不得在 Inno/cmd 新增独立 schtasks 注册逻辑（委托 Engine `--install`）。
7. 不得新增 Tray/runtime shell 自注册、自愈 marker 或第二个 shell owner（唯一 owner = `DesktopShellRegistration`）。
8. 不得在 runtime 读取 `installRoot\\lib\\SmartGuard.Settings.xaml` 或 `installRoot\\lib\\SmartGuard.ico` 作为真源。
9. 不得在 publisher/staging/installer 各自维护第二份项目/文件/进程清单（唯一真源 = `DesktopDistributionManifest`）。
10. 不得重新引入 `SmartGuard.LogViewer.exe` 作为 live product artifact、进程清单或用户入口。
11. Repo grep / contract gates 对旧真相的豁免只能按 path boundary（`docs/archive/**`、`scripts/archive/**`、`docs/superpowers/plans/**`），不得再按日期、`PHASE-`、`MIGRATION`、单文件名做豁免。
12. 不得在 `installer/SmartGuard.iss` 手写 live exe/task file-name literal（`MyAppExeName`、`UninstallDisplayIcon` 直写 exe、`[Run]` 内 Engine/Cli/schtasks）；必须消费 `InnoDistributionManifestWriter` 生成的 Constants/Run include；`TryStopSmartGuardProcesses()` 亦不得手写 `SmartGuard.Engine.exe`，只能消费 `{#EngineExeName}`，并且不得用 `tasklist` 失败开放地绕过 `EngineLifecycle`。
13. 不得在 live `scripts/` 保留 legacy cleanup 入口或第二份 `SmartPowerPlan Guardian/Tray` 名单；唯一 live 真源 = `LegacyScheduledTaskCatalog`；历史脚本仅允许 `scripts/archive/**`。
14. 不得在 runtime 使用 `SystemIcons.Shield`、caller-supplied fallback icon 或 `SmartGuardPaths.BrandIcon` 作为品牌图标第二显示入口；`BrandIconLoader` 必须 embedded-only，缺失时 fail-fast。
15. Repo **implementation-path grep** 只扫描 `installer/`、`src/`、活动 `scripts/`（排除 `scripts/archive/**`）中的 live residual；不得把 `docs/`、`Tests/` 的合法断言/契约文本当作 runtime 命中。`docs/tests may mention retired literals only as assertions or contract text`。
16. `DesktopSuitePublisher.Publish` 结束必须调用 `SystemEventsRidAssetGuard.EnsureAligned`；`StagingLayoutValidator.Validate` 必须复用 `SystemEventsRidAssetGuard.Validate`；禁止第二套 SystemEvents stub 检测逻辑。
17. `BatteryInfoProvider` 必须先保留 `GetSystemPowerStatus` 原生结果，再尝试 WMI 可选补充；WMI 查询失败不得把有效原生状态降级为 `100% / 插电`。

---

## 5. 变更流程

1. **RED** — 更新本文档 + 添加/修改 Architecture.Tests 或 Pester Phase 8，确认失败。
2. **GREEN** — 最小实现使测试通过。
3. **REFACTOR** — 全绿后清理；`Run-Tests.ps1` 通过。
4. **git commit** — 每 TDD Slice 一 commit。
5. **终验** — 全部 Slice 后按 §2 问题表与 AGENTS 逐项验收。

---

## 7. 已关闭延期项（2026-07-01 深挖治理）

| ID | 状态 | 真源 |
|----|------|------|
| M-04 | **已关闭** | `PowerPlanCatalogProvider`（Engine/Settings 单源；Engine 经 `TryLoadOnceAsync`、Settings 经 `LoadWithRetryAsync`，同步 `TryLoad()` 已在第二阶段删除） |
| M-06 | **已关闭** | `TrayApplicationContext` → `TrayDisplaySettingsCache(_configRepository, _root)` + `ConfigFileWatcher` |
| M-07 | **已关闭** | `SettingsThemeCoordinator.SaveThemePreferences` → `SettingsPolicyCoordinator.SaveThemePreferences` → `SettingsSaveCoordinator.Save` |
| M-08 | **已关闭** | `LogViewIdleReader`（Contract §1 登记） |
| M-11 | **已关闭** | `DesktopAppBootstrap` |
| M-14 | **已关闭** | `status.json` `enginePid` |
| M-16 | **已关闭** | `BrandIconLoader` |
| L-02 | **已关闭** | `GuardConfigRepository` 仅经 `FileReadStamp` 按需检测外部变更（只比对写入时间会在粗粒度文件时钟下漏掉同一时间片内的改动，2026-11-20 修正）；不得为短生命周期 Repository 实例创建未释放的 `FileSystemWatcher` |
| L-03 | **已关闭** | `LegacyPaths` |
| L-04 | **已关闭** | 本文档 + `docs/archive/**` 历史边界；live contract 不得保留指向已归档 migration 文档的索引指针 |

仍开放（有门禁折中或下轮）：

| ID | 状态 | 说明 |
|----|------|------|
| M-15 | 折中 | `EngineLifecycle` 按镜像名停止 + Architecture 禁止关键脚本裸 `Stop-Process`/`taskkill` Engine |
| L-01 | **已关闭（展示层）** | `SettingsInitialValues` 为 UI clamp/换算，非配置真源；默认阈值真源仍为 `GuardConfig` |

## 8. 第二轮治理关闭项（2026-07-01）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| S-01 | **已关闭** | Tray 暂停菜单/切换读 `status.paused`（`TrayPauseState`） |
| S-02 | **已关闭** | `GuardianRecovery` 消费 `enginePid` 跳过多余 `schtasks` |
| S-03/S-04 | **已关闭** | `CreateDefault` → `PowerPlanCatalogProvider` + `SmartGuardPaths.DefaultLogFile` |
| S-05 | **已关闭** | Settings 计划目录由 `SettingsPlanCatalogCoordinator` 以可取消的 `LoadWithRetryAsync` 在后台加载，UI 线程只读 `PowerPlanCatalogSnapshot`，并区分正在加载、无法读取列表、名称读取失败与未在本机找到；不再有同步 `TryLoad` |
| S-06 | **已关闭** | AutoStart UI 读 `config.AutoStartEnabled`；写经 `SettingsSaveCoordinator` + `AutoStartService.SetEnabled` |
| S-07 | **已关闭** | 长生命周期 `TrayDisplaySettingsCache` 使用共享 `ConfigFileWatcher`；短生命周期 `GuardConfigRepository` 使用时间戳检测，避免监听器泄漏 |
| S-08 | **已关闭** | `Status.cmd` 与 `ScheduledTaskRegistrar` 任务名一致 |
| E-01 | **已关闭** | `Start-Core.cmd` → `schtasks`；`Debug-Engine.cmd` dev 前台 |
| E-02 | **已关闭** | `LogViewer.exe` → Settings `--page logs` |
| E-04～E-07 | **已关闭** | 脚本/集成 stop 委托 `SmartGuardStop.ps1`；legacy 任务名 `LegacyScheduledTaskCatalog`（历史脚本 `scripts/archive/**`） |
| E-03 | **登记** | Inno `[Run]` + `GuardianRecovery` 双 `schtasks /Run`（设计允许） |
| E-08 | **折中** | 同 M-15 |
| 上帝模块 | **已关闭** | `SettingsLogPageHost` &lt;300 行 + 提取 Export/FollowTail/Search 模块 |

## 9. 第三轮治理关闭项（2026-07-01）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| ME-08 | **已关闭** | `SettingsLogsPageLauncher.Open`（Tray + LogViewer 单源） |
| ME-03 | **已关闭** | `PerformanceTestEngineLifecycle.Stop` → `EngineLifecycle.StopProcesses()` |
| ME-12 | **已关闭** | 运行时错误信息统一 `build.cmd`（移除 `Publish-All.ps1`） |
| LOG-SCRIPT | **已关闭** | `scripts/SmartGuardPathConstants.ps1` + `Measure-EngineStartup.ps1` |
| ME-01 | **登记** | `Measure-EngineStartup.ps1` 保留 `# benchmark-only-start` + `Start-Process`（冷启动基准专用） |
| 上帝模块 | **已关闭** | Toast / Policy / About / AppDialog / Tray 拆分 + `*LineCountTests` &lt;300 |

## 10. 第四轮治理关闭项（2026-07-01）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| ME-09 | **已关闭** | `SettingsMainPageLauncher.Open`（Tray 设置 spawn 单源） |
| DOC-01～03 | **已关闭** | 本文档 §2/§6/§9 与实现对齐 |
| ME-12-R | **已关闭** | 集成测试注释移除 `Publish-All` |
| GOD-01 | **已关闭** | `LogSearchFilterBar` &lt;300 行门禁 |
| E-10 | **已收口为阻断规则** | 当前强制语义见 §12：shell user entry 只能经 `DesktopShellRegistration` 落地；dev `Start-Tray.cmd` / `Restart-Tray.cmd` 仅保留为开发辅助 |
| E-11 | **已收口为阻断规则** | 当前强制语义见 §12：提升后的 install/uninstall 只能委托 `InstallCommands` + `DesktopShellRegistration` |
| E-11b | **已收口为阻断规则** | 当前强制语义见 §12：Inno 不得再拥有 `[Icons]` 用户快捷方式；仅允许透传 `--create-desktop-shortcut` |
| PERF | **已关闭** | 性能测试 settle + 5000ms 预算 + lifecycle 门禁 |

---

## 11. 第五轮治理关闭项（2026-07-02）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| M-17 | **已关闭** | `InstallCommands` → `SmartGuardPaths.StartupLogFile` |
| M-18 | **已关闭** | `ScheduledTaskRegistrar` / `ToastShortcutResolver` → `SmartGuardPaths.*Exe` |
| M-19 | **已关闭** | `EngineLifecycle` → `SmartGuardPaths.ProcessImageNames` |
| GOD-02 | **已关闭** | `UpdateDownloadProgressWindowFactory` + `SettingsUpdateCheckCoordinator` &lt;300 行 |
| GOD-03 | **已关闭** | `SettingsPauseHandler` + `SettingsDebouncedSaver`；`SettingsPolicyCoordinator` 231 行 |
| GOD-04 | **已关闭** | `ToastRegistryWriter` + `StartMenuShortcutWriter` 自 `ToastAumidRegistrar` 提取 |
| GOD-05 | **已关闭** | `GuardianFirstRunInitializer` + `GuardianExceptionStormHandler` 自 `GuardianLoop` 提取 |
| DOC-04 | **已关闭** | §2 `StopForUninstall` 与实现对齐；§11 首次记录 E-11/E-11b，当前阻断语义见 §12 |

---

## 12. 第六轮治理关闭项（2026-07-02）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| M-20 | **已关闭** | `SmartGuardPaths` 扩展：`SettingsXaml`、`InitMarkerFile`、`ElevationDeclinedMarkerFile` |
| M-21 | **已关闭** | `SmartGuardPathConstants.ps1/.cmd` + Inno `#define` 与 `SmartGuardPaths.*FileName` parity |
| S-09 | **已关闭** | Settings `tglPaused` 初始展示读 `status.json`（`SettingsPauseDisplayArchitectureTests`） |
| E-10 | **阻断** | shell user entry 只能经 `DesktopShellRegistration` 落地；生产 Tray 入口不得回退到第二 owner |
| E-11 | **阻断** | 提升后的 install/uninstall 只能委托 `InstallCommands` + `DesktopShellRegistration` |
| E-11b | **阻断** | Inno 不得再拥有 `[Icons]` 用户快捷方式；仅允许透传 `--create-desktop-shortcut` |
| DOC-05 | **已关闭** | 本文档 §1/§2/§6/§12 + `MULTI-SOURCE-GOVERNANCE-ACCEPTANCE` 第六轮终验 |

---

## 13. 第七轮治理关闭项（2026-07-02 — cmd → C#）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| R7-OPS | **已关闭** | `GuardianTaskRunner`、`TrayLauncher`、`TrayLifecycle`、`StatusReporter`；`InstallCommands` 迁至 `Configuration` |
| R7-CLI | **已关闭** | `SmartGuard.Cli.exe` 薄路由 + dev cmd 单行转发；`Request-AdminElevation.cmd` 删除 |
| R7-INNO | **阻断** | 用户 shell 入口不得回流到 Inno `[Icons]`；唯一 owner = `DesktopShellRegistration` |
| R7-PUBLISH | **阻断** | 分发/安装包构件必须收敛到 `DesktopDistributionManifest`，不得保留第二份列表 |
| DOC-06 | **已关闭** | 本文档 §1/§2/§6/§13 + `StatusReporter` 替代 `Status.cmd` |

**DEV-ONLY 保留：** `Run-Tests.cmd`、`Clean-Workspace.cmd`（测试编排，非运行时入口）。

---

## 6. 门禁索引

| 门禁 | 位置 |
|------|------|
| 文档契约 | `ArchitectureContractDocTests` |
| 单 RootResolver | `SingleRootResolverArchitectureTests` |
| 无 Engine LoadFromFile | `ConfigLoadStackArchitectureTests` |
| 任务名单源 | `TaskNameSingleSourceArchitectureTests` |
| Inno 停止委托 Engine | `InnoStopDelegationArchitectureTests` |
| 计划档位显示名 | `PolicyEngineDisplayNameArchitectureTests`、`SettingsDisplayNameArchitectureTests` |
| SmartGuardPaths | `SmartGuardPathsArchitectureTests` |
| `BrandIconLoader` | `BrandIconLoaderArchitectureTests` |
| `DesktopAppBootstrap` | `DesktopAppBootstrapTests` |
| `SettingsWindowController` &lt;300 行 | `SettingsWindowControllerLineCountTests` |
| `SettingsLogPageHost` &lt;300 行 | `SettingsLogPageHostLineCountTests` |
| Tray 暂停读 status | `TrayPauseArchitectureTests` |
| 脚本 Engine 停止委托 | `ScriptStopArchitectureTests` |
| `GuardianRecovery` enginePid | `GuardianRecoveryEnginePidTests` |
| `GuardConfig.CreateDefault` 单源 | `GuardConfigDefaultsArchitectureTests` |
| `Start-Core.cmd` Cli 转发 | `StartCoreArchitectureTests` |
| LogViewer 退场 | `LogViewerProgramArchitectureTests` |
| 配置变更检测生命周期边界 | `ConfigFileWatcherArchitectureTests` |
| `GuardianIterationRunner` | `GuardianIterationRunnerArchitectureTests` |
| 第四轮文档契约 | `ArchitectureContractFourthRoundTests` |
| 日志页启动单源 | `SettingsLogsPageLauncherArchitectureTests` |
| 主题落盘单源 | `SettingsThemeSaveArchitectureTests` |
| Tray 配置缓存 | `TrayDisplaySettingsCacheArchitectureTests` |
| 性能测试 stop | `EnginePerformanceStopArchitectureTests`、`EnginePerformanceStartupArchitectureTests` |
| 基准脚本常量 | `MeasureEngineStartupArchitectureTests` |
| 移除 Publish-All 引用 | `PublishAllReferenceArchitectureTests` |
| Toast 拆分 &lt;300 行 | `ToastNotificationLineCountTests` |
| Policy 拆分 &lt;300 行 | `SettingsPolicyCoordinatorLineCountTests` |
| About 拆分 &lt;300 行 | `SettingsAboutCoordinatorLineCountTests` |
| AppDialog 拆分 &lt;300 行 | `AppDialogLineCountTests` |
| Tray 拆分 &lt;300 行 | `TrayApplicationContextLineCountTests` |
| 设置 spawn 单源 | `SettingsMainPageLauncherArchitectureTests` |
| `LogSearchFilterBar` &lt;300 行 | `LogSearchFilterBarLineCountTests` |
| Dev Tray 脚本登记 | `DevTrayScriptArchitectureTests` |
| 路径/exe 单源 | `SmartGuardPathsSingleSourceArchitectureTests` |
| 更新检查拆分 &lt;300 行 | `SettingsUpdateCheckCoordinatorLineCountTests` |
| 更新进度窗工厂 | `SettingsUpdateCheckCoordinatorArchitectureTests` |
| Policy 暂停/保存拆分 | `SettingsPolicyCoordinatorArchitectureTests` |
| Toast AUMID 拆分 | `ToastAumidRegistrarArchitectureTests` |
| GuardianLoop 拆分 | `GuardianLoopArchitectureTests` |
| 第六轮路径扩展 | `SmartGuardPathsExtensionsArchitectureTests` |
| 脚本/Inno 常量 parity | `ScriptPathConstantsParityArchitectureTests` |
| Settings 暂停展示 | `SettingsPauseDisplayArchitectureTests` |
| UAC 单源 | `ElevationSingleSourceArchitectureTests`、`ElevationHelperTests` |
| Inno 包装脚本 | `InnoShortcutWrapperArchitectureTests` |
| 第六轮文档契约 | `ArchitectureContractSixthRoundTests` |
| Run-Tests 并行/分层 | `RunTestsOptimizationArchitectureTests` |
| 第七轮操作真源 | `GuardianTaskRunnerArchitectureTests`、`TrayLifecycleArchitectureTests`、`InstallCommandsLocationArchitectureTests` |
| Cli 薄路由 | `CliThinFacadeArchitectureTests`、`CliLineCountTests`、`CliForbiddenReferencesArchitectureTests` |
| Cli 委托 Launcher/Install | `CliOpenDelegatesLauncherArchitectureTests`、`CliInstallDelegatesArchitectureTests`、`CliStartDelegatesArchitectureTests` |
| 发布单源 | `PublishSingleSourceArchitectureTests`、`DesktopSuitePublisherTests` |
| legacy cleanup 单源 | `LegacyTaskCleanupArchitectureTests`、`LegacyTaskCleanerTests` |
| 状态报告 | `StatusReporterArchitectureTests` |
| 电池/供电状态真源 | `BatteryStatusSourceArchitectureTests`、`BatteryInfoProviderCacheTests` |
| 第七轮文档契约 | `ArchitectureContractSeventhRoundTests` |
| Archive 边界 | `ArchiveBoundaryArchitectureTests` |
| 退役敏感字段清理 | `RetiredSecretFieldArchitectureTests`、`RetiredSecretFieldMigrationTests` |
| 诊断包导出脱敏 | `DiagnosticBundleExporterArchitectureTests`、`DiagnosticBundleExporterTests`、`SettingsDiagnosticExportCoordinatorTests` |
| Pester Phase 8 | `Tests/SmartGuard.Tests.ps1` |
| Toast 品牌 Logo | `ToastAppLogoArchitectureTests`、`ToastNotificationXmlBuilderTests` |
| 计划任务安装跳过 | `InstallCommandsSkipArchitectureTests` |
| 计划任务测试隔离契约 | `ArchitectureContractScheduledTaskIsolationTests` |
| 日志阶段一止血 | `ArchitectureContractLoggingPhase1Tests` |
| 日志阶段二异步管线 | `ArchitectureContractLoggingPhase2Tests` |
| 日志阶段三结构化与可观测 | `ArchitectureContractLoggingPhase3Tests` |
| 日志阶段四 UI 与查询升级 | `ArchitectureContractLoggingPhase4Tests` |
| 日志工作台 P0 唯一真源 | `LoggingWorkbenchArchitectureTests` |
| 日志工作台 P1 诊断工作流 | `LoggingWorkbenchArchitectureTests`、`LogWorkbenchP1AcceptanceTests`、`DiagnosticBundleExporterArchitectureTests` |
| 统一自动决策入口 | `GuardRulesArchitectureTests` |
| 前台事实采集与受控监听 | `GuardRulesArchitectureTests` |
| 守护状态稳定代码与展示边界 | `GuardStatusCodesArchitectureTests` |
| 前台限制设置拆分 &lt;300 行 | `SettingsPolicyCoordinatorLineCountTests` |

---

## 14. 第十轮 live entry/source 收口（2026-07-03）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| INNO-LIVE | **阻断** | `SmartGuard.iss` 不得手写 live exe/task literal；只能消费 `generated/SmartGuard.Distribution.Constants.iss.inc` 与 `generated/SmartGuard.Distribution.Run.iss.inc` |
| LEGACY-CLEANUP | **阻断** | `LegacyScheduledTaskCatalog.TaskNames` 是唯一 live 旧任务名真源；`scripts/archive/**` 保留历史人工脚本 |
| DOC-07 | **已关闭** | 本文档 §1/§4/§14 + `MULTI-SOURCE-GOVERNANCE-ACCEPTANCE` 第十轮终验 |

---

## 15. 最终收口（2026-07-03 — final closeout checklist）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| INNO-STOP | **阻断** | `TryStopSmartGuardProcesses()` 只消费 `{#EngineExeName}`，以 Engine `--uninstall` 退出码为停止结果；不得手写 `SmartGuard.Engine.exe` 或用 `tasklist` 作为停止门禁 |
| ICON-EMBED | **阻断** | `TrayIconLoader` / `BrandIconLoader.LoadWinFormsIcon()` embedded-only；禁止 `SystemIcons.Shield` 与 caller-supplied fallback |
| TRUTH-GATE | **阻断** | **implementation-path grep** 只扫 `installer/`、`src/`、活动 `scripts/`；`docs/tests may mention retired literals only as assertions or contract text` |
| ICON-CONSUMER | **阻断** | 允许的 Windows icon consumers 仅 `ApplicationIcon`、`window.Icon`、`Tray.exe,0`、`IconUri=SmartGuard.ico`（安装时部署到 `{root}/bin/SmartGuard.ico`，Toast 标题栏图标真源）；Toast XML 不得包含 `appLogoOverride`/`<image>`（内容区大图，Windows 11+ 不替代标题栏图标，位置错误）；不得使用 `IconUri=Tray.exe`（.exe 不被 toast 识别）、不得指向 runtime loose `.ico` 或系统 shield fallback |
| DOC-08 | **已关闭** | 本文档 §1/§4/§15 + `MULTI-SOURCE-GOVERNANCE-ACCEPTANCE` 最终收口终验 |

---

## 16. Toast 品牌 Logo 修复（2026-07-05）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| TOAST-LOGO | **已关闭** | Toast 标题栏图标由 AUMID 注册表 `IconUri` 提供（安装时 `DesktopShellRegistration.RegisterAppUserModelId` 经 `BrandIconLoader.SaveEmbeddedIconToFile` 部署 `SmartGuard.ico` 到 `{root}/bin/SmartGuard.ico`，Windows Shell 自动渲染）；Toast XML 只含 `<text>`，不含 `appLogoOverride`/`<image>`（内容区大图，位置错误，已移除）；`appLogoOverride=SmartGuard-toast.png`、`SaveEmbeddedToastIconToFile`、`BrandToastIconFile` 已废弃删除 |
| DOC-09 | **已关闭** | 本文档 §1/§3/§15 + `ToastAppLogoArchitectureTests` 终验 |

---

## 17. 计划任务测试隔离（2026-07-17）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| TASK-SKIP | **阻断** | `ScheduledTaskInstallGate` + `InstallCommands`：当 `SMARTGUARD_SKIP_SCHEDULED_TASKS` 或 `--skip-scheduled-tasks` 生效时，不得 `RegisterGuardian`/`RegisterTray`/`DesktopShellRegistration.Apply` |
| INT-TASK-CAPTURE | **阻断** | Inno 安装/卸载集成测试须 `Capture-SmartGuardScheduledTasks` 于 `Stop` 前，`finally`/`AfterAll` 中 `Restore-SmartGuardScheduledTasks`；默认测试安装须 `SMARTGUARD_SKIP_SCHEDULED_TASKS=1`（`Invoke-SmartGuardSilentInstall` 默认 `$SkipScheduledTasks = $true`） |
| DOC-10 | **已关闭** | 本文档 §1/§2/§3/§6/§17 + `InstallCommandsSkipArchitectureTests`、`ArchitectureContractScheduledTaskIsolationTests` 终验 |

---

## 18. 测试基础设施根治契约（2026-07-26）

| ID | 状态 | 契约 |
|----|------|------|
| TEST-TRUTH | **阻断** | 测试子进程真值 = `Process.ExitCode` + TRX `ResultSummary/Counters` 双重校验；两者矛盾、TRX 缺失、损坏、`total = 0` 或 Outcome 非 `Completed` 时必须判 Failed（fail-closed），不得以 stdout 文本或局部计数替代 |
| ELEVATED-TRUTH | **阻断** | 提权测试真值 = 子进程磁盘 JSON + transcript + 父进程验证三者齐备；零测试、缺 JSON、JSON 损坏或子退出码非零均判 Failed，父门禁不得把"提权窗口成功返回"当作通过 |
| WPF-QUIESCENT | **阻断** | WPF controller `Dispose` 后不得再有任何 UI apply（apply 计数必须为 0）；每个 WPF 用例结束必须 quiescent：窗口数 0、active refresh 0、timer/测试钩子复位；禁止固定 `Thread.Sleep` 充当 UI 完成条件 |
| WPF-TEST-OWNER | **阻断** | WPF test fixture/host owns Window, Dispatcher, hook, timer, controller, and test-created background flight；所有资源必须在用例结束前由同一 owner 取消、Dispose 并证明 quiescent。 |
| WPF-TEST-ISOLATION | **阻断** | deliberate ThreadPool blocking must run in a child process；ordinary WPF suite must not change global ThreadPool capacity。隔离试验的退出、超时与清理不得污染常规 WPF 测试进程。 |
| WPF-TEST-EVIDENCE | **阻断** | WPF 失败必须保留 exit code, TRX, summary, test order, counters, stack/trace or an explicit capture-failure reason；任一必要证据缺失、损坏或零测试均 fail-closed。 |
| WPF-TEST-ADVERSARIAL | **阻断** | 高影响 WPF 测试基础设施改动必须 review hypothesis, falsifier/counterexample, contamination, ownership, resource bounds, and fail-closed evidence；review 流程见 `docs/testing/WPF-TEST-ADVERSARIAL-REVIEW.md`，它不替代本文档。online LLM is not a runtime dependency of the deterministic automated gate。 |
| INT-LIFECYCLE | **阻断** | Integration 必须按 Capture → Stop → Test → Restore 执行；查询仅将 `/HRESULT` 的 `ERROR_FILE_NOT_FOUND` 视为 Missing，其他查询错误、空或无效 XML 必须阻断 Capture，不得进入 Stop；原生命令复用已有参数转义入口，保持带空格 root 为一个参数；Stop 失败或 Restore 失败任一发生，当前测试必须判红，不得 best-effort 吞掉后继续；恢复按快照决定进程运行态，孤儿进程只清理不复活 |
| NO-RETRY-GREEN | **阻断** | 稳定性门禁（`scripts/Invoke-TestStabilityGate.ps1`）任一轮失败立即停止并整体判 Failed；禁止重跑失败轮以"重试后成功"覆盖第一失败证据 |
| DOC-11 | **已关闭** | 本文档 §18 + Pester `Run-Tests optimization contract`/`Invoke-TestStabilityGate` 契约测试（Tasks 2–8，commits e9dc556..f148110）终验 |

---

## 19. 日志系统阶段一（风险止血）（2026-07-30）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| LOG-P1-ISOLATE | 阻断 | `FileLogger.Write`/`TryWrite` 全路径异常隔离 + 防递归；日志失败不得上抛、不得递归记日志 |
| LOG-P1-EXCHAIN | 阻断 | 异常记录唯一格式化入口 `ExceptionFormatter.Format`（类型/StackTrace/InnerException/AggregateException 展开） |
| LOG-P1-WRITER | 阻断 | 日志追加唯一入口 `SmartGuardLogWriter.TryAppendLine`；禁止散落 `File.AppendAllText` 直写日志 |
| LOG-P1-CRASH | 阻断 | 四进程启动经 `GlobalExceptionHandlers.Register` + `CrashLogWriter` 崩溃应急同步写入 |
| LOG-P1-FALLBACK | 阻断 | Engine 日志降级链 `LogFallbackChain`（主 → startup → `%TEMP%\SmartGuard\emergency.log`），每层异常保护 |
| DOC-LOG-P1 | 已关闭 | 本文档 §1/§3/§6/§19 + `ArchitectureContractLoggingPhase1Tests` 终验 |

---

## 20. 日志系统阶段二（异步化与写入管线）（2026-08-01）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| LOG-P2-LEVEL | 阻断 | 日志级别六级 `Trace/Debug/Info/Warn/Error/Critical` + `Heart` 末位；`GuardianLoop.WriteLog` 经 `LogLevelFilter.ShouldWrite` 统一过滤，配置项 `LogMinimumLevel`（默认 `Info`，非法回退 `Info`） |
| LOG-P2-QUEUE | 阻断 | `LogPipeline` 有界双优先级队列（`FullMode.Wait` + `TryWrite` 非阻塞入队，满时按优先级计数丢弃）；禁用 `DropWrite`（无法计数）；业务线程永不阻塞 |
| LOG-P2-LIFECYCLE | 阻断 | `FlushAsync` 返回 `Task<bool>`，最多 active/next 两个合并代，各代冻结双队列接受水位，取消仅取消调用者等待；`FlushOnce` 只在无活动消费者时持有 SingleReader。`StopAsync` 关闭接收并冻结最终水位，只有正常消费、本次记录全确认且无超时才 true；默认 3s，晚到消费者/冷排空任务持有信号资源直到退出，不提前 Dispose。Engine 在关接收前入队 AppStopping（停止意图），关闭失败只保留有界 crash ring，不递归入队。批量触发仍为 100ms/200 条。 |
| LOG-P2-WIRE | 阻断 | `GuardianLoop.WriteLog` 经 `LogPipeline.TryEnqueue` 非阻塞入队（Warn/Error/Critical 走 High）；满队列或背压只计数并返回拒绝，普通异常路径无直写旁路。主批写仅归 `WriteBatchToDisk`，消费侧 `LogConsumerRecovery` 经 `LogFallbackChain.WriteBatch` 各层一次批量追加，保留 JSONL eventId/属性。`LogBatchWriteResult` 按记录划分 PrimaryWritten/FallbackWritten/Lost（非负且和等于批量）；未知 IO 前缀按未确认处理；非法结果或 callback 异常标记消费者失败，不虚报落盘。 |
| LOG-P2-HEART | 阻断 | Heart 心跳经 `HeartbeatSchedule` 使用 `TimeProvider` 单调时间调度，只有 `GuardianLogWriteResult.IsAccepted` 才推进窗口；Resume 只触发一次立即尝试，配置项 `LogHeartMinIntervalSec`（默认 60 秒，0 禁用） |
| LOG-P2-RETAIN | 阻断 | 归档保留三重上限：`LogRetentionDays`（默认 7，范围 1–365 天）+ `LogMaxArchiveFiles`（默认 30，0 禁用）+ `LogDirectoryMaxBytes`（默认 104_857_600，0 禁用）；删除只选归档、最旧先删、永不删当前日志（`LogArchivePlanner` 纯函数 + `FileLogger.EnforceArchiveLimits`）；`SmartGuardLogWriter` 跨日归档后不再访问旧主日志路径的长度，同一批继续创建并追加当前日志（`SmartGuardLogWriterTests`）。 |
| DOC-LOG-P2 | 已关闭 | 本文档 §3/§6/§20 + `ArchitectureContractLoggingPhase2Tests` 终验 |

---

## 21. 日志系统阶段三（结构化与可观测）（2026-08-01）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| LOG-P3-CRASH | 阻断 | `CrashRingBuffer` 崩溃环形缓冲 + `EmergencySyncWriter` 应急同步写入；仅接收 Warn/Error/Critical；容量默认 500；崩溃时同步 flush 到 `%TEMP%\SmartGuard\crash-ring.log` |
| LOG-P3-RECOVER | 阻断 | `LogRecoveryStateMachine` + `LogConsumerRecovery` 只由消费者推进；连续主写失败阈值 3、冷却 30s，注入 TimeProvider 用单调耗时；冷却跳过不增加主写失败计数，回退成功不代表主写恢复。Healthy 边界转换仅尽力入队一次 RecoveryDegraded/RecoveryRestored，不逐条回退递归写入。 |
| LOG-P3-DEDUP | 阻断 | `LogDeduplicationFilter` 重复日志聚合：对入队行按指纹聚合，连续相同指纹超过阈值后折叠为 `[DEDUP x{count}]` 前缀行；`GuardianLoop.WriteLog` 替代 `_tickLogFingerprints` HashSet 去重；阈值默认 5 |
| LOG-P3-BACKPRESSURE | 阻断 | `LogBackpressureController` 背压控制器：监控 Normal 队列深度超过阈值时主动丢弃低价值行；`LogPipeline` 暴露 `NormalQueueDepth`/`HighQueueDepth` 供背压读取；阈值默认容量 80% |
| LOG-P3-OPID | 阻断 | `OperationIdContext` AsyncLocal 进程内传播 + `status.json` `operationId` 字段跨进程中继；禁止引入独立共享内存/命名管道 |
| LOG-P3-OPID-READ | 阻断 | `StatusStore` / `StatusReader` 读取 `status.json` `operationId` 字段并调用 `OperationIdContext.Set`/`Clear` |
| LOG-P3-EVENTNAME | 阻断 | `EventNameRegistry` 事件名称唯一注册表 + 门禁扫描 `WriteLog` 调用点；`EventNameMigration` 维护历史别名兼容；阶段三不做全量重命名 |
| LOG-P3-OUTCOME | 阻断 | `LogEventOutcome` 是日志事件结果属性唯一注册表；生产端显式写已登记 outcome，禁止从 Severity 或 Message 推导状态；默认表格不分配常驻列，结构化 outcome 保留在 `LogEvent` 与 JSONL |
| LOG-P3-INJECT | 阻断 | `LogFailureInjector` 故障注入测试框架：磁盘写满/路径权限/文件锁定/消费线程异常；不修改生产代码路径 |
| LOG-P3-HEALTH | 阻断 | 消费分区用单个不可变快照原子发布，生产丢弃用 Interlocked；累计 WrittenLines=Primary+Fallback，FailedBatches=含 Lost 的批次；单列 PrimaryFailedBatches、LostLines、DroppedByBackpressure。既有 5 分钟健康采样通过正常结构化管线输出，未测量 Average/P99 延迟为 null；跨线程各计数不是全局强一致事务。 |
| DOC-LOG-P3 | 已关闭 | 本文档 §3/§6/§21 + `ArchitectureContractLoggingPhase3Tests` 终验 |

---

## 22. 日志系统阶段四（UI 与查询升级 / 易诊断）（2026-08-01）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| LOG-P4-TRACE | 阻断 | `TraceIdContext`/`SpanIdContext`/`UserActionIdContext` AsyncLocal 进程内传播；`LogLineFormatter.Format` 扩展可选上下文参数；`GuardianLoop.WriteLog` 传入 `Current` 值 |
| LOG-P4-QUERY | 阻断 | `LogIndexBuilder` + `LogQueryService` 查询索引架构：日志文件 → 内存索引 → 多条件过滤 + 分页查询；CancellationToken 可取消；索引可重建 |
| LOG-P4-UI-FILTER | 阻断 | `LogViewController` 新增 `OperationIdFilter`/`EventNameFilter`/`ProcessIdFilter`/`ThreadIdFilter` 属性并扩展 `GetFilteredLines`；S3 解锁阶段三 4 个 WIP 禁触 UI 文件 |
| LOG-P4-UI-ASYNC | 阻断 | `SettingsLogRefreshPipeline` 为 UI 非阻塞唯一入口：受控的后台执行完成读取/过滤，结果仅经 Dispatcher apply 回 UI；`LogViewBackgroundQuery` 为可复用后台查询原语；`CancellationToken` 取消；禁止 UI 线程执行大日志文件过滤。具体调度原语不得取代上述所有权、取消与资源边界契约。 |
| LOG-P4-JSONL | 阻断 | `LogJsonlFormatter` 日志行与 JSONL 双向转换（D1 落点：主日志保持纯文本，JSONL 为独立诊断文件 `logs/SmartGuard.jsonl`；`FormatLine` 输出纯 JSON）；`DiagnosticBundleExporter` 追加 `logs/SmartGuard.jsonl` 导出条目 |
| LOG-P4-FILTERSPEC | 阻断 | `LogQueryFilterSpec` 结构化筛选规格 + `LogFilterBuilder` 流式构建器；`LogViewController.ApplyFilterSpec` 接入统一筛选入口；`LogQueryFilterSpec.ToQueryRequest` 组合器统一 UI 直筛与索引查询筛选真源 |
| LOG-P4-HISTORY | 阻断 | `LogTailReader` 为日志页历史读取唯一入口：按新到旧消费 `SmartGuard.log` 与同目录 `SmartGuard.log.*.bak`，恢复为时间顺序后与 startup 回退合并；总载入量受最近 256KB 预算约束，状态栏统计仅基于该已载入集合 |
| LOG-P4-VERIFY | 阻断 | 终验：§22 八条 LOG-P4-* 收口行齐备 + DOC-LOG-P4 状态"已关闭"；§3 含阶段四全部新类型行；§6 含阶段四门禁索引行；WIP 禁触清单调整为 9 个（含 `SettingsLogPageHost`、`SettingsLogRefreshPipeline` 等） |
| DOC-LOG-P4 | 已关闭 | 本文档 §3/§6/§22 + `ArchitectureContractLoggingPhase4Tests` 终验 |

---

## 23. 日志工作台 P0（唯一真源与反重复）（2026-08-02）

**关联 ADR：** [docs/decisions/0001-log-workbench-architecture.md](decisions/0001-log-workbench-architecture.md)

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| LOG-WB-HISTORY | 已关闭 | `LogTailReader` 为 configured 主日志与同目录归档历史读取唯一实现；扩展 cursor 分页时仍禁止第二套主日志 `FileStream` reader；startup 为只读兼容 source，由 `LogTailReader` 合并 |
| LOG-WB-QUERY | 已关闭 | `LogIndexBuilder` + `LogQueryService` 为日志查询与内存索引唯一真源；UI 筛选与诊断查询准备不得另建 query service |
| LOG-WB-CONFIG | 已关闭 | 日志筛选器、视图偏好及相关 config 字段只经 `GuardConfigRepository.TryLoad` / `Save` 持久化；禁止旁路全量写或 Engine/Tray 直读磁盘 config 决定日志 UI 状态 |
| LOG-WB-EXPORT | 已关闭 | `DiagnosticBundleExporter` 为诊断包 zip 唯一真源；UI 只构造 `DiagnosticBundleRequest` 并注入共享 `ILogEventSource`；exporter 禁止自行打开 configured 主日志/归档，允许读取 config/status 与 request 显式包含的 startup/emergency/crash |
| LOG-WB-VIEWPORT | 已关闭 | `LogViewportController` 为日志 ScrollViewer 唯一滚动 mutation 所有者；`SettingsLogRefreshPipeline` 只提交 delta/状态 |
| LOG-WB-EVENT | 已关闭 | `LogEvent` + `LogEventDecoder` + `LogJsonlFormatter` 为结构化事件真源；configured 主日志物理追加只经 `SmartGuardLogWriter` |
| DOC-LOG-WB | 已关闭 | 本文档 §23 + ADR `0001-log-workbench-architecture.md` + `LoggingWorkbenchArchitectureTests` + `LogWorkbenchPerformanceTests` |

---

## 24. 日志工作台 P1（组合查询、关联与诊断包）（2026-08-03）

**关联 ADR：** [docs/decisions/0001-log-workbench-architecture.md](decisions/0001-log-workbench-architecture.md)

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| LOG-WB-P1-AST | 已关闭 | `LogQueryParser` + `LogQueryExpression` + `LogQueryService.QueryAsync` 为组合查询唯一 AST/执行真源；`LogFilterBuilder`/`LogQueryFilterSpec` 编译到同一 AST；无效表达式保留上次有效结果 |
| LOG-WB-P1-REGEX | 已关闭 | `LogRegexSpec` 为安全正则唯一规格；优先 `RegexOptions.NonBacktracking`；回溯 pattern 100ms timeout + 首次 timeout 终止整次查询 |
| LOG-WB-P1-MATCH | 已关闭 | `LogMatchNavigator` + `SettingsLogSearchCoordinator` 为匹配导航与 query generation 唯一协调入口；扫描中 `M+`、完成后精确总数 |
| LOG-WB-P1-EXCEPTION | 已关闭 | `LogExceptionGroupProjector` 为异常折叠/归组唯一投影；折叠仅影响视图，复制/导出保留完整 stack |
| LOG-WB-P1-CORRELATION | 已关闭 | `LogCorrelationCoordinator` + `LogOperationProjector` 为 Operation/Trace/UserAction 关联唯一入口；返回主视图恢复 cursor/anchor/selection |
| LOG-WB-P1-DIAGNOSTIC | 已关闭 | `DiagnosticBundleRequest` + `DiagnosticRedactor` + `DiagnosticBundleExporter.ExportAsync` 为 scoped 诊断包唯一链路；UI 只构造 request 并注入共享 `ILogEventSource`；取消删除临时 zip、默认不覆盖既有目标 |
| DOC-LOG-WB-P1 | 已关闭 | 本文档 §24 + `LoggingWorkbenchArchitectureTests` + `LogWorkbenchP1AcceptanceTests` + `DiagnosticBundleExporterArchitectureTests` |

## 25. 日志工作台 P2（保存、会话、统计与键盘无障碍）

**关联 ADR：** [docs/decisions/0001-log-workbench-architecture.md](decisions/0001-log-workbench-architecture.md)

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| LOG-WB-P2-CONFIG | 已关闭 | `SavedLogFilterCoordinator` 经 `GuardConfigRepository` 保存筛选器与视图偏好；禁止旁路 config 写入 |
| LOG-WB-P2-SESSION | 已关闭 | `LogSessionCoordinator` 将会话选择编译为既有查询 clause；会话元数据懒加载，不复制事件集合 |
| LOG-WB-SESSION-SCOPE | 阻断 | `LogViewController.ScopeExpression` 是事件列表唯一临时会话范围入口；session clause 必须经 `LogViewFilterState.WithScope` 与当前 base query 组合。`LogSessionCoordinator` 是会话选择、首次主视图快照和恢复的唯一 owner；Host 只编排加载/取消/代际，不得手工过滤事件窗口或复制 `LogQueryAndExpression` 组合逻辑 |
| LOG-WB-P2-STATS | 已退役 | 诊断统计面板与聚合链（`LogAggregationService` / `LogAggregationRequest` / `LogAggregationResult` / `LogStatisticsPresenter` / `LogErrorTrendChart`）已于 2026-09-25 退役并删除；排障真源回到原始日志查询（`LogQueryService` / `LogTailReader`）与 `DiagnosticBundleExporter` 诊断包导出 |
| LOG-WB-P2-INTERACTION | 已关闭 | `LogWorkbenchCommands` 是快捷键 RoutedCommand 真源；`SettingsLogPageHostWiring` 统一接入搜索、匹配、导出、视口和临时层；`LogViewportController` 仍是唯一滚动 mutation owner |
| LOG-WB-P2-A11Y | 已关闭 | 日志查询、列表、状态和每条日志行提供 Automation Name/HelpText；状态使用 `AutomationProperties.LiveSetting=Polite`，所有状态更新经 `LogStatusLiveRegion.Set` 写入文本并显式触发 `LiveRegionChanged`；2026-08-07 原生 UIA 通过 200% DPI 状态边界、无匹配/有效匹配、Ctrl+F/F3 焦点、滚动边界和跟随最新切换，独立 UIA 客户端收到 `LiveRegionChanged`；实际系统矩阵在 `HighContrast=True`、`ClientAreaAnimation=False` 下通过同一交互并完成截图检查，退出后恢复原设置；Narrator 人工听觉、100% DPI 和 Computer Use 完整视觉闭环按当前交付范围延期且不阻断，Computer Use `EnumWindows 0x80070003` 记录为外部验收工具回归而非产品失败 |
| DOC-LOG-WB-P2 | 已关闭 | 自动化门禁全绿（Pester 118/118、.NET 1369/1369、Tray 2/2、Installer 9/9；高 DPI 修复提交 `02ab477`、LiveRegionChanged 修复提交 `031e74c`）；200% DPI、高对比度、减少动画和原生 UIA 验收通过；未执行的 Narrator 人工听觉、100% DPI、Computer Use 完整视觉、P0 两分钟滚轮/Thumb 观察和 P1 手工解包均保留真实记录并按当前交付范围延期、非阻断 |

## 26. 日志中心真分页（2026-08-08）

| ID | 状态 | 真源/入口 |
|----|------|-----------|
| LOG-WB-PAGER | 已关闭 | `LogTailReader` 通过 `ILogSnapshotPhysicalLineSource` 实现 snapshot-aware physical scan；`LogQueryService` 构建稀疏 `LogQueryPageIndex` 并读取精确页；Settings 只持有当前页，不建立第二 reader/query engine；性能门禁由 `LogWorkbenchPerformanceTests` 证明，真实 WPF 分页矩阵由 `LogWorkbenchP1AcceptanceTests.A_page_matrix_runs_against_the_real_settings_window` 证明，最终证据写入 `.work/log-pagination-acceptance/` |
