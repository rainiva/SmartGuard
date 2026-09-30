# 统一自动守护规则实现计划

**日期：** 2026-09-30

**状态：** 方案已确认；2026-09-30 经 grill-me 自主审查及多专业视角复审，采纳修订后可按顺序实现，无需用户决定的阻塞项。尚未实现，本计划不代表功能或桌面验收已通过。

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
- 首版按既有检查周期采样，不追求即时响应：限制开关、名单编辑和前台变化在下一次允许执行的轮次生效，默认检查间隔为 15 秒。短暂切入切出可能不被采样，不承诺逐次捕获；发起新动作前仍核验最新前台资格。

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
| `src/SmartGuard.Configuration/GuardConfig.cs` | 新增 `ForegroundRestrictionEnabled=false`、`ForegroundProgramPaths=[]` |
| `GuardConfigRepository` / `SettingsSaveCoordinator` | 沿用配置读取、原子保存和设置保存入口；同步 Validator、SnapshotMapper、ResetService |
| `src/SmartGuard.Contracts/StatusModels.cs` | 新增可选 `guardSource` / `guardReason`，使用登记的稳定代码；原因包括空名单、未命中、识别失败、配置不可用及执行被生命周期或探测失败阻止；不新增另一份健康存储，不改变 `paused` 含义 |
| `StatusPublisher` / `StatusJsonReader` / `StatusStore` / `StatusReader` / `TrayDisplayState` | 保持 Engine 单写者及共享读取链；更新 Publisher 相等判定、Tray 相等判定与 Clone，原因变化必须传至 UI；旧状态缺字段显示未知 |

## 3. 影响与资源边界

按项目规则属于**高影响改动**：涉及配置、跨进程状态和自动决策，但不新增后台监听。下列数值为本计划提出的工程预算，不是已测结果。

| Change Impact Matrix | 本次处理与证据 |
|---|---|
| Function / Architecture | 上表行为矩阵；单一决策入口；架构门禁证明采集、UI 不切换计划 |
| Data / Compatibility | 新字段默认关闭；保存、局部 mutation、重置、旧配置和旧状态测试；无法读出有效配置时不因默认回退意外绕过已启用限制 |
| Performance / Resource | 最多 128 个路径；先用有界 O(N) 线性匹配，不建集合缓存；单轮最多初次采集和动作前复核两次，不扫描全部进程；不增加线程、Timer、队列或轮询频率；路径查询缓冲区上限 32,768 个字符，超过则 Unknown，不无限重试 |
| Concurrency & Time | 复用现有串行轮次和恢复 directive；执行前重新采集并经同一入口判断；不重置 idle，不改变人工期限格式；在途动作完成收尾，停止仍遵循已有取消协议 |
| Reliability / Recovery | HWND 为空、进程退出、路径访问失败均显式 Unknown；只影响当次准入，下一轮重新采集；每次释放句柄，无额外监听生命周期；配置失败时禁止新动作，恢复读成功后重新计算 |
| Security / Observability | 路径查询使用最小权限，不新增 UAC；不读取窗口标题或记录程序使用历史；状态区分暂停、等待、空名单和识别异常；日志只记转换并受现有去重控制 |
| Operations | 安装器、计划任务注册和发行流程 N/A；新版本读旧配置受测。旧二进制不会执行新限制，回滚须明确恢复原行为，不能声称限制仍生效 |

**Critical Module Check：** 配置 Owner 为 `GuardConfigRepository` / 已登记 mutation API；运行状态单写者为 `StatusPublisher`；轮次执行 Owner 为 `GuardianIterationRunner`；时间与休眠仍经 `IdleTracker` / `PowerLifecycleStateMachine`；日志和通知仍走现有管线。必须保持：配置不丢字段、状态不旁路写、采集资源有界且释放、生命周期安全门禁不绕过、规则未命中不误纠偏。

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

**文件：** 新 `ForegroundProgramReader.cs`、`src/SmartGuard.Engine/Worker/GuardianLoop.cs` / `GuardianIterationRunner.cs`；新 `Tests/SmartGuard.Engine.Tests/ForegroundGuardIntegrationTests.cs`；现有 `GuardianPowerLifecycleIntegrationTests.cs`；架构契约。

- [ ] RED 用可注入事实序列覆盖采样间切换、动作前切换、进程退出、查询失败、休眠恢复、停止及配置变化；短暂切换未采样属于已声明语义，不能要求测试捕获每个事件。
- [ ] 使用 `GetForegroundWindow` → `GetWindowThreadProcessId` → `QueryFullProcessImageNameW` 在原轮次采样；路径查询只申请 `PROCESS_QUERY_LIMITED_INFORMATION`，失败返回 Unknown。
- [ ] 限制关闭或人工接管有效时跳过前台采集；启用自动限制时采样，无新线程或监听订阅。采集失败不让循环退出；恢复后重新读取前台。
- [ ] 发起计划切换及切前亮度同步前复核最新事实与配置，经同一决策入口决定是否执行。已经成功切换后的亮度恢复完成收尾，不因前台变化留下半完成状态；不承诺检测与系统切换绝对原子化。
- [ ] 非守护期间成功探测计划则更新观察基线；首次进入自动准入时吸收当前计划，再按本轮策略纠偏，不补发历史外部变更通知。复核失败时同样不发纠偏通知。
- [ ] 探测失败时不切换计划，但仍发布“本轮无法执行”的原因；保留的计划信息必须标识为上次观察，不显示为新鲜测量，不为此建设独立遥测系统。
- [ ] 命令：`dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj`；预期 PASS；所有查询句柄释放，停止后无新增后台任务。

### 任务 4：轻量设置与状态展示

**文件：** `src/SmartGuard.Settings/SmartGuard.Settings.xaml`、`SettingsPolicyCoordinator.cs`、`SettingsWindowController.cs`、`StatusReader.cs`；`src/SmartGuard.Contracts/StatusModels.cs`；`src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs`；`src/SmartGuard.Tray/TrayStatusFormatter.cs` / `TrayDisplayState.cs` / `StatusStore.cs`；对应 Settings、Tray、Engine 状态测试。

- [ ] RED 覆盖开关、选择 exe、去重、删除、重启保留、空名单提示、旧状态缺字段，以及等待名单不会切换暂停开关。
- [ ] UI 仅编辑配置、展示 Engine 结果。空名单显示“添加程序后生效”；未命中显示“等待指定程序”；识别失败显示“暂时无法识别前台程序”；人工接管显示“临时高性能”。
- [ ] 将运行时配置/识别异常和状态陈旧与普通未命中分开；字段缺失或状态过期不得显示确定的正在守护。
- [ ] RED 覆盖“目标计划等其他字段不变，只有 guardReason 改变”仍写入 status 并更新 Tray；Clone 保留新字段。Source / Reason 为跨进程稳定代码，展示文案只在 UI 映射；不根据空闲标签推导守护是否在执行。
- [ ] 分别运行 `dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj`、`dotnet test Tests/SmartGuard.Tray.Tests/SmartGuard.Tray.Tests.csproj` 和 Engine 状态测试；预期 PASS。

### 任务 5：业务链验收与性能证据

- [ ] 在实现前和实现后，以相同机器、相同检查间隔测 0 / 1 / 128 项名单、静止前台及采样间切换；记录 CPU、内存、句柄、轮次数和响应延迟。预算：无新后台线程、无额外轮询频率、单轮至多两次前台采集、无全进程扫描、原性能门禁不退化；下一允许轮次重新决策，报告检查间隔及轮次耗时，不承诺 2 秒响应。
- [ ] 执行 `dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj`，确认单一入口及契约；执行全量 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1`。
- [ ] 同时核对退出码与 `.work/test-results/` summary JSON，逐项记录 Pester、.NET、Tray Integration、Installer Integration 的通过/失败/跳过数与未运行原因，不引用旧结果。
- [ ] 真实桌面走通：添加目标 → 命中升档 → 无输入达到原阈值降档 → 离开保留计划 → 名单外人工改计划不纠偏 → 返回重新决策 → 名单外手动高性能有效且到期回归规则。
- [ ] 实测采样间切换、目标退出、锁屏/UAC、休眠恢复、查询故障、配置读写失败及重启；确认等待与故障可区分，资源无持续增长。不同桌面/权限下无法识别应保留当前计划。
- [ ] 检查键盘添加/删除和焦点、可访问名称、100% / 200% DPI、高对比度与减少动画相关路径；未执行标记 `NOT RUN`，环境阻塞标记 `BLOCKED`。
- [ ] 保存本次 RED/GREEN、性能和桌面证据到 `.work/guard-rules/`；检查 diff，只提交已验证的任务文件，不提交现有无关修改。

## 5. 完成标准与依据

任务 1–5 的行为、架构、配置、生命周期及真实桌面证据齐备才能宣称功能完成。旧入口无生产调用、前台事实无第二采集真源、规则不干预不误纠偏是阻断验收项。后台渲染保护、应用包身份、多规则编排、安装器改造均不在本次范围。

本计划只描述后续工作；本次文档交付仅检查 UTF-8 无 BOM、链接/结构和 `git diff --check`，不运行全量代码测试。

- 项目规则：[PROJECT-RULES.md](../PROJECT-RULES.md)；权威架构：[ARCHITECTURE-CONTRACT.md](../ARCHITECTURE-CONTRACT.md)。本计划不取代两者。
- 官方依据：[GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow) 明确可能返回空窗口；[GetWindowThreadProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid) 查询窗口所属进程；[QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew) 支持 limited-information 权限和 Win32 路径格式。实际应用兼容性与时延须以桌面验收确认。

## 6. grill-me 自主审查记录（2026-09-30）

**结论：** 已按推荐项修订，无需用户选择的阻塞项。审查方式为同一审查者先逐分支质疑，再切换专业视角复查；不冒称多个独立专家或已执行测试。已确认需求与工程建议分开：前台准入、离开保留、原规则降档、人工例外为用户决策；周期采样、有界线性匹配及不建 hook 为本轮采纳的工程建议。

### 6.1 决策树收敛

| 自主质疑 | 依据与采用的推荐项 | 处置 |
|---|---|---|
| 统一自动架构是否意味着迁移人工即时操作？ | `HighPerformanceBoost.Apply` 先写期限再经 activator 切换；用户明确人工例外。保留人工路径，统一 Engine 自动决策，不加 IPC 总线 | 已解决 |
| 是否必须有 hook、每秒调度和 2 秒目标？ | 用户未要求即时响应；`GuardianIterationTiming` 已有检查周期。采用原周期采样，承认短暂切换会漏采；新动作前复核资格 | 已解决，删减复杂度 |
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
| Windows / 生命周期 | HWND 失效、受限进程、休眠后旧事实、长期句柄泄漏 | 每轮重新采集、最小权限、显式 Unknown、句柄释放；不引入监听线程；不绕过恢复 directive |
| 并发 / 恢复 | 采样后前台切走；复核失败却先发纠偏通知；切换后中断亮度恢复 | 复核覆盖全部切前副作用；失败无新纠偏通知；已提交动作完成收尾，保留原取消规则，不保证原子切换 |
| 配置 / 兼容 | 保存普通设置丢名单；坏 JSON 被当成默认关闭；回滚旧版本仍声称受规则保护 | mapper/save/reset/mutation 全链测试；坏配置禁止 Engine 新动作；旧配置默认关闭，旧版本明确不支持限制 |
| 测试 | 新类型不存在造成编译失败被当 RED；缺少任务依赖；自动化被当桌面验收 | 先配置后决策；分离可编译行为 RED 与契约门禁；目标业务链、全量 summary 和真实桌面证据分层 |
| 产品 / 无障碍 | 名单外高性能仍保留；开关保存后用户期待即时生效；等待状态被显示为暂停 | 固定“不干预、保留计划”；说明按检查周期生效；一个开关和名单，少解释性装饰；键盘和状态可访问验证 |
| 性能 / 个人维护 | 为预计增长引入缓存、线程、健康框架和广泛配置治理 | 首版 O(N≤128)、至多两次采集、无新调度；只修新规则必需的配置有效性与状态传播，不处理范围外治理 |

**事实、推断与未验证信息：** 上述代码行为已在本轮通过 CodeGraph 读取当前文件核对；“hook 暂不值得引入”和“线性匹配足够作为首版”是基于规模与频率的工程判断，不是性能实测。目标应用兼容性、真实延迟、UIA 和资源增长尚未验证，保留在任务 5；不得据此声称实现通过。

**后续重新进入设计的触发条件：** 只有出现明确即时响应需求、真实性能回退或指定应用无法以 exe 路径表达时，才重新评估事件监听、缓存或应用身份；本轮不预建这些设施。没有需求阻塞问题需要抛给用户；实现阶段的 RED、门禁与桌面证据仍是必须完成的验收条件。
