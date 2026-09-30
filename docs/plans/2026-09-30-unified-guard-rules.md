# 统一自动守护规则实现计划

**日期：** 2026-09-30

**状态：** 方案已确认，尚未实现；本计划不代表功能或桌面验收已通过。

**目标：** 自动守护统一经现有 `PolicyEngine` 决策；第一版增加前台程序准入，保留手动高性能例外，配置保持克制。
**技术：** 现有 .NET 8 / Windows Engine、WPF Settings、Tray 与 Win32 API，不引入规则引擎依赖。

## 1. 已确认的行为

按以下顺序判定；电源生命周期的 `AllowPolicyMutation` 仍是执行层安全门禁，任何规则都不能绕过：

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
- 前台窗口所属进程可能不是启动器；首版不承诺覆盖所有打包应用和托管窗口。路径变化保留条目并提示重新选择。

## 2. 责任与契约

**唯一链路：** 前台事实采集 → `PolicyEngine` → `GuardianIterationRunner` 执行 → `StatusPublisher` 发布 → Settings / Tray 展示。

以下是本次设计名称，尚未存在的类型在对应任务创建；不再建立第二套策略入口：

| 文件或类型 | 责任 |
|---|---|
| `src/SmartGuard.Engine/Domain/PolicyEngine.cs` | 新增统一 `Evaluate` 决策入口，输入配置、空闲、电源、前台事实和显式当前时间；迁移现有生产调用，删除退役决策入口 |
| 新建 `src/SmartGuard.Engine/Domain/GuardDecision.cs` | 决策值：可空 `TargetPlanGuid`、`Source`（Manual / Automatic / None）、`Reason`；空目标明确表示不干预 |
| 新建 `src/SmartGuard.Engine/Domain/ForegroundObservation.cs` | 事实值：识别是否成功、可空完整路径、失败原因；不依赖 Win32/WPF，不携带窗口标题 |
| 新建 `src/SmartGuard.Engine/Infrastructure/ForegroundProgramMonitor.cs` | Win32 前台采集与事件监听；只通知事实变化，不执行策略；生命周期归 `GuardianLoop` |
| `src/SmartGuard.Configuration/GuardConfig.cs` | 新增 `ForegroundRestrictionEnabled=false`、`ForegroundProgramPaths=[]` |
| `GuardConfigRepository` / `SettingsSaveCoordinator` | 沿用配置读取、原子保存和设置保存入口；同步 Validator、SnapshotMapper、ResetService |
| `src/SmartGuard.Contracts/StatusModels.cs` | 新增可选规则来源、原因和前台采集健康字段；不改变 `paused` 含义 |
| `StatusPublisher` / `StatusJsonReader` / `StatusStore` / `StatusReader` | 保持 Engine 单写者及共享读取链；旧状态缺字段显示未知，不推导为正在守护 |

## 3. 影响与资源边界

按项目规则属于**高影响改动**：涉及配置、跨进程状态、自动决策和后台监听。下列数值为本计划提出的工程预算，不是已测结果。

| Change Impact Matrix | 本次处理与证据 |
|---|---|
| Function / Architecture | 上表行为矩阵；单一决策入口；架构门禁证明采集、UI 不切换计划 |
| Data / Compatibility | 新字段默认关闭；保存、局部 mutation、重置、旧配置和旧状态测试；无法读出有效配置时不因默认回退意外绕过已启用限制 |
| Performance / Resource | 最多 128 个路径；配置变化时构建 O(N) 集合，单次匹配平均 O(1)；不扫描全部进程；一个监听线程、一份最新待处理信号、无增长事件队列 |
| Concurrency & Time | 回调只合并信号；前台事件驱动的策略轮次最多每秒一次，使用单调时间；执行前重新采集并经同一入口判断；不改变现有空闲与人工期限语义 |
| Reliability / Recovery | HWND 为空、进程退出、路径访问失败均显式 Unknown；监听失败降级为原周期采样并发布健康状态；退出释放 hook、线程与句柄；恢复后重新采集 |
| Security / Observability | 路径查询使用最小权限，不新增 UAC；不读取窗口标题或记录程序使用历史；状态区分暂停、等待、空名单和识别异常；日志只记转换并受现有去重控制 |
| Operations | 安装器、计划任务注册和发行流程 N/A；新版本读旧配置受测。旧二进制不会执行新限制，回滚须明确恢复原行为，不能声称限制仍生效 |

**Critical Module Check：** 配置 Owner 为 `GuardConfigRepository` / 已登记 mutation API；运行状态单写者为 `StatusPublisher`；后台监听 Owner 为 `GuardianLoop`；时间与休眠仍经 `IdleTracker` / `PowerLifecycleStateMachine`；日志和通知仍走现有管线。必须保持：配置不丢字段、状态不旁路写、后台资源有界且可退出、生命周期安全门禁不绕过、规则未命中不误纠偏。

执行前重新读取目标文件与 Git 状态，保护现有修改。异常配置测试须覆盖破损 JSON 和保存失败：最后有效配置可用于展示，但配置无有效读证据时不发起新自动动作；恢复读成功后重新决策，不新增第二配置缓存真源。

## 4. 开发顺序

每项按“行为测试 → 保存符合预期的 RED → 最小实现 → GREEN / 回归 → 检查 diff → 只提交本任务文件”推进。RED 必须由目标行为缺口导致，不能把编译或环境失败作为证据。每项实现前先同步相关架构契约并补架构门禁。

### 任务 1：统一决策与行为矩阵

**文件：** `PolicyEngine.cs`、新 `GuardDecision.cs` / `ForegroundObservation.cs`、`GuardianIterationRunner.cs`；`Tests/SmartGuard.Engine.Tests/PolicyEngineTests.cs`；新增 `Tests/SmartGuard.Architecture.Tests/GuardRulesArchitectureTests.cs`；`docs/ARCHITECTURE-CONTRACT.md`。

- [ ] 先以限制关闭的输入证明迁移前后结果一致，覆盖阈值前后、低电量、插电、暂停和手动接管；注入时间测试接管到期边界。
- [ ] 再测试空名单、未知、未命中不干预；命中保持原策略；先用合成前台事实，不提前引入 Windows 监听。
- [ ] 输出统一决策，Runner 的切换与纠偏资格消费同一决策；迁移生产调用，不留下两套自动目标算法。
- [ ] 目标命令：`dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --filter FullyQualifiedName~PolicyEngineTests`；预期先行为 RED，再全部 PASS。

### 任务 2：配置持久化与旧数据兼容

**文件：** `src/SmartGuard.Configuration/` 下的 `GuardConfig.cs`、`GuardConfigRepository.cs`、`GuardConfigValidator.cs`、`SettingsSnapshotMapper.cs`、`GuardConfigResetService.cs`、必要的 `ConfigMutationService.cs`；`Tests/SmartGuard.Configuration.Tests/` 下对应行为测试；架构契约。

- [ ] RED 覆盖旧配置默认关闭、中文路径往返、路径重复、超过 128 项拒绝保存、保存其他设置及暂停/手动接管不丢名单。
- [ ] 经已有 repository/save 链实现；恢复默认关闭限制并清空名单；无效名单不悄悄变成无限制模式；破损配置按 §3 收敛。
- [ ] 命令：`dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj`；预期 PASS，并保留保存失败后正式配置仍有效的证据。

### 任务 3：真实前台采集与受控执行

**文件：** 新 `ForegroundProgramMonitor.cs`、`src/SmartGuard.Engine/Worker/GuardianLoop.cs` / `GuardianIterationRunner.cs`；新 `Tests/SmartGuard.Engine.Tests/ForegroundGuardIntegrationTests.cs`；现有 `GuardianPowerLifecycleIntegrationTests.cs`；架构契约。

- [ ] RED 用可注入事实序列覆盖快速切换、进程退出、监听失败、休眠恢复、重复信号、停止及配置变化。
- [ ] 使用 `GetForegroundWindow` → `GetWindowThreadProcessId` → `QueryFullProcessImageNameW`；使用 `EVENT_SYSTEM_FOREGROUND` / `WINEVENT_OUTOFCONTEXT` 提供唤醒，现有周期作为补偿。
- [ ] 限制关闭时不创建前台监听；启用后创建、禁用后受控释放；监听失败须可观测，周期采样成功仍可决策。
- [ ] 发起计划切换及切前亮度同步前复核最新事实与配置，经同一决策入口决定是否执行。已经成功切换后的亮度恢复完成收尾，不因前台变化留下半完成状态；不承诺检测与系统切换绝对原子化。
- [ ] 不干预期间更新计划基线，不误发纠偏通知；规则和采集健康状态独立于电源计划探测成功与否发布。
- [ ] 命令：`dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj`；预期 PASS；hook / 线程 / 句柄在停止后释放，计数有界。

### 任务 4：轻量设置与状态展示

**文件：** `src/SmartGuard.Settings/SmartGuard.Settings.xaml`、`SettingsPolicyCoordinator.cs`、`SettingsWindowController.cs`、`StatusReader.cs`；`src/SmartGuard.Contracts/StatusModels.cs`；`src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs`；`src/SmartGuard.Tray/TrayStatusFormatter.cs` / `StatusStore.cs`；对应 Settings、Tray、Engine 状态测试。

- [ ] RED 覆盖开关、选择 exe、去重、删除、重启保留、空名单提示、旧状态缺字段，以及等待名单不会切换暂停开关。
- [ ] UI 仅编辑配置、展示 Engine 结果。空名单显示“添加程序后生效”；未命中显示“等待指定程序”；识别失败显示“暂时无法识别前台程序”；人工接管显示“临时高性能”。
- [ ] 将运行时配置/识别异常和状态陈旧与普通未命中分开；字段缺失或状态过期不得显示确定的正在守护。
- [ ] 分别运行 `dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj`、`dotnet test Tests/SmartGuard.Tray.Tests/SmartGuard.Tray.Tests.csproj` 和 Engine 状态测试；预期 PASS。

### 任务 5：业务链验收与性能证据

- [ ] 在实现前和实现后，以相同机器与场景测 0 / 1 / 128 项名单、静止前台及快速切换；记录 CPU、内存、句柄、轮次数和响应延迟。预算：单个待处理信号、前台事件轮次不超过每秒一次、无全进程扫描、原性能门禁不退化；监听可用且执行链无在途动作时稳定前台在 2 秒内进入新决策，降级时延迟按当前检查间隔明确报告。
- [ ] 执行 `dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj`，确认单一入口及契约；执行全量 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1`。
- [ ] 同时核对退出码与 `.work/test-results/` summary JSON，逐项记录 Pester、.NET、Tray Integration、Installer Integration 的通过/失败/跳过数与未运行原因，不引用旧结果。
- [ ] 真实桌面走通：添加目标 → 命中升档 → 无输入达到原阈值降档 → 离开保留计划 → 名单外人工改计划不纠偏 → 返回重新决策 → 名单外手动高性能有效且到期回归规则。
- [ ] 实测快速切换、目标退出、锁屏/UAC、休眠恢复、监听故障、配置读写失败及重启；确认等待与故障可区分，资源无持续增长。不同桌面/权限下无法识别应保留当前计划。
- [ ] 检查键盘添加/删除和焦点、可访问名称、100% / 200% DPI、高对比度与减少动画相关路径；未执行标记 `NOT RUN`，环境阻塞标记 `BLOCKED`。
- [ ] 保存本次 RED/GREEN、性能和桌面证据到 `.work/guard-rules/`；检查 diff，只提交已验证的任务文件，不提交现有无关修改。

## 5. 完成标准与依据

任务 1–5 的行为、架构、配置、生命周期及真实桌面证据齐备才能宣称功能完成。旧入口无生产调用、前台事实无第二采集真源、规则不干预不误纠偏是阻断验收项。后台渲染保护、应用包身份、多规则编排、安装器改造均不在本次范围。

本计划只描述后续工作；本次文档交付仅检查 UTF-8 无 BOM、链接/结构和 `git diff --check`，不运行全量代码测试。

- 项目规则：[PROJECT-RULES.md](../PROJECT-RULES.md)；权威架构：[ARCHITECTURE-CONTRACT.md](../ARCHITECTURE-CONTRACT.md)。本计划不取代两者。
- 官方依据：[GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow) 明确可能返回空窗口；[SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook) 要求注册线程具备消息循环，支持异步 out-of-context 通知。实际应用兼容性与时延须以桌面验收确认。
