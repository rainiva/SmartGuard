# SmartGuard 架构契约 — 单真源与单入口

**状态：** 生效  
**关联：** [AGENTS.md](../AGENTS.md) §8、§11；Pester `Phase 8`；`SmartGuard.Architecture.Tests`

本文档是 SmartGuard **配置、路径、状态、生命周期** 的唯一架构权威。新增字段、入口或共享类型必须先更新本文档并补充门禁测试（TDD RED）。

---

## 1. 真源注册表

| 概念 | 权威来源 | 允许读者 | 允许写者 | 禁止模式 |
|------|----------|----------|----------|----------|
| 安装根 `{root}` | `InstallRootResolver` | 全部 exe | — | 各 exe 内私有 `RootResolver` |
| 配置文件 | `{root}/SmartGuard.config.json` | 全部经 `GuardConfigRepository.TryLoad` | `GuardConfigRepository.Save`、`ConfigMutationService` | Engine 直调 `GuardConfig.LoadFromFile` |
| 运行时状态 | `{root}/SmartGuard.status.json` | `StatusJsonReader` / `StatusStore` | `StatusPublisher`（Engine） | Settings/Tray 各自解析 JSON |
| 电池与供电状态 | `BatteryInfoProvider`；Windows `GetSystemPowerStatus` 为主来源，WMI 仅作未知字段的可选补充 | Engine 轮询、电源事件、状态发布 | — | WMI 异常覆盖原生状态；独立采集入口；异常直接伪造 `100% / 插电` |
| 主日志路径 | `config.LogFile` 或 `SmartGuardPaths.DefaultLogFile(root)` | Engine、Settings 日志页 | Engine、`AppendInfoLog` | Settings 硬编码 `SmartGuard.log` |
| 启动日志路径 | `SmartGuardPaths.StartupLogFile(root)` | Engine CLI、Settings/Tray 暂停回退 | — | `InstallCommands` 硬编码 `SmartGuard.startup.log` |
| 日志行追加落盘 | `SmartGuardLogWriter.TryAppendLine` | 全部进程 | `SmartGuardLogWriter` | 各处 `File.AppendAllText` 直写日志 |
| 可执行文件路径 | `SmartGuardPaths.EngineExe` / `TrayExe` / `SettingsExe` / `CliExe` | 注册、shell、启动 | — | `Path.Combine(root, "bin", "*.exe")` 散落 |
| 进程镜像名 | `SmartGuardPaths.ProcessImageNames` | `EngineLifecycle` | — | `EngineLifecycle` 内独立 exe 名字面量列表 |
| Settings XAML | `src/SmartGuard.Settings/SmartGuard.Settings.xaml`（编译嵌入/BAML） | `SettingsXamlLoader.TryLoadEmbeddedWindow` | Settings 项目源文件 | runtime 读取 `installRoot\\lib\\SmartGuard.Settings.xaml` |
| 品牌图标 | `assets/branding/official/*.png` 官方源帧 -> `assets/branding/SmartGuard.ico`（经项目嵌入资源消费） | `BrandIconLoader`、`AppBrandIcon`、Tray 图标；允许的 Windows consumers 仅 `ApplicationIcon`、`window.Icon`、`Tray.exe,0`、`IconUri=SmartGuard.ico`（安装时经 `BrandIconLoader.SaveEmbeddedIconToFile` 部署到 `{root}/bin/SmartGuard.ico`，为 Toast 标题栏图标真源）；Toast XML 不得含 `appLogoOverride`/`<image>` | official 源帧 + canonical `.ico` + csproj 引用 | runtime 读取 `installRoot\\lib\\SmartGuard.ico`；`IconUri=Tray.exe`（.exe 不被 toast 识别）；`SystemIcons.Shield`、caller-supplied fallback，或 live `assets/icon/**` 草稿树 |
| 初始化标记 | `SmartGuardPaths.InitMarkerFile(root)` | Engine 首次运行 | Engine | `".SmartGuard.initialized"` 字面量散落 |
| 提升拒绝标记 | `SmartGuardPaths.ElevationDeclinedMarkerFile(root)` | Engine install/uninstall | `ElevationDeclinedMarker` | `".SmartGuard.elevation-declined"` 字面量散落 |
| 脚本/Inno 文件名 | `SmartGuardPaths.*FileName`（C# 权威）→ `InnoDistributionManifestWriter` 生成 `installer/generated/SmartGuard.Distribution.Constants.iss.inc` | ps1/Inno | — | 脚本与 Inno 硬编码文件名；`SmartGuard.iss` 手写 live exe/task literal |
| 计划任务名 | `ScheduledTaskRegistrar.GuardianTaskName` / `TrayTaskName` → generated constants/run include | 注册、恢复、安装 | `ScheduledTaskRegistrar` | 字面量 `"SmartGuard Guardian"` 散落；`SmartGuard.iss` 手写 `/TN` |
| Inno live entry/file-name truth | `SmartGuardPaths.*ExeFileName` + `ScheduledTaskRegistrar.GuardianTaskName` → `InnoDistributionManifestWriter` generated `installer/generated/SmartGuard.Distribution.Constants.iss.inc` / `installer/generated/SmartGuard.Distribution.Run.iss.inc` | Inno 安装流程 | `InnoDistributionManifestWriter` | `SmartGuard.iss` 仅保留安装流程与 UI，不得手写 live exe/task literal |
| legacy cleanup 任务名 | `LegacyScheduledTaskCatalog.TaskNames` | `LegacyTaskCleaner`（经 `InstallCommands`） | — | `LegacyTaskCleaner` 内联旧任务名；live `scripts/*.ps1` 维护第二份名单 |
| 开机自启 UI | `config.AutoStartEnabled` | Settings | `SettingsSaveCoordinator` + `AutoStartService.SetEnabled` | Settings 读 `schtasks`/`SyncFromTasks` 决定展示 |
| 暂停状态展示 | `status.json` `paused`（Tray 菜单与 Settings `tglPaused` 初始展示读源） | Tray、Settings | `ConfigMutationService.SetPaused` | Tray/Settings 读 `config.Paused` 决定展示或切换 |
| 版本号（UI） | `installer/version.txt` → 各 csproj `<Version>` | Settings About | Packaging bump | 仅 Settings 同步版本 |
| Engine 停止 | `EngineLifecycle.StopForUninstall` | CLI uninstall、Inno `TryStopSmartGuardProcesses`（委托 Engine `--uninstall`，以 `Exec` 成功且退出码 0 为准）、测试 helpers | `EngineLifecycle` | 独立 taskkill/schtasks 脚本副本；Inno 以 `tasklist` 探测结果跳过停止 |
| 计划任务安装跳过 | `ScheduledTaskInstallGate`；`InstallCommands` 读 `SMARTGUARD_SKIP_SCHEDULED_TASKS` / `--skip-scheduled-tasks` | Engine `--install`、Cli `install`、Inno 集成测试 | `ScheduledTaskInstallGate` | skip 时仍调用 `RegisterGuardian`/`RegisterTray`/`DesktopShellRegistration.Apply` |
| 日志页空闲秒数 | `LogViewIdleReader.TryRead` / `TryReadSeconds` | Settings 日志页 | — | Settings 内不得重复 `GetLastInputInfo` 计算 |
| UAC 提升 | `ElevationHelper` + `CliAdminRunner` | `InstallCommands`、`UpdateInstallerLauncher`、`SmartGuard.Cli` | — | 内联 `Verb = "runas"` / VBS 散落 |
| shell 集成 | `DesktopShellRegistration` | Installer / repair | `DesktopShellRegistration.Apply`、`DesktopShellRegistration.Remove` | Tray runtime 自注册、marker 自愈、Inno `[Icons]` 用户快捷方式 |
| 分发清单 | `DesktopDistributionManifest` | `DesktopSuitePublisher`、`StageCommand`、`PayloadCopier`、`StagingLayout`、Inno include 生成（Files/Processes/LegacyCleanup/Constants/Run） | — | 在 publisher/staging/installer 各自维护项目/文件/进程列表；`SmartGuard.iss` 手写 `[Run]` exe/task |
| 运维子命令 | `SmartGuard.Cli` 薄路由 | dev cmd 单行转发、README | `Configuration` 操作真源 | Cli 内嵌 `schtasks`/`taskkill`/spawn 逻辑 |
| GitHub Token（内存） | `GuardConfig.GitHubToken` | Settings 更新检查（`SettingsUpdateCheckCoordinator`） | `GuardConfigRepository.Save`（经 Settings 关于页 `SettingsGitHubTokenCoordinator` 或策略保存透传） | Engine/Tray 读取；磁盘明文 `GitHubToken` |
| GitHub Token（磁盘） | `SmartGuard.config.json` 字段 `GitHubTokenProtected`（DPAPI `CurrentUser`，经 `GitHubTokenProtector`） | `GuardConfigRepository.TryLoad` | `GuardConfigRepository.Save` | 明文 `GitHubToken` 写入；诊断包导出须剔除 `GitHubToken` 与 `GitHubTokenProtected` |
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
| `GuardConfigRepository` | `SmartGuard.Configuration` | 读（含迁移）、全量 Save |
| `ConfigMutationService` | `SmartGuard.Configuration` | 原子局部写：Paused、ManualHighPerformanceUntil |
| `StatusJsonReader` | `SmartGuard.Configuration` | 读取并反序列化 status.json |
| `BatteryInfoProvider` | `SmartGuard.Engine.Infrastructure` | 以 `GetSystemPowerStatus` 为电量/供电状态主来源；WMI 失败不得丢弃原生结果 |
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
| `GitHubTokenProtector` | `SmartGuard.Configuration` | DPAPI 保护 `GitHubTokenProtected` 读写 |
| `DiagnosticBundleExporter` | `SmartGuard.Configuration` | 诊断包 zip 导出；config 剔除 `GitHubToken` / `GitHubTokenProtected` |
| `BrandIconLoader.SaveEmbeddedIconToFile` | `SmartGuard.Configuration` | 安装时将嵌入 .ico 提取到 `{root}/bin/SmartGuard.ico`，供 AUMID `IconUri`（Toast 标题栏图标真源）消费 |
| `ExceptionFormatter` | `SmartGuard.Configuration` | 异常完整链格式化（类型/消息/StackTrace/InnerException/AggregateException 展开）唯一入口 |
| `SmartGuardLogWriter` | `SmartGuard.Configuration` | 统一日志追加：命名 Mutex + FileShare 重试 + 永不抛出 |
| `GlobalExceptionHandlers` | `SmartGuard.Configuration` | 注册 AppDomain/TaskScheduler 全局未处理异常处理器 |
| `CrashLogWriter` | `SmartGuard.Configuration` | 崩溃应急同步写入（主日志尽力 + `%TEMP%\SmartGuard\crash.log` 必写） |
| `LogFallbackChain` | `SmartGuard.Configuration` | 日志三层降级链（主日志 → startup 回退 → `%TEMP%\SmartGuard\emergency.log`），每层异常隔离 |
| `LogLevelFilter` | `SmartGuard.Engine.Infrastructure` | 日志级别统一过滤入口：`ShouldWrite`（Heart 恒放行）+ `ParseMinimum`（大小写不敏感，非法回退 Info）；配置项 `GuardConfig.LogMinimumLevel` |
| `LogPipeline` | `SmartGuard.Configuration` | 异步日志管线：有界双优先级队列 + 批量出队（High 先）+ 诊断计数；Slice 3 起含单写者消费线程与生命周期 |
| `LogPipelineOptions` | `SmartGuard.Configuration` | 管线容量/批量/定时/关闭超时参数（默认 20000/2000/200/100ms/3000ms） |
| `LogPipelineDiagnostics` | `SmartGuard.Configuration` | 管线诊断快照：Enqueued/DroppedNormal/DroppedHigh/WrittenLines/FailedBatches |
| `LogPriority` | `SmartGuard.Configuration` | 日志行优先级（Normal/High），满时先丢 Normal |
| `LogPipelineState` | `SmartGuard.Configuration` | 管线生命周期状态机：Starting/Running/Flushing/Stopped |
| `HeartLogSampler` | `SmartGuard.Engine.Infrastructure` | Heart 心跳持久化节流（注入时钟可测，被节流不重置窗口）；配置项 `GuardConfig.LogHeartMinIntervalSec` |

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
| M-04 | **已关闭** | `PowerPlanCatalogProvider.TryLoad()`（Engine/Settings 单源） |
| M-06 | **已关闭** | `TrayApplicationContext` → `TrayDisplaySettingsCache(_configRepository, _root)` + `ConfigFileWatcher` |
| M-07 | **已关闭** | `SettingsThemeCoordinator.SaveThemePreferences` → `SettingsPolicyCoordinator.SaveThemePreferences` → `SettingsSaveCoordinator.Save` |
| M-08 | **已关闭** | `LogViewIdleReader`（Contract §1 登记） |
| M-11 | **已关闭** | `DesktopAppBootstrap` |
| M-14 | **已关闭** | `status.json` `enginePid` |
| M-16 | **已关闭** | `BrandIconLoader` |
| L-02 | **已关闭** | `GuardConfigRepository` `FileSystemWatcher` + `LastWriteTimeUtc` |
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
| S-05 | **已关闭** | Settings 计划目录 `PowerPlanCatalogProvider.TryLoad()` |
| S-06 | **已关闭** | AutoStart UI 读 `config.AutoStartEnabled`；写经 `SettingsSaveCoordinator` + `AutoStartService.SetEnabled` |
| S-07 | **已关闭** | `ConfigFileWatcher` |
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
| `ConfigFileWatcher` 共享 | `ConfigFileWatcherArchitectureTests` |
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
| GitHub Token 保护 | `GitHubTokenArchitectureTests`、`GitHubTokenProtectorTests`、`GuardConfigRepositoryTokenTests` |
| 诊断包导出脱敏 | `DiagnosticBundleExporterArchitectureTests`、`DiagnosticBundleExporterTests`、`SettingsDiagnosticExportCoordinatorTests` |
| Pester Phase 8 | `Tests/SmartGuard.Tests.ps1` |
| Toast 品牌 Logo | `ToastAppLogoArchitectureTests`、`ToastNotificationXmlBuilderTests` |
| 计划任务安装跳过 | `InstallCommandsSkipArchitectureTests` |
| 计划任务测试隔离契约 | `ArchitectureContractScheduledTaskIsolationTests` |
| 日志阶段一止血 | `ArchitectureContractLoggingPhase1Tests` |
| 日志阶段二异步管线 | `ArchitectureContractLoggingPhase2Tests` |

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
| INT-LIFECYCLE | **阻断** | Integration 必须按 Capture → Stop → Test → Restore 执行；Stop 失败或 Restore 失败任一发生，当前测试必须判红，不得 best-effort 吞掉后继续；恢复按快照决定进程运行态，孤儿进程只清理不复活 |
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
| LOG-P2-LIFECYCLE | 阻断 | 管线受控关闭：`StopAsync` 按"停止接收 → 排空队列 → Flush → Stopped"执行，`ShutdownTimeoutMs`（默认 3s）超时上限，超时返回 false 且消费者任务自行收敛不泄漏；批量触发为 `FlushIntervalMs=100` 与 `MaxBatchSize=200` 先到先触发 |
| LOG-P2-WIRE | 阻断 | `GuardianLoop.WriteLog` 经 `LogPipeline.TryEnqueue` 非阻塞入队（Warn/Error/Critical 走 High），消费侧 `FileLogger.TryWriteLines` 单次打开批量追加；入队失败与批量落盘失败均回退阶段一 `LogFallbackChain` 同步三层降级 |
| LOG-P2-HEART | 阻断 | Heart 心跳持久化经 `HeartLogSampler.ShouldPersist` 节流，配置项 `LogHeartMinIntervalSec`（默认 60 秒，0 禁用）；默认 10 分钟心跳下行为不变，仅防秒级心跳源刷爆 |
| LOG-P2-RETAIN | 阻断 | 归档保留三重上限：7 天（既有）+ `LogMaxArchiveFiles`（默认 30，0 禁用）+ `LogDirectoryMaxBytes`（默认 104_857_600，0 禁用）；删除只选归档、最旧先删、永不删当前日志（`LogArchivePlanner` 纯函数 + `FileLogger.EnforceArchiveLimits`） |
| DOC-LOG-P2 | 已关闭 | 本文档 §3/§6/§20 + `ArchitectureContractLoggingPhase2Tests` 终验 |
