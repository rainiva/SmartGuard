# 统一自动守护规则实现计划

**日期：** 2026-09-30

**状态：** 方案已确认；2026-09-30 完成 grill-me 复审后，按用户补充的性能、功耗要求修正为优先事件通知、严格限制后续工作、复用现有周期补偿，以实测决定是否保留复杂度。§7 为当前采集机制裁决。2026-10-01：任务 1–5 实现与自动化验证已完成（aeee74a..f675d0e）；真实桌面验收因锁定会话 BLOCKED，本计划不代表功能或桌面验收已通过（证据见 `.work/guard-rules/`）。

**目标：** 自动守护统一经现有 `PolicyEngine` 决策；第一版增加前台程序准入，保留手动高性能例外，配置保持克制。
**技术：** 现有 .NET 8 / Windows Engine、WPF Settings、Tray 与 Win32 API，不引入规则引擎依赖。

## 1. 已确认的行为

有效配置下按以下顺序判定；配置不可用时不从默认值猜测授权。电源生命周期的 `AllowPolicyMutation` 仍是 Engine 执行层安全门禁，任何规则都不能绕过：

| 优先级 | 条件 | 决策 |
|---|---|---|
| 1 | 手动临时高性能有效 | 沿用现有人工接管逻辑，不受名单和暂停限制 |
| 2 | 用户暂停 | 不干预 |
| 3 | 前台限制开启，名单为空 | 不干预，原因是名单为空 |
| 4 | 前台限制开启，前台无法识别 | 不干预，原因是识别不可用 |
| 5 | 前台限制开启，前台未命中 | 不干预，原因是等待指定程序 |
| 6 | 限制关闭，或前台命中任意条目 | 沿用现有空闲时间、电量、插电策略选择目标计划 |

- 前台限制默认关闭；旧配置缺少新字段时保持原行为。
- 离开名单后停止自动切换及纠偏，保留当前计划；不恢复旧计划、不主动切回平衡。
- 再次命中按当前状态计算，不重置空闲计时；目标程序始终在前台时仍按原阈值降档。
- 不干预时继续采集、发布健康状态和观察当前计划；不发“将纠偏”通知，不把正常未命中写成 `Paused=true`。
- 非守护期间观察到的计划作为当前基线，重新命中不得把过去的变化误报为刚发生的外部变更。
- 首版通过选择 `.exe` 添加，按规范化的完整路径匹配，重复项合并；名称只用于展示。匹配不作为安全身份认证。
- 设置只增加一个开关、名单、添加和删除。不开设多条规则编排、通配符、正则、父子进程继承或每程序独立动作。
- 前台窗口所属进程可能不是启动器；首版不承诺覆盖所有打包应用和托管窗口。路径变化保留条目，说明可删除后重新添加，不自动追踪安装目录或扫描程序是否存在。
- 优先使用前台变化事件通知，复用既有检查周期补偿，不新增高频周期轮询；默认补偿检查间隔仍为 15 秒。事件只表示需要重新核验当前前台，不重放每个历史窗口，不承诺逐次切换均触发动作或零漏判；发起新动作前仍核验最新资格。

## 2. 责任与契约

**自动决策唯一链路：** 前台事实采集 → `PolicyEngine` → `GuardianIterationRunner` 执行 → `StatusPublisher` 发布 → Settings / Tray 展示。

人工路径保持 `HighPerformanceBoost.Apply` → `ConfigMutationService` 写期限及现有 activator 切换；Engine 后续通过统一决策维持该接管。本次不迁移人工即时动作，也不建设跨进程命令总线。暂停优先级和人工期限的本地时间格式保持现状，只把决策中的当前时间改为显式输入，不顺带迁移时间 Schema。

以下是本次设计名称，尚未存在的类型在对应任务创建；不再建立第二套策略入口：

| 文件或类型 | 责任 |
|---|---|
| `src/SmartGuard.Engine/Domain/PolicyEngine.cs` | 新增统一 `Evaluate` 决策入口，输入配置、空闲、电源、前台事实和显式当前时间；迁移现有生产调用，删除退役决策入口 |
| 新建 `src/SmartGuard.Engine/Domain/GuardDecision.cs` | 决策值：可空 `TargetPlanGuid`、`Source`（Manual / Automatic / None）、`Reason`；空目标明确表示不干预 |
| 新建 `src/SmartGuard.Engine/Domain/ForegroundObservation.cs` | 事实值：识别是否成功、可空完整路径、失败原因；不依赖 Win32/WPF，不携带窗口标题 |
| 新建 `src/SmartGuard.Engine/Infrastructure/ForegroundProgramReader.cs` | 在既有轮次中采集 Win32 前台事实；无 hook、监听线程、Timer、事件队列或进程身份缓存；每次查询释放进程句柄 |
| 新建 `src/SmartGuard.Engine/Infrastructure/ForegroundEventListener.cs` | 仅监听前台变化并提交合并信号；最多一个 hook 和消息循环线程，归 `GuardianLoop` 启动、停止、等待退出；不拥有规则、IO 或电源动作 |
| `src/SmartGuard.Configuration/GuardConfig.cs` | 新增 `ForegroundRestrictionEnabled=false`、`ForegroundProgramPaths=[]` |
| `GuardConfigRepository` / `SettingsSaveCoordinator` | 沿用配置读取、原子保存和设置保存入口；同步 Validator、SnapshotMapper、ResetService |
| `src/SmartGuard.Contracts/StatusModels.cs` | 新增可选 `guardSource` / `guardReason` 和 `foregroundEventsAvailable`，使用登记的稳定代码；原因包括空名单、未命中、识别失败、配置不可用及执行被生命周期或探测失败阻止；监听失败标识周期补偿降级，不新增另一份健康存储，不改变 `paused` 含义 |
| `StatusPublisher` / `StatusJsonReader` / `StatusStore` / `StatusReader` / `TrayDisplayState` | 保持 Engine 单写者及共享读取链；更新 Publisher 相等判定、Tray 相等判定与 Clone，原因变化必须传至 UI；旧状态缺字段显示未知 |

## 3. 影响与资源边界

按项目规则属于**高影响改动**：涉及配置、跨进程状态、自动决策和一个受控后台监听。下列数值为本计划提出的初始工程预算，不是已测结果；事件路径须先通过任务 3 的对比试验。

| Change Impact Matrix | 本次处理与证据 |
|---|---|
| Function / Architecture | 上表行为矩阵；单一决策入口；架构门禁证明采集、UI 不切换计划 |
| Data / Compatibility | 新字段默认关闭；保存、局部 mutation、重置、旧配置和旧状态测试；无法读出有效配置时不因默认回退意外绕过已启用限制 |
| Performance / Resource | 最多 128 个路径，O(N) 线性匹配，无进程身份/集合缓存和全进程扫描；最多一个阻塞消息循环线程及一个 hook、一个待处理前台标记，无每事件 Task 或历史队列；采集缓冲区上限 32,768 字符；新增轻量事件处理初始最多每秒一次，新增完整评估初始至少间隔 5 秒；均用既有等待逻辑和少量时间戳，不新增 Timer 或通用调度器 |
| Concurrency & Time | 复用既有串行执行和恢复 directive；轻量事件与周期到期分别处理，完整周期截止时间不得被事件推迟；受限期间保留最新待处理标记，不能丢弃最后信号后一直等下一周期；不干预资格可先更新，完整动作前重新核验，不依据旧电量/idle 发起动作；不重置 idle，不改人工期限 |
| Reliability / Recovery | HWND 为空、进程退出、路径查询失败为 Unknown；注册失败或监听线程退出可观测，降级到同一 Reader 的原周期采样；不在高频路径重启监听；hook 在注册线程卸载，等待线程退出、释放回调和句柄；恢复重新采集，配置失败禁止新动作 |
| Security / Observability | 路径查询使用最小权限，不新增 UAC；不读取窗口标题或记录程序使用历史；状态区分暂停、等待、空名单和识别异常；日志只记转换并受现有去重控制 |
| Operations | 安装器、计划任务注册和发行流程 N/A；新版本读旧配置受测。旧二进制不会执行新限制，回滚须明确恢复原行为，不能声称限制仍生效 |

**Critical Module Check：** 配置 Owner 为 `GuardConfigRepository` / 已登记 mutation API；运行状态单写者为 `StatusPublisher`；轮次执行 Owner 为 `GuardianIterationRunner`；时间与休眠仍经 `IdleTracker` / `PowerLifecycleStateMachine`；日志和通知仍走现有管线。必须保持：配置不丢字段、状态不旁路写、采集资源有界且释放、生命周期安全门禁不绕过、规则未命中不误纠偏。

事件回调只原子设置前台 dirty 标记，仅在无待处理信号转为有信号时唤醒 Engine；不得查询进程、读配置、写日志或直接调用完整 Runner。Engine 消费信号后调用同一 Reader 读取当前事实，通过 `PolicyEngine` 中同一准入规则比较与上次结果的变化；该准入判断由统一 Evaluate 复用，不复制名单规则。匹配/未知及健康状态未变时立即结束，不查询电源计划或 WMI、不写 status 或日志；未命中先撤销自动动作资格，命中转变才安排受限的完整评估并刷新 idle/电源输入。周期到期仍执行原策略链，前台事件不得拖延空闲降档。保存的上次准入结果只是有界运行状态，不是通用缓存。

执行前重新读取目标文件与 Git 状态，保护现有修改。Engine 的动作资格改用既有 `GuardConfigRepository.TryLoad` 的成功结果；`LoadOrDefault` 不可作为动作授权依据。TryLoad 的文件元数据访问也可能抛异常，由本轮 Engine 读取边界转换为配置不可用。配置缺失、破损、访问异常或新规则字段非法时不发起新动作，也不从旧快照重建人工期限；人工 UI 命令的既有失败行为不在本次重构范围。恢复有效读取后重新决策，不新增缓存或全项目配置恢复框架。原启动初始化允许首次生成默认文件，但不得用默认内容覆盖已存在的坏文件。

规则路径语义固定为 Windows Win32 绝对 `.exe` 路径，每项最长 32,767 个字符，经 `Path.GetFullPath` 规范化，用 `OrdinalIgnoreCase` 匹配及去重；不支持相对路径、通配符、设备路径或解析符号链接/短路径别名。输入保存与运行时验证共用同一规则校验，不重复维护算法；非法条目拒绝保存，磁盘无效值不能被静默忽略后继续守护。无需每轮 `File.Exists` 扫描；添加时由文件选择器确认目标，路径更新后由用户重新添加。

## 4. 开发顺序

每项按“行为测试 → 保存符合预期的 RED → 最小实现 → GREEN / 回归 → 检查 diff → 只提交本任务文件”推进。RED 必须由目标行为缺口导致，不能把编译或环境失败作为证据。每项实现前先同步相关架构契约并补架构门禁。

### 任务 1：配置持久化与旧数据兼容

**文件：** `src/SmartGuard.Configuration/` 下的 `GuardConfig.cs`、`GuardConfigRepository.cs`、`GuardConfigValidator.cs`、`SettingsSnapshotMapper.cs`、`GuardConfigResetService.cs`、必要的 `ConfigMutationService.cs`；`Tests/SmartGuard.Configuration.Tests/` 下对应行为测试；架构契约。

- [ ] RED 覆盖旧配置默认关闭、中文路径往返、路径重复、超过 128 项拒绝保存、保存其他设置及暂停/手动接管不丢名单。
- [ ] 经已有 repository/save 链实现；恢复默认关闭限制并清空名单；无效名单不悄悄变成无限制模式；破损配置按 §3 收敛。
- [ ] 命令：`dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj`；预期 PASS，并保留保存失败后正式配置仍有效的证据。

### 任务 2：统一决策与行为矩阵

**文件：** `PolicyEngine.cs`、新 `GuardDecision.cs` / `ForegroundObservation.cs`、`GuardianIterationRunner.cs` / `GuardianLoop.cs`；`Tests/SmartGuard.Engine.Tests/PolicyEngineTests.cs`；新增 `Tests/SmartGuard.Architecture.Tests/GuardRulesArchitectureTests.cs`；`docs/ARCHITECTURE-CONTRACT.md`。

- [ ] 使用任务 1 已存在的配置字段，先以限制关闭的输入证明迁移前后结果一致，覆盖阈值前后、低电量、插电、暂停和手动接管；注入本地当前时间测试接管到期边界。
- [ ] RED 验证空名单、未知、未命中不干预；命中保持原策略；先用合成前台事实，限制开启但真实采集尚未接入时返回 Unknown，不旁路回退。
- [ ] 输出统一决策，Runner 的切换与纠偏资格消费同一决策；同时接入有效配置读取资格。迁移旧 `GetExpectedPlanGuid` 的生产调用，不保留并行自动目标算法；展示辅助方法继续保留。
- [ ] 目标命令：`dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --filter FullyQualifiedName~PolicyEngineTests`，并跑 Runner 受影响测试；预期先行为 RED，再全部 PASS。

**RED 口径：** 先在已有 API 上写能编译的行为断言，例如将带新字段的 JSON 经 `GuardConfig.LoadFromJson` 反序列化，再经 repository 保存到新的测试路径，断言新字段与名单仍保留；当前类型会忽略新字段，该往返可形成行为 RED。决策测试使用合成事实；新 API 可先有无副作用的最小声明，但未实现行为断言必须失败。不存在的类型、方法或属性引起的编译失败不计为 RED。本轮仅确定测试设计，尚未运行此用例。

### 任务 3：真实前台采集与受控执行

**文件：** 新 `ForegroundProgramReader.cs` / `ForegroundEventListener.cs`、`src/SmartGuard.Engine/Worker/GuardianLoop.cs` / `GuardianIterationRunner.cs`、必要的 `src/SmartGuard.Configuration/GuardianIterationTiming.cs`；新 `Tests/SmartGuard.Engine.Tests/ForegroundGuardIntegrationTests.cs`；现有 `GuardianPowerLifecycleIntegrationTests.cs` / `GuardianIterationTimingTests.cs`；架构契约。

- [ ] 先做有界对比试验：用同一 Reader 对比原周期采样和事件通知加周期补偿，固定电源计划及业务工作负载，测静止、同准入程序间切换、名单内外快速切换；按 §7 判定并记录。仅用临时测试入口比较，不建设双模式产品开关，最终保留一个正常机制及其必要降级。
- [ ] RED 用可注入事件/事实序列和单调时钟覆盖事件合并、末次信号在节流后仍处理、注册失败/线程退出、查询中 HWND/PID 改变、动作前切换、进程退出、休眠恢复和停止；不要求按历史事件逐个切换计划。
- [ ] 使用 `GetForegroundWindow` → `GetWindowThreadProcessId` → `QueryFullProcessImageNameW`，查询后核验前台 HWND/PID 仍一致，不一致返回 Unknown；路径查询只申请 `PROCESS_QUERY_LIMITED_INFORMATION`。周期补偿与事件消费共用 Reader，不按事件携带的旧窗口直接执行动作。
- [ ] 监听使用 `EVENT_SYSTEM_FOREGROUND` / `WINEVENT_OUTOFCONTEXT`，只保留当前 dirty 信号；注册线程具有阻塞消息循环，固定托管回调生命周期，在同一线程 Unhook，停止后无回调写入或后台工作。
- [ ] 限制关闭时停止监听并跳过采集；暂停、空名单或人工接管有效时忽略前台事件工作，仅保留受控订阅以避免频繁注册卸载，状态变化按原周期重新检查。恢复自动准入条件后立即标记重采集；异常不会让守护循环静默退出。
- [ ] 验证“只变前台、准入结果不变”不会增加完整 Runner 次数、PowerCfg/WMI 次数、status 写入或日志条数；只有轻量读取计数允许按节流上限增加。周期到期按原链运行，不能被持续事件饿死。
- [ ] 发起计划切换及切前亮度同步前复核最新事实与配置，经同一决策入口决定是否执行。已经成功切换后的亮度恢复完成收尾，不因前台变化留下半完成状态；不承诺检测与系统切换绝对原子化。
- [ ] 非守护期间成功探测计划则更新观察基线；首次进入自动准入时吸收当前计划，再按本轮策略纠偏，不补发历史外部变更通知。复核失败时同样不发纠偏通知。
- [ ] 探测失败时不切换计划，但仍发布“本轮无法执行”的原因；保留的计划信息必须标识为上次观察，不显示为新鲜测量，不为此建设独立遥测系统。
- [ ] 命令：`dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj`；预期 PASS；句柄释放、hook / 消息线程退出，重复启停不积累后台资源。初始节流参数按试验结果收敛并冻结，不对用户暴露调参项。

### 任务 4：轻量设置与状态展示

**文件：** `src/SmartGuard.Settings/SmartGuard.Settings.xaml`、`SettingsPolicyCoordinator.cs`、`SettingsWindowController.cs`、`StatusReader.cs`；`src/SmartGuard.Contracts/StatusModels.cs`；`src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs`；`src/SmartGuard.Tray/TrayStatusFormatter.cs` / `TrayDisplayState.cs` / `StatusStore.cs`；对应 Settings、Tray、Engine 状态测试。

- [ ] RED 覆盖开关、选择 exe、去重、删除、重启保留、空名单提示、旧状态缺字段，以及等待名单不会切换暂停开关。
- [ ] UI 仅编辑配置、展示 Engine 结果。空名单显示“添加程序后生效”；未命中显示“等待指定程序”；识别失败显示“暂时无法识别前台程序”；人工接管显示“临时高性能”。
- [ ] 将运行时配置/识别异常和状态陈旧与普通未命中分开；字段缺失或状态过期不得显示确定的正在守护。
- [ ] RED 覆盖“目标计划等其他字段不变，只有 guardReason 改变”仍写入 status 并更新 Tray；Clone 保留新字段。Source / Reason 为跨进程稳定代码，展示文案只在 UI 映射；不根据空闲标签推导守护是否在执行。
- [ ] 分别运行 `dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj`、`dotnet test Tests/SmartGuard.Tray.Tests/SmartGuard.Tray.Tests.csproj` 和 Engine 状态测试；预期 PASS。

### 任务 5：业务链验收与性能证据

- [ ] 在实现前后及任务 3 对比方案间，以相同机器、检查间隔和工作负载测 0 / 1 / 128 项名单、静止前台、同准入切换及名单内外快速切换。记录进程 CPU 时间、唤醒、线程/句柄、内存、Reader 调用、完整评估、PowerCfg/WMI、status/日志写入和响应延迟；静止时监听不得自行周期唤醒，同准入事件不得触发完整工作；末次稳定前台须在节流结束后重新评估，原性能门禁不退化，不承诺 2 秒响应。
- [ ] 功耗分两层：固定电源计划、亮度和供电状态比较采集机制自身开销；再单独测规则实际切换下的整机能耗。相同条件做至少三组交替顺序重复，记录样本波动与测量工具；能耗不可测或差异小于测量分辨率则标记未验证/无可分辨差异，不以 CPU 占用或短时间电池百分比推断省电。不安装常驻监控组件。
- [ ] 执行 `dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj`，确认单一入口及契约；执行全量 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1`。
- [ ] 同时核对退出码与 `.work/test-results/` summary JSON，逐项记录 Pester、.NET、Tray Integration、Installer Integration 的通过/失败/跳过数与未运行原因，不引用旧结果。
- [ ] 真实桌面走通：添加目标 → 命中升档 → 无输入达到原阈值降档 → 离开保留计划 → 名单外人工改计划不纠偏 → 返回重新决策 → 名单外手动高性能有效且到期回归规则。
- [ ] 实测事件间快速切换、目标退出、锁屏/UAC、休眠恢复、查询及监听故障、配置读写失败及重启；确认等待与降级/故障可区分，资源无持续增长。不同桌面/权限下无法识别应保留当前计划。
- [ ] 检查键盘添加/删除和焦点、可访问名称、100% / 200% DPI、高对比度与减少动画相关路径；未执行标记 `NOT RUN`，环境阻塞标记 `BLOCKED`。
- [ ] 保存本次 RED/GREEN、性能和桌面证据到 `.work/guard-rules/`；检查 diff，只提交已验证的任务文件，不提交现有无关修改。

## 5. 完成标准与依据

任务 1–5 的行为、架构、配置、生命周期及真实桌面证据齐备才能宣称功能完成。旧入口无生产调用、前台事实无第二采集真源、规则不干预不误纠偏是阻断验收项。后台渲染保护、应用包身份、多规则编排、安装器改造均不在本次范围。

本计划只描述后续工作；本次文档交付仅检查 UTF-8 无 BOM、链接/结构和 `git diff --check`，不运行全量代码测试。

- 项目规则：[PROJECT-RULES.md](../PROJECT-RULES.md)；权威架构：[ARCHITECTURE-CONTRACT.md](../ARCHITECTURE-CONTRACT.md)。本计划不取代两者。
- 官方依据：[GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow) 明确可能返回空窗口；[GetWindowThreadProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid) 查询窗口所属进程；[QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew) 支持 limited-information 权限和 Win32 路径格式。实际应用兼容性与时延须以桌面验收确认。
- 监听依据：[SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook) 要求消息循环并支持 out-of-context 回调；[UnhookWinEvent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unhookwinevent) 要求在原注册线程卸载。事件并不提供检测与电源动作的原子性保证。

## 6. grill-me 自主审查记录（2026-09-30）

**结论：** 无需用户选择的阻塞项。审查方式为同一审查者先逐分支质疑，再切换专业视角复查；不冒称多个独立专家或已执行测试。前台准入、离开保留、原规则降档、人工例外为用户决策。初次审查建议的“仅周期采样、不建 hook”已被用户随后确认的性能/功耗修订取代，当前执行以 §7 及已同步的任务 3 / 5 为准；有界线性匹配和不预建缓存仍保留。

### 6.1 决策树收敛

| 自主质疑 | 依据与采用的推荐项 | 处置 |
|---|---|---|
| 统一自动架构是否意味着迁移人工即时操作？ | `HighPerformanceBoost.Apply` 先写期限再经 activator 切换；用户明确人工例外。保留人工路径，统一 Engine 自动决策，不加 IPC 总线 | 已解决 |
| 是否必须有 hook、每秒调度和 2 秒目标？ | 按用户性能/功耗修订，优先前台事件加原周期补偿；有限轻量处理，非每事件完整轮次；响应延迟及资源以实测裁决，不设 2 秒硬承诺 | 已修订，见 §7 |
| 128 项是否需要 HashSet 缓存及失效协议？ | 有界列表且原周期至少 5 秒；先线性匹配，后续仅凭性能证据考虑优化 | 已解决，删除缓存 |
| 开启限制但配置损坏，能否回退默认？ | `GuardConfigRepository.LoadOrDefault` 会创建默认配置，默认限制关闭。动作读取必须能区分有效配置与回退；局部使用现有 TryLoad，不全局重写 repository | 已解决，加入失败用例 |
| 先决策、后配置能否形成可执行 TDD 顺序？ | 原任务 1 依赖任务 2 才创建的字段。交换为先配置、后统一决策；RED 必须为可编译的行为失败 | 已解决 |
| 名单外没有观察到计划变化，返回时会误报吗？ | Runner 使用 LastKnownGuid 判外部变化。新增准入转换的基线语义，首次准入不补发过去的纠偏通知 | 已解决 |
| 状态新增字段后会自然更新吗？ | `StatusPublisher.StatusPayloadEquals` 与 `TrayDisplayState.StatusEquals` / Clone 都逐字段处理，新增字段不会自动参与 | 已解决，补齐链路验收 |
| “所有规则统一”是否需要自由规则编排？ | 用户已选“架构统一，配置克制”。仅支持固定顺序与两个新配置字段，不添加表达式、插件、优先级编辑器 | 已解决，明确范围 |

### 6.2 专业视角复审

| 视角 | 反例或主要风险 | 写回的结论与验证要求 |
|---|---|---|
| 架构 | 采集器直接改电源计划，或人工路径被误纳入重构 | 自动统一 Evaluate → Runner；人工路径保留；架构门禁检查调用职责，保留展示辅助方法 |
| Windows / 生命周期 | HWND 失效、受限进程、休眠后旧事实、hook/句柄泄漏 | 同一 Reader 采集、最小权限、Unknown；一个有 Owner 的阻塞监听线程；同线程卸载及等待退出，不绕过恢复 directive |
| 并发 / 恢复 | 采样后前台切走；复核失败却先发纠偏通知；切换后中断亮度恢复 | 复核覆盖全部切前副作用；失败无新纠偏通知；已提交动作完成收尾，保留原取消规则，不保证原子切换 |
| 配置 / 兼容 | 保存普通设置丢名单；坏 JSON 被当成默认关闭；回滚旧版本仍声称受规则保护 | mapper/save/reset/mutation 全链测试；坏配置禁止 Engine 新动作；旧配置默认关闭，旧版本明确不支持限制 |
| 测试 | 新类型不存在造成编译失败被当 RED；缺少任务依赖；自动化被当桌面验收 | 先配置后决策；分离可编译行为 RED 与契约门禁；目标业务链、全量 summary 和真实桌面证据分层 |
| 产品 / 无障碍 | 名单外高性能仍保留；事件通知被误认为绝对实时；等待状态被显示为暂停 | 固定“不干预、保留计划”；事件受节流和在途动作约束、失败按周期补偿；一个开关和名单，键盘和状态可访问验证 |
| 性能 / 功耗 / 个人维护 | 每事件跑完整链放大 WMI/IO；把少线程或低 CPU 当省电；为预计增长引入框架 | O(N≤128)、信号合并、轻重工作分离、周期不加频；记录 CPU/唤醒/IO 及可测能耗，以对比试验决定是否保留 hook；不预建调度器、缓存或常驻功耗框架 |

**事实、推断与未验证信息：** 代码依据来自初次复审的当前文件读取，实施前仍须重新核对。“事件加周期补偿是否更值得”和“线性匹配足够作为首版”目前是待实测的工程判断；官方接口约束已核对，但不能据此推断能耗。目标应用兼容性、真实延迟、UIA、资源增长和整机能耗均尚未验证，保留在任务 3 / 5；不得据此声称实现通过。

**后续重新进入设计的触发条件：** 事件试验没有可测收益、出现资源/功耗回退、准入延迟不满足实际使用或指定应用无法以 exe 路径表达时，返回有界方案比较，不叠加缓存、调度器或身份框架补救。实现阶段的 RED、门禁与桌面证据仍是必须完成的验收条件。

## 7. 性能与功耗修订裁决（2026-09-30，用户确认）

**当前原则：** 优先事件通知，严格限制后续工作，复用现有周期补偿；以实测决定是否值得保留这份复杂度。不预建通用调度器、缓存系统或功耗监控框架。

- 初次“少一个线程所以更省”的推理不成立：线程数量不是唤醒、执行工作或能耗的替代指标。本次明确撤回仅据简单性排除 hook 的推荐；不声称事件方案一定更省。
- 正常路径为事件驱动的轻量准入更新；原检查周期承担策略更新和漏通知补偿。事件不执行动作，不建立第二套名单解析或电源策略。注册失败可降级到同一采集/决策链的周期路径，状态明确标识；无独立常驻探活轮询。
- 一份 dirty 标记、一次在途处理、有界节流；初始轻量每秒最多一次、新增完整评估至少间隔 5 秒，仅为试验起点。复用 Engine 等待截止时间，不能让轻量事件唤醒自动进入全部 PowerCfg/WMI/文件写入；持续事件不拖延原周期。不得为保持计数更低而放弃末次稳定前台的更新或动作前资格核验。
- 监听线程等待消息时阻塞，不忙等。回调短且无 IO；停用后受控卸载/退出，暂停和人工接管时不为事件做无用业务查询。只保存必要运行状态，不建进程身份缓存；PID/HWND 不能作为跨进程退出重启后的永久身份。
- **实测裁决：** 同条件对比周期基线与事件候选。候选须在名单内外切换的响应延迟上有可重复收益，同时静止及同准入切换不增加完整业务 IO、不破坏既有性能预算、无资源增长；若能耗可测，差异须超出样本波动才能判断增加/降低。不可测时只能报告机制开销及功耗未验证，不能标为省电通过。
- 若候选没有可重复收益或引入不能收敛的开销，记录结果并返回周期方案的延迟边界复审，不把两者做成长期产品模式，也不自动宣称试验成功。个人项目只保留有证据值得维护的必要复杂度。
- 守护程序开销与电源计划导致的整机能耗分别报告。离开名单保留高性能仍可能继续耗电，这是既定产品行为；本次不改变退出动作，也不保证规则功能整体省电。

**本次交付边界：** 只修订计划与审查记录；未实现监听、未执行性能/功耗试验。当前只有文档编码、链接、结构与 diff 检查证据。
