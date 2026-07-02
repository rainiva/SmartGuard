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
| 主日志路径 | `config.LogFile` 或 `SmartGuardPaths.DefaultLogFile(root)` | Engine、Settings 日志页、LogViewer | Engine、`AppendInfoLog` | Settings 硬编码 `SmartGuard.log` |
| 计划任务名 | `ScheduledTaskRegistrar.GuardianTaskName` / `TrayTaskName` | 注册、恢复、安装 | `ScheduledTaskRegistrar` | 字面量 `"SmartGuard Guardian"` 散落 |
| 开机自启 UI | `config.AutoStartEnabled` + `AutoStartService.SyncFromTasks` | Settings | `SettingsSaveCoordinator` + `AutoStartService` | 仅写 config 不读 schtasks |
| 暂停状态展示 | `status.json` `paused`（Tray 菜单与切换读源） | Tray | `ConfigMutationService.SetPaused` | Tray 读 `config.Paused` 决定切换 |
| 版本号（UI） | `installer/version.txt` → 各 csproj `<Version>` | Settings About | Packaging bump | 仅 Settings 同步版本 |
| Engine 停止 | `EngineLifecycle.StopForUninstall` | CLI uninstall、Inno `StopSmartGuardProcesses`（委托 Engine `--uninstall`）、测试 helpers | `EngineLifecycle` | 独立 taskkill/schtasks 脚本副本 |
| 日志页空闲秒数 | `LogViewIdleReader.TryRead` / `TryReadSeconds` | Settings 日志页 | — | Settings 内不得重复 `GetLastInputInfo` 计算 |

---

## 2. 入口注册表

| 用户操作 | 唯一推荐入口 | 说明 |
|----------|--------------|------|
| 注册计划任务 | `SmartGuard.Engine.exe --install` | Inno / `Register-AllTasks.cmd` 委托此入口 |
| 启动 Engine（生产） | 计划任务 `SmartGuard Guardian` | Tray/Inno/`Start-Core.cmd` 均 `schtasks /Run`；dev 前台用 `Debug-Engine.cmd` |
| 启动 Tray（生产） | 计划任务 `SmartGuard Tray` | 登录时自动；dev 直启用 `Start-Tray.cmd` / `Restart-Tray.cmd`（**E-10 登记**） |
| 停止 Engine（卸载） | `EngineLifecycle.Stop` | CLI、Inno、集成测试共用 |
| 暂停/恢复守护 | `ConfigMutationService.SetPaused` | Tray 菜单、Settings `tglPaused` 均经此 API；全量保存从 repository 读回 `Paused` |
| 打开设置 | `SettingsMainPageLauncher.Open`（Tray 经 `ExternalToolLauncher.OpenSettings` 委托） | 命名管道激活或 spawn |
| 打开日志 | `SettingsLogsPageLauncher.Open`（Tray 经 `ExternalToolLauncher.OpenLogViewer` 委托） | `LogViewer.exe` 兼容重定向；禁止独立 WinForms 日志壳 |
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
| `ScheduledTaskRegistrar` | `SmartGuard.Configuration` | 任务名与 XML 唯一源 |
| `EngineLifecycle` | `SmartGuard.Configuration` | 停止进程与任务顺序 |

---

## 4. 禁止清单（机器可测 — Phase 8 / Architecture.Tests）

1. `SmartGuard.Engine` 中不得调用 `GuardConfig.LoadFromFile`（经 Repository）。
2. 不得在 Tray/Settings/LogViewer/Engine 中定义 `class RootResolver` 或 `internal static class RootResolver`（使用 `InstallRootResolver`）。
3. 不得在 `GuardianRecovery` 等处重复字面量 Guardian 任务名（使用 `ScheduledTaskRegistrar.GuardianTaskName`）。
4. 不得在 Settings 硬编码 `Path.Combine(root, "SmartGuard.log")`（使用 `SmartGuardPaths` + `config.LogFile`）。
5. 不得新增绕过 `SettingsSaveCoordinator` 的主题/config 全量写（含 `SaveThemePreferences` 直写 `_originalConfig`）。
6. 不得在 Inno/cmd 新增独立 schtasks 注册逻辑（委托 Engine `--install`）。

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
| L-04 | **已关闭** | 本文档 + PHASE/INNO/MIGRATION 索引指针（见 MIGRATION） |

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
| S-06 | **已关闭** | AutoStart UI `SyncFromTasks()`；保存经 `SettingsSaveCoordinator` |
| S-07 | **已关闭** | `ConfigFileWatcher` |
| S-08 | **已关闭** | `Status.cmd` 与 `ScheduledTaskRegistrar` 任务名一致 |
| E-01 | **已关闭** | `Start-Core.cmd` → `schtasks`；`Debug-Engine.cmd` dev 前台 |
| E-02 | **已关闭** | `LogViewer.exe` → Settings `--page logs` |
| E-04～E-07 | **已关闭** | 脚本/集成 stop 委托 `SmartGuardStop.ps1`；legacy 任务名 `LegacyScheduledTaskNames.ps1` |
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
| E-10 | **登记** | 生产 Tray = 计划任务；dev = `Start-Tray.cmd` / `Restart-Tray.cmd` |
| PERF | **已关闭** | 性能测试 settle + 5000ms 预算 + lifecycle 门禁 |

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
| `Start-Core.cmd` schtasks | `StartCoreArchitectureTests` |
| LogViewer 重定向 Settings | `LogViewerProgramArchitectureTests` |
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
| Pester Phase 8 | `Tests/SmartGuard.Tests.ps1` |
